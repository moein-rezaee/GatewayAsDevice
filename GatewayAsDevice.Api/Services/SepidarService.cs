using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using System.Text.Encodings.Web;
using Microsoft.Extensions.Options;
using SepidarGateway.Api.Interfaces;
using SepidarGateway.Api.Options;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using SepidarGateway.Api.Models;

namespace SepidarGateway.Api.Services;

public class SepidarService : ISepidarService
{
    private readonly IHttpClientWrapper _httpClientWrapper;
    private readonly IOptions<SepidarOptions> _options;
    private readonly ILogger<SepidarService> _logger;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        // Keep original property casing for interop with upstream service
        PropertyNamingPolicy = null,
        // Avoid escaping '+' and '/' in base64 strings in logs
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public SepidarService(
        IHttpClientWrapper httpClientWrapper,
        IOptions<SepidarOptions> options,
        ILogger<SepidarService> logger)
    {
        _httpClientWrapper = httpClientWrapper;
        _options = options;
        _logger = logger;
    }

    public async Task<JsonNode?> RegisterDeviceAsync(string serial, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(serial))
        {
            throw new ArgumentException("ارسال سریال دستگاه الزامی است.", nameof(serial));
        }

        var settings = _options.Value ?? throw new InvalidOperationException("تنظیمات سپیدار مقداردهی نشده است.");
        var registerDevice = settings.RegisterDevice ?? throw new InvalidOperationException("پیکربندی رجیستر دستگاه یافت نشد.");

        if (string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            throw new InvalidOperationException("آدرس پایه سپیدار در appsettings تنظیم نشده است.");
        }

        if (string.IsNullOrWhiteSpace(registerDevice.Endpoint))
        {
            throw new InvalidOperationException("مسیر اندپوینت رجیستر دستگاه مشخص نشده است.");
        }

        var rawSerial = serial.Trim(); // بدون تغییر حروف؛ فقط Trim فضای خالی
        var integrationId = ExtractIntegrationId(rawSerial, registerDevice.IntegrationIdLength);
        var url = CombineUrl(settings.BaseUrl, registerDevice.Endpoint);

        // کلید بر اساس دستور داک: دو بار سریال پشت سر هم (و در صورت نیاز تکرار تا 16 کاراکتر)، بدون تغییر کیس
        var keyPreview = BuildKeyFromSerial(rawSerial);
        var enc = EncryptIntegrationId(rawSerial, integrationId, keyPreview);

        // Debug log raw inputs used for calculation
        _logger.LogInformation("Raw inputs => Serial: {Serial}; IntegrationID: {IntegrationID}; Key(16): {Key}", rawSerial, integrationId, keyPreview);

        var request = new RegisterDeviceUpstreamRequest
        {
            Cypher = enc.Cipher,
            IV = enc.Iv,
            IntegrationID = integrationId
        };

        // Log outgoing request details (URL, headers, body) and ready-to-run curl commands
        var jsonBody = JsonSerializer.Serialize(request, _jsonOptions);
        var (curlBash, curlPwsh) = BuildCurlCommands(url, jsonBody);
        _logger.LogInformation(
            "درخواست رجیستر دستگاه سپیدار در شُرُوع ارسال\nURL: {Url}\nHeaders:\n  Content-Type: application/json\nBody (Postman raw JSON):\n{Body}\nCurl (bash/sh):\n{CurlBash}\nCurl (PowerShell):\n{CurlPwsh}",
            url, jsonBody, curlBash, curlPwsh);

        try
        {
            var headers = new Dictionary<string, string> { ["Accept"] = "application/json" };
            var payload = await _httpClientWrapper.PostRawAsync(url, request, headers: headers, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(payload))
            {
                return null;
            }

            // Parse upstream JSON to a mutable node
            var root = JsonNode.Parse(payload) as JsonObject;
            if (root is null)
            {
                // Fallback: return raw text as a single field
                return new JsonObject
                {
                    ["Raw"] = payload
                };
            }

            // Try to find Cypher and IV either at root or nested (e.g., Data)
            if (TryLocateCypherAndIv(root, out var hostObject, out var cypherB64, out var ivB64))
            {
                try
                {
                    var key16 = BuildKeyFromSerial(rawSerial);
                    var xml = DecryptToXmlString(cypherB64, ivB64, key16);

                    // Attempt to parse XML for RSA parameters
                    var (modulus, exponent) = TryParseRsaXml(xml);

                    // Attach results next to the found Cypher/IV
                    hostObject["PublicKeyXml"] = xml;
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
                    _logger.LogWarning(ex, "رمزگشایی پاسخ سپیدار یا استخراج XML با خطا مواجه شد.");
                    hostObject["PublicKeyDecryptError"] = ex.Message;
                }
            }

            return root;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogError(ex, "فراخوانی سرویس رجیستر دستگاه سپیدار با خطا مواجه شد.");
            throw;
        }
    }

    private static (string Bash, string PowerShell) BuildCurlCommands(string url, string jsonBody)
    {
        // Bash-compatible quoting (single quotes); escape single quotes just in case
        static string QuoteBash(string s) => "'" + s.Replace("'", "'\"'\"'") + "'";
        var curlBash = $"curl --location {QuoteBash(url)} --header {QuoteBash("Content-Type: application/json")} --data {QuoteBash(jsonBody)}";

        // PowerShell-compatible quoting (double quotes); escape embedded double quotes with backtick
        static string QuotePwsh(string s) => "\"" + s.Replace("`", "``").Replace("\"", "`\"") + "\"";
        var curlPwsh = $"curl --location {QuotePwsh(url)} --header {QuotePwsh("Content-Type: application/json")} --data {QuotePwsh(jsonBody)}";

        return (curlBash, curlPwsh);
    }

    private static int ExtractIntegrationId(string serial, int digitCount)
    {
        if (digitCount <= 0)
        {
            throw new InvalidOperationException("طول شناسه یکپارچه‌سازی باید بزرگ‌تر از صفر باشد.");
        }

        var digits = new string(serial.Where(char.IsDigit).ToArray());
        if (digits.Length < digitCount)
        {
            throw new InvalidOperationException($"سریال باید حداقل {digitCount} رقم داشته باشد.");
        }

        var integrationIdSlice = digits[..digitCount];
        if (!int.TryParse(integrationIdSlice, NumberStyles.None, CultureInfo.InvariantCulture, out var integrationId))
        {
            throw new InvalidOperationException("امکان تبدیل شناسه یکپارچه‌سازی به عدد وجود ندارد.");
        }

        return integrationId;
    }

    private static (string Cipher, string Iv) EncryptIntegrationId(string serial, int integrationId, string key16)
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

    private static string DecryptToXmlString(string cypherBase64, string ivBase64, string key16)
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
        var xml = Encoding.UTF8.GetString(plainBytes).Trim();
        return xml;
    }

    private static (string Modulus, string Exponent) TryParseRsaXml(string xml)
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

    private static bool TryLocateCypherAndIv(JsonObject root, out JsonObject host, out string cypherB64, out string ivB64)
    {
        // Case-insensitive search at root
        if (TryGetStringProperty(root, "Cypher", out cypherB64) && TryGetStringProperty(root, "IV", out ivB64))
        {
            host = root;
            return true;
        }

        // Common wrapper: Data or data
        foreach (var key in new[] { "Data", "data", "Result", "result" })
        {
            if (root[key] is JsonObject obj)
            {
                if (TryGetStringProperty(obj, "Cypher", out cypherB64) && TryGetStringProperty(obj, "IV", out ivB64))
                {
                    host = obj;
                    return true;
                }
            }
        }

        // Fallback: scan first-level objects
        foreach (var kv in root)
        {
            if (kv.Value is JsonObject candidate)
            {
                if (TryGetStringProperty(candidate, "Cypher", out cypherB64) && TryGetStringProperty(candidate, "IV", out ivB64))
                {
                    host = candidate;
                    return true;
                }
            }
        }

        cypherB64 = string.Empty;
        ivB64 = string.Empty;
        host = root;
        return false;
    }

    private static bool TryGetStringProperty(JsonObject obj, string name, out string value)
    {
        value = string.Empty;
        // Exact
        if (obj.TryGetPropertyValue(name, out var node) && node is JsonValue jv1 && jv1.TryGetValue(out string? s1) && !string.IsNullOrWhiteSpace(s1))
        {
            value = s1;
            return true;
        }
        // Case-insensitive
        var match = obj.FirstOrDefault(kv => string.Equals(kv.Key, name, StringComparison.OrdinalIgnoreCase));
        if (match.Value is JsonValue jv2 && jv2.TryGetValue(out string? s2) && !string.IsNullOrWhiteSpace(s2))
        {
            value = s2;
            return true;
        }
        return false;
    }

    private static string BuildKeyFromSerial(string serial)
    {
        var src = (serial ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(src))
        {
            throw new InvalidOperationException("سریال دستگاه نامعتبر است.");
        }

        // طبق داک: کلید = سریال + سریال (و در صورت نیاز تکرار تا طول 16)، بدون تغییر حروف
        var doubled = string.Concat(src, src);
        if (doubled.Length >= 16)
        {
            return doubled[..16];
        }

        var sb = new StringBuilder(16);
        while (sb.Length < 16)
        {
            sb.Append(src);
        }

        return sb.ToString()[..16];
    }

    private static string CombineUrl(string baseUrl, string endpoint)
    {
        if (Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri))
        {
            return new Uri(baseUri, endpoint).ToString();
        }

        throw new InvalidOperationException("آدرس پایه سپیدار معتبر نیست.");
    }

}


// URL: http://187.131.66.32:7373/api/Devices/Register
// Headers:
//   Content-Type: application/json
// Body:
// {"Cypher":"20coil5J/9tQTD8w3Dniiw==","IV":"vJ2IPQAvkM\u002BetCCiVTKuCA==","IntegrationID":1000}
// Curl:
// curl --location 'http://187.131.66.32:7373/api/Devices/Register' --header 'Content-Type: application/json' --data '{"Cypher":"20coil5J/9tQTD8w3Dniiw==","IV":"vJ2IPQAvkM\u002BetCCiVTKuCA==","IntegrationID":1000}'

// fail: SepidarGateway.Api.Services.SepidarService[0]
//       فراخوانی سرویس رجیستر دستگاه سپیدار با خطا مواجه شد.
//       System.Net.Http.HttpRequestException: A connection attempt failed because the connected party did not properly respond after a period of time, or established connection failed because connected host has failed to respond. (187.131.66.32:7373)
//        ---> System.Net.Sockets.SocketException (10060): A connection attempt failed because the connected party did not properly respond after a period of time, or established connection failed because connected host has failed to respond.
//          at System.Net.Sockets.Socket.AwaitableSocketAsyncEventArgs.ThrowException(SocketError error, CancellationToken cancellationToken)
//          at System.Net.Sockets.Socket.AwaitableSocketAsyncEventArgs.System.Threading.Tasks.Sources.IValueTaskSource.GetResult(Int16 token)
//          at System.Net.Sockets.Socket.<ConnectAsync>g__WaitForConnectWithCancellation|285_0(AwaitableSocketAsyncEventArgs saea, ValueTask connectTask, CancellationToken cancellationToken)
//          at System.Net.Http.HttpConnectionPool.ConnectToTcpHostAsync(String host, Int32 port, HttpRequestMessage initialRequest, Boolean async, CancellationToken cancellationToken)
//          --- End of inner exception stack trace ---
//          at System.Net.Http.HttpConnectionPool.ConnectToTcpHostAsync(String host, Int32 port, HttpRequestMessage initialRequest, Boolean async, CancellationToken cancellationToken)
//          at System.Net.Http.HttpConnectionPool.ConnectAsync(HttpRequestMessage request, Boolean async, CancellationToken cancellationToken)
//          at System.Net.Http.HttpConnectionPool.CreateHttp11ConnectionAsync(HttpRequestMessage request, Boolean async, CancellationToken cancellationToken)
//          at System.Net.Http.HttpConnectionPool.InjectNewHttp11ConnectionAsync(QueueItem queueItem)
//          at System.Threading.Tasks.TaskCompletionSourceWithCancellation`1.WaitWithCancellationAsync(CancellationToken cancellationToken)
//          at System.Net.Http.HttpConnectionPool.SendWithVersionDetectionAndRetryAsync(HttpRequestMessage request, Boolean async, Boolean doRequestAuth, CancellationToken cancellationToken)
//          at System.Net.Http.DiagnosticsHandler.SendAsyncCore(HttpRequestMessage request, Boolean async, CancellationToken cancellationToken)
//          at System.Net.Http.RedirectHandler.SendAsync(HttpRequestMessage request, Boolean async, CancellationToken cancellationToken)
//          at Microsoft.Extensions.Http.Logging.LoggingHttpMessageHandler.<SendCoreAsync>g__Core|4_0(HttpRequestMessage request, Boolean useAsync, CancellationToken cancellationToken)
//          at Microsoft.Extensions.Http.Logging.LoggingScopeHttpMessageHandler.<SendCoreAsync>g__Core|4_0(HttpRequestMessage request, Boolean useAsync, CancellationToken cancellationToken)
//          at System.Net.Http.HttpClient.<SendAsync>g__Core|83_0(HttpRequestMessage request, HttpCompletionOption completionOption, CancellationTokenSource cts, Boolean disposeCts, CancellationTokenSource pendingRequestsCts, CancellationToken originalCancellationToken)
//          at SepidarGateway.Api.Services.HttpClientWrapper.SendAsync[TRequest,TResponse](HttpMethod method, String url, TRequest body, IDictionary`2 headers, IDictionary`2 queryParameters, CancellationToken cancellationToken) in C:\MoeinRezaee\GatewayAsDevice\GatewayAsDevice.Api\Services\HttpClientWrapper.cs:line 78
//          at SepidarGateway.Api.Services.SepidarService.RegisterDeviceAsync(String serial, CancellationToken cancellationToken) in C:\MoeinRezaee\GatewayAsDevice\GatewayAsDevice.Api\Services\SepidarService.cs:line 78
