using System;
using System.Collections.Generic;
using System.Numerics;

namespace NipplePiercings.Logic
{
    /// <summary>The nipple's tip on her body mesh: of the skin near the game's piercing point and close to the nipple's axis, the
    /// point furthest out along the nipple's direction.</summary>
    public static class NippleApex
    {
        /// <summary>
        /// The search radius for a piercing point of <paramref name="pointScale"/> (its lossy scale, which follows her height and
        /// nipple size): 1.2 x, 2.7 cm on the first save, where the tip is 1.1 - 1.5 cm from the point. 4 cm let a hand in front
        /// of the breast win: the left tip was taken 3.7 cm off and kept [observed 2026-10-26].
        /// </summary>
        public static float Radius(float pointScale) => Math.Clamp(0.012f * pointScale, 0.012f, 0.06f);

        /// <summary>Index of the apex among <paramref name="points"/> (world space): within <paramref name="radius"/> of
        /// <paramref name="anchor"/> and at most <paramref name="radius"/> x <paramref name="aside"/> off the line through it
        /// along <paramref name="outward"/> (a unit vector), the largest step along it; -1 if none is near.</summary>
        public static int Find(IReadOnlyList<Vector3> points, Vector3 anchor, Vector3 outward, float radius, float aside = 0.75f)
        {
            int best = -1;
            float bestOut = float.MinValue, r2 = radius * radius, a2 = radius * aside * radius * aside;
            for (int i = 0; i < points.Count; i++)
            {
                Vector3 d = points[i] - anchor;
                if (d.LengthSquared() > r2) continue;
                float o = Vector3.Dot(d, outward);
                if ((d - outward * o).LengthSquared() > a2) continue;
                if (o > bestOut) { bestOut = o; best = i; }
            }
            return best;
        }
    }
}
