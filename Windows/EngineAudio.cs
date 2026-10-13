using NAudio.Wave;
using Touareg.Core;

namespace Touareg.Windows;

internal sealed class EngineAudio : ISampleProvider, IDisposable
{
    readonly EngineSamples samples;
    WaveOutEvent? output;
    bool failed;
    internal event Action<string>? Error;
    public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(44100, 2);
    internal EngineAudio()
    {
        using var b = Assets.Open("audio/v8-burnout-loop.wav"); using var d = Assets.Open("audio/v8-drive-loop.wav");
        samples = new(Pcm.Load(b), Pcm.Load(d));
    }
    internal void Sync(World world, Settings settings, bool hidden)
    {
        bool active = world.EngineArmed && !world.Paused && !hidden && settings.Sound;
        if (active && output is null && !failed)
        {
            try
            {
                output = new WaveOutEvent { DesiredLatency = 80, NumberOfBuffers = 3 };
                output.Init(this); output.Play();
                output.PlaybackStopped += (_, e) => { if (e.Exception is not null) { failed = true; Error?.Invoke(e.Exception.Message); } };
            }
            catch (Exception e) { failed = true; output?.Dispose(); output = null; Error?.Invoke(e.Message); }
        }
        samples.Set(active ? world.Phase == Motion.Burnout ? "burnout" : "drive" : "silent", (float)settings.Volume / 100);
    }
    public int Read(float[] buffer, int offset, int count) => samples.Read(buffer, offset, count);
    public void Dispose() { output?.Stop(); output?.Dispose(); }
}
