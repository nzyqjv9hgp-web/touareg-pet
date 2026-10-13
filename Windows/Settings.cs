using System.Text.Json;
using Microsoft.Win32;

namespace Touareg.Windows;

internal sealed class Settings
{
    public bool Sound { get; set; } = true;
    public int Volume { get; set; } = 45;
    public int Size { get; set; } = 194;
    public int Speed { get; set; } = 100;
    public bool AutoUpdate { get; set; } = true;
    public bool HideFullscreen { get; set; } = true;
    public bool Startup { get; set; }
    internal static Settings Load()
    {
        try
        {
            var s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(Path.Combine(Install.Root, "settings.json"))) ?? new();
            s.Size = Math.Clamp(s.Size, 150, 280); s.Volume = Math.Clamp(s.Volume, 0, 100); s.Speed = Math.Clamp(s.Speed, 50, 150); return s;
        }
        catch { return new(); }
    }
    internal void Save() => Install.WriteJson(Path.Combine(Install.Root, "settings.json"), this);
    internal void SetStartup(bool value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        if (value) key.SetValue("TouaregPet", "\"" + Install.Launcher + "\"");
        else key.DeleteValue("TouaregPet", false);
        Startup = value; Save();
    }
}
