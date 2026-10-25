using System;
using System.Collections.Generic;
using System.Numerics;

namespace NipplePiercings.Logic
{
    /// <summary>
    /// A hanging chain of rigid pieces, simulated as points at its joints. Joints 0 and 1 are the ends of the piece fixed to the
    /// nipple (a stud, a barbell); the rest swing under gravity, lose speed to damping and stay out of <see cref="Colliders"/>
    /// (her breasts, arms, the floor). Lengths are kept exactly by "follow the leader" with velocity correction (Müller et al. 2012, "Fast
    /// Simulation of Inextensible Hair and Fur"): each joint is put back at its distance from the one above, and the joint
    /// above loses the speed that fix would add. Plain position-based solving left a 4.5 cm chain ~1 mm stretched under its
    /// heavy crystal (more than a link's size). Everything is in world space (metres).
    /// </summary>
    public sealed class PendantChain
    {
        public const float StepSeconds = 1f / 120f;
        public const int MaxSteps = 8;        // per call; a longer frame is simulated as 8 steps (slow motion, not an explosion)
        public const float Correction = 0.9f; // share of the follow-the-leader fix taken from the joint above's speed (1 = all)
        public const float TeleportDistance = 0.5f; // m the stud may move in one call before the chain is hung afresh

        private readonly Vector3[] _rest;     // joints in the nipple frame, model units
        private readonly float[] _thickness;  // per segment, model units: half width of its piece (keeps it off the skin)
        private readonly Vector3[] _x, _v, _p, _fix, _contact; // _contact: outward normal of a joint touching the sphere, else 0
        private float _scale = 1f, _carry;
        private bool _hung;

        public float Damping = 1.5f;          // 1/s: share of the speed lost per second (exponential)
        public Vector3 Gravity = new Vector3(0f, -9.81f, 0f);
        public bool Collide = true;
        public readonly List<Collider> Colliders = new List<Collider>();

        public int Count => _x.Length;
        public Vector3 Joint(int k) => _x[k];

        /// <param name="rest">Joints top to bottom in the nipple frame (model units): stud centre, stud end, link ends ..., tip.</param>
        /// <param name="thickness">Half width of each segment's piece (model units), rest.Length - 1 values.</param>
        public PendantChain(Vector3[] rest, float[] thickness)
        {
            if (rest == null || rest.Length < 3) throw new ArgumentException("a chain needs at least three joints");
            if (thickness == null || thickness.Length != rest.Length - 1) throw new ArgumentException("one thickness per segment");
            _rest = (Vector3[])rest.Clone();
            _thickness = (float[])thickness.Clone();
            _x = new Vector3[rest.Length];
            _v = new Vector3[rest.Length];
            _p = new Vector3[rest.Length];
            _fix = new Vector3[rest.Length + 1];
            _contact = new Vector3[rest.Length];
        }

        /// <summary>Forget the motion: the next <see cref="Update"/> hangs the chain straight down from the stud.</summary>
        public void Reset() => _hung = false;

        private float Length(int k) => Vector3.Distance(_rest[k], _rest[k + 1]) * _scale;

        /// <summary>Advance by <paramref name="dt"/> seconds with the nipple at <paramref name="anchor"/> / <paramref name="rotation"/>
        /// (the nipple frame in the world) and <paramref name="scale"/> metres per model unit.</summary>
        public void Update(float dt, Vector3 anchor, Quaternion rotation, float scale)
        {
            Vector3 top = anchor + Vector3.Transform(_rest[0] * scale, rotation);
            Vector3 studEnd = anchor + Vector3.Transform(_rest[1] * scale, rotation);
            if (!_hung || Math.Abs(scale - _scale) > 1e-6f || Vector3.Distance(studEnd, _x[1]) > TeleportDistance)
            {
                _scale = scale;
                Hang(top, studEnd);
                return;
            }
            if (!(dt > 0f)) return;
            _carry = Math.Min(_carry + dt, MaxSteps * StepSeconds);
            int steps = (int)(_carry / StepSeconds);
            if (steps == 0) return;
            _carry -= steps * StepSeconds;
            // The stud moves in a straight line across the steps of this call.
            Vector3 top0 = _x[0], end0 = _x[1];
            float keep = (float)Math.Exp(-Damping * StepSeconds);
            float h = StepSeconds;
            for (int s = 1; s <= steps; s++)
            {
                float f = (float)s / steps;
                _x[0] = _p[0] = Vector3.Lerp(top0, top, f);
                _x[1] = _p[1] = Vector3.Lerp(end0, studEnd, f);
                for (int k = 2; k < _x.Length; k++)
                {
                    _v[k] = _v[k] * keep + Gravity * h;
                    _p[k] = _x[k] + _v[k] * h;
                }
                Array.Clear(_contact, 0, _contact.Length);
                for (int k = 0; k < _fix.Length; k++) _fix[k] = Vector3.Zero;
                bool collide = Collide && Colliders.Count > 0;
                for (int pass = 0; pass < (collide ? 3 : 1); pass++)
                {
                    if (collide)
                        for (int k = 1; k + 1 < _x.Length; k++) PushOut(k);
                    // Follow the leader: each joint back at its distance from the one above.
                    for (int k = 1; k + 1 < _x.Length; k++)
                    {
                        Vector3 d = _p[k + 1] - _p[k];
                        float len = d.Length();
                        Vector3 to = len < 1e-9f ? _p[k] + Vector3.Normalize(Gravity) * Length(k) : _p[k] + d * (Length(k) / len);
                        _fix[k + 1] += to - _p[k + 1];
                        _p[k + 1] = to;
                    }
                }
                for (int k = 2; k < _x.Length; k++)
                {
                    _v[k] = (_p[k] - _x[k]) / h - _fix[k + 1] * (Correction / h);
                    // Resting on the breast, not bouncing off it: no speed away from the sphere after a push.
                    float away = Vector3.Dot(_v[k], _contact[k]);
                    if (away > 0f) _v[k] -= _contact[k] * away;
                    _x[k] = _p[k];
                }
            }
        }

        /// <summary>The joints straight down from the stud's end, at rest.</summary>
        private void Hang(Vector3 top, Vector3 studEnd)
        {
            Vector3 down = Gravity.LengthSquared() > 1e-12f ? Vector3.Normalize(Gravity) : -Vector3.UnitY;
            _x[0] = top;
            _x[1] = studEnd;
            for (int k = 2; k < _x.Length; k++)
            {
                _x[k] = _x[k - 1] + down * Length(k - 1);
                _v[k] = Vector3.Zero;
            }
            _carry = 0f;
            _hung = true;
        }

        private static readonly float[] Samples = { 0.5f, 0.75f, 1f };

        /// <summary>Segment k (joints k, k + 1) out of the colliders, plus its piece's thickness: the lower joint moves (the segment
        /// turns about the joint above, as a hanging piece does). The segment is checked at its middle, three quarters and its lower
        /// end - not higher: a contact at the upper end cannot be solved by turning the segment (that is the segment above's job),
        /// and pushing the lower joint for it raised the crystal until it stood out sideways [observed 2026-10-25]; this also caps
        /// the push on the lower joint at twice the depth (larger factors set the chain flailing).</summary>
        private void PushOut(int k)
        {
            Vector3 a = _p[k], ab = _p[k + 1] - a;
            float margin = _thickness[k] * _scale, deepest = 0f, at = 1f;
            Vector3 way = Vector3.Zero;
            foreach (float t in Samples)
                foreach (var c in Colliders)
                {
                    float depth = c.Depth(a + ab * t, margin, out Vector3 n);
                    if (depth > deepest) { deepest = depth; way = n; at = t; }
                }
            if (deepest <= 0f) return;
            _p[k + 1] += way * (deepest / at);
            _contact[k + 1] = way;
        }

        /// <summary>Each segment's rotation: the nipple frame turned so the segment's rest direction points at where it hangs now.</summary>
        public Quaternion Segment(int k, Quaternion rotation)
        {
            Vector3 from = Vector3.Normalize(Vector3.Transform(_rest[k + 1] - _rest[k], rotation));
            Vector3 d = _x[k + 1] - _x[k];
            if (d.LengthSquared() < 1e-14f) return rotation;
            return Quaternion.Normalize(FromTo(from, Vector3.Normalize(d)) * rotation);
        }

        /// <summary>The shortest rotation taking unit vector <paramref name="a"/> to unit vector <paramref name="b"/>.</summary>
        public static Quaternion FromTo(Vector3 a, Vector3 b)
        {
            float dot = Vector3.Dot(a, b);
            if (dot > 0.999999f) return Quaternion.Identity;
            if (dot < -0.999999f)
            {
                Vector3 axis = Vector3.Cross(Vector3.UnitX, a);
                if (axis.LengthSquared() < 1e-6f) axis = Vector3.Cross(Vector3.UnitY, a);
                return Quaternion.CreateFromAxisAngle(Vector3.Normalize(axis), (float)Math.PI);
            }
            Vector3 c = Vector3.Cross(a, b);
            return Quaternion.Normalize(new Quaternion(c.X, c.Y, c.Z, 1f + dot));
        }
    }
}
