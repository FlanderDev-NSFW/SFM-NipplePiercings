using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text;

namespace NipplePiercings.Logic
{
    /// <summary>A material of the models: its base colour, how metallic and how see-through (transmission) it is.</summary>
    public sealed class PendantMaterial
    {
        public string Name = "";
        public Vector4 Color;
        public float Metallic, Transmission;

        /// <summary>A gem (see-through and not metal): drawn transparent, with rim light and sparkle.</summary>
        public bool IsGem => Transmission > 0.3f || (Metallic < 0.5f && Name.IndexOf("ruby", StringComparison.OrdinalIgnoreCase) >= 0);
    }

    /// <summary>One rigid piece of a piercing and the bone it hangs on (exported by tools/export_model.py).</summary>
    public sealed class PendantBone
    {
        public string Name = "";
        public int Parent = -1;
        public Vector3 Head, Tail;            // Blender axes, relative to the piercing's attach point
        public Vector3[] Positions = Array.Empty<Vector3>();
        public Vector3[] Normals = Array.Empty<Vector3>();
        public List<(int Material, int[] Indices)> Submeshes = new List<(int, int[])>();

        /// <summary>
        /// Blender axes to the nipple's frame (the game's piercing anchor: +z out of the nipple, +y up, +x her right):
        /// (x, y, z) -> (-x, z, -y). The models face the viewer along Blender's -y, so their front goes out of the nipple, and
        /// Blender's +x (the viewer's right, her left) becomes her left. The map flips handedness (Blender is right-handed,
        /// Unity left-handed), so the shape is not mirrored, but the triangles' winding has to be reversed for Unity.
        /// </summary>
        public static Vector3 ToNipple(Vector3 b) => new Vector3(-b.X, b.Z, -b.Y);

        /// <summary>The piece in the nipple's frame, relative to the bone's head (the bone's pivot), winding reversed.</summary>
        public (Vector3[] Positions, Vector3[] Normals, List<(int Material, int[] Indices)> Submeshes) ToLocal()
        {
            var head = ToNipple(Head);
            var pos = new Vector3[Positions.Length];
            var nrm = new Vector3[Normals.Length];
            for (int i = 0; i < pos.Length; i++)
            {
                pos[i] = ToNipple(Positions[i]) - head;
                nrm[i] = ToNipple(Normals[i]);
            }
            var subs = new List<(int, int[])>();
            foreach (var (mat, idx) in Submeshes)
            {
                var flipped = (int[])idx.Clone();
                for (int t = 0; t + 2 < flipped.Length; t += 3) (flipped[t + 1], flipped[t + 2]) = (flipped[t + 2], flipped[t + 1]);
                subs.Add((mat, flipped));
            }
            return (pos, nrm, subs);
        }

        /// <summary>How far the piece reaches from its bone's axis (the line head - tail), in model units.</summary>
        public float HalfWidth()
        {
            Vector3 axis = Tail - Head;
            float len2 = axis.LengthSquared();
            float best = 0f;
            foreach (var p in Positions)
            {
                Vector3 d = p - Head;
                float t = len2 < 1e-12f ? 0f : Math.Clamp(Vector3.Dot(d, axis) / len2, 0f, 1f);
                best = Math.Max(best, (d - axis * t).Length());
            }
            return best;
        }

        /// <summary>Half the piece's thickness front to back (Blender y) about its bone's axis, in model units: what keeps it off
        /// the skin it hangs against (a ring is wide but thin; <see cref="HalfWidth"/> would hold it a ring's radius away).</summary>
        public float HalfDepth()
        {
            float best = 0f;
            Vector3 axis = Tail - Head;
            float len2 = axis.LengthSquared();
            foreach (var p in Positions)
            {
                float t = len2 < 1e-12f ? 0f : Math.Clamp(Vector3.Dot(p - Head, axis) / len2, 0f, 1f);
                best = Math.Max(best, Math.Abs(p.Y - (Head.Y + axis.Y * t)));
            }
            return best;
        }
    }

    public enum PiercingKind : byte
    {
        Tip = 0,      // sits on the nipple's tip (a stud)
        Through = 1,  // goes through the nipple (barbells, rings)
        Link = 2,     // a chain link for the chain between the nipples
    }

    /// <summary>One piercing: its bones (one chain, the first fixed to the nipple), placed by its kind.</summary>
    public sealed class PiercingModel
    {
        public string Id = "";
        public PiercingKind Kind;
        public Vector3 Hook;                  // Blender axes: where a chain between the nipples hangs from ...
        public int HookBone;                  // ... on this bone
        public readonly List<PendantBone> Bones = new List<PendantBone>();

        /// <summary>It has hanging pieces (more than the one fixed to the nipple).</summary>
        public bool Swings => Bones.Count >= 2;

        /// <summary>The joints top to bottom in model units, nipple frame: the first bone's head, then each bone's tail (a chain).</summary>
        public Vector3[] Joints()
        {
            var j = new Vector3[Bones.Count + 1];
            j[0] = PendantBone.ToNipple(Bones[0].Head);
            for (int i = 0; i < Bones.Count; i++) j[i + 1] = PendantBone.ToNipple(Bones[i].Tail);
            return j;
        }

        /// <summary>From its highest to its lowest point, in model units.</summary>
        public float Length()
        {
            float top = float.MinValue, bottom = float.MaxValue;
            foreach (var b in Bones)
                foreach (var p in b.Positions)
                {
                    top = Math.Max(top, p.Z);
                    bottom = Math.Min(bottom, p.Z);
                }
            return top - bottom;
        }

        /// <summary>Per segment (bone) half thickness front to back, model units (collision margins).</summary>
        public float[] Thickness()
        {
            var t = new float[Bones.Count];
            for (int b = 0; b < t.Length; b++) t[b] = Bones[b].HalfDepth();
            return t;
        }
    }

    /// <summary>All piercings and their shared materials (assets/piercings.npd).</summary>
    public sealed class PiercingSet
    {
        public const uint Magic = 0x4450504E; // "NPPD"
        public readonly List<PendantMaterial> Materials = new List<PendantMaterial>();
        public readonly List<PiercingModel> Models = new List<PiercingModel>();

        public PiercingModel Get(string id) => Models.Find(m => string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase));

        /// <summary>The ids a player can wear (not the chain link).</summary>
        public List<string> Wearable()
        {
            var ids = new List<string>();
            foreach (var m in Models) if (m.Kind != PiercingKind.Link) ids.Add(m.Id);
            return ids;
        }

        public static PiercingSet Read(Stream stream)
        {
            using var r = new BinaryReader(stream);
            if (r.ReadUInt32() != Magic) throw new InvalidDataException("not a piercing model file (magic)");
            uint version = r.ReadUInt32();
            if (version != 2) throw new InvalidDataException("piercing model version " + version + " (expected 2)");
            var set = new PiercingSet();
            int mats = checked((int)r.ReadUInt32());
            for (int i = 0; i < mats; i++)
                set.Materials.Add(new PendantMaterial
                {
                    Name = Str(r),
                    Color = new Vector4(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle()),
                    Metallic = r.ReadSingle(),
                    Transmission = r.ReadSingle(),
                });
            int models = checked((int)r.ReadUInt32());
            for (int m = 0; m < models; m++)
            {
                var model = new PiercingModel { Id = Str(r), Kind = (PiercingKind)r.ReadByte(), Hook = V(r), HookBone = checked((int)r.ReadUInt32()) };
                int bones = checked((int)r.ReadUInt32());
                if (bones < 1) throw new InvalidDataException(model.Id + ": no bones");
                if (model.HookBone >= bones) throw new InvalidDataException(model.Id + ": hook on bone " + model.HookBone + " of " + bones);
                for (int i = 0; i < bones; i++)
                {
                    var b = new PendantBone { Name = Str(r), Parent = r.ReadInt32(), Head = V(r), Tail = V(r) };
                    if (b.Parent != i - 1) throw new InvalidDataException(model.Id + "." + b.Name + ": not a single chain (parent " + b.Parent + ")");
                    int n = checked((int)r.ReadUInt32());
                    b.Positions = new Vector3[n];
                    b.Normals = new Vector3[n];
                    for (int k = 0; k < n; k++) b.Positions[k] = V(r);
                    for (int k = 0; k < n; k++) b.Normals[k] = V(r);
                    int subs = checked((int)r.ReadUInt32());
                    for (int s = 0; s < subs; s++)
                    {
                        int mat = checked((int)r.ReadUInt32());
                        if (mat >= mats) throw new InvalidDataException(model.Id + "." + b.Name + ": material " + mat + " of " + mats);
                        int ni = checked((int)r.ReadUInt32());
                        if (ni % 3 != 0) throw new InvalidDataException(model.Id + "." + b.Name + ": " + ni + " indices");
                        var idx = new int[ni];
                        for (int k = 0; k < ni; k++)
                        {
                            uint v = r.ReadUInt32();
                            if (v >= n) throw new InvalidDataException(model.Id + "." + b.Name + ": index " + v + " of " + n);
                            idx[k] = (int)v;
                        }
                        b.Submeshes.Add((mat, idx));
                    }
                    model.Bones.Add(b);
                }
                set.Models.Add(model);
            }
            return set;
        }

        private static string Str(BinaryReader r) => Encoding.UTF8.GetString(r.ReadBytes(r.ReadByte()));
        private static Vector3 V(BinaryReader r) => new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
    }
}
