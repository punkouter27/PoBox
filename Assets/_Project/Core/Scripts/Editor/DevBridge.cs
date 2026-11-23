using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PoBox.EditorTools
{
    /// <summary>
    /// DevTools' commands from outside the editor without the Unity CLI (2026-11-23: the CLI is not installed on this PC).
    /// Append lines to <c>Temp/pobox-bridge.txt</c>; they run one at a time on the editor's update loop, each line's
    /// output appended to <c>Temp/pobox-bridge-out.txt</c>. Lines the bridge does not know go to <see cref="DevTools"/>
    /// (play, stop, skip, step, shot-begin, shot-end, state, perf, call ...). The bridge's own:
    ///
    ///     wait S                    let S seconds of editor time pass before the next line
    ///     until-playing             hold until play mode is on (and the first frames are in)
    ///     open PATH                 open a scene in edit mode (unsaved changes are discarded, never saved)
    ///     roots                     the open scene's root objects
    ///     select NAME               select a GameObject (a hierarchy path or a name) and frame it in the Scene view
    ///     tool move                 the Move tool, so the selection shows its gizmo
    ///     nudge NAME DX DY DZ       move it by that much in world space, as a drag of the gizmo would (undoable)
    ///     where NAME                its world position
    ///     undo                      Edit > Undo
    ///     scene-view | game-view    bring that window to the front
    ///     done                      writes the line "done" (a marker to wait for)
    ///
    /// Nothing here saves a scene.
    /// </summary>
    [InitializeOnLoad]
    public static class DevBridge
    {
        const string InPath = "Temp/pobox-bridge.txt";
        const string OutPath = "Temp/pobox-bridge-out.txt";
        static int s_done;
        static DateTime s_made;
        static double s_waitUntil;
        static bool s_untilPlaying;
        static double s_next;

        static DevBridge()
        {
            EditorApplication.update += Tick;
        }

        static void Tick()
        {
            double now = EditorApplication.timeSinceStartup;
            if (now < s_next || now < s_waitUntil) return;
            s_next = now + 0.25;
            if (s_untilPlaying)
            {
                if (!EditorApplication.isPlaying || Time.frameCount < 30) return;
                s_untilPlaying = false;
                Out("playing, frame " + Time.frameCount);
            }
            if (!File.Exists(InPath)) { s_done = 0; return; }
            string[] lines;
            try { lines = File.ReadAllLines(InPath); }
            catch (IOException) { return; }
            // A new file (written again, not appended to) starts again from its first line.
            DateTime made = File.GetCreationTimeUtc(InPath);
            if (made != s_made) { s_made = made; s_done = 0; }
            if (lines.Length < s_done) s_done = 0;
            if (s_done >= lines.Length) return;
            string line = lines[s_done++].Trim();
            if (line.Length == 0 || line.StartsWith("#")) return;
            string result;
            try { result = Run(line); }
            catch (Exception e) { result = "ERROR " + e.GetBaseException().Message; }
            Out($"> {line}\n{result}");
        }

        static string Run(string line)
        {
            string[] a = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            switch (a[0])
            {
                case "wait":
                    s_waitUntil = EditorApplication.timeSinceStartup + double.Parse(a[1], System.Globalization.CultureInfo.InvariantCulture);
                    return "waiting " + a[1] + " s";
                case "until-playing": s_untilPlaying = true; return "waiting for play mode";
                case "open":
                    if (EditorApplication.isPlaying) return "stop play first";
                    EditorSceneManager.OpenScene(a[1], OpenSceneMode.Single);
                    return "opened " + a[1];
                case "roots":
                    return string.Join(", ", EditorSceneManager.GetActiveScene().GetRootGameObjects().Select(g => g.name));
                case "select":
                {
                    var go = Find(Rest(a, 1));
                    if (go == null) return "not found";
                    Selection.activeGameObject = go;
                    EditorGUIUtility.PingObject(go);
                    if (SceneView.lastActiveSceneView != null) SceneView.lastActiveSceneView.FrameSelected();
                    return "selected " + PathOf(go.transform);
                }
                case "tool":
                    Tools.current = a[1] == "rotate" ? Tool.Rotate : a[1] == "scale" ? Tool.Scale : Tool.Move;
                    return "tool " + Tools.current;
                case "nudge":
                {
                    var inv = System.Globalization.CultureInfo.InvariantCulture;
                    int n = a.Length;
                    var d = new Vector3(float.Parse(a[n - 3], inv), float.Parse(a[n - 2], inv), float.Parse(a[n - 1], inv));
                    var go = Find(string.Join(" ", a.Skip(1).Take(n - 4)));
                    if (go == null) return "not found";
                    Undo.RecordObject(go.transform, "Move " + go.name);
                    Vector3 from = go.transform.position;
                    go.transform.position = from + d;
                    SceneView.RepaintAll();
                    return $"{go.name}: {from} -> {go.transform.position}";
                }
                case "where":
                {
                    var go = Find(Rest(a, 1));
                    return go == null ? "not found" : $"{PathOf(go.transform)} at {go.transform.position}";
                }
                case "undo": Undo.PerformUndo(); return "undone";
                case "scene-view": EditorWindow.FocusWindowIfItsOpen<SceneView>(); return "scene view";
                case "game-view":
                    EditorApplication.ExecuteMenuItem("Window/General/Game");
                    return "game view";
                case "done": return "done";
                default:
                    File.WriteAllText("Temp/pobox-dev.txt", line + "\n");
                    DevTools.RunScript();
                    return "(DevTools: see Temp/pobox-dev-out.txt)";
            }
        }

        static string Rest(string[] a, int from) => string.Join(" ", a.Skip(from));

        static GameObject Find(string name)
        {
            var go = GameObject.Find(name);
            if (go != null) return go;
            var all = new List<GameObject>();
            foreach (var root in EditorSceneManager.GetActiveScene().GetRootGameObjects())
                all.AddRange(root.GetComponentsInChildren<Transform>(true).Select(t => t.gameObject));
            return all.FirstOrDefault(g => g.name == name);
        }

        static string PathOf(Transform t) => t.parent == null ? t.name : PathOf(t.parent) + "/" + t.name;

        static void Out(string text)
        {
            Directory.CreateDirectory("Temp");
            File.AppendAllText(OutPath, text + "\n", Encoding.UTF8);
        }
    }
}
