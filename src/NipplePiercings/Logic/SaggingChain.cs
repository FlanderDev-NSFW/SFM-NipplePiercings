using System;
using System.Collections.Generic;
using System.Numerics;

namespace NipplePiercings.Logic
{
    /// <summary>
    /// A chain hung between two points (the piercings' hooks): links as points, both ends held, sagging under gravity, losing
    /// speed to damping and kept out of <see cref="Colliders"/> (position-based Verlet; both ends held rules out the hanging
    /// pendants' follow-the-leader). The lengths are swept from both ends in turn, many times per step: a chain ~10 links long
    /// stays within a few per cent of its length, and its links overlap more than that. World space, metres.
    /// </summary>
    public sealed class SaggingChain
    {
        public const float StepSeconds = 1f / 120f;
        public const int MaxSteps = 8;
        public const int Iterations = 24;
        public const float TeleportDistance = 0.5f;

        private readonly Vector3[] _x, _prev, _contact;
        private readonly float _pitch, _thickness;
        private float _carry;
        private bool _hung;

        public float Damping = 1.5f;
        public Vector3 Gravity = new Vector3(0f, -9.81f, 0f);
        public bool Collide = true;
        public readonly List<Collider> Colliders = new List<Collider>();

        public int Count => _x.Length;
        public Vector3 Joint(int k) => _x[k];

        /// <param name="links">Number of links (segments).</param>
        /// <param name="pitch">Distance between the joints of a link, m.</param>
        /// <param name="thickness">Half thickness of a link, m (kept off the skin).</param>
        public SaggingChain(int links, float pitch, float thickness)
        {
            if (links < 2) throw new ArgumentException("a chain needs at least two links");
            _x = new Vector3[links + 1];
            _prev = new Vector3[links + 1];
            _contact = new Vector3[links + 1];
            _pitch = pitch;
            _thickness = thickness;
        }

        public void Reset() => _hung = false;

        /// <summary>The chain's length when hanging free, m.</summary>
        public float Length => _pitch * (_x.Length - 1);

        public void Update(float dt, Vector3 a, Vector3 b)
        {
            int n = _x.Length - 1;
            if (!_hung || Vector3.Distance(a, _x[0]) > TeleportDistance || Vector3.Distance(b, _x[n]) > TeleportDistance)
            {
                Hang(a, b);
                return;
            }
            if (!(dt > 0f)) return;
            _carry = Math.Min(_carry + dt, MaxSteps * StepSeconds);
            int steps = (int)(_carry / StepSeconds);
            if (steps == 0) return;
            _carry -= steps * StepSeconds;
            Vector3 a0 = _x[0], b0 = _x[n];
            float keep = (float)Math.Exp(-Damping * StepSeconds);
            Vector3 g = Gravity * (StepSeconds * StepSeconds);
            for (int s = 1; s <= steps; s++)
            {
                float f = (float)s / steps;
                _x[0] = _prev[0] = Vector3.Lerp(a0, a, f);
                _x[n] = _prev[n] = Vector3.Lerp(b0, b, f);
                for (int k = 1; k < n; k++)
                {
                    Vector3 v = (_x[k] - _prev[k]) * keep;
                    _prev[k] = _x[k];
                    _x[k] += v + g;
                    _contact[k] = Vector3.Zero;
                }
                for (int it = 0; it < Iterations; it++)
                {
                    if (it % 2 == 0) for (int k = 0; k < n; k++) Keep(k);
                    else for (int k = n - 1; k >= 0; k--) Keep(k);
                    if (Collide)
                        for (int k = 1; k < n; k++) PushOut(k);
                }
                // Resting against her, not bouncing off: no speed away from a collider after a push.
                for (int k = 1; k < n; k++)
                {
                    if (_contact[k] == Vector3.Zero) continue;
                    Vector3 v = _x[k] - _prev[k];
                    float away = Vector3.Dot(v, _contact[k]);
                    if (away > 0f) _prev[k] += _contact[k] * away;
                }
            }
        }

        /// <summary>A parabola between the ends with the chain's length (the sag of a hanging chain, near enough to start from).</summary>
        private void Hang(Vector3 a, Vector3 b)
        {
            int n = _x.Length - 1;
            float span = Vector3.Distance(a, b), length = Length;
            float sag = length > span ? (float)Math.Sqrt(3.0 / 8.0 * span * (length - span)) : 0f;
            Vector3 down = Gravity.LengthSquared() > 1e-12f ? Vector3.Normalize(Gravity) : -Vector3.UnitY;
            for (int k = 0; k <= n; k++)
            {
                float t = (float)k / n;
                _x[k] = _prev[k] = Vector3.Lerp(a, b, t) + down * (sag * 4f * t * (1f - t));
            }
            _carry = 0f;
            _hung = true;
        }

        private void Keep(int k)
        {
            int n = _x.Length - 1;
            float wa = k == 0 ? 0f : 1f, wb = k + 1 == n ? 0f : 1f;
            if (wa + wb <= 0f) return;
            Vector3 d = _x[k + 1] - _x[k];
            float len = d.Length();
            if (len < 1e-9f) return;
            Vector3 corr = d * ((len - _pitch) / (len * (wa + wb)));
            _x[k] += corr * wa;
            _x[k + 1] -= corr * wb;
        }

        private void PushOut(int k)
        {
            float deepest = 0f;
            Vector3 way = Vector3.Zero;
            foreach (var c in Colliders)
            {
                float depth = c.Depth(_x[k], _thickness, out Vector3 n);
                if (depth > deepest) { deepest = depth; way = n; }
            }
            if (deepest <= 0f) return;
            _x[k] += way * deepest;
            _contact[k] = way;
        }

        /// <summary>
        /// Link k (joints k, k + 1): its centre and rotation. The link mesh lies in its frame with its long axis along x and its
        /// flat side facing z (<see cref="PendantBone.ToNipple"/> of the exported link); neighbouring links are turned 90 degrees
        /// about the chain, as real chain links interlock.
        /// </summary>
        public (Vector3 Centre, Quaternion Rotation) Link(int k)
        {
            Vector3 a = _x[k], b = _x[k + 1], axis = b - a;
            float len = axis.Length();
            axis = len < 1e-9f ? Vector3.UnitX : axis / len;
            Vector3 side = Vector3.Cross(axis, Vector3.UnitY);
            if (side.LengthSquared() < 1e-6f) side = Vector3.Cross(axis, Vector3.UnitZ);
            side = Vector3.Normalize(side);
            Vector3 up = Vector3.Cross(side, axis);       // in the chain's vertical plane
            Vector3 face = k % 2 == 0 ? side : up;        // the link's flat side
            Vector3 plane = k % 2 == 0 ? up : -side;
            return ((a + b) / 2f, LookRotation(face, plane));
        }

        /// <summary>Unity's Quaternion.LookRotation: +z onto <paramref name="forward"/>, +y onto <paramref name="up"/> (both unit,
        /// perpendicular); +x = up x forward (left-handed).</summary>
        public static Quaternion LookRotation(Vector3 forward, Vector3 up)
        {
            Vector3 right = Vector3.Cross(up, forward);
            // Columns right, up, forward as a rotation matrix (System.Numerics stores rows: transpose).
            var m = new Matrix4x4(right.X, right.Y, right.Z, 0f, up.X, up.Y, up.Z, 0f, forward.X, forward.Y, forward.Z, 0f, 0f, 0f, 0f, 1f);
            return Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(m));
        }
    }
}
