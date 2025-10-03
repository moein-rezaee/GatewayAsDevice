using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Sepidar.Extension.Interfaces;

namespace Sepidar.Extension.Services;

public class PublicKeyProcessor : IPublicKeyProcessor
{
    private readonly ISerialKeyDeriver _deriver;
    public PublicKeyProcessor(ISerialKeyDeriver? deriver = null)
    {
        _deriver = deriver ?? new SerialKeyDeriver();
    }

    public (bool Ok, string Xml, string Strategy) TryDecryptPublicKeyWithStrategies(string cypherB64, string ivB64, string serial)
    {
        foreach (var (k, name) in _deriver.GenerateCandidateKeys(serial))
        {
            try
            {
                var xml = DecryptToXml(cypherB64, ivB64, k);
                if (LooksLikeRsaXml(xml))
                {
                    return (true, xml, name);
                }
            }
            catch { }
        }
        return (false, string.Empty, string.Empty);
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
        catch { return (string.Empty, string.Empty); }
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
            catch { }
        }
        return false;
    }

    public string EncryptGuidArbitraryCode(Guid guid, RSAParameters rsaParams)
    {
        var guidBytes = GuidToRfc4122Bytes(guid);
        using var rsa = RSA.Create();
        rsa.ImportParameters(rsaParams);
        var enc = rsa.Encrypt(guidBytes, RSAEncryptionPadding.Pkcs1);
        return Convert.ToBase64String(enc);
    }

    private static byte[] GuidToRfc4122Bytes(Guid guid)
    {
        var b = guid.ToByteArray();
        var r = new byte[16];
        r[0] = b[3]; r[1] = b[2]; r[2] = b[1]; r[3] = b[0];
        r[4] = b[5]; r[5] = b[4];
        r[6] = b[7]; r[7] = b[6];
        Array.Copy(b, 8, r, 8, 8);
        return r;
    }

    private static string DecryptToXml(string cypherBase64, string ivBase64, string key16)
    {
        var cipherBytes = Convert.FromBase64String(cypherBase64);
        var iv = Convert.FromBase64String(ivBase64);
        var key = Encoding.UTF8.GetBytes(key16);
        using var aes = Aes.Create();
        aes.KeySize = 128; aes.BlockSize = 128; aes.Mode = CipherMode.CBC; aes.Padding = PaddingMode.PKCS7;
        aes.Key = key; aes.IV = iv;
        using var decryptor = aes.CreateDecryptor(aes.Key, aes.IV);
        var plainBytes = decryptor.TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);
        return Encoding.UTF8.GetString(plainBytes).Trim();
    }

    private static bool LooksLikeRsaXml(string xml)
        => !string.IsNullOrWhiteSpace(xml) && xml.Contains("<RSAKeyValue", StringComparison.OrdinalIgnoreCase);
}
