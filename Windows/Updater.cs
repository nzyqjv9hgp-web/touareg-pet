using System.Net.Http;
using System.Text.Json;
using Touareg.Core;

namespace Touareg.Windows;

internal sealed class Updater
{
    readonly HttpClient client = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromMinutes(8) };
    bool busy;
    internal string Status { get; private set; } = "Канал обновлений пока не подключён";
    internal bool Ready { get; private set; }
    internal event Action? Changed;
    internal Updater()
    {
        try
        {
            using var channel = JsonDocument.Parse(File.ReadAllText(Path.Combine(Install.Root, "update-channel.json")));
            if (!string.IsNullOrWhiteSpace(channel.RootElement.GetProperty("url").GetString())) Status = "Канал обновлений подключён";
        }
        catch { }
    }
    internal async Task<HttpResponseMessage> Get(Uri uri, CancellationToken token)
    {
        for (int i = 0; i < 6; i++)
        {
            if (uri.Scheme != "https" || !string.IsNullOrEmpty(uri.UserInfo)) throw new InvalidDataException("Требуется HTTPS");
            var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token);
            if ((int)response.StatusCode is >= 300 and <= 399)
            {
                var location = response.Headers.Location; response.Dispose();
                uri = location is null ? throw new InvalidDataException("Некорректное перенаправление") : location.IsAbsoluteUri ? location : new Uri(uri, location);
                continue;
            }
            response.EnsureSuccessStatusCode(); return response;
        }
        throw new InvalidDataException("Слишком много перенаправлений");
    }
    internal async Task Check()
    {
        if (busy) return; busy = true; string? temp = null;
        try
        {
            using var channel = JsonDocument.Parse(File.ReadAllText(Path.Combine(Install.Root, "update-channel.json")));
            string? url = channel.RootElement.GetProperty("url").GetString();
            if (string.IsNullOrWhiteSpace(url)) { Status = "Канал обновлений пока не подключён"; return; }
            Status = "Проверяю обновления…"; Changed?.Invoke();
            using var cancel = new CancellationTokenSource(TimeSpan.FromMinutes(8));
            using var manifestResponse = await Get(new Uri(url), cancel.Token);
            if (manifestResponse.Content.Headers.ContentLength > 16000) throw new InvalidDataException();
            using var manifestStream = await manifestResponse.Content.ReadAsStreamAsync(cancel.Token);
            using var memory = new MemoryStream(); byte[] chunk = new byte[4096]; int got;
            while ((got = await manifestStream.ReadAsync(chunk, cancel.Token)) > 0)
            {
                if (memory.Length + got > 16000) throw new InvalidDataException(); memory.Write(chunk, 0, got);
            }
            string json = System.Text.Encoding.UTF8.GetString(memory.ToArray());
            Release release;
            try { release = UpdateSecurity.Verify(json, Assets.Text("update-public.pem"), Install.Version); }
            catch (InvalidDataException e) when (e.Message == "Версия не новее установленной") { Status = "Установлена последняя версия"; return; }
            string final = Install.VersionExe(release.Version); Directory.CreateDirectory(Path.GetDirectoryName(final)!);
            if (File.Exists(final)) UpdateSecurity.VerifyFile(final, release);
            else
            {
                Status = "Загружаю новую версию…"; Changed?.Invoke(); temp = final + ".download";
                using var response = await Get(new Uri(release.Url), cancel.Token);
                using var stream = await response.Content.ReadAsStreamAsync(cancel.Token);
                await using (var file = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    long total = 0; byte[] buffer = new byte[65536];
                    while ((got = await stream.ReadAsync(buffer, cancel.Token)) > 0)
                    {
                        total += got; if (total > release.Size) throw new InvalidDataException("Файл слишком большой");
                        await file.WriteAsync(buffer.AsMemory(0, got), cancel.Token);
                    }
                }
                UpdateSecurity.VerifyFile(temp, release); File.Move(temp, final, true); temp = null;
            }
            Install.Select(release.Version); Ready = true;
            Status = $"Версия {release.Version} готова — включится при следующем запуске";
        }
        catch (Exception e) { Status = "Обновления временно недоступны — попробую позже"; Log.Write("update", e); }
        finally
        {
            if (temp is not null) { try { File.Delete(temp); } catch { } }
            busy = false; Changed?.Invoke();
        }
    }
}
