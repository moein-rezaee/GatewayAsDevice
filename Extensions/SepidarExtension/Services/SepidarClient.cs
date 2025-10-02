using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Sepidar.Extension.Interfaces;
using HttpClientRestExtension.Interfaces;
using HttpClientRestExtension.Services;
using Sepidar.Extension.Services;

namespace Sepidar.Extension.Services;

public class SepidarClient : ISepidarClient
{
    private readonly IHttpRestClient _rest;
    private readonly IIntegrationIdExtractor _idExtractor;
    private readonly ISerialKeyDeriver _keyDeriver;
    private readonly IPublicKeyProcessor _pkProcessor;

    public SepidarClient(
        IHttpRestClient? restClient = null,
        IIntegrationIdExtractor? idExtractor = null,
        ISerialKeyDeriver? keyDeriver = null,
        IPublicKeyProcessor? pkProcessor = null)
    {
        _rest = restClient ?? new HttpRestClient();
        _idExtractor = idExtractor ?? new IntegrationIdExtractor();
        _keyDeriver = keyDeriver ?? new SerialKeyDeriver();
        _pkProcessor = pkProcessor ?? new PublicKeyProcessor(_keyDeriver);
    }

    public async Task<JsonObject?> RegisterAsync(SepidarClientOptions options, CancellationToken ct = default)
    {
        ValidateOptions(options, requireCredentials: false);
        var url = CombineUrl(options.BaseUrl, options.RegisterEndpoint);
        var serial = (options.Serial ?? string.Empty).Trim();
        var integrationId = _idExtractor.Extract(serial, options.IntegrationIdLength);
        var keyPreview = _keyDeriver.BuildKey(serial);
        var (cipher, iv) = EncryptIntegrationId(integrationId, keyPreview);

        var body = new JsonObject
        {
            ["Cypher"] = cipher,
            ["IV"] = iv,
            ["IntegrationID"] = integrationId
        };

        var headers = new Dictionary<string, string> { ["Accept"] = "application/json" };
        var payload = await _rest.PostAsync(url, body, headers, null, ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(payload)) return null;

        var root = JsonNode.Parse(payload) as JsonObject;
        if (root is null) return new JsonObject { ["Raw"] = payload };

        if (TryLocateCypherAndIv(root, out var hostObject, out var cypherB64, out var ivB64))
        {
            try
            {
                var (ok, xml, usedStrategy) = _pkProcessor.TryDecryptPublicKeyWithStrategies(cypherB64, ivB64, serial);
                if (ok)
                {
                    var (modulus, exponent) = _pkProcessor.TryParseRsaXml(xml);
                    hostObject["PublicKeyXml"] = xml;
                    hostObject["PublicKeyDerivation"] = usedStrategy;
                    if (!string.IsNullOrWhiteSpace(modulus) || !string.IsNullOrWhiteSpace(exponent))
                    {
                        hostObject["PublicKey"] = new JsonObject
                        {
                            ["Modulus"] = modulus,
                            ["Exponent"] = exponent
                        };
                    }
                }
                else
                {
                    hostObject["PublicKeyDecryptError"] = "Failed to decrypt public key";
                }
            }
            catch (Exception ex)
            {
                hostObject["PublicKeyDecryptError"] = ex.Message;
            }
        }

        return root;
    }

    public async Task<JsonObject?> LoginAsync(SepidarClientOptions options, JsonObject registerNode, CancellationToken ct = default)
    {
        ValidateOptions(options, requireCredentials: true);
        if (registerNode is null) throw new InvalidOperationException("Invalid register response");

        var url = CombineUrl(options.BaseUrl, options.UsersLoginEndpoint);
        var integrationId = _idExtractor.Extract(options.Serial, options.IntegrationIdLength);

        if (!_pkProcessor.TryGetRsaParameters(registerNode, out var rsaParams))
            throw new InvalidOperationException("No RSA public key in register response");

        var arbitraryGuid = Guid.NewGuid();
        var arbitraryCode = arbitraryGuid.ToString();
        var guidBytes = GuidToRfc4122Bytes(arbitraryGuid);
        string encArbitraryCode;
        using (var rsa = RSA.Create())
        {
            rsa.ImportParameters(rsaParams);
            var enc = rsa.Encrypt(guidBytes, RSAEncryptionPadding.Pkcs1);
            encArbitraryCode = Convert.ToBase64String(enc);
        }

        var passwordHashHex = ToHex(MD5.HashData(Encoding.UTF8.GetBytes(options.Password)));

        var headers = new Dictionary<string, string>
        {
            ["Content-Type"] = "application/json",
            ["GenerationVersion"] = options.GenerationVersion.ToString(CultureInfo.InvariantCulture),
            ["IntegrationID"] = integrationId.ToString(CultureInfo.InvariantCulture),
            ["ArbitraryCode"] = arbitraryCode,
            ["EncArbitraryCode"] = encArbitraryCode
        };

        var body = new JsonObject
        {
            ["UserName"] = options.Username,
            ["PasswordHash"] = passwordHashHex
        };

        var payload = await _rest.PostAsync(url, body, headers, null, ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(payload)) return null;
        return JsonNode.Parse(payload) as JsonObject ?? new JsonObject { ["Raw"] = payload };
    }

    private static void ValidateOptions(SepidarClientOptions o, bool requireCredentials)
    {
        if (string.IsNullOrWhiteSpace(o.BaseUrl)) throw new InvalidOperationException("BaseUrl is required");
        if (string.IsNullOrWhiteSpace(o.Serial)) throw new InvalidOperationException("Serial is required");
        if (requireCredentials)
        {
            if (string.IsNullOrWhiteSpace(o.Username)) throw new InvalidOperationException("Username is required");
            if (string.IsNullOrWhiteSpace(o.Password)) throw new InvalidOperationException("Password is required");
        }
    }

    private static string CombineUrl(string baseUrl, string endpoint)
    {
        if (Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri))
            return new Uri(baseUri, endpoint).ToString();
        throw new InvalidOperationException("Invalid BaseUrl");
    }

    private static (string Cipher, string Iv) EncryptIntegrationId(int integrationId, string key16)
    {
        var key = Encoding.UTF8.GetBytes(key16);
        using var aes = Aes.Create();
        aes.KeySize = 128;
        aes.BlockSize = 128;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.Key = key;
        aes.GenerateIV();

        var plainBytes = Encoding.ASCII.GetBytes(integrationId.ToString(CultureInfo.InvariantCulture));
        using var encryptor = aes.CreateEncryptor(aes.Key, aes.IV);
        var cipherBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);
        return (Convert.ToBase64String(cipherBytes), Convert.ToBase64String(aes.IV));
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
    private static string ToHex(byte[] bytes)
    {
        var sb = new StringBuilder(bytes.Length * 2);
        foreach (var b in bytes) sb.Append(b.ToString("x2"));
        return sb.ToString();
    }
    private static bool TryLocateCypherAndIv(JsonObject root, out JsonObject host, out string cypherB64, out string ivB64)
    {
        if (TryGetStringProperty(root, "Cypher", out cypherB64) && TryGetStringProperty(root, "IV", out ivB64))
        { host = root; return true; }
        foreach (var key in new[] { "Data", "data", "Result", "result" })
        {
            if (root[key] is JsonObject obj)
            {
                if (TryGetStringProperty(obj, "Cypher", out cypherB64) && TryGetStringProperty(obj, "IV", out ivB64))
                { host = obj; return true; }
            }
        }
        foreach (var kv in root)
        {
            if (kv.Value is JsonObject candidate)
            {
                if (TryGetStringProperty(candidate, "Cypher", out cypherB64) && TryGetStringProperty(candidate, "IV", out ivB64))
                { host = candidate; return true; }
            }
        }
        cypherB64 = string.Empty; ivB64 = string.Empty; host = root; return false;
    }
    private static bool TryGetStringProperty(JsonObject obj, string name, out string value)
    {
        value = string.Empty;
        if (obj.TryGetPropertyValue(name, out var node) && node is JsonValue jv1 && jv1.TryGetValue(out string? s1) && !string.IsNullOrWhiteSpace(s1))
        { value = s1; return true; }
        var match = obj.FirstOrDefault(kv => string.Equals(kv.Key, name, StringComparison.OrdinalIgnoreCase));
        if (match.Value is JsonValue jv2 && jv2.TryGetValue(out string? s2) && !string.IsNullOrWhiteSpace(s2))
        { value = s2; return true; }
        return false;
    }
}
