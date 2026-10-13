namespace Touareg.Core;

// Short one-shot effects synthesized in code, so no extra recordings or licences are needed.
public static class ImpactSound
{
    const int Rate = 44100;

    // Body slam into a wall: a low thump, a burst of crunch and a short metallic ring.
    public static float[] Crash()
    {
        var random = new Random(7);
        float[] result = new float[(int)(Rate * .6)];
        double phase = 0, noise = 0;
        (double F, double A, double D)[] ring = [(640, .20, 11), (1170, .13, 14), (1730, .09, 17), (2390, .05, 20)];
        for (int i = 0; i < result.Length; i++)
        {
            double t = (double)i / Rate;
            phase += 2 * Math.PI * (42 + 80 * Math.Exp(-t * 18)) / Rate;
            noise += (random.NextDouble() * 2 - 1 - noise) * .35;
            double value = Math.Sin(phase) * Math.Exp(-t * 9) + noise * .9 * Math.Exp(-t * 32);
            foreach (var (f, a, d) in ring) value += Math.Sin(2 * Math.PI * f * t) * a * Math.Exp(-t * d);
            result[i] = (float)value;
        }
        return Normalize(result, .9f);
    }

    // Tyres and suspension landing on a window or the taskbar.
    public static float[] Thud()
    {
        var random = new Random(11);
        float[] result = new float[(int)(Rate * .3)];
        double phase = 0, noise = 0;
        for (int i = 0; i < result.Length; i++)
        {
            double t = (double)i / Rate;
            phase += 2 * Math.PI * (55 + 45 * Math.Exp(-t * 25)) / Rate;
            noise += (random.NextDouble() * 2 - 1 - noise) * .12;
            result[i] = (float)(Math.Sin(phase) * Math.Exp(-t * 14) + noise * 1.4 * Math.Exp(-t * 45));
        }
        return Normalize(result, .75f);
    }

    static float[] Normalize(float[] samples, float peak)
    {
        float max = samples.Max(Math.Abs), attack = Rate * .002f, release = Rate * .01f;
        for (int i = 0; i < samples.Length; i++)
        {
            float edge = Math.Min(1, Math.Min(i / attack, (samples.Length - 1 - i) / release));
            samples[i] = samples[i] / max * peak * edge;
        }
        return samples;
    }
}
