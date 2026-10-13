using System.Diagnostics;
using System.Text.Json;
using Touareg.Core;

namespace Touareg.Windows;

internal static class Install
{
    internal static string Root => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TouaregPet");
    internal static string Launcher => Path.Combine(Root, "TouaregPet.exe");
    internal static string Version => typeof(Install).Assembly.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "1.0.1";
    internal static string VersionExe(string version) => Path.Combine(Root, "versions", version, "TouaregPet.exe");
    internal static void WriteJson<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(value)); File.Move(temp, path, true);
    }
    internal static string Current()
    {
        try
        {
            string v = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, "current.json"))).RootElement.GetProperty("version").GetString()!;
            if (System.Version.TryParse(v, out var parsed) && parsed.ToString() == v && File.Exists(VersionExe(v))) return v;
        }
        catch { }
        return Version;
    }
    internal static void Select(string version) => WriteJson(Path.Combine(Root, "current.json"), new { version });
    internal static void Launch()
    {
        Directory.CreateDirectory(Root); string executable = Environment.ProcessPath!;
        string versionExe = VersionExe(Version); Directory.CreateDirectory(Path.GetDirectoryName(versionExe)!);
        bool first = !File.Exists(Path.Combine(Root, "current.json"));
        if (!File.Exists(versionExe)) File.Copy(executable, versionExe);
        if (!File.Exists(Launcher)) File.Copy(executable, Launcher);
        string channelPath = Path.Combine(Root, "update-channel.json");
        string bundledChannel = Assets.Text("update-channel.json");
        if (!File.Exists(channelPath) || InstallPolicy.NeedsChannel(File.ReadAllText(channelPath), bundledChannel))
            File.WriteAllText(channelPath, bundledChannel);
        // A configured channel packaged beside the downloaded EXE is accepted on first installation only.
        string supplied = Path.Combine(Path.GetDirectoryName(executable)!, "update-channel.json");
        if (first && File.Exists(supplied)) File.Copy(supplied, channelPath, true);
        if (first) { Select(Version); Shortcut(); }
        else Select(InstallPolicy.ChooseVersion(Current(), Version));
        var p = new ProcessStartInfo(VersionExe(Current())) { UseShellExecute = true };
        p.ArgumentList.Add("--installed"); if (first) p.ArgumentList.Add("--welcome"); Process.Start(p);
    }
    internal static void Shortcut()
    {
        Type? shellType = Type.GetTypeFromProgID("WScript.Shell"); if (shellType is null) return;
        object? shell = null, shortcut = null;
        try
        {
            shell = Activator.CreateInstance(shellType);
            dynamic s = shell!; shortcut = s.CreateShortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Touareg — питомец.lnk"));
            dynamic link = shortcut; link.TargetPath = Launcher; link.WorkingDirectory = Root; link.Description = "Мини-я на Touareg"; link.Save();
        }
        catch (Exception e) { Log.Write("shortcut", e); }
        finally
        {
            if (shortcut is not null) System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shortcut);
            if (shell is not null) System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
        }
    }
}

internal static class Log
{
    internal static void Write(string action, Exception e)
    {
        try
        {
            Directory.CreateDirectory(Install.Root); string file = Path.Combine(Install.Root, "diagnostics.log");
            if (File.Exists(file) && new FileInfo(file).Length > 500000) File.Move(file, file + ".old", true);
            File.AppendAllText(file, $"{DateTime.UtcNow:O} {action}: {e}\n");
        }
        catch { }
    }
}
