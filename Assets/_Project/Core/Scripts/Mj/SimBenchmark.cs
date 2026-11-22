using System.Diagnostics;
using Mujoco;

namespace PoBox.Mj
{
    /// <summary>
    /// The Tab overlay's training-speed benchmark. Real training (MuJoCo Warp + PPO on a GPU, training/train_box.py)
    /// cannot run in a browser or a phone, so this measures the part this device can: it copies the running fight's
    /// MuJoCo model and state into a scratch mjData and steps that copy as fast as it can for a fixed time, a few
    /// milliseconds a frame so the game keeps running. The result is physics steps a second on this device's CPU (one
    /// world) and the control steps a second that makes (4 physics steps each, as the game and the trainer step). The
    /// fight itself is never touched. Bounded: it stops after <see cref="Seconds"/> of wall time.
    /// </summary>
    public sealed unsafe class SimBenchmark
    {
        public const int PhysicsStepsPerControlStep = 4;
        public float Seconds { get; }
        public bool Running { get; private set; }
        public bool Done { get; private set; }
        public long Steps { get; private set; }
        public double BusySeconds { get; private set; }
        public string Error { get; private set; }

        /// <summary>Physics steps a second of stepping time (the time spent inside mj_step, not the frame time between).</summary>
        public double StepsPerSecond => BusySeconds > 0 ? Steps / BusySeconds : 0;
        public double ControlStepsPerSecond => StepsPerSecond / PhysicsStepsPerControlStep;

        MujocoLib.mjModel_* _m;
        MujocoLib.mjData_* _d;
        readonly Stopwatch _wall = new Stopwatch();

        public SimBenchmark(float seconds = 3f) { Seconds = seconds; }

        /// <summary>Copies the running scene's model and state. False (with <see cref="Error"/>) when no MuJoCo scene runs.</summary>
        public bool Begin()
        {
            MjScene scene = MjScene.InstanceExists ? MjScene.Instance : null;
            if (scene == null || scene.Model == null || scene.Data == null) { Error = "no MuJoCo scene running"; Done = true; return false; }
            _m = scene.Model;
            _d = MujocoLib.mj_makeData(_m);
            if (_d == null) { Error = "mj_makeData failed"; Done = true; return false; }
            MujocoLib.mj_copyData(_d, _m, scene.Data);
            Steps = 0; BusySeconds = 0; Error = null; Done = false; Running = true;
            _wall.Restart();
            return true;
        }

        /// <summary>Steps the copy for up to <paramref name="budgetMs"/> this frame; true while it has time left.</summary>
        public bool Tick(float budgetMs = 6f)
        {
            if (!Running) return false;
            var frame = Stopwatch.StartNew();
            while (frame.Elapsed.TotalMilliseconds < budgetMs)
            {
                for (int i = 0; i < PhysicsStepsPerControlStep; i++) MujocoLib.mj_step(_m, _d);
                Steps += PhysicsStepsPerControlStep;
            }
            BusySeconds += frame.Elapsed.TotalSeconds;
            if (_wall.Elapsed.TotalSeconds >= Seconds) End();
            return Running;
        }

        public void End()
        {
            if (_d != null) { MujocoLib.mj_deleteData(_d); _d = null; }
            _m = null;
            Running = false;
            Done = true;
        }

        public string Summary()
        {
            if (Error != null) return "Benchmark: " + Error;
            if (!Done) return $"Benchmark: running… {Steps:N0} steps";
            return $"Benchmark ({Seconds:0} s, this device, 1 world, CPU): {StepsPerSecond:N0} physics steps/s = {ControlStepsPerSecond:N0} samples/s. Local sim speed, not a training run.";
        }
    }
}
