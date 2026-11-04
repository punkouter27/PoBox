using System.Collections.Generic;
using UnityEngine;

namespace PoBox.Fx
{
    /// <summary>
    /// The skeleton of one of the ring's people who are not boxers (the referee, the cornermen, the card walker:
    /// Assets/Art/Exports/GLB/SK_*.glb, built by BLENDER/2026-11-03_figures/build_figures.py). They are scenery, posed
    /// by code a frame at a time: each bone is turned from its rest pose about one of its own axes. In the rigs every
    /// bone's +Y runs along it; an arm is raised out to the side about Z (+ on the right, - on the left), a thigh swung
    /// forward about +X and a shin bent back about -X. The rest pose is an A-pose, the arms 45 degrees down.
    /// </summary>
    public sealed class FigureRig
    {
        readonly Dictionary<string, Transform> _bone = new Dictionary<string, Transform>();
        readonly Dictionary<Transform, Quaternion> _rest = new Dictionary<Transform, Quaternion>();
        float _stride;

        public FigureRig(Transform root)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (_bone.ContainsKey(t.name)) continue;
                _bone[t.name] = t;
                _rest[t] = t.localRotation;
            }
        }

        public Transform Bone(string name) => _bone.TryGetValue(name, out Transform t) ? t : null;

        /// <summary>Everything back in its rest pose.</summary>
        public void Rest()
        {
            foreach (var kv in _rest) kv.Key.localRotation = kv.Value;
        }

        /// <summary>A bone turned from its rest pose by this many degrees about one of its own axes.</summary>
        public void Turn(string name, Vector3 axis, float degrees)
        {
            Transform t = Bone(name);
            if (t != null) t.localRotation = _rest[t] * Quaternion.AngleAxis(degrees, axis);
        }

        /// <summary>
        /// The legs of a walk at this speed (m/s), kept going from frame to frame: the thighs swing against each other
        /// and the shin bends as its leg comes through. Still, they settle back to standing.
        /// </summary>
        public void Walk(float speed, float dt)
        {
            float swing = Mathf.Clamp01(speed / 1.2f);
            _stride += dt * Mathf.Lerp(0f, 9f, swing);
            float s = Mathf.Sin(_stride);
            Turn("Thigh.L", Vector3.right, 24f * swing * s);
            Turn("Thigh.R", Vector3.right, -24f * swing * s);
            Turn("Shin.L", Vector3.right, -30f * swing * Mathf.Max(0f, -s));
            Turn("Shin.R", Vector3.right, -30f * swing * Mathf.Max(0f, s));
        }

        /// <summary>Arms hanging at the sides (from the A-pose, 35 degrees further down), a little bent.</summary>
        public void ArmsDown()
        {
            Turn("UpperArm.L", Vector3.forward, 35f);
            Turn("UpperArm.R", Vector3.forward, -35f);
            Turn("ForeArm.L", Vector3.forward, -10f);
            Turn("ForeArm.R", Vector3.forward, 10f);
        }

        /// <summary>The body moved towards a point on the floor at up to this speed and turned to face a way; the speed it moved at.</summary>
        public static float Approach(Transform body, Vector3 target, Vector3 facing, float speed, float dt)
        {
            Vector3 from = body.position;
            target.y = from.y;
            Vector3 next = Vector3.MoveTowards(from, target, speed * dt);
            body.position = next;
            facing.y = 0f;
            if (facing.sqrMagnitude > 1e-4f)
                body.rotation = Quaternion.RotateTowards(body.rotation, Quaternion.LookRotation(facing), 240f * dt);
            return dt > 0f ? (next - from).magnitude / dt : 0f;
        }
    }
}
