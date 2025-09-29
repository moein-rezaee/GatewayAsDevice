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

    public async Task<JsonDocument?> RegisterDeviceAsync(string serial, CancellationToken cancellationToken = default)
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

        var normalizedSerial = serial.Trim();
        var integrationId = ExtractIntegrationId(normalizedSerial, registerDevice.IntegrationIdLength);
        var url = CombineUrl(settings.BaseUrl, registerDevice.Endpoint);

        // اگر حالت Auto است، چند حالت مرسوم کلید را امتحان می‌کنیم تا به موفقیت برسیم
        var modes = registerDevice.KeyMode == KeyDerivationMode.Auto
            ? new[]
            {
                KeyDerivationMode.Left16Digits,
                KeyDerivationMode.RepeatDigitsTo16,
                KeyDerivationMode.Left16Chars,
                KeyDerivationMode.RepeatCharsTo16
            }
            : new[] { registerDevice.KeyMode };

        foreach (var mode in modes)
        {
            var enc = EncryptIntegrationId(normalizedSerial, integrationId, mode);
            _logger.LogDebug("Sepidar encryption prepared. Mode={Mode}; IntegrationID={IntegrationID}", mode, integrationId);

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
                "درخواست رجیستر دستگاه سپیدار در شُرُوع ارسال\nMode: {Mode}\nURL: {Url}\nHeaders:\n  Content-Type: application/json\nBody (Postman raw JSON):\n{Body}\nCurl (bash/sh):\n{CurlBash}\nCurl (PowerShell):\n{CurlPwsh}",
                mode, url, jsonBody, curlBash, curlPwsh);

            try
            {
                var result = await _httpClientWrapper.PostAsync<RegisterDeviceUpstreamRequest, JsonDocument>(
                    url,
                    request,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                return result;
            }
            catch (HttpRequestException ex) when (ex.Message.Contains("عدم تطابق دستگاه") || ex.Message.Contains("400"))
            {
                _logger.LogWarning("کلید با Mode={Mode} تطابق نداشت. تلاش با حالت بعدی...", mode);
                // ادامه با حالت بعدی
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                _logger.LogError(ex, "فراخوانی سرویس رجیستر دستگاه سپیدار با خطا مواجه شد.");
                throw;
            }
        }

        return null;
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

    private static (string Cipher, string Iv) EncryptIntegrationId(string serial, int integrationId, KeyDerivationMode keyMode)
    {
        var key = BuildEncryptionKey(serial, keyMode);
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

    private static byte[] BuildEncryptionKey(string serial, KeyDerivationMode mode)
    {
        // کلید از 16 کاراکتر/رقم سمت چپ سریال ساخته می‌شود.
        var src = (serial ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(src))
        {
            throw new InvalidOperationException("سریال دستگاه نامعتبر است.");
        }

        string keyStr = mode switch
        {
            KeyDerivationMode.Left16Digits => BuildFromDigits(src),
            KeyDerivationMode.RepeatDigitsTo16 => RepeatTo16(new string(src.Where(char.IsDigit).ToArray())),
            KeyDerivationMode.RepeatCharsTo16 => RepeatTo16(src.ToUpperInvariant()),
            _ => BuildFromChars(src)
        };

        return Encoding.UTF8.GetBytes(keyStr);

        static string BuildFromChars(string s)
        {
            var u = s.ToUpperInvariant();
            return u.Length >= 16 ? u[..16] : u.PadRight(16, '0');
        }

        static string BuildFromDigits(string s)
        {
            var digits = new string(s.Where(char.IsDigit).ToArray());
            if (digits.Length >= 16)
            {
                return digits[..16];
            }

            return digits.PadRight(16, '0');
        }

        static string RepeatTo16(string seed)
        {
            if (string.IsNullOrEmpty(seed))
            {
                throw new InvalidOperationException("سریال فاقد محتوای کافی برای ساخت کلید است.");
            }

            var sb = new StringBuilder(16);
            while (sb.Length < 16)
            {
                var take = Math.Min(seed.Length, 16 - sb.Length);
                sb.Append(seed.AsSpan(0, take));
            }

            return sb.ToString();
        }
    }

    private static string CombineUrl(string baseUrl, string endpoint)
    {
        if (Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri))
        {
            return new Uri(baseUri, endpoint).ToString();
        }

        throw new InvalidOperationException("آدرس پایه سپیدار معتبر نیست.");
    }

    private sealed record RegisterDeviceUpstreamRequest
    {
        public string Cypher { get; init; } = string.Empty;
        public string IV { get; init; } = string.Empty;
        public int IntegrationID { get; init; }
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
