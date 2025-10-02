using System.Text.Json.Nodes;
using SepidarGateway.Api.Models;
using SepidarGateway.Api.Interfaces;
using SepidarGateway.Api.Interfaces.Sepidar;

namespace SepidarGateway.Api.Services.Sepidar;

public class RegisterService : IRegisterService
{
    private readonly IHttpClientWrapper _http;
    private readonly IConfigService _config;
    private readonly ICryptoService _crypto;

    public RegisterService(IHttpClientWrapper http, IConfigService config, ICryptoService crypto)
    {
        _http = http;
        _config = config;
        _crypto = crypto;
    }

    public async Task<JsonObject?> RegisterAsync(string serial, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(serial))
            throw new ArgumentException("ارسال سریال دستگاه الزامی است.", nameof(serial));

        var baseUrl = _config.GetBaseUrlForRegisterOrThrow();
        var endpoint = _config.GetRegisterEndpointOrThrow();
        var url = _config.CombineUrl(baseUrl, endpoint);

        var rawSerial = serial.Trim();
        var integrationId = _config.ExtractIntegrationIdOrThrow(rawSerial, _config.GetIntegrationIdLengthOrDefault());

        var keyPreview = _crypto.BuildKeyFromSerial(rawSerial);
        var enc = _crypto.EncryptIntegrationId(integrationId, keyPreview);

        var request = new RegisterDeviceUpstreamRequest
        {
            Cypher = enc.Cipher,
            IV = enc.Iv,
            IntegrationID = integrationId
        };

        var headers = new Dictionary<string, string> { ["Accept"] = "application/json" };
        var payload = await _http.PostRawAsync(url, request, headers: headers, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(payload))
            return null;

        var root = JsonNode.Parse(payload) as JsonObject;
        if (root is null)
        {
            return new JsonObject { ["Raw"] = payload };
        }

        if (TryLocateCypherAndIv(root, out var hostObject, out var cypherB64, out var ivB64))
        {
            try
            {
                var (ok, xml, usedStrategy) = _crypto.TryDecryptPublicKeyWithStrategies(cypherB64, ivB64, rawSerial);
                if (!ok)
                    throw new InvalidOperationException("رمزگشایی کلید عمومی از پاسخ رجیستر ناموفق بود. احتمالاً کلید AES نادرست مشتق شده است.");

                var (modulus, exponent) = _crypto.TryParseRsaXml(xml);
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
            catch (Exception ex)
            {
                hostObject["PublicKeyDecryptError"] = ex.Message;
            }
        }

        return root;
    }

    private static bool TryLocateCypherAndIv(JsonObject root, out JsonObject host, out string cypherB64, out string ivB64)
    {
        if (TryGetStringProperty(root, "Cypher", out cypherB64) && TryGetStringProperty(root, "IV", out ivB64))
        {
            host = root; return true;
        }
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
