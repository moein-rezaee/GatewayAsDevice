using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Xml.Linq;

using SepidarGateway.Api.Interfaces.Sepidar;

namespace SepidarGateway.Api.Services.Sepidar;

public class CryptoService : ICryptoService
{
    public (string Cipher, string Iv) EncryptIntegrationId(int integrationId, string key16)
    {
        var key = Encoding.UTF8.GetBytes(key16);
        using var aes = Aes.Create();
        aes.KeySize = 128;
        aes.BlockSize = 128;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.Key = key;
        aes.GenerateIV();

        var plainBytes = Encoding.ASCII.GetBytes(integrationId.ToString());
        using var encryptor = aes.CreateEncryptor(aes.Key, aes.IV);
        var cipherBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);
        return (Convert.ToBase64String(cipherBytes), Convert.ToBase64String(aes.IV));
    }

    public string DecryptToXml(string cypherBase64, string ivBase64, string key16)
    {
        if (string.IsNullOrWhiteSpace(cypherBase64)) throw new ArgumentException("Cypher خالی است.", nameof(cypherBase64));
        if (string.IsNullOrWhiteSpace(ivBase64)) throw new ArgumentException("IV خالی است.", nameof(ivBase64));
        if (string.IsNullOrWhiteSpace(key16) || key16.Length != 16) throw new ArgumentException("کلید AES نامعتبر است.", nameof(key16));

        var cipherBytes = Convert.FromBase64String(cypherBase64);
        var iv = Convert.FromBase64String(ivBase64);
        var key = Encoding.UTF8.GetBytes(key16);

        using var aes = Aes.Create();
        aes.KeySize = 128;
        aes.BlockSize = 128;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.Key = key;
        aes.IV = iv;

        using var decryptor = aes.CreateDecryptor(aes.Key, aes.IV);
        var plainBytes = decryptor.TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);
        return Encoding.UTF8.GetString(plainBytes).Trim();
    }

    public (bool Ok, string Xml, string Strategy) TryDecryptPublicKeyWithStrategies(string cypherB64, string ivB64, string serial)
    {
        foreach (var (k, name) in GenerateCandidateKeys(serial))
        {
            try
            {
                var xml = DecryptToXml(cypherB64, ivB64, k);
                if (LooksLikeRsaXml(xml))
                    return (true, xml, name);
            }
            catch
            {
                // try next
            }
        }
        return (false, string.Empty, string.Empty);
    }

    public bool TryGetRsaParameters(JsonNode? responseNode, out RSAParameters rsaParams)
    {
        rsaParams = default;
        if (responseNode is null) return false;
        if (responseNode["PublicKey"] is JsonObject pk)
        {
            var modStr = pk["Modulus"]?.GetValue<string>();
            var expStr = pk["Exponent"]?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(modStr) && !string.IsNullOrWhiteSpace(expStr))
            {
                rsaParams = new RSAParameters
                {
                    Modulus = Convert.FromBase64String(modStr!),
                    Exponent = Convert.FromBase64String(expStr!)
                };
                return true;
            }
        }
        if (responseNode["PublicKeyXml"]?.GetValue<string>() is string xml && !string.IsNullOrWhiteSpace(xml))
        {
            try
            {
                var (mod, exp) = TryParseRsaXml(xml);
                if (!string.IsNullOrWhiteSpace(mod) && !string.IsNullOrWhiteSpace(exp))
                {
                    rsaParams = new RSAParameters
                    {
                        Modulus = Convert.FromBase64String(mod),
                        Exponent = Convert.FromBase64String(exp)
                    };
                    return true;
                }
            }
            catch
            {
                // ignore
            }
        }
        return false;
    }

    public (string Modulus, string Exponent) TryParseRsaXml(string xml)
    {
        try
        {
            var doc = XDocument.Parse(xml);
            var root = doc.Root;
            if (root is null) return (string.Empty, string.Empty);
            var modulus = root.Element("Modulus")?.Value ?? string.Empty;
            var exponent = root.Element("Exponent")?.Value ?? string.Empty;
            return (modulus, exponent);
        }
        catch
        {
            return (string.Empty, string.Empty);
        }
    }

    public string BuildKeyFromSerial(string serial)
    {
        var src = (serial ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(src))
            throw new InvalidOperationException("سریال دستگاه نامعتبر است.");

        var doubled = string.Concat(src, src);
        if (doubled.Length >= 16)
            return doubled[..16];

        var sb = new StringBuilder(16);
        while (sb.Length < 16) sb.Append(src);
        return sb.ToString()[..16];
    }

    public byte[] GuidToRfc4122Bytes(Guid guid)
    {
        var b = guid.ToByteArray();
        var r = new byte[16];
        r[0] = b[3]; r[1] = b[2]; r[2] = b[1]; r[3] = b[0];
        r[4] = b[5]; r[5] = b[4];
        r[6] = b[7]; r[7] = b[6];
        Array.Copy(b, 8, r, 8, 8);
        return r;
    }

    public string ToHex(byte[] bytes)
    {
        var sb = new StringBuilder(bytes.Length * 2);
        foreach (var b in bytes) sb.Append(b.ToString("x2"));
        return sb.ToString();
    }

    private static IEnumerable<(string Key, string Name)> GenerateCandidateKeys(string serial)
    {
        serial = (serial ?? string.Empty).Trim();
        var digits = new string(serial.Where(char.IsDigit).ToArray());
        yield return (CutOrRepeat(serial + serial, 16), "Serial+Serial Left16");
        yield return (CutOrRepeat(serial, 16), "Serial Left16");
        if (!string.IsNullOrEmpty(digits)) yield return (CutOrRepeat(digits, 16), "Digits Left/Repeat16");
        yield return (RepeatToLength(serial, 16), "Serial RepeatTo16");
        if (!string.IsNullOrEmpty(digits)) yield return (RepeatToLength(digits, 16), "Digits RepeatTo16");
        var up = serial.ToUpperInvariant();
        var down = serial.ToLowerInvariant();
        yield return (CutOrRepeat(up + up, 16), "Upper Serial+Serial Left16");
        yield return (CutOrRepeat(down + down, 16), "Lower Serial+Serial Left16");
    }

    private static string CutOrRepeat(string s, int len)
    {
        if (string.IsNullOrEmpty(s)) throw new InvalidOperationException("سریال برای استخراج کلید خالی است.");
        if (s.Length >= len) return s[..len];
        return RepeatToLength(s, len);
    }

    private static string RepeatToLength(string s, int len)
    {
        if (string.IsNullOrEmpty(s)) throw new InvalidOperationException("رشته ورودی برای تکرار خالی است.");
        var sb = new StringBuilder(len);
        while (sb.Length < len) sb.Append(s);
        return sb.ToString()[..len];
    }

    private static bool LooksLikeRsaXml(string xml)
        => !string.IsNullOrWhiteSpace(xml) && xml.Contains("<RSAKeyValue", StringComparison.OrdinalIgnoreCase);
}
