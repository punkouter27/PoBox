using System;
using System.Collections.Generic;
using System.Reflection;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.InferenceEngine;
using Unity.Jobs;

namespace PoBox.Mj
{
    /// <summary>
    /// A brain run directly, without the Inference Engine's scheduling. The shipped policies are small MLPs: the
    /// observation normalised (Sub, Div, Clip), then an actor and a critic tower of Dense and Elu layers. On the CPU
    /// backend a call costs 0.28-0.36 ms (measured 2026-11-16) whatever the batch (16 rows cost 0.33 ms), so most of it
    /// is the engine's own scheduling; with sixteen boxers each on its own network in a free-for-all that is most of
    /// the frame. This reads the loaded model's graph once into one flat buffer and runs the whole network as a single
    /// Burst-compiled job on the calling thread (plain C# was 1.3 ms: the maths needs SIMD), in the same layer order, so
    /// its answers match the Inference Engine's to float rounding (MlpPolicyTests checks every shipped brain). A
    /// graph with any other layer is not taken: <see cref="TryBuild"/> returns null and the boxer keeps the Inference
    /// Engine.
    ///
    /// The layer classes are internal to the Inference Engine, so they are read by type name and field name; a
    /// package update that renames them only makes TryBuild return null.
    /// </summary>
    public sealed class MlpPolicy : IDisposable
    {
        enum Kind { Sub, Div, Clip, Dense, Elu, Relu, Tanh }

        sealed class Op
        {
            public Kind kind;
            public int a, b, c, output;     // value slots: inputs and output (-1 none)
            public float alpha;
            public bool relu;               // a Dense with a fused Relu
            public int rows, cols;          // a Dense's weights: rows (in) x cols (out)
        }

        readonly List<Op> _ops = new List<Op>();
        readonly float[][] _values;         // by tensor index: constants filled once, the rest per call
        readonly int _input;
        int _actions = -1, _value = -1;

        public int InputSize { get; }
        public int ActionSize => _actions >= 0 ? _values[_actions].Length : 0;

        MlpPolicy(int slots, int input, int inputSize)
        {
            _values = new float[slots][];
            _input = input;
            InputSize = inputSize;
            _values[input] = new float[inputSize];
        }

        /// <summary>The direct evaluator for this model, or null if its graph has anything it does not run.</summary>
        public static MlpPolicy TryBuild(Model model, out string why)
        {
            why = null;
            try { return Build(model, out why); }
            catch (Exception e) { why = e.GetType().Name + ": " + e.Message; return null; }
        }

        static MlpPolicy Build(Model model, out string why)
        {
            why = null;
            if (model.inputs.Count != 1) { why = "inputs " + model.inputs.Count; return null; }
            int slots = 0;
            foreach (Constant c in model.constants) slots = Math.Max(slots, c.index + 1);
            foreach (var l in model.layers) foreach (int o in l.outputs) slots = Math.Max(slots, o + 1);
            slots = Math.Max(slots, model.inputs[0].index + 1);
            DynamicTensorShape inShape = model.inputs[0].shape;
            int inSize = inShape.Get(inShape.rank - 1);
            var p = new MlpPolicy(slots, model.inputs[0].index, inSize);

#pragma warning disable CS0618 // Constant.weights is marked deprecated, and is the public way to read a constant
            foreach (Constant c in model.constants)
            {
                if (c.dataType != DataType.Float) { why = "constant " + c.index + " is " + c.dataType; return null; }
                var data = new float[c.shape.length];
                if (data.Length > 0) c.weights.AsReadOnlySpan<float>(data.Length).CopyTo(data);
                p._values[c.index] = data;
            }
#pragma warning restore CS0618

            // Shapes, to size each value: a constant's own; a layer's from its inputs.
            var length = new int[slots];
            for (int i = 0; i < slots; i++) length[i] = p._values[i] != null ? p._values[i].Length : -1;
            var denseCols = new Dictionary<int, (int rows, int cols)>();
            foreach (Constant c in model.constants)
                if (c.shape.rank == 2) denseCols[c.index] = (c.shape[0], c.shape[1]);

            foreach (var layer in model.layers)
            {
                string name = layer.GetType().Name;
                var op = new Op { a = Arg(layer, 0), b = Arg(layer, 1), c = Arg(layer, 2), output = layer.outputs[0] };
                switch (name)
                {
                    case "Sub": op.kind = Kind.Sub; break;
                    case "Div": op.kind = Kind.Div; break;
                    case "Clip": op.kind = Kind.Clip; break;
                    case "Elu": op.kind = Kind.Elu; op.alpha = Field<float>(layer, "alpha"); break;
                    case "Relu": op.kind = Kind.Relu; break;
                    case "Tanh": op.kind = Kind.Tanh; break;
                    case "Dense":
                        op.kind = Kind.Dense;
                        if (!denseCols.TryGetValue(op.b, out var rc)) { why = "Dense weights not a constant matrix"; return null; }
                        op.rows = rc.rows; op.cols = rc.cols;
                        string act = Field<object>(layer, "fusedActivation")?.ToString() ?? "None";
                        if (act == "Relu") op.relu = true;
                        else if (act != "None") { why = "Dense activation " + act; return null; }
                        if (op.c >= 0 && length[op.c] != op.cols) { why = "Dense bias length"; return null; }
                        break;
                    default: why = "layer " + name; return null;
                }
                if (layer.outputs.Length != 1) { why = name + " has " + layer.outputs.Length + " outputs"; return null; }
                int n;
                if (op.kind == Kind.Dense)
                {
                    if (length[op.a] != op.rows) { why = $"Dense input {length[op.a]} for {op.rows} rows"; return null; }
                    n = op.cols;
                }
                else if (op.kind == Kind.Sub || op.kind == Kind.Div)
                {
                    int la = length[op.a], lb = length[op.b];
                    n = Math.Max(la, lb);
                    if ((la != n && la != 1) || (lb != n && lb != 1)) { why = $"{name} broadcast {la} {lb}"; return null; }
                }
                else
                {
                    n = length[op.a];
                    if (op.kind == Kind.Clip && ((op.b >= 0 && length[op.b] != 1) || (op.c >= 0 && length[op.c] != 1))) { why = "Clip bounds not scalars"; return null; }
                }
                if (n <= 0) { why = name + " has no size"; return null; }
                length[op.output] = n;
                p._values[op.output] = new float[n];
                p._ops.Add(op);
            }

            int actions = -1, value = -1;
            foreach (var o in model.outputs)
            {
                if (o.name == "actions") actions = o.index;
                else if (o.name == "value") value = o.index;
            }
            if (actions < 0) { why = "no actions output"; return null; }
            p._actions = actions;
            p._value = value;
            p.Pack();
            return p;
        }

        static int Arg(Layer l, int i) => i < l.inputs.Length ? l.inputs[i] : -1;

        static T Field<T>(object o, string name)
        {
            FieldInfo f = o.GetType().GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (f == null) throw new MissingFieldException(o.GetType().Name, name);
            return (T)f.GetValue(o);
        }

        // ------------------------------------------------------------ the packed program, run by Burst

        // Op codes and the program's layout: one row of OpWidth ints an op.
        const int OpSub = 0, OpDiv = 1, OpClip = 2, OpDense = 3, OpElu = 4, OpRelu = 5, OpTanh = 6, OpWidth = 12;
        NativeArray<float> _arena;
        NativeArray<int> _program;
        int _inputAt, _actionsAt, _valueAt = -1, _actionCount;

        /// <summary>Every value (constants and layer outputs) laid end to end in one buffer, and the ops as offsets into it.</summary>
        void Pack()
        {
            var offset = new int[_values.Length];
            int total = 0;
            for (int i = 0; i < _values.Length; i++) { offset[i] = total; if (_values[i] != null) total += _values[i].Length; }
            _arena = new NativeArray<float>(Math.Max(1, total), Allocator.Persistent);
            for (int i = 0; i < _values.Length; i++)
                if (_values[i] != null && _values[i].Length > 0) NativeArray<float>.Copy(_values[i], 0, _arena, offset[i], _values[i].Length);
            _program = new NativeArray<int>(Math.Max(1, _ops.Count * OpWidth), Allocator.Persistent);
            int Off(int slot) => slot >= 0 ? offset[slot] : -1;
            int Len(int slot) => slot >= 0 && _values[slot] != null ? _values[slot].Length : 0;
            for (int k = 0; k < _ops.Count; k++)
            {
                Op op = _ops[k];
                int code = op.kind switch { Kind.Sub => OpSub, Kind.Div => OpDiv, Kind.Clip => OpClip, Kind.Dense => OpDense, Kind.Elu => OpElu, Kind.Relu => OpRelu, _ => OpTanh };
                int at = k * OpWidth;
                _program[at + 0] = code;
                _program[at + 1] = Off(op.a); _program[at + 2] = Len(op.a);
                _program[at + 3] = Off(op.b); _program[at + 4] = Len(op.b);
                _program[at + 5] = Off(op.c); _program[at + 6] = Len(op.c);
                _program[at + 7] = Off(op.output); _program[at + 8] = Len(op.output);
                _program[at + 9] = op.rows; _program[at + 10] = op.cols;
                _program[at + 11] = op.kind == Kind.Elu ? BitConverter.SingleToInt32Bits(op.alpha) : op.relu ? 1 : 0;
            }
            _inputAt = offset[_input];
            _actionsAt = offset[_actions];
            _actionCount = Len(_actions);
            _valueAt = _value >= 0 ? offset[_value] : -1;
        }

        /// <summary>One answer: <paramref name="observation"/> (InputSize numbers) in, the actions into <paramref name="actions"/>, the critic's value out (0 without one).</summary>
        public float Run(ReadOnlySpan<float> observation, Span<float> actions)
        {
            var arena = _arena;
            for (int i = 0; i < InputSize; i++) arena[_inputAt + i] = observation[i];
            new Program { arena = _arena, program = _program, ops = _ops.Count }.Run();
            int n = Math.Min(_actionCount, actions.Length);
            for (int i = 0; i < n; i++) actions[i] = arena[_actionsAt + i];
            return _valueAt >= 0 ? arena[_valueAt] : 0f;
        }

        public void Dispose()
        {
            if (_arena.IsCreated) _arena.Dispose();
            if (_program.IsCreated) _program.Dispose();
        }

        /// <summary>The whole network, op by op, in one Burst-compiled job run on the calling thread.</summary>
        [BurstCompile(FloatPrecision.Standard, FloatMode.Default, CompileSynchronously = true, DisableSafetyChecks = true)]
        unsafe struct Program : IJob
        {
            public NativeArray<float> arena;
            [ReadOnly] public NativeArray<int> program;
            public int ops;

            public void Execute()
            {
                for (int k = 0; k < ops; k++)
                {
                    int at = k * OpWidth;
                    int code = program[at], a = program[at + 1], aLen = program[at + 2], b = program[at + 3], bLen = program[at + 4];
                    int c = program[at + 5], o = program[at + 7], n = program[at + 8], rows = program[at + 9], cols = program[at + 10], extra = program[at + 11];
                    switch (code)
                    {
                        case OpSub:
                        case OpDiv:
                            for (int i = 0; i < n; i++)
                            {
                                float u = arena[a + (aLen == 1 ? 0 : i)], v = arena[b + (bLen == 1 ? 0 : i)];
                                arena[o + i] = code == OpSub ? u - v : u / v;
                            }
                            break;
                        case OpClip:
                        {
                            float lo = b >= 0 ? arena[b] : float.MinValue, hi = c >= 0 ? arena[c] : float.MaxValue;
                            for (int i = 0; i < n; i++) arena[o + i] = Min(Max(arena[a + i], lo), hi);
                            break;
                        }
                        case OpDense:
                        {
                            float* p = (float*)NativeArrayUnsafeUtility.GetUnsafePtr(arena);
                            Dense(p + o, p + a, p + b, c >= 0 ? p + c : null, rows, cols, extra == 1);
                            break;
                        }
                        case OpElu:
                        {
                            float alpha = BitConverter.Int32BitsToSingle(extra);
                            for (int i = 0; i < n; i++) { float x = arena[a + i]; arena[o + i] = x >= 0f ? x : alpha * (Unity.Mathematics.math.exp(x) - 1f); }
                            break;
                        }
                        case OpRelu:
                            for (int i = 0; i < n; i++) arena[o + i] = Max(arena[a + i], 0f);
                            break;
                        default:
                            for (int i = 0; i < n; i++) arena[o + i] = Unity.Mathematics.math.tanh(arena[a + i]);
                            break;
                    }
                }
            }

            /// <summary>o = x W + bias (W rows x cols, row-major), row by row through W so every read is contiguous; the
            /// four regions never overlap (separate values in the arena), which [NoAlias] tells Burst so it vectorises.</summary>
            static void Dense([NoAlias] float* o, [NoAlias] float* x, [NoAlias] float* w, [NoAlias] float* bias, int rows, int cols, bool relu)
            {
                if (bias != null) for (int q = 0; q < cols; q++) o[q] = bias[q];
                else for (int q = 0; q < cols; q++) o[q] = 0f;
                for (int r = 0; r < rows; r++)
                {
                    float xr = x[r];
                    float* row = w + r * cols;
                    for (int q = 0; q < cols; q++) o[q] += xr * row[q];
                }
                if (relu) for (int q = 0; q < cols; q++) o[q] = Max(o[q], 0f);
            }

            static float Min(float x, float y) => x < y ? x : y;
            static float Max(float x, float y) => x > y ? x : y;
        }
    }
}
