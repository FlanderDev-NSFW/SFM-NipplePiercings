using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime;
using NipplePiercings.Logic;
using UnityEngine;
using NVector3 = System.Numerics.Vector3;
using NQuaternion = System.Numerics.Quaternion;

namespace NipplePiercings
{
    /// <summary>
    /// The objects of one piercing (or of the chain between the nipples): one object per bone / link holding its rigid piece
    /// (mesh in the bone's frame, origin at the bone's head - <see cref="PendantBone.ToLocal"/>; a chain link's origin is its
    /// centre). Not parented to her: placed in the world every frame before rendering, from the drawn nipples.
    /// </summary>
    internal sealed class PendantRig
    {
        private readonly GameObject _root;
        private readonly Transform[] _bones;

        public PendantRig(PiercingModel model, string name, int layer, Materials materials, int copies = 1)
        {
            _root = new GameObject(name);
            UnityEngine.Object.DontDestroyOnLoad(_root);
            _root.layer = layer;
            var meshes = Meshes(model);
            int n = model.Bones.Count;
            _bones = new Transform[n * copies];
            for (int c = 0; c < copies; c++)
                for (int b = 0; b < n; b++)
                {
                    var g = new GameObject(name + "." + model.Bones[b].Name + (copies > 1 ? c.ToString("00") : ""));
                    g.layer = layer;
                    g.transform.SetParent(_root.transform, false);
                    _bones[c * n + b] = g.transform;
                    if (meshes[b] == null) continue;
                    g.AddComponent<MeshFilter>().sharedMesh = meshes[b];
                    var r = g.AddComponent<MeshRenderer>();
                    var mats = new Material[model.Bones[b].Submeshes.Count];
                    for (int s = 0; s < mats.Length; s++) mats[s] = materials.Get(model.Bones[b].Submeshes[s].Material);
                    r.sharedMaterials = mats;
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                }
            _root.SetActive(false);
        }

        public int Count => _bones.Length;

        private static readonly Dictionary<PiercingModel, Mesh[]> _meshes = new Dictionary<PiercingModel, Mesh[]>(); // shared by all rigs

        private static Mesh[] Meshes(PiercingModel model)
        {
            if (_meshes.TryGetValue(model, out var cached)) return cached;
            var result = new Mesh[model.Bones.Count];
            for (int b = 0; b < result.Length; b++)
            {
                var bone = model.Bones[b];
                if (bone.Positions.Length == 0) continue; // a bone without a piece (a ring's pierced point)
                var (pos, nrm, subs) = bone.ToLocal();
                if (model.Kind == PiercingKind.Link) // centred on the link, not on its bone's head
                {
                    NVector3 centre = NVector3.Zero;
                    foreach (var p in bone.Positions) centre += PendantBone.ToNipple(p);
                    centre /= bone.Positions.Length;
                    for (int i = 0; i < pos.Length; i++) pos[i] = PendantBone.ToNipple(bone.Positions[i]) - centre;
                }
                var mesh = new Mesh { name = "NipplePiercings." + model.Id + "." + bone.Name };
                var v = new Vector3[pos.Length];
                var n = new Vector3[nrm.Length];
                for (int i = 0; i < v.Length; i++) v[i] = U(pos[i]);
                for (int i = 0; i < n.Length; i++) n[i] = U(nrm[i]);
                mesh.vertices = v;
                mesh.normals = n;
                mesh.subMeshCount = subs.Count;
                for (int s = 0; s < subs.Count; s++) mesh.SetTriangles(subs[s].Indices, s);
                mesh.RecalculateBounds();
                mesh.hideFlags = HideFlags.DontUnloadUnusedAsset; // survives map changes (UnloadUnusedAssets)
                result[b] = mesh;
            }
            _meshes[model] = result;
            return result;
        }

        public void SetVisible(bool visible)
        {
            if (_root != null && _root.activeSelf != visible) _root.SetActive(visible);
        }

        /// <summary>Piece <paramref name="b"/> with its origin at <paramref name="at"/>, turned by <paramref name="rotation"/>,
        /// <paramref name="scale"/> metres per model unit.</summary>
        public void Place(int b, NVector3 at, NQuaternion rotation, float scale)
        {
            _bones[b].SetPositionAndRotation(U(at), new Quaternion(rotation.X, rotation.Y, rotation.Z, rotation.W));
            _bones[b].localScale = Vector3.one * scale;
        }

        public void Destroy()
        {
            if (_root != null) UnityEngine.Object.Destroy(_root);
        }

        internal static Vector3 U(NVector3 v) => new Vector3(v.X, v.Y, v.Z);
        internal static NVector3 N(Vector3 v) => new NVector3(v.x, v.y, v.z);
    }

    /// <summary>
    /// The set's materials as copies of the game's handcuff material (UnityChan toon shader: lit and outlined like the game's own
    /// items). Metal in a light silver (the toon shader has no reflections; the model's dark metal grey looked like iron). Gems
    /// (crystal, ruby) on the toon shader's see-through variant (TransClipping; the plain transparent variant is not in the
    /// game [observed 2026-10-25]), a little see-through, with rim light and a highlight, and a sparkle that <see cref="Sparkle"/>
    /// brightens for a moment.
    /// </summary>
    internal sealed class Materials
    {
        private readonly Material[] _mats;
        private readonly bool[] _gem;
        private readonly Color[] _high;

        public Materials(PiercingSet set)
        {
            Material source = FindHandcuffMaterial();
            Shader seeThrough = Shader.Find("UnityChanToonShader/Toon_DoubleShadeWithFeather_TransClipping");
            _mats = new Material[set.Materials.Count];
            _gem = new bool[_mats.Length];
            _high = new Color[_mats.Length];
            for (int i = 0; i < _mats.Length; i++)
            {
                var m = set.Materials[i];
                Material mat;
                if (source != null) mat = new Material(source);
                else
                {
                    Shader shader = Shader.Find("UnityChanToonShader/Toon_DoubleShadeWithFeather");
                    if (shader == null) shader = Shader.Find("Standard");
                    mat = new Material(shader);
                }
                mat.name = "NipplePiercings." + m.Name;
                mat.hideFlags = HideFlags.DontUnloadUnusedAsset;
                Color c = m.Metallic > 0.5f ? new Color(0.80f, 0.82f, 0.86f, 1f) : new Color(m.Color.X, m.Color.Y, m.Color.Z, 1f).gamma;
                _gem[i] = m.IsGem;
                if (m.IsGem)
                {
                    if (seeThrough != null) mat.shader = seeThrough;
                    c.a = 0.75f;
                    Set(mat, "_ClippingMask", Texture2D.whiteTexture);
                    Set(mat, "_IsBaseMapAlphaAsClippingMask", 1f);
                    Set(mat, "_Tweak_transparency", -0.25f); // a little see-through
                    Set(mat, "_RimLight", 1f);
                    Set(mat, "_RimLightColor", Color.Lerp(c, Color.white, 0.6f));
                    Set(mat, "_RimLight_Power", 0.45f);
                    Set(mat, "_RimLight_InsideMask", 0.0001f);
                    mat.renderQueue = 3000;
                }
                else
                {
                    Set(mat, "_RimLight", 1f);                // a metal shine along the edges
                    Set(mat, "_RimLightColor", new Color(1f, 1f, 1f, 1f));
                    Set(mat, "_RimLight_Power", 0.25f);
                }
                // The toon outline is sized for the game's large items: on thin chain links it drew them nearly black
                // [observed 2026-10-25, the heart pendant's chain]. A third of it keeps the cartoon edge.
                if (mat.HasProperty("_Outline_Width")) mat.SetFloat("_Outline_Width", mat.GetFloat("_Outline_Width") * 0.35f);
                _high[i] = m.IsGem ? Color.white : new Color(0.9f, 0.9f, 0.9f, 1f);
                Set(mat, "_HighColor", _high[i] * 0.5f);
                Set(mat, "_HighColor_Power", m.IsGem ? 0.25f : 0.15f);
                foreach (string tex in new[] { "_MainTex", "_BaseMap", "_1st_ShadeMap", "_2nd_ShadeMap" })
                    if (mat.HasProperty(tex)) mat.SetTexture(tex, Texture2D.whiteTexture);
                Set(mat, "_BaseColor", c);
                Set(mat, "_Color", c);
                Set(mat, "_1st_ShadeColor", new Color(c.r * 0.75f, c.g * 0.75f, c.b * 0.75f, c.a));
                Set(mat, "_2nd_ShadeColor", new Color(c.r * 0.5f, c.g * 0.5f, c.b * 0.5f, c.a));
                _mats[i] = mat;
            }
        }

        public Material Get(int i) => _mats[i];

        /// <summary>The gems' sparkle: 0 = their resting highlight, 1 = a bright flash.</summary>
        public void Sparkle(float amount)
        {
            for (int i = 0; i < _mats.Length; i++)
            {
                if (!_gem[i]) continue;
                Set(_mats[i], "_HighColor", _high[i] * (0.5f + 2.5f * amount));
                Set(_mats[i], "_HighColor_Power", 0.25f + 0.35f * amount);
            }
        }

        private static void Set(Material m, string name, float v) { if (m.HasProperty(name)) m.SetFloat(name, v); }
        private static void Set(Material m, string name, Color v) { if (m.HasProperty(name)) m.SetColor(name, v); }
        private static void Set(Material m, string name, Texture v) { if (m.HasProperty(name)) m.SetTexture(name, v); }

        /// <summary>The game's handcuff material (name "tejyou..."), found among the loaded materials [observed 2026-10-11, SpreaderBar].</summary>
        private static Material FindHandcuffMaterial()
        {
            try
            {
                var all = Resources.FindObjectsOfTypeAll(Il2CppType.Of<Material>());
                foreach (var o in all)
                {
                    var mat = o.TryCast<Material>();
                    if (mat != null && mat.name != null && mat.name.StartsWith("tejyou", StringComparison.Ordinal)) return mat;
                }
            }
            catch (Exception e) { Plugin.Logger.LogWarning("handcuff material lookup failed: " + e.Message); }
            Plugin.Logger.LogWarning("handcuff material not found; using a toon/standard shader");
            return null;
        }
    }
}
