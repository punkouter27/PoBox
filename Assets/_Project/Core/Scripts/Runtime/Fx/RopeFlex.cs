using System;
using UnityEngine;
using UnityEngine.Splines;
using PoBox.Mj;
using PoBox.Sim;

namespace PoBox.Fx
{
    /// <summary>
    /// The ropes, drawn as ropes: every one a tube that bows out where a body is against it, and shivers
    /// when it is let go.
    ///
    /// The simulation's ropes are straight and do not move: in MuJoCo each is a fixed capsule with a soft
    /// contact, which lets a body leaning on it sink in by a few centimetres, and more when it arrives at
    /// speed. That sinking in is the rope giving, and this draws it. Each rope is a row of points between
    /// its two posts. Wherever MuJoCo has a boxer pressed into the ropes (<see cref="MjRing.RopeLoad"/>, every
    /// boxer in the ring), the ropes at that height are carried out round the place, further the harder it
    /// presses, so they wrap round whatever is leaning on them. The points are
    /// joined to their neighbours by tension and to their rest places by a spring, so the bow spreads along
    /// the rope, and when the body comes away the rope swings back through its line and settles. A body
    /// landing on the canvas gives every rope a kick.
    ///
    /// All sixteen ropes are one mesh and one draw call. A rope nobody is near and that has stopped moving
    /// costs nothing. <see cref="Strain"/> and <see cref="StrainAt"/> are how far out the most bowed rope is
    /// and where: the creak in <see cref="Audio.AudioDirector"/> follows them.
    ///
    /// Where a rope rests is its line between the posts with a little sag (<see cref="sag"/>, metres at the
    /// middle), or, given <see cref="restShape"/>, the spline of the same index in that container (Unity
    /// Splines: knots placed in the scene, so the droop and any kink at a post are authored there). The tube
    /// has UVs (round the rope, and along it in rope widths) and tangents, so a braided rope material with a
    /// normal map (M_Rope_Braided) lies on it.
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class RopeFlex : MonoBehaviour
    {
        [Serializable]
        public struct Rope
        {
            public Vector3 a, b;        // the two ends, at the posts
            public Vector3 outward;     // unit, horizontal, away from the middle of the ring
        }

        public Rope[] ropes = new Rope[0];
        [Tooltip("Points along each rope, ends included.")]
        public int points = 15;
        public float radius = 0.022f;
        [Tooltip("How hard a rope pulls back to its line, and how hard each point pulls on its neighbours (1/s^2).")]
        public float stiffness = 260f, tension = 900f;
        [Tooltip("How quickly the swing dies down (1/s).")]
        public float damping = 5f;
        [Tooltip("Rope load (MjRing.RopeLoad, N) that bows the ropes furthest, and how far that is, metres.")]
        public float fullLoad = 1500f, maxBow = 0.12f;
        [Tooltip("Load under which nothing shows, N.")]
        public float minLoad = 25f;
        [Tooltip("How far along a rope, and above or below it, a body pressing on the ropes reaches, metres.")]
        public float reach = 0.3f, band = 0.32f;
        [Header("Rest shape")]
        [Tooltip("How far the middle of a rope hangs below its line between the posts, metres. Not used for a rope that has a spline.")]
        [SerializeField] float sag = 0.015f;
        [Tooltip("Optional: one spline per rope, same order as Ropes, giving where each rope rests (world space). Ropes past its count sag.")]
        [SerializeField] SplineContainer restShape;
        [Tooltip("How many times the braid repeats per half-circumference of rope length. 1 keeps the texels square.")]
        [SerializeField] float uvAlong = 1f;

        const int Sides = 12;        // round in a close-up (6 read as hexagons, 2026-10-31)
        const int Ring = Sides + 1;  // one more vertex a ring: the seam, where the texture wraps
        Mesh _mesh;
        Vector3[] _vertices, _normals, _rest;
        float[] _x, _v, _push;         // per point: how far out it is, how fast, how far out a body holds it
        bool[] _awake;
        bool _dirty = true;
        MjRing _ring;
        readonly MjBoxer[] _pair = new MjBoxer[2];

        /// <summary>How far out the most bowed rope is now, metres, and where that is.</summary>
        public float Strain { get; private set; }
        public Vector3 StrainAt { get; private set; }

        void Awake()
        {
            int n = ropes.Length * points;
            _x = new float[n]; _v = new float[n]; _push = new float[n];
            _awake = new bool[ropes.Length];
            _rest = new Vector3[n];
            Rest();
            Build();
        }

        void OnEnable()
        {
            SimBus.FloorImpact += OnFloor;
        }

        void OnDisable()
        {
            SimBus.FloorImpact -= OnFloor;
        }

        void OnFloor(Vector3 point, float impulse)
        {
            // The canvas is tied to the same posts: a body landing on it shakes every rope a little.
            float kick = Mathf.Clamp01(impulse / 120f) * 0.9f;
            for (int r = 0; r < ropes.Length; r++)
            {
                for (int i = 1; i < points - 1; i++)
                    _v[r * points + i] += kick * Mathf.Sin(Mathf.PI * i / (points - 1)) * (r % 2 == 0 ? 1f : -1f);
                _awake[r] = true;
            }
        }

        /// <summary>Where every point of every rope rests: its spline, or its line with the sag.</summary>
        void Rest()
        {
            int splines = restShape != null ? restShape.Splines.Count : 0;
            for (int r = 0; r < ropes.Length; r++)
                for (int i = 0; i < points; i++)
                {
                    float u = i / (float)(points - 1);
                    Vector3 at;
                    if (r < splines) at = transform.InverseTransformPoint(restShape.EvaluatePosition(r, u));
                    else at = Vector3.Lerp(ropes[r].a, ropes[r].b, u) + Vector3.down * (sag * 4f * u * (1f - u));
                    _rest[r * points + i] = at;
                }
        }

        void Build()
        {
            int rings = ropes.Length * points;
            _vertices = new Vector3[rings * Ring];
            _normals = new Vector3[rings * Ring];
            var uvs = new Vector2[rings * Ring];
            var tangents = new Vector4[rings * Ring];
            var triangles = new int[ropes.Length * (points - 1) * Sides * 6];
            int t = 0;
            for (int r = 0; r < ropes.Length; r++)
                for (int i = 0; i < points - 1; i++)
                    for (int s = 0; s < Sides; s++)
                    {
                        int a = (r * points + i) * Ring + s, b = a + 1;
                        int c = a + Ring, d = b + Ring;
                        triangles[t++] = a; triangles[t++] = c; triangles[t++] = b;
                        triangles[t++] = b; triangles[t++] = c; triangles[t++] = d;
                    }
            // UVs and tangents are set once: u round the rope, v along it in half-circumferences (the braid
            // texture is twice as wide as it is tall), the tangent along the line between the posts.
            float half = Mathf.PI * Mathf.Max(1e-4f, radius);
            for (int r = 0; r < ropes.Length; r++)
            {
                Vector3 line = ropes[r].b - ropes[r].a;
                Vector3 along = line.normalized;
                float length = line.magnitude;
                for (int i = 0; i < points; i++)
                    for (int s = 0; s < Ring; s++)
                    {
                        int k = (r * points + i) * Ring + s;
                        uvs[k] = new Vector2(s / (float)Sides, length * i / (points - 1) / half * uvAlong);
                        tangents[k] = new Vector4(along.x, along.y, along.z, 1f);
                    }
            }
            _mesh = new Mesh { name = "Ropes" };
            _mesh.MarkDynamic();
            Shape();
            _mesh.vertices = _vertices;
            _mesh.normals = _normals;
            _mesh.uv = uvs;
            _mesh.tangents = tangents;
            _mesh.triangles = triangles;
            // Big enough for any bow a rope can take: the bounds are never recomputed.
            var bounds = new Bounds(ropes.Length > 0 ? ropes[0].a : Vector3.zero, Vector3.zero);
            foreach (Rope rope in ropes) { bounds.Encapsulate(rope.a); bounds.Encapsulate(rope.b); }
            bounds.Expand(2f);
            _mesh.bounds = bounds;
            GetComponent<MeshFilter>().sharedMesh = _mesh;
        }

        /// <summary>The tube's vertices from where the points are now.</summary>
        void Shape()
        {
            for (int r = 0; r < ropes.Length; r++)
            {
                Rope rope = ropes[r];
                Vector3 up = Vector3.up, side = rope.outward;
                for (int i = 0; i < points; i++)
                {
                    int p = r * points + i;
                    Vector3 centre = _rest[p] + rope.outward * _x[p];
                    for (int s = 0; s < Ring; s++)
                    {
                        float a = s * Mathf.PI * 2f / Sides;
                        Vector3 n = side * Mathf.Cos(a) + up * Mathf.Sin(a);
                        _vertices[p * Ring + s] = centre + n * radius;
                        _normals[p * Ring + s] = n;
                    }
                }
            }
        }

        void LateUpdate()
        {
            if (ropes.Length == 0) return;
            if (_ring == null) _ring = FindAnyObjectByType<MjRing>();

            float dt = Mathf.Min(Time.deltaTime, 1f / 30f);
            bool moved = _dirty;
            _dirty = false;
            if (dt > 0f) moved |= Step(dt);
            if (!moved) return;
            Shape();
            _mesh.vertices = _vertices;
            _mesh.normals = _normals;
        }

        bool Step(float dt)
        {
            bool any = false;
            for (int i = 0; i < _push.Length; i++) _push[i] = 0f;
            float strain = 0f;
            // Every boxer the ring knows: the pair of a bout, or the eight of a free-for-all.
            MjBoxer[] boxers = null;
            if (_ring != null && _ring.Ready)
            {
                boxers = _ring.boxers;
                if (boxers == null || boxers.Length == 0) { _pair[0] = _ring.red; _pair[1] = _ring.blue; boxers = _pair; }
            }
            for (int r = 0; r < ropes.Length; r++)
            {
                Rope rope = ropes[r];
                Vector3 line = rope.b - rope.a;
                float length = line.magnitude;
                Vector3 along = line / Mathf.Max(1e-4f, length);
                int first = r * points;
                bool touched = false;

                // How far out each boxer against the ropes carries this one, at each point it covers.
                for (int k = 0; boxers != null && k < boxers.Length; k++)
                {
                    if (boxers[k] == null) continue;
                    float load = _ring.RopeLoad(boxers[k], out Vector3 at);
                    if (load < minLoad) continue;
                    Vector3 rel = at - rope.a;
                    float dy = rel.y, alongAt = Vector3.Dot(rel, along);
                    if (Mathf.Abs(dy) >= band || alongAt < -reach || alongAt > length + reach) continue;
                    if (Mathf.Abs(Vector3.Dot(rel, rope.outward)) > band) continue;      // another side of the ring
                    touched = true;
                    float depth = maxBow * Mathf.Sqrt(Mathf.Clamp01(load / fullLoad)) * (1f - (dy * dy) / (band * band));
                    for (int i = 1; i < points - 1; i++)
                    {
                        float d = alongAt - length * i / (points - 1);
                        if (Mathf.Abs(d) > reach) continue;
                        // Round, not flat: furthest out at the middle of what is pressing.
                        float push = depth * Mathf.Sqrt(1f - (d * d) / (reach * reach));
                        if (push > _push[first + i]) _push[first + i] = push;
                    }
                }

                if (!touched && !_awake[r]) continue;
                // Two half-steps: the tension between neighbours is stiff.
                float h = dt * 0.5f, energy = 0f;
                for (int sub = 0; sub < 2; sub++)
                    for (int i = 1; i < points - 1; i++)
                    {
                        int p = first + i;
                        float pull = -stiffness * _x[p] + tension * (_x[p - 1] + _x[p + 1] - 2f * _x[p]) - damping * _v[p];
                        _v[p] += pull * h;
                        _x[p] += _v[p] * h;
                        // Held out by whatever is against it.
                        if (_x[p] < _push[p]) { _x[p] = _push[p]; if (_v[p] < 0f) _v[p] = 0f; }
                        energy += Mathf.Abs(_x[p]) + Mathf.Abs(_v[p]) * 0.05f;
                        if (sub == 1 && Mathf.Abs(_x[p]) > strain)
                        {
                            strain = Mathf.Abs(_x[p]);
                            StrainAt = _rest[p] + rope.outward * _x[p];
                        }
                    }
                _awake[r] = touched || energy > 0.002f;
                if (!_awake[r])
                    for (int i = 0; i < points; i++) { _x[first + i] = 0f; _v[first + i] = 0f; }
                any = true;
            }
            Strain = strain;
            return any;
        }

        /// <summary>How far out the middle of a rope is, metres. For tests and the debug readout.</summary>
        public float MaxBow
        {
            get
            {
                float most = 0f;
                for (int i = 0; i < _x.Length; i++) if (Mathf.Abs(_x[i]) > most) most = Mathf.Abs(_x[i]);
                return most;
            }
        }
    }
}
