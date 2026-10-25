using System.Collections.Generic;
using System.Numerics;

namespace NipplePiercings.Logic
{
    /// <summary>The nipple's tip on her body mesh: of the skin near the game's piercing point, the point furthest out along the
    /// nipple's direction.</summary>
    public static class NippleApex
    {
        /// <summary>Index of the apex among <paramref name="points"/> (world space): within <paramref name="radius"/> of
        /// <paramref name="anchor"/>, the largest step along <paramref name="outward"/> (a unit vector); -1 if none is near.</summary>
        public static int Find(IReadOnlyList<Vector3> points, Vector3 anchor, Vector3 outward, float radius)
        {
            int best = -1;
            float bestOut = float.MinValue, r2 = radius * radius;
            for (int i = 0; i < points.Count; i++)
            {
                Vector3 d = points[i] - anchor;
                if (d.LengthSquared() > r2) continue;
                float o = Vector3.Dot(d, outward);
                if (o > bestOut) { bestOut = o; best = i; }
            }
            return best;
        }
    }
}
