using System;

namespace NipplePiercings.Logic
{
    /// <summary>A small metal chime, synthesised (the game has no bell or jingle among its sounds [observed 2026-10-25]): a few
    /// inharmonic partials (a bell's ratios) with a soft attack and exponential decays.</summary>
    public static class Chime
    {
        private static readonly (float Ratio, float Level, float Decay)[] Partials =
        {
            (1.00f, 1.00f, 9f), (2.76f, 0.55f, 14f), (5.40f, 0.30f, 20f), (8.93f, 0.15f, 28f),
        };

        /// <summary>Mono samples in -1 .. 1 (peak <paramref name="peak"/>).</summary>
        public static float[] Synthesize(int sampleRate, float seconds, float baseHz, float peak = 0.6f)
        {
            int n = Math.Max(1, (int)(sampleRate * seconds));
            var s = new float[n];
            float max = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / sampleRate;
                float attack = Math.Min(1f, t / 0.002f);
                float v = 0f;
                foreach (var (ratio, level, decay) in Partials)
                    v += level * (float)Math.Exp(-decay * t) * (float)Math.Sin(2 * Math.PI * baseHz * ratio * t);
                s[i] = v * attack;
                max = Math.Max(max, Math.Abs(s[i]));
            }
            if (max > 0f) for (int i = 0; i < n; i++) s[i] *= peak / max;
            return s;
        }
    }

    /// <summary>When a swinging piercing jingles: a jolt (the fastest piece's speed crossing <see cref="Threshold"/> m/s while
    /// rising) rings it, at most once per <see cref="Cooldown"/> s, louder the harder the jolt.</summary>
    public sealed class JingleTrigger
    {
        public float Threshold = 0.5f, Cooldown = 0.3f, FullAt = 1.4f; // 0.35 rang on most steps while walking [observed 2026-10-25]
        private float _last = float.NegativeInfinity, _previous;

        /// <summary>The loudness to ring at now (0 .. 1), or 0 for no ring.</summary>
        public float Update(float speed, float now)
        {
            bool rising = speed > _previous;
            _previous = speed;
            if (!rising || speed < Threshold || now - _last < Cooldown) return 0f;
            _last = now;
            return Math.Clamp((speed - Threshold) / (FullAt - Threshold), 0.2f, 1f);
        }
    }
}
