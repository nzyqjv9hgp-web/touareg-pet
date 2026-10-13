using System.Reflection;
using System.Text.Json;

namespace Touareg.Windows;

internal static class Assets
{
    internal static Stream Open(string path)
    {
        var assembly = Assembly.GetExecutingAssembly();
        string name = assembly.GetManifestResourceNames().First(n => n.Replace('\\', '/').Equals("Assets/" + path));
        return assembly.GetManifestResourceStream(name)!;
    }
    internal static Bitmap Image(string path)
    {
        using var stream = Open(path); using var image = System.Drawing.Image.FromStream(stream);
        return new Bitmap(image);
    }
    internal static Icon Icon() { using var stream = Open("pet.ico"); return new Icon(stream); }
    internal static string Text(string path) { using var stream = Open(path); using var r = new StreamReader(stream); return r.ReadToEnd(); }
}

internal sealed class Sprites : IDisposable
{
    internal readonly Dictionary<string, Bitmap[]> Frames = [];
    internal readonly PointF[][] Wheels;
    internal Sprites()
    {
        foreach (string name in new[] { "drive", "jump", "land", "tilt" })
            Frames[name] = Enumerable.Range(1, 6).Select(i => Assets.Image($"frames/{name}/frame-{i:00}.png")).ToArray();
        using var metadata = JsonDocument.Parse(Assets.Text("sprites.json"));
        Wheels = metadata.RootElement.GetProperty("wheels").EnumerateArray().Select(frame => frame.EnumerateArray()
            .Select(w => new PointF(w.GetProperty("x").GetSingle(), w.GetProperty("y").GetSingle())).ToArray()).ToArray();
    }
    public void Dispose() { foreach (var image in Frames.Values.SelectMany(f => f)) image.Dispose(); }
}
