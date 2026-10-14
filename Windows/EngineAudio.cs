using NAudio.Wave;
using Touareg.Core;

namespace Touareg.Windows;

internal sealed class EngineAudio : ISampleProvider, IDisposable
{
    readonly EngineSamples samples;
    readonly ImpactBank impacts = new(Environment.TickCount);
    WaveOutEvent? output;
    bool failed;
    internal event Action<string>? Error;
    public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(44100, 2);
    internal EngineAudio()
    {
        using var b = Assets.Open("audio/v8-burnout-loop.wav"); using var d = Assets.Open("audio/v8-drive-loop.wav");
        samples = new(Pcm.Load(b), Pcm.Load(d), ImpactSound.HoverLoop());
    }
    internal void Sync(World world, Settings settings, bool hidden)
    {
        bool audible = !world.Paused && !hidden && settings.Sound, played = false;
        while (world.Impacts.TryDequeue(out var hit))
            if (audible) { var (sound, _, pitch, gain) = impacts.Next(hit); samples.Play(sound, gain, pitch); played = true; }
        bool jets = world.Thrust > .05, active = (world.EngineArmed || jets) && audible;
        if ((active || played) && output is null && !failed)
        {
            try
            {
                output = new WaveOutEvent { DesiredLatency = 80, NumberOfBuffers = 3 };
                output.Init(this); output.Play();
                output.PlaybackStopped += (_, e) => { if (e.Exception is not null) { failed = true; Error?.Invoke(e.Exception.Message); } };
            }
            catch (Exception e) { failed = true; output?.Dispose(); output = null; Error?.Invoke(e.Message); }
        }
        float volume = (float)settings.Volume / 100 * (jets ? (float)(.35 + .65 * world.Thrust) : 1);
        samples.Set(active ? jets ? "hover" : world.Phase == Motion.Burnout ? "burnout" : "drive" : "silent", volume);
    }
    public int Read(float[] buffer, int offset, int count) => samples.Read(buffer, offset, count);
    public void Dispose() { output?.Stop(); output?.Dispose(); }
}
