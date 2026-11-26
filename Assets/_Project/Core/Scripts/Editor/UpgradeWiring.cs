using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using PoBox.Audio;
using PoBox.Fx;
using PoBox.Online;
using PoBox.Sim;

namespace PoBox.EditorTools
{
    /// <summary>
    /// Places the 2026-11-25 upgrades (impact spray, blood and fall decals, braided ropes and leather pads, the
    /// reactive crowd, modal impacts, chat votes, lip-sync) into the scenes and boxer prefabs, as authored
    /// objects the scene keeps. Idempotent: run it again and it changes nothing that is already in place.
    /// Menu <c>PoBox/Setup/Wire 2026-11 Upgrades</c>; it prints what it did to the console and to
    /// <c>Temp/pobox-wiring.txt</c>.
    /// </summary>
    public static class UpgradeWiring
    {
        const string Settings = "Assets/_Project/Data/Settings";
        const string Materials = "Assets/_Project/Environments/Materials";
        const string Boxers = "Assets/_Project/Agents/Boxers";
        static readonly string[] Scenes = { "Assets/_Project/Scenes/Arena.unity", "Assets/_Project/Scenes/Melee.unity" };
        const int BloodMarks = 8, FallMarks = 6;

        static StringBuilder _log;

        [MenuItem("PoBox/Setup/Wire 2026-11 Upgrades")]
        public static void WireAll()
        {
            _log = new StringBuilder();
            if (EditorApplication.isPlaying) { Note("stop play mode first"); Flush(); return; }

            var impact = Asset<ImpactModalSettings>("ImpactModalSettings");
            var crowd = Asset<CrowdExcitementSettings>("CrowdExcitementSettings");
            var chat = Asset<ChatVoteSettings>("ChatVoteSettings");

            string before = EditorSceneManager.GetActiveScene().path;
            foreach (string path in Scenes)
            {
                var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                Note($"== {path}");
                WireScene(impact, crowd, chat);
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }
            WirePrefabs();
            AssetDatabase.SaveAssets();
            if (!string.IsNullOrEmpty(before) && File.Exists(before)) EditorSceneManager.OpenScene(before, OpenSceneMode.Single);
            Flush();
        }

        static void WireScene(ImpactModalSettings impact, CrowdExcitementSettings crowdSettings, ChatVoteSettings chat)
        {
            // Impact spray on the object that already makes the hit sparks.
            var vfx = Object.FindAnyObjectByType<ImpactVfx>(FindObjectsInactive.Include);
            if (vfx != null)
            {
                var spray = vfx.GetComponent<HitSpray>();
                if (spray == null) { spray = Undo.AddComponent<HitSpray>(vfx.gameObject); Note("HitSpray added"); }
                var so = new SerializedObject(spray);
                Set(so, "dropMaterial", Mat("Fx_Sweat"));
                Set(so, "dustMaterial", Mat("Fx_Dust"));
                so.ApplyModifiedPropertiesWithoutUndo();
                if (so.FindProperty("spit").objectReferenceValue == null)
                {
                    typeof(HitSpray).GetMethod("CreateDefaultEmitters", BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(spray, null);
                    EditorUtility.SetDirty(spray);
                    Note("HitSpray emitters created");
                }
            }
            else Note("no ImpactVfx: HitSpray skipped");

            // Blood and fall decals beside the existing spot pool.
            var marks = Object.FindAnyObjectByType<CanvasMarks>(FindObjectsInactive.Include);
            if (marks != null)
            {
                DecalProjector template = marks.spots != null && marks.spots.Length > 0 ? marks.spots[0] : null;
                if (marks.blood == null || marks.blood.Length == 0) { marks.blood = Pool(marks.transform, "Blood", BloodMarks, Mat("M_Decal_Blood"), template); Note($"{BloodMarks} blood decals"); }
                if (marks.falls == null || marks.falls.Length == 0) { marks.falls = Pool(marks.transform, "Fall", FallMarks, Mat("Decal_Scuff"), template); Note($"{FallMarks} fall decals"); }
                EditorUtility.SetDirty(marks);
            }
            else Note("no CanvasMarks: decals skipped");

            // Braided ropes and leather pads in place of the flat materials.
            Swap("Rope", "M_Rope_Braided");
            Swap("Pad_Red", "M_Turnbuckle_Red");
            Swap("Pad_Blue", "M_Turnbuckle_Blue");
            Swap("Pad_White", "M_Turnbuckle_White");

            // Reactive crowd and modal impacts on the sound director.
            var audio = Object.FindAnyObjectByType<AudioDirector>(FindObjectsInactive.Include);
            if (audio != null)
            {
                var excite = audio.GetComponent<CrowdExcitement>();
                if (excite == null) { excite = Undo.AddComponent<CrowdExcitement>(audio.gameObject); Note("CrowdExcitement added"); }
                var so = new SerializedObject(excite);
                Set(so, "settings", crowdSettings);
                so.ApplyModifiedPropertiesWithoutUndo();
                audio.impactModes = impact;
                audio.crowdExcitement = excite;
                EditorUtility.SetDirty(audio);
            }
            else Note("no AudioDirector: crowd and impacts skipped");

            WireTowels(marks);
            WireDolly(marks);

            // Chat votes between rounds: only where rounds are run (the 1v1 bout).
            var bout = Object.FindAnyObjectByType<Bout>(FindObjectsInactive.Include);
            if (bout != null)
            {
                var corner = Object.FindAnyObjectByType<ChatCorner>(FindObjectsInactive.Include);
                if (corner == null)
                {
                    var go = new GameObject("Chat Corner");
                    Undo.RegisterCreatedObjectUndo(go, "Chat Corner");
                    corner = go.AddComponent<ChatCorner>();
                    Note("Chat Corner added");
                }
                var so = new SerializedObject(corner);
                Set(so, "bout", bout);
                Set(so, "hud", Object.FindAnyObjectByType<PoBox.UI.HudView>(FindObjectsInactive.Include));
                Set(so, "settings", chat);
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        // A towel over the top rope beside the red and the blue post, hanging outside the ring.
        const float TopRope = 1.40f, TowelOut = 0.09f, TowelFrom = 0.28f, TowelWidth = 0.42f;

        static void WireTowels(CanvasMarks marks)
        {
            if (GameObject.Find("Towels") != null) return;
            Vector3 c = marks != null ? marks.ringCentre : Vector3.zero;
            float h = marks != null ? marks.ringHalf : 3.05f;
            var holder = new GameObject("Towels");
            Undo.RegisterCreatedObjectUndo(holder, "Towels");
            // Red post at (-h, -h): the towel on the rope running +x, just outside it (-z).
            Towel(holder.transform, "Towel Red", c + new Vector3(-h + TowelFrom, TopRope, -h - TowelOut), Vector3.right);
            // Blue post at (+h, +h): the towel on the rope running -x, just outside it (+z).
            Towel(holder.transform, "Towel Blue", c + new Vector3(h - TowelFrom, TopRope, h + TowelOut), Vector3.left);
            Note("two corner towels");
        }

        static void Towel(Transform parent, string name, Vector3 start, Vector3 along)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>();
            go.AddComponent<MeshRenderer>().sharedMaterial = Mat("M_Cloth_Towel");
            var a = new GameObject("Pin A").transform;
            var b = new GameObject("Pin B").transform;
            a.SetParent(go.transform, false); b.SetParent(go.transform, false);
            a.position = start; b.position = start + along * TowelWidth;
            var cloth = go.AddComponent<VerletCloth>();
            var so = new SerializedObject(cloth);
            SerializedProperty pins = so.FindProperty("pins");
            pins.arraySize = 2;
            pins.GetArrayElementAtIndex(0).objectReferenceValue = a;
            pins.GetArrayElementAtIndex(1).objectReferenceValue = b;
            so.FindProperty("width").floatValue = TowelWidth;
            so.FindProperty("length").floatValue = 0.5f;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // The dolly: an arc of rail on the wide camera's side of the ring, for the intro before the first bell.
        const float RailRadius = 5.0f, RailHeight = 2.4f, RailHalfDegrees = 55f, AimHeight = 1.1f;

        static void WireDolly(CanvasMarks marks)
        {
            var director = Object.FindAnyObjectByType<PoBox.Broadcast.BroadcastDirector>(FindObjectsInactive.Include);
            if (director == null || director.dollyCam != null) return;
            Vector3 c = marks != null ? marks.ringCentre : Vector3.zero;
            Vector3 toWide = director.wideCam != null ? director.wideCam.transform.position - c : Vector3.back;
            toWide.y = 0f;
            float facing = Mathf.Atan2(toWide.x, toWide.z) * Mathf.Rad2Deg;

            var railGo = new GameObject("Dolly Rail");
            Undo.RegisterCreatedObjectUndo(railGo, "Dolly Rail");
            railGo.transform.position = c;
            var rail = railGo.AddComponent<UnityEngine.Splines.SplineContainer>();
            var spline = new UnityEngine.Splines.Spline();
            for (int i = 0; i <= 4; i++)
            {
                float deg = facing - RailHalfDegrees + i * (2f * RailHalfDegrees / 4f);
                float r = deg * Mathf.Deg2Rad;
                spline.Add(new UnityEngine.Splines.BezierKnot(new Unity.Mathematics.float3(Mathf.Sin(r) * RailRadius, RailHeight, Mathf.Cos(r) * RailRadius)), UnityEngine.Splines.TangentMode.AutoSmooth);
            }
            rail.Spline = spline;

            var aim = new GameObject("Dolly Aim").transform;
            aim.SetParent(railGo.transform, false);
            aim.localPosition = new Vector3(0f, AimHeight, 0f);

            var camGo = new GameObject("Dolly Cam");
            camGo.transform.SetParent(director.wideCam != null ? director.wideCam.transform.parent : null, false);
            var cam = camGo.AddComponent<Unity.Cinemachine.CinemachineCamera>();
            if (director.wideCam != null) cam.Lens = director.wideCam.Lens;
            cam.Priority = 10;
            cam.LookAt = aim;
            var dolly = camGo.AddComponent<Unity.Cinemachine.CinemachineSplineDolly>();
            dolly.Spline = rail;
            camGo.AddComponent<Unity.Cinemachine.CinemachineRotationComposer>();
            camGo.AddComponent<BroadcastDolly>();
            director.dollyCam = cam;
            EditorUtility.SetDirty(director);
            Note("dolly rail and camera");
        }

        static void WirePrefabs()
        {
            // The Fighter lives on the corner prefabs (NAME Red / NAME Blue, built by MjRetrofit), not on NAME.prefab.
            foreach (string dir in Directory.GetDirectories(Boxers))
            {
                string name = Path.GetFileName(dir);
                foreach (string side in new[] { "Red", "Blue" })
                {
                    string path = $"{dir}/{name} {side}.prefab".Replace('\\', '/');
                    if (!File.Exists(path)) continue;
                    GameObject root = PrefabUtility.LoadPrefabContents(path);
                    try
                    {
                        Fighter fighter = root.GetComponentInChildren<Fighter>(true);
                        if (fighter == null || fighter.GetComponent<LipSync>() != null) continue;
                        fighter.gameObject.AddComponent<LipSync>();
                        PrefabUtility.SaveAsPrefabAsset(root, path);
                        Note($"LipSync on {name} {side}");
                    }
                    finally { PrefabUtility.UnloadPrefabContents(root); }
                }
            }
        }

        static DecalProjector[] Pool(Transform parent, string label, int count, Material material, DecalProjector template)
        {
            var holder = parent.Find(label + " Marks");
            if (holder == null)
            {
                holder = new GameObject(label + " Marks").transform;
                holder.SetParent(parent, false);
            }
            var pool = new List<DecalProjector>();
            for (int i = 0; i < count; i++)
            {
                var go = new GameObject($"{label} {i}");
                go.transform.SetParent(holder, false);
                go.transform.localRotation = template != null ? template.transform.localRotation : Quaternion.Euler(90f, 0f, 0f);
                var d = go.AddComponent<DecalProjector>();
                d.material = material;
                if (template != null)
                {
                    d.size = template.size;
                    d.pivot = template.pivot;
                    d.fadeFactor = template.fadeFactor;
                    d.drawDistance = template.drawDistance;
                }
                d.enabled = false;
                pool.Add(d);
            }
            return pool.ToArray();
        }

        static void Swap(string from, string to)
        {
            Material a = Mat(from), b = Mat(to);
            if (a == null || b == null) { Note($"missing material {from} or {to}"); return; }
            int n = 0;
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include))
            {
                Material[] m = r.sharedMaterials;
                bool hit = false;
                for (int i = 0; i < m.Length; i++) if (m[i] == a) { m[i] = b; hit = true; }
                if (!hit) continue;
                Undo.RecordObject(r, "material");
                r.sharedMaterials = m;
                n++;
            }
            if (n > 0) Note($"{from} -> {to} on {n} renderers");
        }

        static T Asset<T>(string name) where T : ScriptableObject
        {
            string path = $"{Settings}/{name}.asset";
            var a = AssetDatabase.LoadAssetAtPath<T>(path);
            if (a != null) return a;
            a = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(a, path);
            Note($"created {path}");
            return a;
        }

        static Material Mat(string name) => AssetDatabase.LoadAssetAtPath<Material>($"{Materials}/{name}.mat");

        static void Set(SerializedObject so, string field, Object value)
        {
            SerializedProperty p = so.FindProperty(field);
            if (p != null) p.objectReferenceValue = value;
            else Note($"no field {field} on {so.targetObject.GetType().Name}");
        }

        static void Note(string line) => _log.AppendLine(line);

        static void Flush()
        {
            string text = _log.ToString();
            Debug.Log("[UpgradeWiring]\n" + text);
            File.WriteAllText("Temp/pobox-wiring.txt", text);
        }
    }
}
