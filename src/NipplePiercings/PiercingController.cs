using System;
using System.Collections.Generic;
using System.Globalization;
using ExposureUnnoticed2.Object3D.Player.Scripts;
using ExposureUnnoticed2.Object3D.Player.Scripts.Other;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.Attributes;
using NipplePiercings.Logic;
using SfmKit.Game;
using UnityEngine;
using UnityEngine.Audio;
using NVector3 = System.Numerics.Vector3;
using NQuaternion = System.Numerics.Quaternion;
using Collider = NipplePiercings.Logic.Collider;

namespace NipplePiercings
{
    /// <summary>
    /// Puts the chosen piercing on each nipple. The anchors are the game's own nipple-piercing points: the two
    /// <c>ChikubiPiasController</c> objects (<c>PiasPosition</c>) under her breast bones, which follow the nipples with the
    /// breasts' DynamicBone jiggle and the nipple size [observed 2026-10-25: +z out of the nipple, +y up, +x her right]. The
    /// nipples' tips come from her body mesh (<see cref="BakeApexes"/>). Every frame before rendering (Camera.onPreCull, after all
    /// bone adjustments) the hanging pieces are stepped and placed, the chain between the nipples too; a hard swing jingles
    /// (a synthesised chime) and makes the gems sparkle.
    /// Bridge diagnostics: <c>game get NipplePiercings.PiercingController::Status</c>.
    /// </summary>
    public sealed class PiercingController : MonoBehaviour
    {
        internal static PiercingSet Set;
        public const float BakeInterval = 2f;   // s between bakes of her body for the nipples' tips
        public const float UnitMetres = 0.065f / 2.67f; // metres per model unit at Size 1, height 1: the crystal pendant 6.5 cm long
        public const float ReferenceNipple = 2.2385f / 1.3125f; // piercing point's scale per unit of height, first save's nipples
        public static string Status = "starting";
        public static int Jingles;              // diagnostics: chimes played since start

        private sealed class Side
        {
            public string Name;
            public Transform Anchor;       // PiasPosition
            public Transform Breast;       // its parent, the breast's root bone
            public int ApexVertex = -1;    // the nipple's tip on her body mesh
            public Vector3 ApexLocal;      // ... in the anchor's frame, from the last bake
            public bool HasApex;
            public string Id = "";         // what is worn now
            public PiercingModel Model;
            public PendantRig Rig;
            public PendantChain Chain;     // null for rigid piercings
            public NVector3[] Pos;         // each bone's origin and rotation as placed
            public NQuaternion[] Rot;
            public NVector3 Hook;          // where the chain between the nipples hangs from
            public NVector3 LastTip;
            public bool Fresh = true;      // no speed yet: just put on (a speed from the old piece's place rang falsely)
            public readonly JingleTrigger Jingle = new JingleTrigger();
            public AudioSource Audio;
        }

        private readonly Side[] _sides = { new Side { Name = "L" }, new Side { Name = "R" } };
        private Materials _materials;
        private float _nextSearch, _nextBake, _sparkle;
        private Transform _avatar;                // Manustia: its scale is her height setting
        private SkinnedMeshRenderer _body;        // Selestia_body
        private Animator _anim;
        private Mesh _bake;
        private Camera.CameraCallback _preCull;
        private int _placedFrame = -1;
        private AudioClip _chime;
        private AudioMixerGroup _seGroup;
        private readonly List<string> _colliderNames = new List<string>(); // diagnostics, parallel to the colliders

        // The chain between the nipples.
        private SaggingChain _between;
        private PendantRig _betweenRig;
        private string _betweenKey = "";
        private NVector3 _lastMiddle;

        public PiercingController(IntPtr pointer) : base(pointer) { }

        private void Start()
        {
            _preCull = DelegateSupport.ConvertDelegate<Camera.CameraCallback>(new Action<Camera>(OnPreCull));
            Camera.onPreCull = Camera.onPreCull == null
                ? _preCull
                : Il2CppSystem.Delegate.Combine(Camera.onPreCull, _preCull).Cast<Camera.CameraCallback>();
        }

        private void OnDestroy()
        {
            if (_preCull != null && Camera.onPreCull != null)
                Camera.onPreCull = Il2CppSystem.Delegate.Remove(Camera.onPreCull, _preCull)?.Cast<Camera.CameraCallback>();
            foreach (var s in _sides)
            {
                s.Rig?.Destroy();
                if (s.Audio != null) UnityEngine.Object.Destroy(s.Audio.gameObject);
            }
            _betweenRig?.Destroy();
            if (_bake != null) UnityEngine.Object.Destroy(_bake);
        }

        private bool _closetAdded;

        private void Update()
        {
            try { if (!_closetAdded) _closetAdded = Closet.TryAdd(Set.Wearable()); }
            catch (Exception e) { _closetAdded = true; Fail("adding the costume menu items", e); }
            try
            {
                if (!Plugin.Enabled.Value || Time.time < _nextSearch) return;
                if (_sides[0].Anchor != null && _sides[1].Anchor != null) return;
                _nextSearch = Time.time + 1f;
                FindAnchors();
            }
            catch (Exception e) { Fail("finding the nipples", e); }
        }

        /// <summary>Her two piercing points, under the player's animator (not the menu's preview body).</summary>
        [HideFromIl2Cpp]
        private void FindAnchors()
        {
            var player = PlayerController.Instance;
            var anim = player == null || player.Pca == null ? null : player.Pca.Animator;
            if (anim == null) return;
            _anim = anim;
            _avatar = anim.transform;
            _body = null;
            foreach (var r in anim.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (r.name == "Selestia_body") { _body = r; break; }
            _nextBake = 0f;
            foreach (var c in anim.GetComponentsInChildren<ChikubiPiasController>(true))
            {
                Transform t = c.transform, parent = t.parent;
                if (parent == null) continue;
                int i = parent.name.EndsWith("_L", StringComparison.Ordinal) ? 0 : parent.name.EndsWith("_R", StringComparison.Ordinal) ? 1 : -1;
                if (i < 0 || _sides[i].Anchor == t) continue;
                var s = _sides[i];
                s.Anchor = t;
                s.Breast = parent;
                s.ApexVertex = -1;
                s.HasApex = false;
                s.Id = "";             // rebuilt for the new body
                Plugin.Logger.LogInfo("[SFM-ACCEPT] anchor: " + s.Name + " nipple at " + Path(t));
            }
        }

        /// <summary>
        /// The nipples' tips from her drawn body mesh, kept in each anchor's frame (so they follow the jiggle between bakes; a bake
        /// every <see cref="BakeInterval"/> s picks up nipple shape changes). The game's piercing point lies inside the nipple, and
        /// a fixed offset in front of it left a stud above the tip [observed 2026-10-25]. Bake to world needs the body's lossy
        /// scale (api-notes "Bake to world"). The tip vertex is searched once (within 4 cm, furthest along the nipple's +z), then
        /// only that vertex is read.
        /// </summary>
        [HideFromIl2Cpp]
        private void BakeApexes()
        {
            if (_body == null || (_sides[0].Anchor == null && _sides[1].Anchor == null)) return;
            if (_bake == null) _bake = new Mesh { name = "NipplePiercings.Bake", hideFlags = HideFlags.DontUnloadUnusedAsset };
            _body.BakeMesh(_bake, true);
            var verts = _bake.vertices;
            Transform t = _body.transform;
            Vector3 pos = t.position, ls = t.lossyScale;
            Quaternion rot = t.rotation;
            NVector3[] world = null;
            foreach (var s in _sides)
            {
                if (s.Anchor == null) continue;
                if (s.ApexVertex < 0 || s.ApexVertex >= verts.Length)
                {
                    if (world == null)
                    {
                        world = new NVector3[verts.Length];
                        for (int i = 0; i < verts.Length; i++) world[i] = PendantRig.N(pos + rot * Vector3.Scale(ls, verts[i]));
                    }
                    s.ApexVertex = NippleApex.Find(world, PendantRig.N(s.Anchor.position), PendantRig.N(s.Anchor.forward), 0.04f);
                    if (s.ApexVertex < 0) { s.HasApex = false; continue; }
                    Plugin.Logger.LogInfo("[SFM-ACCEPT] apex: " + s.Name + " nipple tip is vertex " + s.ApexVertex + ", " +
                                          Cm(Vector3.Distance(PendantRig.U(world[s.ApexVertex]), s.Anchor.position)) + " cm from the piercing point");
                }
                Vector3 apex = pos + rot * Vector3.Scale(ls, verts[s.ApexVertex]);
                s.ApexLocal = s.Anchor.InverseTransformPoint(apex);
                s.HasApex = true;
            }
        }

        /// <summary>Diagnostics (bridge: <c>game set NipplePiercings.PiercingController::DebugView 'right 0.12 R'</c>): the main
        /// camera looks at her from a side at the distance given (m) - with a third word L / R at that nipple, M between them
        /// (SfmKit <see cref="DebugCamera"/>). Empty = off.</summary>
        public static string DebugView = "";

        [HideFromIl2Cpp]
        private Vector3? NippleTarget(string name)
        {
            Vector3 down = (PlayerController.Instance != null ? PlayerController.Instance.transform.up : Vector3.up) * 0.02f;
            if (name == "M" && _sides[0].Anchor != null && _sides[1].Anchor != null)
                return (_sides[0].Anchor.position + _sides[1].Anchor.position) / 2f - down * 2f;
            var side = name == "L" ? _sides[0] : name == "R" ? _sides[1] : null;
            if (side?.Anchor == null) return null;
            return side.Anchor.position - down;
        }

        [HideFromIl2Cpp]
        private void OnPreCull(Camera cam)
        {
            try { DebugCamera.Aim(cam, DebugView, NippleTarget); }
            catch (Exception e) { Fail("debug camera", e); }
            if (_placedFrame == Time.frameCount) return;
            _placedFrame = Time.frameCount;
            try
            {
                if (Plugin.Enabled.Value && Time.time >= _nextBake)
                {
                    _nextBake = Time.time + BakeInterval;
                    BakeApexes();
                }
            }
            catch (Exception e) { Fail("finding the nipples' tips", e); }
            try
            {
                bool on = Plugin.Enabled.Value;
                var sb = new System.Text.StringBuilder();
                var colliders = on ? Colliders() : null;
                foreach (var s in _sides)
                {
                    // The breast bone, not the piercing point: the game keeps PiasPosition inactive for a while after a load
                    // (it still follows the breast) [observed 2026-10-25].
                    bool show = on && s.Anchor != null && s.Breast != null && s.Breast.gameObject.activeInHierarchy && Wear(s);
                    s.Rig?.SetVisible(show);
                    if (!show) { sb.Append(s.Name).Append(": off; "); continue; }
                    Place(s, colliders, sb);
                }
                PlaceChain(on, colliders, sb);
                FadeSparkle();
                Status = sb.ToString();
            }
            catch (Exception e) { Fail("placing the piercings", e); }
        }

        /// <summary>Make sure the side wears what the config says; false = nothing.</summary>
        [HideFromIl2Cpp]
        private bool Wear(Side s)
        {
            string want = (s == _sides[0] ? Plugin.Left : Plugin.Right).Value;
            if (want == s.Id) return s.Model != null;
            s.Rig?.Destroy();
            s.Rig = null;
            s.Chain = null;
            s.Model = null;
            s.Id = want;
            var model = Set.Get(want);
            if (model == null || model.Kind == PiercingKind.Link) return false;
            _materials ??= new Materials(Set);
            s.Model = model;
            s.Rig = new PendantRig(model, "NipplePiercings." + s.Name, s.Anchor.gameObject.layer, _materials);
            if (model.Swings) s.Chain = new PendantChain(model.Joints(), model.Thickness());
            s.Fresh = true;
            s.Pos = new NVector3[model.Bones.Count];
            s.Rot = new NQuaternion[model.Bones.Count];
            Plugin.Logger.LogInfo("[SFM-ACCEPT] wear: " + s.Name + " " + want + (model.Swings ? " (swings, " + model.Bones.Count + " pieces)" : " (rigid)"));
            return true;
        }

        private float Height => _avatar != null ? _avatar.lossyScale.y : 1f;

        /// <summary>Her breasts (a sphere behind each nipple), arms (capsules on the bones) and the floor under her.</summary>
        [HideFromIl2Cpp]
        private List<Collider> Colliders()
        {
            var list = new List<Collider>();
            _colliderNames.Clear();
            if ((Plugin.DebugDisable.Value & 2) != 0) return list;
            foreach (var s in _sides)
            {
                if (s.Anchor == null || s.Breast == null || Plugin.BreastSize.Value <= 0f) continue;
                float radius = Plugin.BreastSize.Value * Vector3.Distance(s.Anchor.position, s.Breast.position);
                list.Add(Collider.Sphere(PendantRig.N(s.Anchor.position - s.Anchor.forward * radius), radius));
                _colliderNames.Add("breast " + s.Name);
            }
            if (Plugin.ArmCollision.Value && _anim != null)
            {
                // About 10 % / 8 % of the bone's length: the upper arm near the elbow is ~7.5 %, thicker higher up (api-notes).
                Arm(list, HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, 0.10f);
                Arm(list, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand, 0.08f);
                Arm(list, HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, 0.10f);
                Arm(list, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand, 0.08f);
            }
            var player = PlayerController.Instance;
            if (Plugin.FloorCollision.Value && player != null)
            {
                list.Add(Collider.Plane(PendantRig.N(player.transform.position), NVector3.UnitY));
                _colliderNames.Add("floor");
            }
            return list;
        }

        // A plain method, not a local function: the compiler gives a local function a by-ref closure parameter, which the
        // Il2Cpp class injector cannot convert (NullReferenceException when registering the type) [observed 2026-10-25].
        [HideFromIl2Cpp]
        private void Arm(List<Collider> list, HumanBodyBones a, HumanBodyBones b, float share)
        {
            Transform ta = _anim.GetBoneTransform(a), tb = _anim.GetBoneTransform(b);
            if (ta == null || tb == null) return;
            float r = share * Vector3.Distance(ta.position, tb.position);
            list.Add(Collider.Capsule(PendantRig.N(ta.position), PendantRig.N(tb.position), r));
            _colliderNames.Add(a + " r " + Cm(r));
        }

        [HideFromIl2Cpp]
        private void Place(Side s, List<Collider> colliders, System.Text.StringBuilder sb)
        {
            int debug = Plugin.DebugDisable.Value;
            var model = s.Model;
            Transform a = s.Anchor;
            // Studs and pendants are sized to her (her height); what goes through the nipple to the nipple: the piercing point's
            // scale follows her height and the nipple size setting (ChikubiPiasController) - sized by her height alone, a barbell
            // was shorter than her nipple is wide and vanished in it [observed 2026-10-25].
            float nipple = a.lossyScale.z / ReferenceNipple;
            bool through = model.Kind == PiercingKind.Through;
            float scale = UnitMetres * Plugin.Size.Value * (through ? nipple * Plugin.NippleFit.Value : Height);
            NVector3 forward = PendantRig.N(a.forward);
            NVector3 tipOfNipple = PendantRig.N(s.HasApex ? a.TransformPoint(s.ApexLocal) : a.position);
            // A stud: its ball half pressed into the tip. Barbells and rings: through the nipple a little behind the tip.
            float out_ = through ? -Plugin.DepthCm.Value / 100f * nipple : model.Bones[0].HalfWidth() * scale * 0.5f;
            NVector3 origin = tipOfNipple + forward * (out_ + Plugin.ForwardCm.Value / 100f);
            var r = a.rotation;
            var rot = new NQuaternion(r.x, r.y, r.z, r.w);
            var joints = model.Joints();
            int n = model.Bones.Count;
            if (s.Chain == null || (debug & 1) != 0)
            {
                // Rigid (or as modelled for tests): every piece in the nipple's frame.
                for (int b = 0; b < n; b++)
                {
                    s.Pos[b] = origin + NVector3.Transform(joints[b] * scale, rot);
                    s.Rot[b] = rot;
                }
                s.Chain?.Reset();
            }
            else
            {
                var chain = s.Chain;
                chain.Damping = Plugin.Damping.Value;
                chain.Gravity = new NVector3(0f, -9.81f * Plugin.Gravity.Value, 0f);
                chain.Colliders.Clear();
                chain.Colliders.AddRange(colliders);
                chain.Collide = (debug & 2) == 0;
                chain.Update(Time.deltaTime, origin, rot, scale);
                for (int b = 0; b < n; b++)
                {
                    s.Pos[b] = chain.Joint(b);
                    s.Rot[b] = chain.Segment(b, rot);
                }
            }
            for (int b = 0; b < n; b++) s.Rig.Place(b, s.Pos[b], s.Rot[b], scale);
            var hookBone = model.Bones[model.HookBone];
            s.Hook = s.Pos[model.HookBone] + NVector3.Transform((PendantBone.ToNipple(model.Hook) - PendantBone.ToNipple(hookBone.Head)) * scale, s.Rot[model.HookBone]);

            NVector3 tip = s.Chain != null && (debug & 1) == 0 ? s.Chain.Joint(s.Chain.Count - 1) : s.Pos[n - 1];
            float speed = s.Fresh || !(Time.deltaTime > 0f) ? 0f : NVector3.Distance(tip, s.LastTip) / Time.deltaTime;
            s.LastTip = tip;
            s.Fresh = false;
            if (s.Chain != null) Ring(s, speed, PendantRig.U(tip));
            sb.Append(s.Name).Append(": ").Append(s.Id);
            if (s.Chain != null)
            {
                NVector3 top = s.Chain.Joint(1);
                sb.Append(", tip ").Append(Cm(top.Y - tip.Y)).Append(" cm below, ")
                  .Append(Cm(NVector3.Distance(new NVector3(tip.X, 0f, tip.Z), new NVector3(top.X, 0f, top.Z)))).Append(" cm aside");
            }
            sb.Append(", origin ").Append(Cm(NVector3.Dot(origin - tipOfNipple, forward))).Append(" cm out of the tip");
            if (s.Chain != null) Touching(sb, colliders, k => s.Chain.Joint(k), s.Chain.Count);
            sb.Append("; ");
        }

        /// <summary>The chain between the nipples: hung from the two hooks, rebuilt (its length fixed at slack x their distance)
        /// when it is put on or the piercings change.</summary>
        [HideFromIl2Cpp]
        private void PlaceChain(bool on, List<Collider> colliders, System.Text.StringBuilder sb)
        {
            Side l = _sides[0], r = _sides[1];
            bool want = on && Plugin.Chain.Value && l.Model != null && r.Model != null && l.Rig != null && r.Rig != null
                        && l.Breast.gameObject.activeInHierarchy && r.Breast.gameObject.activeInHierarchy;
            var link = Set.Get("link");
            if (!want || link == null)
            {
                _betweenRig?.SetVisible(false);
                if (!want) _betweenKey = "";
                return;
            }
            float scale = UnitMetres * Plugin.Size.Value * Height;
            string key = l.Id + "|" + r.Id + "|" + Plugin.ChainSlack.Value.ToString(CultureInfo.InvariantCulture) + "|" + scale.ToString("0.0000", CultureInfo.InvariantCulture);
            var bone = link.Bones[0];
            float lx = float.MaxValue, hx = float.MinValue, ly = float.MaxValue, hy = float.MinValue;
            foreach (var p in bone.Positions) { lx = Math.Min(lx, p.X); hx = Math.Max(hx, p.X); ly = Math.Min(ly, p.Y); hy = Math.Max(hy, p.Y); }
            float wire = hy - ly;                           // the link's wire thickness (Blender y)
            float pitch = (hx - lx - 2f * wire) * scale;    // joint to joint: the inner length of a link
            if (key != _betweenKey || _between == null)
            {
                float span = NVector3.Distance(l.Hook, r.Hook);
                int links = Math.Max(3, (int)Math.Ceiling(Plugin.ChainSlack.Value * span / Math.Max(pitch, 1e-4f)));
                _between = new SaggingChain(links, pitch, wire * 0.5f * scale);
                _betweenRig?.Destroy();
                _materials ??= new Materials(Set);
                _betweenRig = new PendantRig(link, "NipplePiercings.Chain", l.Anchor.gameObject.layer, _materials, links);
                _betweenKey = key;
                _lastMiddle = new NVector3(float.NaN);
                Plugin.Logger.LogInfo("[SFM-ACCEPT] chain: " + links + " links of " + Cm(pitch) + " cm between hooks " + Cm(span) + " cm apart");
            }
            _between.Damping = Plugin.Damping.Value;
            _between.Gravity = new NVector3(0f, -9.81f * Plugin.Gravity.Value, 0f);
            _between.Colliders.Clear();
            _between.Colliders.AddRange(colliders);
            _between.Update(Time.deltaTime, l.Hook, r.Hook);
            for (int k = 0; k + 1 < _between.Count; k++)
            {
                var (centre, rot) = _between.Link(k);
                _betweenRig.Place(k, centre, rot, scale);
            }
            _betweenRig.SetVisible(true);
            int mid = _between.Count / 2;
            NVector3 middle = _between.Joint(mid);
            float speed = float.IsNaN(_lastMiddle.X) || !(Time.deltaTime > 0f) ? 0f : NVector3.Distance(middle, _lastMiddle) / Time.deltaTime;
            _lastMiddle = middle;
            Ring(l, speed, PendantRig.U(middle));
            float sag = (l.Hook.Y + r.Hook.Y) / 2f - middle.Y;
            sb.Append("chain: ").Append(_between.Count - 1).Append(" links, middle ").Append(Cm(sag)).Append(" cm below the hooks");
            Touching(sb, colliders, k => _between.Joint(k), _between.Count);
            sb.Append("; ");
        }

        /// <summary>Diagnostics: the colliders a chain's joints come within 2 cm of (their pieces are up to ~1 cm thick), with the joint and how deep.</summary>
        [HideFromIl2Cpp]
        private void Touching(System.Text.StringBuilder sb, List<Collider> colliders, Func<int, NVector3> joint, int count)
        {
            for (int c = 0; c < colliders.Count && c < _colliderNames.Count; c++)
            {
                float deepest = float.MinValue;
                int at = -1;
                for (int k = 2; k < count; k++)
                {
                    float d = colliders[c].Depth(joint(k), 0f, out _);
                    if (d > deepest) { deepest = d; at = k; }
                }
                if (deepest > -0.02f) sb.Append(", ").Append(_colliderNames[c]).Append(" at joint ").Append(at).Append(" (").Append(Cm(deepest)).Append(" cm)");
            }
        }

        // --- jingle and sparkle -----------------------------------------------------------------------------------------------

        [HideFromIl2Cpp]
        private void Ring(Side s, float speed, Vector3 at)
        {
            float loud = s.Jingle.Update(speed, Time.time);
            if (loud <= 0f) return;
            if ((Plugin.DebugDisable.Value & 8) == 0) _sparkle = Math.Max(_sparkle, loud);
            if ((Plugin.DebugDisable.Value & 4) != 0 || Plugin.Volume.Value <= 0f) return;
            var src = Audio(s);
            if (src == null) return;
            src.transform.position = at;
            src.pitch = UnityEngine.Random.Range(0.92f, 1.1f);
            src.PlayOneShot(_chime, Plugin.Volume.Value * loud);
            Jingles++;
        }

        [HideFromIl2Cpp]
        private AudioSource Audio(Side s)
        {
            if (s.Audio != null) return s.Audio;
            if (_chime == null)
            {
                const int rate = 44100;
                float[] samples = Chime.Synthesize(rate, 0.7f, 2350f);
                _chime = AudioClip.Create("NipplePiercings.Chime", samples.Length, 1, rate, false);
                _chime.SetData(samples, 0);
                _chime.hideFlags = HideFlags.DontUnloadUnusedAsset;
                _seGroup = FindSoundEffectGroup();
            }
            var g = new GameObject("NipplePiercings.Jingle." + s.Name);
            UnityEngine.Object.DontDestroyOnLoad(g);
            var src = g.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.spatialBlend = 1f;        // from her chest
            src.minDistance = 0.5f;
            src.maxDistance = 8f;
            src.rolloffMode = AudioRolloffMode.Linear;
            if (_seGroup != null) src.outputAudioMixerGroup = _seGroup;
            s.Audio = src;
            return src;
        }

        /// <summary>The game's sound effect mixer group (so the jingle follows the game's SE volume): a group named "SE" among the
        /// loaded audio sources, else the one her pick-up sound uses.</summary>
        [HideFromIl2Cpp]
        private AudioMixerGroup FindSoundEffectGroup()
        {
            AudioMixerGroup fallback = null;
            var seen = new HashSet<string>();
            foreach (var o in Resources.FindObjectsOfTypeAll(Il2CppType.Of<AudioSource>()))
            {
                var src = o.TryCast<AudioSource>();
                var group = src == null ? null : src.outputAudioMixerGroup;
                if (group == null) continue;
                seen.Add(group.name);
                if (string.Equals(group.name, "SE", StringComparison.OrdinalIgnoreCase)) { Log(group, seen); return group; }
                if (fallback == null && src.name == "PickingAudioSource") fallback = group;
            }
            Log(fallback, seen);
            return fallback;
        }

        [HideFromIl2Cpp]
        private static void Log(AudioMixerGroup g, HashSet<string> seen) =>
            Plugin.Logger.LogInfo("[SFM-ACCEPT] sound: jingle on mixer group " + (g == null ? "none" : g.name) + " (seen: " + string.Join(", ", seen) + ")");

        [HideFromIl2Cpp]
        private void FadeSparkle()
        {
            if (_materials == null) return;
            _materials.Sparkle(_sparkle);
            _sparkle = Math.Max(0f, _sparkle - Time.deltaTime / 0.4f);
        }

        private static string Cm(float m) => (m * 100f).ToString("0.0", CultureInfo.InvariantCulture);

        private static string Path(Transform t)
        {
            string p = t.name;
            for (var x = t.parent; x != null; x = x.parent) p = x.name + "/" + p;
            return p;
        }

        internal static void Fail(string what, Exception e) => Plugin.Logger.LogError(what + " failed: " + e);
    }
}
