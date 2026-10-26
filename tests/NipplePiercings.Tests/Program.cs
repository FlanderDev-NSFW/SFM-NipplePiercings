using System;
using System.IO;
using System.Numerics;
using NipplePiercings.Logic;

namespace NipplePiercings.Tests
{
    internal static class Program
    {
        private static int _passed, _failed;

        private static void Check(bool ok, string name)
        {
            if (ok) { _passed++; Console.WriteLine("  PASS  " + name); }
            else { _failed++; Console.WriteLine("  FAIL  " + name); }
        }

        private static bool Near(float a, float b, float eps = 1e-4f) => Math.Abs(a - b) <= eps;

        private static int Main()
        {
            string modelPath = Path.Combine(AppContext.BaseDirectory, "piercings.npd");
            if (File.Exists(modelPath)) ModelTests(modelPath);
            else Console.WriteLine("  SKIP  model tests (" + modelPath + " missing: export it with mods/NipplePiercings/tools/export_model.py)");
            FormatErrorTests();
            ChainTests();
            ColliderTests();
            SaggingTests();
            ApexTests();
            SoundTests();

            Console.WriteLine(_passed + " passed, " + _failed + " failed");
            return _failed == 0 ? 0 : 1;
        }

        private static void ModelTests(string path)
        {
            PiercingSet set;
            using (var f = File.OpenRead(path)) set = PiercingSet.Read(f);
            var ids = set.Wearable();
            Check(ids.Count == 11 && set.Get("link")?.Kind == PiercingKind.Link, "set: 11 piercings and the chain link (" + string.Join(",", ids) + ")");
            Check(set.Materials.Exists(x => x.Metallic > 0.9f && !x.IsGem) && set.Materials.FindAll(x => x.IsGem).Count == 2,
                  "set: metal, and two gems (crystal, ruby)");
            var m = set.Get("crystal");
            Check(m.Kind == PiercingKind.Tip && m.Bones.Count == 8 && m.Bones[0].Name == "stud" && m.Bones[7].Name == "pendant", "crystal: stud, six links, pendant");
            Check(m.Bones[0].Head.Length() < 1e-4f, "crystal: origin at the stud's centre");
            var j = m.Joints();
            bool down = true;
            for (int k = 1; k < j.Length; k++) down &= j[k].Y < j[k - 1].Y - 1e-3f;
            Check(j.Length == 9 && down, "crystal: joints top to bottom (nipple frame y falls)");
            Check(Near(m.Length(), 2.67f, 0.02f), "crystal: 2.67 units (subdivided) from the stud's top to the tip (" + m.Length() + ")");
            Check(PendantBone.ToNipple(new Vector3(0f, -1f, 0f)) == Vector3.UnitZ && PendantBone.ToNipple(Vector3.UnitZ) == Vector3.UnitY,
                  "axes: Blender's front (-y) out of the nipple, up stays up");
            Check(set.Get("barbell").Kind == PiercingKind.Through && !set.Get("barbell").Swings && !set.Get("curved").Swings, "barbells: through, rigid");
            var ring = set.Get("ring");
            Check(ring.Swings && ring.Bones.Count == 2 && ring.Bones[0].Positions.Length == 0 && ring.Joints()[1].Length() < 1e-4f,
                  "ring: swings about the pierced point (an empty first bone ending there)");
            Check(ring.Bones[1].HalfDepth() < 0.08f && ring.Bones[1].HalfWidth() > 0.2f, "ring: thin front to back (bead included), wide across (" + ring.Bones[1].HalfDepth() + " / " + ring.Bones[1].HalfWidth() + ")");
            Check(set.Get("heart").Bones.Count == 8 && set.Get("heart").HookBone == 0, "heart: bar, six links, heart; hook on the bar");
            Check(Near(ring.Hook.Z, ring.Bones[1].Tail.Z, 1e-4f), "ring: chain hook at its bottom");

            var stud = m.Bones[0];
            var (pos, _, subs) = stud.ToLocal();
            Check(subs[0].Indices[1] == stud.Submeshes[0].Indices[2] && subs[0].Indices[2] == stud.Submeshes[0].Indices[1], "local: winding flipped");
            int agree = 0, total = 0;
            foreach (var model in set.Models)
                foreach (var bone in model.Bones)
                {
                    var (p, n, sm) = bone.ToLocal();
                    foreach (var (_, idx) in sm)
                        for (int i = 0; i < idx.Length; i += 3 * 7)
                        {
                            Vector3 a = p[idx[i]], bb = p[idx[i + 1]], c = p[idx[i + 2]];
                            Vector3 g = Vector3.Cross(bb - a, c - a);
                            if (g.LengthSquared() < 1e-14f) continue;
                            total++;
                            if (Vector3.Dot(g, n[idx[i]] + n[idx[i + 1]] + n[idx[i + 2]]) > 0) agree++;
                        }
                }
            Check(total > 1000 && agree > total * 0.9f, "local: triangles face the way their normals do (" + agree + "/" + total + ")");
            Check(pos.Length > 0 && Near(stud.HalfWidth(), 0.12f, 0.03f), "stud: ball about 0.12 units in radius (" + stud.HalfWidth() + ")");
            Check(m.Bones[7].HalfWidth() > 0.25f && m.Bones[1].HalfWidth() < 0.09f,
                  "thickness: crystal wide, links thin (" + m.Bones[7].HalfWidth() + " / " + m.Bones[1].HalfWidth() + ")");
        }

        private static void FormatErrorTests()
        {
            bool Throws(byte[] data)
            {
                try { PiercingSet.Read(new MemoryStream(data)); return false; }
                catch (InvalidDataException) { return true; }
                catch (EndOfStreamException) { return true; }
            }
            Check(Throws(new byte[] { 1, 2, 3, 4, 2, 0, 0, 0 }), "format: wrong magic refused");
            var ms = new MemoryStream();
            var w = new BinaryWriter(ms);
            w.Write(PiercingSet.Magic); w.Write(1u);
            Check(Throws(ms.ToArray()), "format: old version refused");
            ms = new MemoryStream();
            w = new BinaryWriter(ms);
            w.Write(PiercingSet.Magic); w.Write(2u); w.Write(0u); w.Write(3u);
            Check(Throws(ms.ToArray()), "format: truncated file refused");
        }

        private static void ColliderTests()
        {
            var sphere = Collider.Sphere(Vector3.Zero, 1f);
            Check(Near(sphere.Depth(new Vector3(0, 0.5f, 0), 0.1f, out var n1), 0.6f) && n1 == Vector3.UnitY, "collider: sphere depth and way out");
            var cap = Collider.Capsule(Vector3.Zero, new Vector3(2, 0, 0), 0.5f);
            Check(Near(cap.Depth(new Vector3(1, 0.2f, 0), 0f, out var n2), 0.3f) && n2 == Vector3.UnitY, "collider: capsule measured from its axis");
            Check(cap.Depth(new Vector3(3, 0, 0), 0f, out _) < 0f, "collider: past the capsule's end -> outside");
            var floor = Collider.Plane(Vector3.Zero, Vector3.UnitY);
            Check(Near(floor.Depth(new Vector3(5, -0.2f, 3), 0.05f, out var n3), 0.25f) && n3 == Vector3.UnitY, "collider: below the floor");
        }

        private static void SaggingTests()
        {
            var c = new SaggingChain(12, 0.02f, 0.002f);  // 24 cm of chain between hooks 16 cm apart
            Vector3 a = new Vector3(-0.08f, 1f, 0f), b = new Vector3(0.08f, 1f, 0f);
            for (float t = 0; t < 3f; t += 1f / 60f) c.Update(1f / 60f, a, b);
            float low = float.MaxValue, worst = 0f;
            for (int k = 0; k < c.Count; k++) low = Math.Min(low, c.Joint(k).Y);
            for (int k = 0; k + 1 < c.Count; k++) worst = Math.Max(worst, Math.Abs(Vector3.Distance(c.Joint(k), c.Joint(k + 1)) - 0.02f));
            Check(c.Joint(0) == a && c.Joint(c.Count - 1) == b, "sagging: both ends on the hooks");
            Check(low < 0.95f && low > 0.88f, "sagging: hangs 5-12 cm below the hooks (" + (1f - low).ToString("0.000") + " m)");
            Check(worst < 0.002f, "sagging: links keep their pitch within 2 mm (" + worst.ToString("0.0000") + ")");
            Check(Math.Abs(c.Joint(6).X) < 0.005f, "sagging: lowest in the middle");

            // A sphere (a breast) under one end: the chain stays out of it.
            c = new SaggingChain(12, 0.02f, 0.002f);
            c.Colliders.Add(Collider.Sphere(new Vector3(-0.08f, 0.93f, -0.02f), 0.06f));
            for (float t = 0; t < 3f; t += 1f / 60f) c.Update(1f / 60f, a, b);
            float inside = 0f;
            for (int k = 1; k + 1 < c.Count; k++) inside = Math.Max(inside, c.Colliders[0].Depth(c.Joint(k), 0.002f, out _));
            Check(inside < 0.001f, "sagging: stays out of a sphere (" + inside.ToString("0.0000") + " m)");

            // Links: centres between joints; neighbours turned 90 degrees about the chain.
            var (centre, r0) = c.Link(0);
            var (_, r1) = c.Link(1);
            Check(Vector3.Distance(centre, (c.Joint(0) + c.Joint(1)) / 2f) < 1e-6f, "link: centred between its joints");
            Vector3 axis0 = Vector3.Normalize(c.Joint(1) - c.Joint(0));
            Check(Math.Abs(Math.Abs(Vector3.Dot(Vector3.Transform(Vector3.UnitX, r0), axis0)) - 1f) < 1e-3f, "link: long axis (x) along the chain");
            Check(Math.Abs(Vector3.Dot(Vector3.Transform(Vector3.UnitZ, r0), Vector3.Transform(Vector3.UnitZ, r1))) < 0.3f,
                  "link: neighbours' flat sides at right angles");
            var q = SaggingChain.LookRotation(Vector3.UnitX, Vector3.UnitY);
            Check(Vector3.Distance(Vector3.Transform(Vector3.UnitZ, q), Vector3.UnitX) < 1e-5f && Vector3.Distance(Vector3.Transform(Vector3.UnitY, q), Vector3.UnitY) < 1e-5f,
                  "look rotation: z onto forward, y onto up");
        }

        private static void SoundTests()
        {
            float[] s = Chime.Synthesize(44100, 0.7f, 2350f);
            float peak = 0f, early = 0f, late = 0f;
            for (int i = 0; i < s.Length; i++)
            {
                peak = Math.Max(peak, Math.Abs(s[i]));
                if (i < 4410) early = Math.Max(early, Math.Abs(s[i]));
                if (i > s.Length - 4410) late = Math.Max(late, Math.Abs(s[i]));
            }
            Check(s.Length == 30870 && Near(peak, 0.6f, 1e-3f), "chime: 0.7 s at 44.1 kHz, peak 0.6");
            Check(late < early * 0.05f && Math.Abs(s[0]) < 1e-3f, "chime: soft start, rings out (" + (late / early).ToString("0.000") + ")");

            var j = new JingleTrigger();
            Check(j.Update(0.1f, 0f) == 0f && j.Update(0.2f, 0.02f) == 0f, "jingle: gentle swing is silent");
            float loud = j.Update(0.8f, 0.04f);
            Check(loud > 0.2f && loud < 1f, "jingle: a jolt rings (" + loud.ToString("0.00") + ")");
            Check(j.Update(1.2f, 0.06f) == 0f, "jingle: not again within the cooldown");
            Check(j.Update(1.0f, 0.5f) == 0f, "jingle: falling speed does not ring");
            Check(j.Update(1.5f, 0.6f) == 1f, "jingle: a hard jolt rings at full loudness");
        }

        private static void ApexTests()
        {
            // A breast (sphere r 0.1 around the origin) with a nipple tip at +z; the anchor sits inside the nipple, a little high.
            var pts = new System.Collections.Generic.List<Vector3>();
            for (int i = 0; i < 400; i++)
            {
                float a = i * 2.39996f, z = 1f - 2f * (i + 0.5f) / 400f, r = MathF.Sqrt(1f - z * z);
                pts.Add(new Vector3(MathF.Cos(a) * r, MathF.Sin(a) * r, z) * 0.1f);
            }
            pts.Add(new Vector3(0f, -0.002f, 0.112f)); // the tip
            pts.Add(new Vector3(0.3f, 0f, 0.5f));      // far away (another body part), further out
            var anchor = new Vector3(0f, 0.004f, 0.104f);
            int k = NippleApex.Find(pts, anchor, Vector3.UnitZ, 0.04f);
            Check(k == 400, "apex: the tip, not the anchor's height or a far point (" + k + ")");
            Check(NippleApex.Find(pts, new Vector3(5f, 0, 0), Vector3.UnitZ, 0.04f) == -1, "apex: nothing near -> -1");
            // A hand in front of the breast, 3 cm out and 2.5 cm aside of the nipple: within the old 4 cm it won.
            pts.Add(new Vector3(0.025f, 0.004f, 0.134f));
            float reach = NippleApex.Radius(2.2385f);
            Check(reach > 0.025f && reach < 0.03f, "apex: radius 1.2 x the point's scale, 2.7 cm on the first save (" + reach + ")");
            Check(NippleApex.Find(pts, anchor, Vector3.UnitZ, 0.04f) == 402, "apex (control): with 4 cm the hand wins");
            Check(NippleApex.Find(pts, anchor, Vector3.UnitZ, reach) == 400, "apex: with the radius and the axis limit the tip wins over the hand");
            pts.Add(new Vector3(0f, 0.004f, 0.128f)); // something right on the axis 2.4 cm out
            Check(NippleApex.Find(pts, anchor, Vector3.UnitZ, reach) == 403, "apex: on the axis within reach it still counts (searched again each bake)");
        }

        // A 1 m chain: stud 0.1 m, four links 0.1 m, a 0.5 m heavy tip, hanging along -y in the nipple frame.
        private static PendantChain Chain()
        {
            var rest = new[] { Vector3.Zero, new Vector3(0, -0.1f, 0), new Vector3(0, -0.2f, 0), new Vector3(0, -0.3f, 0),
                               new Vector3(0, -0.4f, 0), new Vector3(0, -0.5f, 0), new Vector3(0, -1.0f, 0) };
            return new PendantChain(rest, new[] { 0f, 0.01f, 0.01f, 0.01f, 0.01f, 0.05f });
        }

        private static void Run(PendantChain c, float seconds, Vector3 anchor, Quaternion rot)
        {
            for (float t = 0; t < seconds; t += 1f / 60f) c.Update(1f / 60f, anchor, rot, 1f);
        }

        // How far the tip is from straight below the stud's end.
        private static float Bend(PendantChain c) => Vector3.Distance(c.Joint(c.Count - 1), c.Joint(1) - new Vector3(0, 0.9f, 0));

        private static void ChainTests()
        {
            var q = PendantChain.FromTo(Vector3.UnitX, Vector3.UnitY);
            Check(Vector3.Distance(Vector3.Transform(Vector3.UnitX, q), Vector3.UnitY) < 1e-5f, "from-to: x onto y");
            Check(Vector3.Distance(Vector3.Transform(Vector3.UnitZ, PendantChain.FromTo(Vector3.UnitZ, -Vector3.UnitZ)), -Vector3.UnitZ) < 1e-5f,
                  "from-to: opposite directions");

            // Tilted nipple frame: the chain still hangs straight down (gravity, not the frame).
            var tilt = Quaternion.CreateFromAxisAngle(Vector3.UnitX, 0.6f);
            var c = Chain();
            Run(c, 0.1f, Vector3.Zero, tilt);
            Vector3 stud = Vector3.Transform(new Vector3(0, -0.1f, 0), tilt);
            Check(Vector3.Distance(c.Joint(1), stud) < 1e-5f, "chain: stud end fixed to the nipple frame");
            Check(Vector3.Distance(c.Joint(6), stud + new Vector3(0, -0.9f, 0)) < 1e-3f, "chain: hangs straight down from the stud");

            // The nipple moves 0.2 m sideways at once: the chain swings, keeps its lengths, then settles below the stud again.
            c = Chain();
            Run(c, 0.5f, Vector3.Zero, Quaternion.Identity);
            float maxBend = 0f, worstLength = 0f;
            var side = new Vector3(0.2f, 0, 0);
            for (float t = 0; t < 0.6f; t += 1f / 60f)
            {
                c.Update(1f / 60f, side, Quaternion.Identity, 1f);
                maxBend = Math.Max(maxBend, Bend(c));
                for (int k = 1; k + 1 < c.Count; k++)
                {
                    float want = k == 5 ? 0.5f : 0.1f;
                    worstLength = Math.Max(worstLength, Math.Abs(Vector3.Distance(c.Joint(k), c.Joint(k + 1)) - want));
                }
            }
            Check(maxBend > 0.05f, "chain: swings when the nipple moves (" + maxBend.ToString("0.000") + " m)");
            Check(worstLength < 1e-5f, "chain: segment lengths exact while swinging (" + worstLength.ToString("0.0000") + ")");
            Run(c, 8f, side, Quaternion.Identity);
            Check(Bend(c) < 0.005f, "chain: settles below the stud (damping, " + Bend(c).ToString("0.0000") + " m)");

            // Damping is what stops it: one second after the jump the undamped chain swings far wider (control).
            float Late(float damping)
            {
                var ch = Chain();
                ch.Damping = damping;
                Run(ch, 0.5f, Vector3.Zero, Quaternion.Identity);
                float most = 0f;
                for (float t = 0; t < 1.5f; t += 1f / 60f)
                {
                    ch.Update(1f / 60f, side, Quaternion.Identity, 1f);
                    if (t > 1f) most = Math.Max(most, Bend(ch));
                }
                return most;
            }
            float damped = Late(1.5f), free = Late(0f);
            Check(free > damped * 2f, "chain: without damping it swings on (" + free.ToString("0.000") + " vs " + damped.ToString("0.000") + " m)");

            // A sphere (the breast) moves in from the side into the hanging chain: it is pushed aside, nothing ends up inside, and
            // it comes to rest against the sphere.
            c = Chain();
            Run(c, 0.5f, Vector3.Zero, Quaternion.Identity);
            float inside = 0f;
            for (float t = 0; t < 6f; t += 1f / 60f)
            {
                c.Colliders.Clear();
                c.Colliders.Add(Collider.Sphere(new Vector3(-0.6f + 0.45f * Math.Min(t / 2f, 1f), -0.6f, 0f), 0.25f));
                c.Update(1f / 60f, Vector3.Zero, Quaternion.Identity, 1f);
                for (int k = 2; k < c.Count; k++) inside = Math.Max(inside, c.Colliders[0].Depth(c.Joint(k), 0f, out _));
            }
            Check(inside < 0.005f, "sphere: no joint inside (" + inside.ToString("0.0000") + " m)");
            Check(c.Joint(6).X > 0.05f, "sphere: the chain is pushed aside (tip x " + c.Joint(6).X.ToString("0.000") + ")");
            var rested = c.Joint(6);
            Run(c, 0.5f, Vector3.Zero, Quaternion.Identity);
            Check(Vector3.Distance(c.Joint(6), rested) < 0.002f, "sphere: at rest against it (" + Vector3.Distance(c.Joint(6), rested).ToString("0.0000") + " m in 0.5 s)");
            c.Collide = false;
            Run(c, 8f, Vector3.Zero, Quaternion.Identity);
            Check(Vector3.Distance(c.Joint(6), new Vector3(0, -1f, 0)) < 0.01f, "sphere: collisions off -> hangs through it (control)");

            // At the pendant's real size (4.5 cm, links ~1.2 mm): the stretch while hanging stays far below a link's size.
            var real = Chain();
            for (float t = 0; t < 2f; t += 1f / 60f) real.Update(1f / 60f, Vector3.Zero, Quaternion.Identity, 0.045f);
            float stretch = Vector3.Distance(real.Joint(1), real.Joint(6)) - 0.9f * 0.045f;
            Check(Math.Abs(stretch) < 1e-4f, "real size: hanging stretch under 0.1 mm (" + (stretch * 1000f).ToString("0.000") + " mm)");

            // The breast's sphere just behind the stud (the stud on its surface), as on her: hanging straight down clears it, so
            // the chain must hang straight down - not be levered out by a contact at its very top.
            c = Chain();
            c.Colliders.Add(Collider.Sphere(new Vector3(0f, -0.1f, -0.5f), 0.5f)); // the stud's end on the surface, the chain in front
            Run(c, 4f, Vector3.Zero, Quaternion.Identity);
            Check(Bend(c) < 0.05f, "sphere: stud on the surface -> hangs (nearly) straight down (" + Bend(c).ToString("0.000") + " m off)");

            // Teleport: a jump of 2 m hangs the chain afresh, no swing.
            c = Chain();
            Run(c, 0.5f, Vector3.Zero, Quaternion.Identity);
            c.Update(1f / 60f, new Vector3(2f, 0, 0), Quaternion.Identity, 1f);
            Check(Bend(c) < 1e-4f, "teleport: hung straight down at the new place");

            // Paused game (dt 0): nothing moves.
            c = Chain();
            Run(c, 0.5f, Vector3.Zero, Quaternion.Identity);
            c.Update(1f / 60f, side, Quaternion.Identity, 1f);
            var before = c.Joint(6);
            c.Update(0f, side, Quaternion.Identity, 1f);
            Check(c.Joint(6) == before, "pause: dt 0 changes nothing");

            // Segment rotation: turns the segment's rest direction (in the nipple frame) onto the joints as they hang.
            c = Chain();
            Run(c, 0.5f, Vector3.Zero, tilt);
            var r = c.Segment(5, tilt);
            Vector3 dir = Vector3.Transform(new Vector3(0, -0.5f, 0), r);
            Check(Vector3.Distance(dir, c.Joint(6) - c.Joint(5)) < 1e-3f, "segment: rest direction onto the hanging segment");
        }
    }
}
