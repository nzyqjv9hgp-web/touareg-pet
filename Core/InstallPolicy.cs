using System.Text.Json;

namespace Touareg.Core;

public static class InstallPolicy
{
    public static string ChooseVersion(string installed, string incoming) =>
        Version.Parse(incoming) > Version.Parse(installed) ? incoming : installed;

    public static bool NeedsChannel(string stored, string bundled)
    {
        static string Url(string json)
        {
            try { using var document = JsonDocument.Parse(json); return document.RootElement.GetProperty("url").GetString() ?? ""; }
            catch { return ""; }
        }
        return string.IsNullOrWhiteSpace(Url(stored)) && !string.IsNullOrWhiteSpace(Url(bundled));
    }
}
