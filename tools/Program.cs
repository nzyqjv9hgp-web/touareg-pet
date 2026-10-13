using System.Security.Cryptography;
using System.Text.Json;

if (args.Length != 5) throw new ArgumentException("EXE VERSION HTTPS_DOWNLOAD_URL PRIVATE_KEY OUTPUT_JSON");
string exe = Path.GetFullPath(args[0]), version = args[1], url = args[2], privateKey = args[3], output = args[4];
if (!Version.TryParse(version, out var parsed) || parsed.ToString() != version) throw new ArgumentException("Use version X.Y.Z");
if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https") throw new ArgumentException("HTTPS required");
using var stream = File.OpenRead(exe);
if (stream.ReadByte() != 'M' || stream.ReadByte() != 'Z') throw new InvalidDataException("Windows EXE required");
stream.Position = 0;
var release = new { version, url, sha256 = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant(), size = stream.Length };
byte[] payload = JsonSerializer.SerializeToUtf8Bytes(release);
using var rsa = RSA.Create(); rsa.ImportFromPem(File.ReadAllText(privateKey));
if (rsa.KeySize < 3072) throw new ArgumentException("RSA 3072+ required");
var envelope = new { payload = Convert.ToBase64String(payload), signature = Convert.ToBase64String(rsa.SignData(payload, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)) };
File.WriteAllText(output, JsonSerializer.Serialize(envelope, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"Signed {version}: {stream.Length} bytes");
