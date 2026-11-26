using UnityEngine;

namespace PoBox.Fx
{
    /// <summary>
    /// A small piece of cloth without PhysX: a grid of a few dozen points stepped with Verlet integration,
    /// its top row pinned to transforms (a bone of a boxer for the flap of the shorts or the tail of a robe,
    /// two empties on a rope for the towel over the corner), held together by distance constraints, kept out
    /// of a few spheres (the thighs) and stirred by a little air. It is drawn as its own two-sided mesh.
    ///
    /// Chosen over a vertex-shader sway because it answers to what the bones do (a boxer turning swings the
    /// robe out, a knockdown throws it up) at a cost that does not show: 6 x 5 points, three constraint
    /// passes at 60 Hz, is a few hundred float operations a frame on the main thread, nothing on the GPU, and
    /// every buffer is made once in Awake.
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class VerletCloth : MonoBehaviour
    {
        [Tooltip("Points across and down. The top row is pinned.")]
        [SerializeField, Range(2, 12)] int columns = 6;
        [SerializeField, Range(2, 12)] int rows = 5;
        [Tooltip("Size of the cloth at rest, metres: across the pinned edge, and hanging down from it.")]
        [SerializeField] float width = 0.45f, length = 0.55f;
        [Tooltip("What the top row hangs from, left to right; the row is spread along them. One: the row is laid along its right axis, Width wide. None: this object's own transform.")]
        [SerializeField] Transform[] pins = new Transform[0];
        [Tooltip("Spheres the cloth is kept out of (thigh bones, a post), and their radius, metres.")]
        [SerializeField] Transform[] colliders = new Transform[0];
        [SerializeField] float colliderRadius = 0.09f;
        [Tooltip("Gravity, m/s^2, and how much of each step's speed is kept (1 = none lost).")]
        [SerializeField] float gravity = 9.81f;
        [SerializeField, Range(0.9f, 1f)] float keep = 0.985f;
        [Tooltip("Constraint passes a step. More is stiffer.")]
        [SerializeField, Range(1, 6)] int iterations = 3;
        [Tooltip("Air: a slow gust along this world direction, m/s^2, and how often it comes and goes, Hz.")]
        [SerializeField] Vector3 wind = new Vector3(0.4f, 0f, 0.25f);
        [SerializeField] float gustHz = 0.35f;
        [Tooltip("A pin that jumps further than this in a frame (a boxer placed in the corner) resets the cloth, metres.")]
        [SerializeField] float teleport = 1f;

        const float Step = 1f / 60f;
        Vector3[] _p, _prev, _vertices, _normals;
        float _dx, _dy, _owed;
        Vector3 _lastAnchor;
        Mesh _mesh;
        bool _started;

        int Count => columns * rows;

        void Awake()
        {
            int n = Count;
            _p = new Vector3[n];
            _prev = new Vector3[n];
            _vertices = new Vector3[n * 2];
            _normals = new Vector3[n * 2];
            _dx = width / (columns - 1);
            _dy = length / (rows - 1);
            BuildMesh();
        }

        void OnEnable() => _started = false;

        void BuildMesh()
        {
            int n = Count;
            var uvs = new Vector2[n * 2];
            var triangles = new int[(columns - 1) * (rows - 1) * 12];
            int t = 0;
            for (int y = 0; y < rows - 1; y++)
                for (int x = 0; x < columns - 1; x++)
                {
                    int a = y * columns + x, b = a + 1, c = a + columns, d = c + 1;
                    // Front, then the back (its own vertices, normals turned round) wound the other way.
                    triangles[t++] = a; triangles[t++] = b; triangles[t++] = c;
                    triangles[t++] = b; triangles[t++] = d; triangles[t++] = c;
                    triangles[t++] = n + a; triangles[t++] = n + c; triangles[t++] = n + b;
                    triangles[t++] = n + b; triangles[t++] = n + c; triangles[t++] = n + d;
                }
            for (int i = 0; i < n; i++)
            {
                var uv = new Vector2((i % columns) / (float)(columns - 1), 1f - (i / columns) / (float)(rows - 1));
                uvs[i] = uv;
                uvs[n + i] = uv;
            }
            _mesh = new Mesh { name = "VerletCloth" };
            _mesh.MarkDynamic();
            _mesh.vertices = _vertices;
            _mesh.normals = _normals;
            _mesh.uv = uvs;
            _mesh.triangles = triangles;
            GetComponent<MeshFilter>().sharedMesh = _mesh;
        }

        void OnDestroy()
        {
            if (_mesh != null) Destroy(_mesh);
        }

        /// <summary>Where the pinned point of a column is now, in the world.</summary>
        Vector3 Pin(int x)
        {
            float u = x / (float)(columns - 1);
            if (pins.Length >= 2)
            {
                float f = u * (pins.Length - 1);
                int i = Mathf.Min((int)f, pins.Length - 2);
                Transform a = pins[i], b = pins[i + 1];
                if (a != null && b != null) return Vector3.Lerp(a.position, b.position, f - i);
            }
            Transform root = pins.Length >= 1 && pins[0] != null ? pins[0] : transform;
            return root.position + root.right * ((u - 0.5f) * width);
        }

        Vector3 Anchor => Pin(0);

        void Settle()
        {
            // Hanging straight down from where the pins are.
            for (int y = 0; y < rows; y++)
                for (int x = 0; x < columns; x++)
                {
                    Vector3 at = Pin(x) + Vector3.down * (y * _dy);
                    _p[y * columns + x] = at;
                    _prev[y * columns + x] = at;
                }
            _lastAnchor = Anchor;
            _started = true;
        }

        void LateUpdate()
        {
            if (!_started || (Anchor - _lastAnchor).sqrMagnitude > teleport * teleport) Settle();
            _lastAnchor = Anchor;

            _owed = Mathf.Min(_owed + Time.deltaTime, Step * 3f);
            if (_owed < Step) return;
            for (; _owed >= Step; _owed -= Step) Simulate(Step);
            Draw();
        }

        void Simulate(float h)
        {
            float gust = 0.5f + 0.5f * Mathf.Sin(Time.time * gustHz * Mathf.PI * 2f) * Mathf.Sin(Time.time * gustHz * 2.3f + 1.1f);
            Vector3 accel = Vector3.down * gravity + wind * gust;
            int n = Count;
            for (int i = columns; i < n; i++)
            {
                Vector3 now = _p[i];
                // A little flutter down the free edge, so the gust is not one flat push.
                Vector3 a = accel * (1f + 0.3f * Mathf.Sin(Time.time * 5.1f + i * 1.7f) * (i / columns) / rows);
                _p[i] = now + (now - _prev[i]) * keep + a * (h * h);
                _prev[i] = now;
            }
            for (int x = 0; x < columns; x++) { _p[x] = Pin(x); _prev[x] = _p[x]; }

            for (int k = 0; k < iterations; k++)
            {
                for (int y = 0; y < rows; y++)
                    for (int x = 0; x < columns; x++)
                    {
                        int i = y * columns + x;
                        if (x < columns - 1) Hold(i, i + 1, _dx);
                        if (y < rows - 1) Hold(i, i + columns, _dy);
                    }
                Collide();
            }
        }

        /// <summary>Moves two points to their rest distance apart; a pinned point (the top row) does not move.</summary>
        void Hold(int a, int b, float rest)
        {
            Vector3 d = _p[b] - _p[a];
            float len = d.magnitude;
            if (len < 1e-6f) return;
            Vector3 fix = d * ((len - rest) / len);
            bool pa = a < columns, pb = b < columns;
            if (pa && pb) return;
            if (pa) _p[b] -= fix;
            else if (pb) _p[a] += fix;
            else { _p[a] += fix * 0.5f; _p[b] -= fix * 0.5f; }
        }

        void Collide()
        {
            for (int c = 0; c < colliders.Length; c++)
            {
                if (colliders[c] == null) continue;
                Vector3 centre = colliders[c].position;
                for (int i = columns; i < _p.Length; i++)
                {
                    Vector3 d = _p[i] - centre;
                    float sq = d.sqrMagnitude;
                    if (sq >= colliderRadius * colliderRadius || sq < 1e-8f) continue;
                    _p[i] = centre + d * (colliderRadius / Mathf.Sqrt(sq));
                }
            }
        }

        void Draw()
        {
            Matrix4x4 toLocal = transform.worldToLocalMatrix;
            int n = Count;
            for (int i = 0; i < n; i++) _vertices[i] = _vertices[n + i] = toLocal.MultiplyPoint3x4(_p[i]);
            for (int y = 0; y < rows; y++)
                for (int x = 0; x < columns; x++)
                {
                    int i = y * columns + x;
                    Vector3 across = _vertices[y * columns + Mathf.Min(x + 1, columns - 1)] - _vertices[y * columns + Mathf.Max(x - 1, 0)];
                    Vector3 down = _vertices[Mathf.Min(y + 1, rows - 1) * columns + x] - _vertices[Mathf.Max(y - 1, 0) * columns + x];
                    // Clockwise front faces (Unity's): across then down gives the front's normal.
                    Vector3 normal = Vector3.Cross(across, down);
                    normal = normal.sqrMagnitude > 1e-12f ? normal.normalized : Vector3.forward;
                    _normals[i] = normal;
                    _normals[n + i] = -normal;
                }
            _mesh.vertices = _vertices;
            _mesh.normals = _normals;
            _mesh.RecalculateBounds();
        }
    }
}
