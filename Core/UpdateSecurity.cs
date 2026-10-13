using System.Security.Cryptography;
using System.Text.Json;

namespace Touareg.Core;

public record Release(string Version, string Url, string Sha256, long Size);
public record Envelope(string Payload, string Signature);
public static class UpdateSecurity
{
    static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    public static Release Verify(string envelopeJson, string publicKey, string currentVersion)
    {
        if (envelopeJson.Length > 16000) throw new InvalidDataException("Манифест слишком большой");
        var envelope = JsonSerializer.Deserialize<Envelope>(envelopeJson, Json) ?? throw new InvalidDataException();
        var bytes = Convert.FromBase64String(envelope.Payload);
        using var rsa = RSA.Create(); rsa.ImportFromPem(publicKey);
        if (rsa.KeySize < 3072 || !rsa.VerifyData(bytes, Convert.FromBase64String(envelope.Signature), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
            throw new CryptographicException("Подпись обновления не подтверждена");
        var release = JsonSerializer.Deserialize<Release>(bytes, Json) ?? throw new InvalidDataException();
        if (!Version.TryParse(release.Version, out var version) || version.ToString() != release.Version || version <= Version.Parse(currentVersion))
            throw new InvalidDataException("Версия не новее установленной");
        if (!Uri.TryCreate(release.Url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || !string.IsNullOrEmpty(uri.UserInfo))
            throw new InvalidDataException("Требуется HTTPS");
        if (release.Size is < 1000000 or > 350000000 || release.Sha256.Length != 64 || !release.Sha256.All(Uri.IsHexDigit))
            throw new InvalidDataException("Некорректный размер или хеш");
        return release;
    }

    public static void VerifyFile(string path, Release release)
    {
        using var file = File.OpenRead(path);
        if (file.Length != release.Size) throw new InvalidDataException("Размер файла не совпадает");
        var hash = Convert.ToHexString(SHA256.HashData(file));
        if (!hash.Equals(release.Sha256, StringComparison.OrdinalIgnoreCase)) throw new CryptographicException("Хеш файла не совпадает");
        file.Position = 0;
        if (file.ReadByte() != 'M' || file.ReadByte() != 'Z') throw new InvalidDataException("Ожидался Windows EXE");
    }
}
