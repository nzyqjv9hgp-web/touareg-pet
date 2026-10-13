using System.Text;

namespace Touareg.Core;

public sealed record Pcm(float[] Samples, int Rate, int Channels)
{
    public int Frames => Samples.Length / Channels;
    public static Pcm Load(Stream stream)
    {
        using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);
        if (Encoding.ASCII.GetString(reader.ReadBytes(4)) != "RIFF") throw new InvalidDataException();
        reader.ReadInt32(); if (Encoding.ASCII.GetString(reader.ReadBytes(4)) != "WAVE") throw new InvalidDataException();
        int channels = 0, rate = 0, bits = 0, format = 0; byte[] data = [];
        while (stream.Position + 8 <= stream.Length)
        {
            string id = Encoding.ASCII.GetString(reader.ReadBytes(4)); int length = reader.ReadInt32();
            if (length < 0 || stream.Position + length > stream.Length) throw new InvalidDataException();
            long end = stream.Position + length;
            if (id == "fmt ") { format = reader.ReadInt16(); channels = reader.ReadInt16(); rate = reader.ReadInt32(); reader.ReadInt32(); reader.ReadInt16(); bits = reader.ReadInt16(); }
            else if (id == "data") data = reader.ReadBytes(length);
            stream.Position = end + (length & 1);
        }
        if (format != 1 || bits != 16 || rate != 44100 || channels != 2 || data.Length % 4 != 0) throw new InvalidDataException("Ожидалась стереозапись PCM16/44100");
        float[] samples = new float[data.Length / 2];
        for (int i = 0; i < samples.Length; i++) samples[i] = BitConverter.ToInt16(data, i * 2) / 32768f;
        return new(samples, rate, channels);
    }
}

// Real recordings at their original pitch; the driving prefix plays once, then the loaded-RPM tail loops.
public sealed class EngineSamples(Pcm burnout, Pcm drive)
{
    sealed class Voice(Pcm pcm, bool driving)
    {
        public readonly Pcm Pcm = pcm;
        public readonly int LoopStart = driving ? (int)Math.Round(1.08 * pcm.Rate) * pcm.Channels : 0;
        public readonly int LoopEnd = driving ? Math.Min(pcm.Samples.Length, (int)Math.Round(1.36 * pcm.Rate) * pcm.Channels) : pcm.Samples.Length;
        public int Position;
        public float Fade;
        public float Next()
        {
            if (Position >= LoopEnd) Position = LoopStart;
            return Pcm.Samples[Position++];
        }
    }
    sealed class Shot(float[] mono, float gain)
    {
        public readonly float[] Mono = mono;
        public readonly float Gain = gain;
        public int Position;
    }
    readonly object gate = new();
    readonly List<Shot> shots = [];
    Voice? current, retiring;
    string mode = "silent";
    float gain, desiredGain;
    public string Mode { get { lock (gate) return mode; } }
    public int Position { get { lock (gate) return current?.Position ?? 0; } }
    public void Set(string next, float volume)
    {
        lock (gate)
        {
            desiredGain = Math.Clamp(volume, 0, 1) * .85f;
            if (next == mode) return;
            retiring = current; mode = next;
            current = next == "drive" ? new(drive, true) : next == "burnout" ? new(burnout, false) : null;
        }
    }
    // Mono one-shot mixed over the engine on both channels, e.g. a crash or a landing.
    public void Play(float[] mono, float strength)
    {
        lock (gate)
        {
            if (shots.Count >= 6) shots.RemoveAt(0);
            shots.Add(new(mono, Math.Clamp(strength, 0, 1)));
        }
    }

    public int Read(float[] buffer, int offset, int count)
    {
        lock (gate)
        {
            float fadeStep = 1f / (44100 * 2 * .035f);
            float smoothing = 1f / (44100 * 2 * .02f);
            for (int i = 0; i < count; i++)
            {
                gain += (desiredGain - gain) * smoothing;
                float value = 0;
                if (current is not null) { current.Fade = Math.Min(1, current.Fade + fadeStep); value += current.Next() * current.Fade; }
                if (retiring is not null)
                {
                    retiring.Fade = Math.Max(0, retiring.Fade - fadeStep); value += retiring.Next() * retiring.Fade;
                    if (retiring.Fade <= 0) retiring = null;
                }
                for (int k = shots.Count - 1; k >= 0; k--)
                {
                    var shot = shots[k];
                    value += shot.Mono[shot.Position] * shot.Gain;
                    // Interleaved stereo: advance the mono shot after the right channel.
                    if ((offset + i) % 2 == 1 && ++shot.Position >= shot.Mono.Length) shots.RemoveAt(k);
                }
                buffer[offset + i] = value * gain;
            }
            return count;
        }
    }
}
