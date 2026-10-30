using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using PoBox.Fx;
using PoBox.Sim;

namespace PoBox.EditorTools
{
    /// <summary>
    /// Puts the intent arrows (Fx/IntentArrows.cs) into the arena, once: one object a corner beside that corner's
    /// balance marks, with two line renderers drawn the way the marks are (their material, flat on the canvas), and
    /// saves the scene. Run again, it changes nothing. The objects are the scene's from then on, edited there.
    /// </summary>
    public static class SceneExtras
    {
        [MenuItem("PoBox/Scene/Add Intent Arrows To Arena")]
        public static void AddIntentArrows() => Debug.Log(AddIntentArrowsTo(Paths.ArenaScene));

        public static string AddIntentArrowsTo(string scenePath)
        {
            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            if (Object.FindObjectsByType<IntentArrows>(FindObjectsInactive.Include).Length > 0) return "intent arrows: already there";
            BalanceMarks[] marks = Object.FindObjectsByType<BalanceMarks>(FindObjectsInactive.Include);
            if (marks.Length == 0) return "intent arrows: no balance marks in the scene to set them beside";
            int made = 0;
            foreach (BalanceMarks m in marks)
            {
                var go = new GameObject(m.corner == 1 ? "Intent Blue" : "Intent Red");
                Undo.RegisterCreatedObjectUndo(go, "Add intent arrows");
                go.transform.SetParent(m.transform.parent, false);
                var arrows = go.AddComponent<IntentArrows>();
                arrows.bout = m.bout;
                arrows.corner = m.corner;
                arrows.colour = m.corner == 1 ? new Color(0.45f, 0.68f, 1f) : new Color(1f, 0.5f, 0.45f);
                arrows.step = Line(go.transform, "Step", m.patch, 0.035f, flat: true);
                arrows.punch = Line(go.transform, "Punch", m.patch, 0.02f, flat: false);
                made++;
            }
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            return $"intent arrows: {made} added to {scenePath}";
        }

        static LineRenderer Line(Transform parent, string name, LineRenderer like, float width, bool flat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.loop = false;
            line.positionCount = 0;
            line.widthMultiplier = width;
            line.numCapVertices = 4;
            line.numCornerVertices = 2;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            if (like != null) line.sharedMaterial = like.sharedMaterial;
            // Flat on the canvas, as the balance marks lie; the punch line faces the camera.
            if (flat)
            {
                line.alignment = LineAlignment.TransformZ;
                go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            }
            line.enabled = false;
            return line;
        }
    }
}
