using System.Collections.Generic;
using NUnit.Framework;
using Unity.InferenceEngine;
using UnityEngine;
using PoBox.Mj;

namespace PoBox.Tests
{
    /// <summary>
    /// Every brain in the project (every ModelAsset: the boxers' three each, the crew's) answers the same through
    /// <see cref="MlpPolicy"/> as through the Inference Engine: 64 observations each, from small to the clipped
    /// extremes, actions and value within 1e-4 (the parity gate's replay mark). Also: it is several times faster.
    /// </summary>
    public class MlpPolicyTests
    {
        static List<(string path, ModelAsset asset)> Brains()
        {
            var list = new List<(string, ModelAsset)>();
#if UNITY_EDITOR
            foreach (string guid in UnityEditor.AssetDatabase.FindAssets("t:ModelAsset", new[] { "Assets" }))
            {
                string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<ModelAsset>(path);
                // The zero action's stand-in (one MatMul, no network) is not a brain anybody fights with.
                if (asset != null && !path.EndsWith("zero_policy.onnx")) list.Add((path, asset));
            }
#endif
            return list;
        }

        [Test]
        public void EveryBrainAnswersTheSameDirect()
        {
            var brains = Brains();
            if (brains.Count == 0) Assert.Ignore("no brains found (a player build)");
            var rng = new System.Random(5);
            int direct = 0;
            var skipped = new List<string>();
            foreach (var (path, asset) in brains)
            {
                Model model = ModelLoader.Load(asset);
                using MlpPolicy mlp = MlpPolicy.TryBuild(model, out string why);
                if (mlp == null) { skipped.Add(path + ": " + why); continue; }
                direct++;
                using var worker = new Worker(model, BackendType.CPU);
                int n = mlp.InputSize;
                var obs = new float[n];
                var mine = new float[mlp.ActionSize];
                float worst = 0f, worstValue = 0f;
                for (int s = 0; s < 64; s++)
                {
                    float scale = s < 16 ? 0.1f : s < 48 ? 2f : 50f;   // the last ones beyond the normaliser's clip
                    for (int i = 0; i < n; i++) obs[i] = (float)((rng.NextDouble() * 2 - 1) * scale);
                    float value = mlp.Run(obs, mine);
                    using var input = new Tensor<float>(new TensorShape(1, n), obs);
                    worker.Schedule(input);
                    var actions = worker.PeekOutput("actions") as Tensor<float>;
                    actions.CompleteAllPendingOperations();
                    var theirs = actions.AsReadOnlySpan();
                    for (int i = 0; i < mine.Length; i++) worst = Mathf.Max(worst, Mathf.Abs(mine[i] - theirs[i]));
                    if (worker.PeekOutput("value") is Tensor<float> v)
                    {
                        v.CompleteAllPendingOperations();
                        worstValue = Mathf.Max(worstValue, Mathf.Abs(value - v.AsReadOnlySpan()[0]) / Mathf.Max(1f, Mathf.Abs(value)));
                    }
                }
                Assert.Less(worst, 1e-4f, $"{path}: actions differ by {worst}");
                Assert.Less(worstValue, 1e-4f, $"{path}: value differs by {worstValue} (relative)");
            }
            if (skipped.Count > 0) Debug.LogWarning("[MlpPolicyTests] on the Inference Engine still:\n" + string.Join("\n", skipped));
            Assert.Greater(direct, 0, "no brain could be run directly");
            Assert.AreEqual(0, skipped.Count, "brains the direct evaluator cannot run:\n" + string.Join("\n", skipped));
        }

        [Test]
        public void DirectIsFaster()
        {
            var brains = Brains();
            if (brains.Count == 0) Assert.Ignore("no brains found (a player build)");
            Model model = ModelLoader.Load(brains[0].asset);
            using MlpPolicy mlp = MlpPolicy.TryBuild(model, out string why);
            Assert.IsNotNull(mlp, why);
            using var worker = new Worker(model, BackendType.CPU);
            var obs = new float[mlp.InputSize];
            var act = new float[mlp.ActionSize];
            using var input = new Tensor<float>(new TensorShape(1, obs.Length), obs);
            for (int i = 0; i < 20; i++) { mlp.Run(obs, act); worker.Schedule(input); (worker.PeekOutput("actions") as Tensor<float>).CompleteAllPendingOperations(); }
            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < 200; i++) mlp.Run(obs, act);
            double direct = sw.Elapsed.TotalMilliseconds / 200;
            sw.Restart();
            for (int i = 0; i < 200; i++) { worker.Schedule(input); var o = worker.PeekOutput("actions") as Tensor<float>; o.CompleteAllPendingOperations(); o.AsReadOnlySpan(); }
            double engine = sw.Elapsed.TotalMilliseconds / 200;
            Debug.Log($"[MlpPolicyTests] {brains[0].path}: direct {direct:0.000} ms, Inference Engine {engine:0.000} ms a call");
            Assert.Less(direct, engine, $"direct {direct:0.000} ms is not faster than {engine:0.000} ms");
        }
    }
}
