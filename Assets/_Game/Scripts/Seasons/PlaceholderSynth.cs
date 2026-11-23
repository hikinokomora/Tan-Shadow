using UnityEngine;

namespace TanShadow.Seasons
{
    // Синтезированные звуки-заглушки: грейбокс звучит до того, как появятся настоящие клипы,
    // и сразу слышно, где какой звук. Общие для всех сезонов.
    public static class PlaceholderSynth
    {
        public const int Rate = 22050;

        public static AudioClip Clip(string name, float[] samples)
        {
            var clip = AudioClip.Create(name, samples.Length, 1, Rate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        public static AudioClip Noise(string name, float duration, float smoothing, float volume, bool loop)
        {
            var s = new float[(int)(Rate * duration)];
            var rnd = new System.Random(name.GetHashCode());
            float f = 0f;
            for (int i = 0; i < s.Length; i++)
            {
                f = Mathf.Lerp(f, (float)rnd.NextDouble() * 2f - 1f, smoothing);
                s[i] = f * volume * 4f;
            }
            if (loop) // мягкие края, чтобы петля не щёлкала
                for (int i = 0; i < 400 && i < s.Length; i++) { float k = i / 400f; s[i] *= k; s[s.Length - 1 - i] *= k; }
            return Clip(name, s);
        }

        public static AudioClip Tone(string name, float duration, float freq, float decay, float volume, float overtone)
        {
            var s = new float[(int)(Rate * duration)];
            for (int i = 0; i < s.Length; i++)
            {
                float t = i / (float)Rate;
                float v = Mathf.Sin(2f * Mathf.PI * freq * t) + 0.4f * Mathf.Sin(2f * Mathf.PI * freq * overtone * t);
                s[i] = v * Mathf.Exp(-t * decay) * Mathf.Min(1f, t * 300f) * volume;
            }
            return Clip(name, s);
        }

        public static AudioClip Gong(string name, float duration)
        {
            var s = new float[(int)(Rate * duration)];
            float[] partials = { 1f, 1.48f, 2.03f, 2.71f, 3.6f };
            for (int i = 0; i < s.Length; i++)
            {
                float t = i / (float)Rate;
                float v = 0f;
                for (int p = 0; p < partials.Length; p++)
                    v += Mathf.Sin(2f * Mathf.PI * 110f * partials[p] * t * (1f + 0.002f * Mathf.Sin(t * 5f))) *
                         Mathf.Exp(-t * (0.9f + p * 0.7f)) / (p + 1);
                s[i] = v * Mathf.Min(1f, t * 200f) * 0.55f;
            }
            return Clip(name, s);
        }

        public static AudioClip Sweep(string name, float duration, float from, float to, float volume)
        {
            var s = new float[(int)(Rate * duration)];
            var rnd = new System.Random(name.GetHashCode());
            float phase = 0f, f = 0f;
            for (int i = 0; i < s.Length; i++)
            {
                float t = i / (float)Rate, k = t / duration;
                phase += 2f * Mathf.PI * Mathf.Lerp(from, to, k) / Rate;
                f = Mathf.Lerp(f, (float)rnd.NextDouble() * 2f - 1f, 0.2f);
                s[i] = (Mathf.Sin(phase) * 0.3f + f * 1.2f) * Mathf.Sin(Mathf.PI * k) * volume;
            }
            return Clip(name, s);
        }

        public static AudioClip Thud(string name, float duration, float freq, float volume)
        {
            var s = new float[(int)(Rate * duration)];
            var rnd = new System.Random(name.GetHashCode());
            float f = 0f;
            for (int i = 0; i < s.Length; i++)
            {
                float t = i / (float)Rate;
                f = Mathf.Lerp(f, (float)rnd.NextDouble() * 2f - 1f, 0.08f);
                float v = Mathf.Sin(2f * Mathf.PI * freq * t * (1f - t * 0.4f)) + f * 1.5f;
                s[i] = v * Mathf.Exp(-t * 4.5f) * Mathf.Min(1f, t * 500f) * volume * 0.7f;
            }
            return Clip(name, s);
        }

        public static AudioClip Bursts(string name, float duration, int count, float volume)
        {
            var s = new float[(int)(Rate * duration)];
            var rnd = new System.Random(name.GetHashCode());
            for (int b = 0; b < count; b++)
            {
                int start = rnd.Next(0, Mathf.Max(1, s.Length - Rate / 10));
                int length = rnd.Next(Rate / 60, Rate / 12);
                float f = 0f;
                for (int i = 0; i < length && start + i < s.Length; i++)
                {
                    f = Mathf.Lerp(f, (float)rnd.NextDouble() * 2f - 1f, 0.5f);
                    s[start + i] += f * Mathf.Exp(-i / (float)length * 4f) * volume;
                }
            }
            return Clip(name, s);
        }
    }
}
