using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SepidarGateway.Api.Interfaces;
using SepidarGateway.Api.Options;

namespace SepidarGateway.Api.Services;

public class SepidarService : ISepidarService
{
    private readonly IHttpClientWrapper _httpClientWrapper;
    private readonly IOptions<SepidarOptions> _options;
    private readonly ILogger<SepidarService> _logger;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

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
        var encryptionResult = EncryptIntegrationId(normalizedSerial, integrationId);

        var request = new RegisterDeviceUpstreamRequest
        {
            Cypher = encryptionResult.Cipher,
            IV = encryptionResult.Iv,
            IntegrationID = integrationId
        };

        var url = CombineUrl(settings.BaseUrl, registerDevice.Endpoint);

        try
        {
            return await _httpClientWrapper.PostAsync<RegisterDeviceUpstreamRequest, JsonDocument>(
                url,
                request,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogError(ex, "فراخوانی سرویس رجیستر دستگاه سپیدار با خطا مواجه شد.");
            throw;
        }
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

    private static (string Cipher, string Iv) EncryptIntegrationId(string serial, int integrationId)
    {
        var key = BuildEncryptionKey(serial);
        using var aes = Aes.Create();
        aes.KeySize = 128;
        aes.BlockSize = 128;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.Key = key;
        aes.GenerateIV();

        var plainBytes = Encoding.UTF8.GetBytes(integrationId.ToString(CultureInfo.InvariantCulture));
        using var encryptor = aes.CreateEncryptor(aes.Key, aes.IV);
        var cipherBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

        return (Convert.ToBase64String(cipherBytes), Convert.ToBase64String(aes.IV));
    }

    private static byte[] BuildEncryptionKey(string serial)
    {
        var digits = new string(serial.Where(char.IsDigit).ToArray());
        if (string.IsNullOrEmpty(digits))
        {
            throw new InvalidOperationException("سریال باید حداقل شامل یک رقم باشد.");
        }

        var doubled = string.Concat(digits, digits);
        if (doubled.Length < 16)
        {
            doubled = doubled.PadRight(16, '0');
        }

        var keyString = doubled.Length > 16 ? doubled[..16] : doubled;
        return Encoding.UTF8.GetBytes(keyString);
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
