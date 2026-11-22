using System.Collections.Generic;
using UnityEngine;
using PoBox.Fx;
using PoBox.Mj;

namespace PoBox.Sim
{
    /// <summary>
    /// Every boxer in the ring at once (the Melee scene), boxed like a bout: three rounds with the bell, a count over
    /// a boxer knocked down, the judges if more than one is left at the end. It takes the place of <see cref="Bout"/>
    /// and <see cref="MatchSetup"/>, which are for two.
    ///
    ///   * The scene holds one corner prefab of each boxer, switched off. Before MuJoCo compiles, each is renamed
    ///     to a prefix of its own (a_, b_, c_, ...: the plugin names MuJoCo's objects after the GameObjects, and the
    ///     prefabs all say a_), given a colour of its own, switched on and handed to the ring. Every body is solid,
    ///     down or not: boxers on the canvas are tripped over and piled on.
    ///   * Each round starts with everybody still in at the ropes, in their guard, facing in. At the bell they walk
    ///     out towards the middle on the footwork policy (the one taught to walk on command; a boxing policy has
    ///     never seen an opponent five metres off), and box once another boxer is a step and a half away. A brain
    ///     (which sees one opponent) is kept facing the nearest boxer on its feet, turns on whoever has just hit
    ///     it, and leaves alone one that two are already on.
    ///   * Going down in the fight, punched or not (a slip is a knockdown too), starts the boxer's own count, a number
    ///     every <see cref="countInterval"/> seconds, while the rest fight on; the count stands still once it is back on
    ///     its feet; up before ten, it fights on; counted out, it stays where it lies. A fall before the bell is not
    ///     counted; a boxer who cannot get up from one in slipSeconds cannot go on.
    ///   * The round runs on through a count: the bell does not save anyone. Between rounds everybody still in
    ///     gets a minute on the stool (Fighter.Respawn) and goes back to the ropes.
    ///   * The last boxer standing wins. If more than one is in after the last round, the judges give it to the one
    ///     who did the most damage, a knockdown worth a clean round (<see cref="Bout.Score"/>).
    ///   * Sixteen (2026-11-16): the eight boxers and a twin of each ("MATT II", the boxer's Blue prefab), until there
    ///     are sixteen boxers of their own. At most <see cref="maxInRing"/> are in the ring at once (sixteen in a
    ///     six-metre ring was one pile-up, fourteen counted out inside half a round); the rest wait off stage, held
    ///     still, hidden and with their brains not run, and tag in, in the scene's order: one whenever a boxer is
    ///     counted out, and one every <see cref="entryEvery"/> seconds while there are fewer than <see cref="entryCap"/>
    ///     in, so all of them are in before the last bell. The counted out are cleared off the canvas at the break.
    ///     The last boxer standing wins only once nobody is left waiting. The placings (winner, the rest still in by the
    ///     judges' score, then the eliminated, last out first) move the ladder's ratings
    ///     (<see cref="PoBox.League.LeagueTable.ReportPlacings"/>); a twin is rated as its boxer, by its better placing.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public class FreeForAll : MonoBehaviour
    {
        public enum Stage { Waiting, Intro, Fight, Break, Over }

        [Tooltip("One corner prefab of each boxer, switched off.")]
        public Fighter[] fighters = new Fighter[0];
        public MjRing ring;
        [Header("Rules: the 1-on-1's")]
        public int rounds = 3;
        public float roundSeconds = 45f;
        public float introSeconds = 2.5f;
        [Tooltip("Real seconds between rounds.")]
        public float breakSeconds = 3f;
        [Tooltip("Seconds per number of a count.")]
        public float countInterval = 1f;
        [Tooltip("Seconds a boxer down with no count (it fell before the bell) may stay down before it is ruled unable to go on.")]
        public float slipSeconds = 15f;
        [Tooltip("Real seconds after the final bell before the ring stops: the last punch and fall finish.")]
        public float settleSeconds = 1.5f;
        [Header("Who fights whom")]
        [Tooltip("Metres inside the ropes the boxers start from.")]
        public float startInside = 0.5f;
        [Tooltip("A new opponent has to be this much better than the present one, metres, to be turned to.")]
        public float switchMargin = 0.4f;
        [Tooltip("Metres an opponent counts as nearer for having just hit this boxer.")]
        public float grudge = 1.0f;
        [Tooltip("Metres an opponent counts as further for every boxer on it beyond the first: one other is fair, two is a pile-on.")]
        public float crowding = 1.0f;
        [Tooltip("Metres an opponent counts as nearer when it is dazed (scaled by how dazed): the opportunist's pick.")]
        public float opportunism = 0.5f;
        [Tooltip("Walking out at the bell, m/s, on the footwork policy (taught 0.3 to 1.0).")]
        public float walkOutSpeed = 0.6f;
        [Tooltip("The walk ends and the boxing starts this near another boxer on its feet, metres (a step and a half: how a training episode starts), or this near the middle.")]
        public float engageAt = 1.6f, middleAt = 0.8f;
        [Tooltip("Seconds a walk out lasts at most.")]
        public float walkSeconds = 6f;
        public Color[] colours =
        {
            new Color(0.89f, 0.24f, 0.24f), new Color(0.20f, 0.49f, 0.94f), new Color(0.98f, 0.76f, 0.18f), new Color(0.22f, 0.78f, 0.45f),
            new Color(0.66f, 0.36f, 0.92f), new Color(0.98f, 0.52f, 0.16f), new Color(0.20f, 0.80f, 0.86f), new Color(0.95f, 0.45f, 0.72f),
            // The twins' (fighters 8-15): hues the first eight do not use, so a twin never looks like its boxer.
            new Color(0.62f, 0.90f, 0.20f), new Color(0.95f, 0.95f, 0.95f), new Color(0.55f, 0.35f, 0.20f), new Color(0.15f, 0.30f, 0.75f),
            new Color(1.00f, 0.60f, 0.60f), new Color(0.45f, 0.95f, 0.80f), new Color(0.80f, 0.20f, 0.55f), new Color(0.98f, 0.92f, 0.45f),
        };
        [Header("Sixteen: who is in the ring")]
        [Tooltip("At most this many in the ring at once; the rest wait off stage and tag in. 0 = everybody at once.")]
        public int maxInRing = 8;
        [Tooltip("Seconds of fighting between entries while fewer than entryCap are in (a boxer counted out brings the next in at once).")]
        public float entryEvery = 12f;
        [Tooltip("No timed entry while this many are in the ring.")]
        public int entryCap = 10;
        [Tooltip("Seconds after a boxer is counted out before the next one comes in.")]
        public float entryDelay = 1.5f;
        [Tooltip("Where those waiting are parked, metres from the ring's middle along x: out of every shot.")]
        public float parkDistance = 60f;
        [Tooltip("Move the ladder's ratings by the placings at the end (League/LeagueTable.ReportPlacings).")]
        public bool rated = true;
        [Tooltip("The Elo K a free-for-all is worth in all, shared out over the pairs it decides.")]
        public float ratingK = 32f;

        /// <summary>What a twin's name carries after its boxer's ("MATT II").</summary>
        public const string TwinSuffix = " II";
        /// <summary>The boxer a name belongs to: a twin's name without its suffix.</summary>
        public static string BaseName(string displayName) =>
            displayName != null && displayName.EndsWith(TwinSuffix) ? displayName.Substring(0, displayName.Length - TwinSuffix.Length) : displayName;

        public static FreeForAll Instance { get; private set; }
        public Stage Phase { get; private set; } = Stage.Waiting;
        public int Round { get; private set; } = 1;
        public float RoundTimeLeft { get; private set; }
        public Fighter Winner { get; private set; }
        public string Method { get; private set; } = "";
        public float PhaseAge { get; private set; }

        readonly Dictionary<Fighter, float> _downFor = new Dictionary<Fighter, float>();
        readonly HashSet<Fighter> _out = new HashSet<Fighter>(), _counted = new HashSet<Fighter>();
        readonly Dictionary<Fighter, MjBrain> _brain = new Dictionary<Fighter, MjBrain>();
        readonly Dictionary<Fighter, int> _on = new Dictionary<Fighter, int>();
        readonly List<Fighter> _outOrder = new List<Fighter>();
        readonly List<Fighter> _waiting = new List<Fighter>();
        readonly HashSet<Fighter> _parked = new HashSet<Fighter>(), _entered = new HashSet<Fighter>();
        readonly Dictionary<Renderer, bool> _shown = new Dictionary<Renderer, bool>();
        float _sinceEntry, _lastOutAt = -99f;
        /// <summary>The placings of the last finished free-for-all, best first; empty while one runs.</summary>
        public readonly List<Fighter> Placings = new List<Fighter>();
        float _roundAge;

        public bool IsOut(Fighter f) => _out.Contains(f);
        /// <summary>Off stage, waiting to tag in.</summary>
        public bool IsWaiting(Fighter f) => _waiting.Contains(f);
        /// <summary>In the ring and still in: not out, not waiting.</summary>
        public bool InRing(Fighter f) => f != null && !_out.Contains(f) && !_waiting.Contains(f);
        /// <summary>Boxers still waiting to tag in.</summary>
        public int WaitingCount => _waiting.Count;
        int InRingCount { get { int n = 0; foreach (Fighter f in fighters) if (InRing(f)) n++; return n; } }
        /// <summary>The count over a boxer knocked down (0 to 10); -1 when it is standing, slipped, or already out.</summary>
        public int CountOf(Fighter f) =>
            !_out.Contains(f) && _counted.Contains(f) && _downFor.TryGetValue(f, out float t) ? Mathf.Min(10, Mathf.FloorToInt(t / countInterval)) : -1;
        /// <summary>Down with no count over it (it fell before the bell).</summary>
        public bool Slipped(Fighter f) => f != null && f.IsDown && !_out.Contains(f) && !_counted.Contains(f);
        public int Left { get { int n = 0; foreach (Fighter f in fighters) if (f != null && !_out.Contains(f)) n++; return n; } }

        void Awake()
        {
            Instance = this;
            var boxers = new List<MjBoxer>();
            for (int i = 0; i < fighters.Length; i++)
            {
                Fighter f = fighters[i];
                if (f == null || f.boxer == null) continue;
                // A prefix of its own: two letters, as SkinBinder expects (it strips "x_" to find the bodies).
                string from = f.boxer.prefix, to = (char)('a' + boxers.Count) + "_";
                if (from != to)
                    foreach (Transform t in f.GetComponentsInChildren<Transform>(true))
                        if (t.name.StartsWith(from)) t.name = to + t.name.Substring(from.Length);
                f.boxer.prefix = to;
                Paint(f, colours[boxers.Count % colours.Length]);
                f.ring = ring;
                // Wired to the ring before it is switched on (MjBrain hooks the ring's punches in OnEnable): an instance
                // without the scene's overrides (the twins, 2026-11-16) otherwise neither takes nor lands a counted punch.
                var wire = f.GetComponent<MjBrain>();
                if (wire != null && wire.ring == null) wire.ring = ring;
                if (f.ringHalf <= 0f) f.ringHalf = ring.ringHalf;
                f.gameObject.SetActive(true);
                _brain[f] = f.GetComponent<MjBrain>();
                boxers.Add(f.boxer);
            }
            if (ring == null || boxers.Count < 2)
            {
                Debug.LogError("[FreeForAll] needs a ring and at least two boxers.", this);
                enabled = false;
                return;
            }
            ring.boxers = boxers.ToArray();
            ring.red = boxers[0];
            ring.blue = boxers[1];
            ring.solidBodies = true;
            // Nothing steps until the bell's first phase: the corner prefabs are authored on top of one another.
            ring.running = false;
            Bout.SimRunning = false;
        }

        void OnEnable() => SimBus.Knockdown += OnKnockdown;

        void OnDisable()
        {
            SimBus.Knockdown -= OnKnockdown;
            if (Instance != this) return;
            Instance = null;
            Bout.SimRunning = false;
        }

        static void Paint(Fighter f, Color c)
        {
            f.cornerColor = c;
            var skin = f.GetComponent<FighterSkin>();
            if (skin == null) return;
            skin.rimColor = c;
            foreach (TrailRenderer t in skin.trails)
                if (t != null) { t.startColor = new Color(c.r, c.g, c.b, 0.55f); t.endColor = new Color(c.r, c.g, c.b, 0f); }
        }

        /// <summary>Down in the fight, by a punch or a slip: the referee counts.</summary>
        void OnKnockdown(Fighter f, HitEvent cause)
        {
            if (Phase != Stage.Fight || f == null || _out.Contains(f)) return;
            _counted.Add(f);
        }

        // ------------------------------------------------------------ flow

        /// <summary>Everybody back on their feet at the ropes, the counts and the result cleared: the same bout again.</summary>
        public void Restart()
        {
            if (!ring.Ready) return;
            _out.Clear();
            _outOrder.Clear();
            Placings.Clear();
            Winner = null;
            Method = "";
            Bout.Clock = 0f;
            Round = 1;
            _waiting.Clear();
            _entered.Clear();
            foreach (Fighter f in fighters) if (f != null) Unpark(f);
            // Those beyond maxInRing wait off stage, in the scene's order (the boxers first, then the twins).
            int inRing = 0;
            foreach (Fighter f in fighters)
            {
                if (f == null) continue;
                if (maxInRing > 0 && inRing >= maxInRing) { _waiting.Add(f); Park(f); }
                else { inRing++; _entered.Add(f); }
            }
            _sinceEntry = 0f;
            _lastOutAt = -99f;
            SetPhase(Stage.Intro, BoutPhase.WalkOn);       // the walk-on's sting, and the canvas wiped
            ToTheRopes(true);
            SimBus.Say(_waiting.Count > 0 ? $"{inRing} in the ring, {_waiting.Count} waiting. Last one standing."
                                          : $"{inRing} in the ring. Last one standing.", 2);
        }

        /// <summary>
        /// Everybody still in to a spot at the ropes, facing the middle, round the ring in the order of the scene, each
        /// set on the boxer across from it. A spot with a body lying on it is passed over for the next one free.
        /// </summary>
        void ToTheRopes(bool newBout)
        {
            Vector3 centre = Centre;
            List<Vector3> spots = Spots();
            if (!newBout)
            {
                // The break: the counted out are carried off, and those waiting fill the ring up again.
                foreach (Fighter f in _out) Park(f);
                while (_waiting.Count > 0 && (maxInRing <= 0 || InRingCount < maxInRing)) Admit(_waiting[0]);
            }
            var used = new bool[spots.Count];
            var inRing = new List<Fighter>();
            foreach (Fighter f in fighters) if (InRing(f)) inRing.Add(f);
            for (int n = 0; n < inRing.Count; n++)
            {
                int want = Mathf.RoundToInt(n * spots.Count / (float)inRing.Count) % spots.Count;
                for (int tries = 0; tries < spots.Count; tries++)
                {
                    int s = (want + tries) % spots.Count;
                    if (used[s] || Blocked(spots[s])) continue;
                    used[s] = true;
                    // Blocked only looks at boxers already out, so placing as we go changes nothing.
                    Fighter f = inRing[n];
                    Vector3 at = spots[s];
                    f.Respawn(at, Quaternion.LookRotation(centre - at), newBout);
                    if (_brain.TryGetValue(f, out MjBrain brain) && brain != null) brain.ResetBrain();
                    // In its guard at the ropes until the bell.
                    ring.Hold(f.boxer, centre - at);
                    break;
                }
            }
            _roundAge = 0f;
            _lastOutAt = -99f;
            _downFor.Clear();
            _counted.Clear();
            Retarget(true);
        }

        /// <summary>The middle of the canvas; the floor's height is this object's (it stands on the canvas).</summary>
        Vector3 Centre => new Vector3(ring.centre.x, transform.position.y, ring.centre.y);

        /// <summary>Sixteen spots round the square of the ropes: corners, the middle of each side, and the points between.</summary>
        List<Vector3> Spots()
        {
            float half = ring.ringHalf - startInside;
            Vector3 centre = Centre;
            var spots = new List<Vector3>();
            for (int i = 0; i < 16; i++)
            {
                float a = Mathf.PI * 2f * i / 16f, c = Mathf.Cos(a), s = Mathf.Sin(a), m = Mathf.Max(Mathf.Abs(c), Mathf.Abs(s));
                spots.Add(centre + new Vector3(c / m, 0f, s / m) * half);
            }
            return spots;
        }

        // ------------------------------------------------------------ off stage and tagging in

        /// <summary>Off stage: held in its guard far from the ring, hidden, its brain not run.</summary>
        void Park(Fighter f)
        {
            if (f == null || _parked.Contains(f) || f.boxer == null) return;
            _parked.Add(f);
            int k = System.Array.IndexOf(fighters, f);
            Vector3 spot = Centre + new Vector3(parkDistance + 2f * (k % 4), 0f, 2f * (k / 4));
            ring.HoldPose(f.boxer, spot, Vector3.forward, null);
            ring.SetIdle(f.boxer, true);
            foreach (Renderer r in f.GetComponentsInChildren<Renderer>(true))
            {
                if (!_shown.ContainsKey(r)) _shown[r] = r.enabled;
                r.enabled = false;
            }
        }

        /// <summary>Back on stage: shown, its brain run again. Where it stands is the caller's (Respawn).</summary>
        void Unpark(Fighter f)
        {
            if (f == null || !_parked.Remove(f)) return;
            if (f.boxer != null) ring.SetIdle(f.boxer, false);
            foreach (Renderer r in f.GetComponentsInChildren<Renderer>(true))
                if (_shown.TryGetValue(r, out bool was)) { r.enabled = was; _shown.Remove(r); }
        }

        /// <summary>From the queue into the ring's count (placed by the caller): it has now entered.</summary>
        void Admit(Fighter f)
        {
            _waiting.Remove(f);
            _entered.Add(f);
            Unpark(f);
        }

        /// <summary>
        /// The next boxer waiting comes in during the fight: on the free spot at the ropes furthest from everybody,
        /// facing the middle, fresh, and straight out of the corner on its footwork as the others did at the bell.
        /// </summary>
        void TagIn()
        {
            if (_waiting.Count == 0) return;
            Vector3 centre = Centre;
            Vector3 best = Vector3.zero;
            float bestRoom = -1f;
            foreach (Vector3 spot in Spots())
            {
                float room = float.MaxValue;
                foreach (Fighter g in fighters)
                {
                    if (g == null || g.pelvis == null || _parked.Contains(g)) continue;
                    room = Mathf.Min(room, Flat(g.pelvis.transform.position - spot));
                }
                if (room > bestRoom) { bestRoom = room; best = spot; }
            }
            if (bestRoom < 1.0f) return;                     // nowhere clear at the ropes: try again next frame
            Fighter f = _waiting[0];
            Admit(f);
            f.Respawn(best, Quaternion.LookRotation(centre - best), true);
            if (_brain.TryGetValue(f, out MjBrain brain) && brain != null)
            {
                brain.ResetBrain();
                brain.WalkOut(walkOutSpeed);
            }
            _sinceEntry = 0f;
            Retarget(true);
            SimBus.Say(_waiting.Count > 0 ? $"{f.displayName} is in! {_waiting.Count} waiting." : $"{f.displayName} is in, the last one.", 2);
        }

        /// <summary>The fight's entries: the next one in after a boxer is counted out, or on the clock while the ring is not full.</summary>
        void Entries(float dt)
        {
            if (_waiting.Count == 0) return;
            _sinceEntry += dt;
            int n = InRingCount;
            bool replace = (maxInRing <= 0 || n < maxInRing) && _roundAge - _lastOutAt >= entryDelay;
            bool onTheClock = _sinceEntry >= entryEvery && n < Mathf.Max(entryCap, maxInRing);
            if (replace || onTheClock || n <= 1) TagIn();
        }

        /// <summary>A body lies within reach of the spot: a boxer stood there would be stood inside it.</summary>
        bool Blocked(Vector3 spot)
        {
            foreach (Fighter f in fighters)
            {
                if (f == null || !_out.Contains(f) || _parked.Contains(f) || f.pelvis == null) continue;
                Vector3 d = f.pelvis.transform.position - spot;
                d.y = 0f;
                if (d.magnitude < 1.0f) return true;
            }
            return false;
        }

        void SetPhase(Stage next, BoutPhase said)
        {
            BoutPhase from = Phase == Stage.Fight ? BoutPhase.Fight : Phase == Stage.Break ? BoutPhase.RoundBreak : Phase == Stage.Over ? BoutPhase.Results : BoutPhase.Intro;
            Phase = next;
            PhaseAge = 0f;
            bool run = next == Stage.Intro || next == Stage.Fight || next == Stage.Over;
            Bout.SimRunning = run;
            ring.running = run;
            if (next == Stage.Intro) RoundTimeLeft = roundSeconds;
            // The bell, the sting and the applause are the arena's sound's, which listens for a bout's phases.
            SimBus.RaisePhase(from, said);
            if (said == BoutPhase.WalkOn) SimBus.RaisePhase(BoutPhase.WalkOn, BoutPhase.Intro);
        }

        void FixedUpdate()
        {
            // Before the brains (MjBrain runs at -40): a boxer whose opponent has just gone down turns to another
            // standing one at once, instead of being held in its guard as if the referee were counting.
            if (Phase == Stage.Intro || Phase == Stage.Fight) Retarget(false);
            if (Phase == Stage.Fight) EndWalks();
        }

        /// <summary>Out of the corner: the walk ends near another boxer on its feet, at the middle, when hit, or after walkSeconds.</summary>
        void EndWalks()
        {
            Vector3 middle = new Vector3(ring.centre.x, 0f, ring.centre.y);
            foreach (Fighter f in fighters)
            {
                if (!InRing(f) || !_brain.TryGetValue(f, out MjBrain brain) || !brain.Walking) continue;
                Vector3 me = f.pelvis.transform.position;
                bool near = Flat(me - middle) < middleAt || _roundAge > walkSeconds || f.TimeSinceHit < 0.5f || f.IsDown;
                foreach (Fighter g in fighters)
                    if (!near && g != f && InRing(g) && !g.IsDown && Flat(g.pelvis.transform.position - me) < engageAt) near = true;
                if (near) brain.EndWalk();
            }
        }

        void Update()
        {
            PhaseAge += Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            float dt = Time.deltaTime;
            switch (Phase)
            {
                case Stage.Waiting:
                    if (ring.Ready) Restart();
                    break;

                case Stage.Intro:
                    if (PhaseAge >= introSeconds)
                    {
                        SetPhase(Stage.Fight, BoutPhase.Fight);
                        SimBus.Say($"ROUND {Round}", 2);
                        foreach (Fighter f in fighters)
                        {
                            if (!InRing(f)) continue;
                            ring.Release(f.boxer);
                            if (_brain.TryGetValue(f, out MjBrain brain)) brain.WalkOut(walkOutSpeed);
                        }
                    }
                    break;

                case Stage.Fight:
                    Bout.Clock += dt;
                    _roundAge += dt;
                    RoundTimeLeft = Mathf.Max(0f, RoundTimeLeft - dt);
                    Count(dt);
                    Entries(dt);
                    if (Decide()) break;
                    // The bell waits for a count to finish: nobody is saved by it.
                    if (RoundTimeLeft <= 0f && !Counting()) EndRound();
                    break;

                case Stage.Break:
                    if (PhaseAge >= breakSeconds)
                    {
                        Round++;
                        ToTheRopes(false);
                        SetPhase(Stage.Intro, BoutPhase.Intro);
                    }
                    break;

                case Stage.Over:
                    // The final bell: the last punch and the last fall finish, then everything stops, as in a bout.
                    if (PhaseAge >= settleSeconds)
                    {
                        Bout.SimRunning = false;
                        ring.running = false;
                    }
                    break;
            }
        }

        bool Counting()
        {
            foreach (Fighter f in _counted) if (f != null && f.IsDown && !_out.Contains(f)) return true;
            return false;
        }

        void Count(float dt)
        {
            foreach (Fighter f in fighters)
            {
                if (!InRing(f)) continue;
                if (!f.IsDown) { _downFor.Remove(f); _counted.Remove(f); continue; }
                if (f.OnFeet) continue;         // up: the count waits while the get-up hands the body over
                _downFor.TryGetValue(f, out float t);
                t += dt;
                _downFor[f] = t;
                // Ten after a knockdown: counted out. Down with no count, a boxer who cannot get up
                // (from under a pile, say) in all that time cannot go on.
                bool knockedDown = _counted.Contains(f);
                if (knockedDown ? Mathf.FloorToInt(t / countInterval) < 10 : t < slipSeconds) continue;
                _out.Add(f);
                _outOrder.Add(f);
                _lastOutAt = _roundAge;
                f.Eliminate();
                SimBus.Say(knockedDown ? $"{f.displayName} is counted out. {Left} left." : $"{f.displayName} cannot go on. {Left} left.", 2);
            }
        }

        /// <summary>Over inside the distance: one left on its feet, or nobody left at all.</summary>
        bool Decide()
        {
            Fighter last = null;
            int standing = 0, left = 0;
            foreach (Fighter f in fighters)
            {
                if (!InRing(f)) continue;
                left++;
                if (!f.IsDown) { standing++; last = f; }
            }
            if (_waiting.Count > 0) return false;           // the next one comes in (Entries) before anybody has won
            if (left == 1 && standing == 1) { Finish(last, $"KO · ROUND {Round} · {Bout.ClockText(roundSeconds - RoundTimeLeft)}"); return true; }
            if (left == 0) { Finish(null, "NOBODY BEAT THE COUNT"); return true; }
            return false;
        }

        void EndRound()
        {
            if (Round < rounds)
            {
                SimBus.Say($"End of round {Round}.", 2);
                SetPhase(Stage.Break, BoutPhase.RoundBreak);
                return;
            }
            // The distance: the judges, on the damage done and the knockdowns scored, among those still in.
            Fighter best = null;
            foreach (Fighter f in fighters)
                if (InRing(f) && (best == null || Bout.Score(f) > Bout.Score(best))) best = f;
            Finish(best, $"JUDGES' DECISION · {Left} WENT THE DISTANCE");
        }

        void Finish(Fighter winner, string method)
        {
            Winner = winner;
            Method = method;
            SetPhase(Stage.Over, BoutPhase.Results);
            SimBus.Say(winner != null ? $"{winner.displayName} wins: {method}" : method, 3);
            Place(winner);
        }

        /// <summary>The placings, best first: the winner, the others still in by the judges' score, then the eliminated, last out first. Rated once.</summary>
        void Place(Fighter winner)
        {
            Placings.Clear();
            if (winner != null) Placings.Add(winner);
            var still = new List<Fighter>();
            foreach (Fighter f in fighters) if (InRing(f) && f != winner) still.Add(f);
            still.Sort((a, b) => Bout.Score(b).CompareTo(Bout.Score(a)));
            Placings.AddRange(still);
            Placings.AddRange(_waiting);
            for (int i = _outOrder.Count - 1; i >= 0; i--) if (_outOrder[i] != null && !Placings.Contains(_outOrder[i])) Placings.Add(_outOrder[i]);
            if (!rated || winner == null) return;
            var names = new List<string>();
            foreach (Fighter f in Placings) if (_entered.Contains(f)) names.Add(BaseName(f.displayName));
            PoBox.League.LeagueTable.ReportPlacings(names, ratingK);
        }

        // ------------------------------------------------------------ who fights whom

        /// <summary>
        /// Every boxer still in gets an opponent: the nearest on its feet, counted nearer if it has just landed on this one and further for
        /// every other boxer already on it; with nobody else standing, the nearest still in (it is then held facing
        /// them, as over a count).
        /// </summary>
        void Retarget(bool fresh)
        {
            // Runs every physics step (200 Hz): one reused dictionary, and the standing count taken once.
            var on = _on;
            on.Clear();
            int standing = 0;
            foreach (Fighter f in fighters)
            {
                if (!InRing(f)) continue;
                if (!f.IsDown) standing++;
                if (f.opponent != null) { on.TryGetValue(f.opponent, out int n); on[f.opponent] = n + 1; }
            }

            foreach (Fighter f in fighters)
            {
                if (!InRing(f) || f.pelvis == null) continue;
                Vector3 me = f.pelvis.transform.position;

                bool anyStanding = standing - (f.IsDown ? 0 : 1) > 0;
                Fighter best = null;
                float bestCost = float.MaxValue, nowCost = float.MaxValue;
                foreach (Fighter g in fighters)
                {
                    if (g == f || !InRing(g) || g.pelvis == null || (anyStanding && g.IsDown)) continue;
                    on.TryGetValue(g, out int already);
                    if (f.opponent == g) already--;
                    float cost = Flat(g.pelvis.transform.position - me) + crowding * Mathf.Max(0, already - 1);
                    if (f.LastHitTaken.attacker == g && f.TimeSinceHit < 1.5f) cost -= grudge;
                    cost -= opportunism * g.Daze01;
                    if (g == f.opponent) nowCost = cost;
                    if (cost < bestCost) { bestCost = cost; best = g; }
                }
                if (best == null) continue;
                if (!fresh && nowCost < float.MaxValue && nowCost - bestCost < switchMargin) continue;
                Face(f, best, on);
            }
        }

        static void Face(Fighter f, Fighter g, Dictionary<Fighter, int> on)
        {
            if (f.opponent == g) return;
            if (f.opponent != null && on.TryGetValue(f.opponent, out int n)) on[f.opponent] = n - 1;
            on.TryGetValue(g, out int m);
            on[g] = m + 1;
            f.opponent = g;
            f.boxer.Face(g.boxer);
        }

        static float Flat(Vector3 d) { d.y = 0f; return d.magnitude; }
    }
}
