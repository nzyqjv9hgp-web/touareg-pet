namespace Touareg.Core;

// Short one-shot effects synthesized in code, so no extra recordings or licences are needed.
// Several variants per kind keep repeated bumps from sounding identical.
public static class ImpactSound
{
    const int Rate = 44100;

    public static float[][] Crashes() => [Slam(), Clang(), Crunch(), Heavy(), Rattle()];
    public static float[][] Landings() => [Thud(), Double(), Squish(), Tap()];

    // Body slam into a wall: a low thump, a burst of crunch and a short metallic ring.
    static float[] Slam() => Make(.6, .9f, 7, (t, n) =>
        Thump(t, 42, 80, 18, 9) + n.Low(.35) * .9 * Math.Exp(-t * 32)
        + Ring(t, (640, .20, 11), (1170, .13, 14), (1730, .09, 17), (2390, .05, 20)));

    // Lighter hit with a bright, longer ringing panel.
    static float[] Clang() => Make(.8, .8f, 13, (t, n) =>
        Thump(t, 60, 60, 25, 16) * .6 + n.High(.5) * .5 * Math.Exp(-t * 60)
        + Ring(t, (880, .26, 6), (1490, .18, 8), (2210, .12, 10), (3050, .07, 13), (4120, .04, 16)));

    // Plastic bumper: a cluster of short cracks over a dull thump.
    static float[] Crunch() => Make(.5, .9f, 21, (t, n) =>
    {
        double cracks = 0;
        foreach (double at in new[] { 0, .018, .041, .067, .095 })
            if (t >= at) cracks += Math.Exp(-(t - at) * 90) * (1 - at * 6);
        return Thump(t, 38, 70, 20, 12) * .8 + n.Low(.6) * 1.3 * cracks;
    });

    // Deep heavy impact with a bit of debris settling afterwards.
    static float[] Heavy() => Make(.9, .95f, 33, (t, n) =>
        Thump(t, 30, 55, 10, 6) * 1.2 + n.Low(.18) * .7 * Math.Exp(-t * 20)
        + Debris(t, n, .14, .23, .31) * .35 + Ring(t, (410, .12, 9), (760, .08, 12)));

    // Thump followed by a loose part bouncing a few times.
    static float[] Rattle() => Make(.8, .85f, 45, (t, n) =>
        Thump(t, 45, 70, 18, 11) + n.Low(.4) * .6 * Math.Exp(-t * 40)
        + Debris(t, n, .09, .16, .21, .25, .28) * .5 + Ring(t, (2900, .05, 30), (3700, .04, 35)));

    // Tyres and suspension landing on a window or the taskbar.
    static float[] Thud() => Make(.3, .75f, 11, (t, n) =>
        Thump(t, 55, 45, 25, 14) + n.Low(.12) * 1.4 * Math.Exp(-t * 45));

    // Front axle, then rear axle.
    static float[] Double() => Make(.4, .75f, 17, (t, n) =>
    {
        double second = t >= .075 ? Thump(t - .075, 50, 40, 25, 16) * .7 : 0;
        return Thump(t, 60, 45, 25, 16) + second + n.Low(.12) * 1.1 * (Math.Exp(-t * 50) + (t >= .075 ? .7 * Math.Exp(-(t - .075) * 50) : 0));
    });

    // Soft landing with a short suspension squeak.
    static float[] Squish() => Make(.45, .7f, 23, (t, n) =>
    {
        double squeak = t > .04 ? Math.Sin(2 * Math.PI * (380 * (t - .04) + 900 * (t - .04) * (t - .04))) * .18 * Math.Exp(-(t - .04) * 14) * (t < .3 ? 1 : 0) : 0;
        return Thump(t, 48, 35, 20, 11) + n.Low(.1) * .9 * Math.Exp(-t * 40) + squeak;
    });

    // Light touchdown: tyre chirp and a small tap.
    static float[] Tap() => Make(.25, .6f, 29, (t, n) =>
        Thump(t, 75, 40, 30, 22) * .8 + n.High(.6) * .5 * Math.Exp(-t * 70));

    static double Thump(double t, double baseHz, double dropHz, double sweep, double decay)
    {
        // Closed form of a sine whose frequency falls from base+drop to base.
        double phase = 2 * Math.PI * (baseHz * t + dropHz * (1 - Math.Exp(-t * sweep)) / sweep);
        return Math.Sin(phase) * Math.Exp(-t * decay);
    }

    static double Ring(double t, params (double F, double A, double D)[] partials)
    {
        double value = 0;
        foreach (var (f, a, d) in partials) value += Math.Sin(2 * Math.PI * f * t) * a * Math.Exp(-t * d);
        return value;
    }

    static double Debris(double t, Noise n, params double[] times)
    {
        double value = 0;
        foreach (double at in times)
            if (t >= at) value += (Math.Sin(2 * Math.PI * 1800 * (t - at)) * .5 + n.Last) * Math.Exp(-(t - at) * 120);
        return value;
    }

    sealed class Noise(int seed)
    {
        readonly Random random = new(seed);
        double low, high, previous;
        public double Last;
        public void Next() { Last = random.NextDouble() * 2 - 1; }
        // One-pole low-pass (smaller = darker) and a simple high-pass of the same white noise.
        public double Low(double k) => low += (Last - low) * k;
        public double High(double k) { double h = (Last - previous) * k + high * (1 - k); previous = Last; return high = h; }
    }

    static float[] Make(double seconds, float peak, int seed, Func<double, Noise, double> voice)
    {
        var noise = new Noise(seed);
        float[] result = new float[(int)(Rate * seconds)];
        for (int i = 0; i < result.Length; i++) { noise.Next(); result[i] = (float)voice((double)i / Rate, noise); }
        return Normalize(result, peak);
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

// Picks a variant for each bump: never the same one twice in a row, with a little pitch and level spread.
public sealed class ImpactBank(int seed = 0)
{
    readonly float[][] crashes = ImpactSound.Crashes(), landings = ImpactSound.Landings();
    readonly Random random = new(seed);
    int lastCrash = -1, lastLanding = -1;

    public int Variants(bool metal) => (metal ? crashes : landings).Length;

    public (float[] Sound, int Variant, float Pitch, float Gain) Next(Impact impact)
    {
        var bank = impact.Metal ? crashes : landings;
        int last = impact.Metal ? lastCrash : lastLanding, pick;
        do pick = random.Next(bank.Length); while (pick == last && bank.Length > 1);
        if (impact.Metal) lastCrash = pick; else lastLanding = pick;
        // Harder hits sound a little lower and louder.
        float pitch = (float)((.92 + random.NextDouble() * .16) * (1.06 - .12 * impact.Strength));
        float gain = (float)((.35 + .65 * impact.Strength) * (.85 + random.NextDouble() * .15));
        return (bank[pick], pick, pitch, gain);
    }
}
