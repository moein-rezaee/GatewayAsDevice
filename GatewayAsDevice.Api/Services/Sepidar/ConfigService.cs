using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using SepidarGateway.Api.Models;
using System.Globalization;
using SepidarGateway.Api.Interfaces.Sepidar;

namespace SepidarGateway.Api.Services.Sepidar;

public class ConfigService : IConfigService
{
    private readonly IOptions<SepidarOptions> _options;
    private readonly IConfiguration _configuration;

    public ConfigService(IOptions<SepidarOptions> options, IConfiguration configuration)
    {
        _options = options;
        _configuration = configuration;
    }

    public SepidarOptions GetOptionsOrThrow()
        => _options.Value ?? throw new InvalidOperationException("تنظیمات سپیدار مقداردهی نشده است.");

    public string GetBaseUrlForRegisterOrThrow()
    {
        var settings = GetOptionsOrThrow();
        if (string.IsNullOrWhiteSpace(settings.BaseUrl))
            throw new InvalidOperationException("آدرس پایه سپیدار در appsettings تنظیم نشده است.");
        return settings.BaseUrl;
    }

    public string GetBaseUrlOrThrow()
    {
        var settings = GetOptionsOrThrow();
        if (string.IsNullOrWhiteSpace(settings.BaseUrl))
            throw new InvalidOperationException("آدرس پایه سپیدار تنظیم نشده است.");
        return settings.BaseUrl;
    }

    public string GetRegisterEndpointOrThrow()
    {
        var settings = GetOptionsOrThrow();
        var register = settings.RegisterDevice ?? throw new InvalidOperationException("پیکربندی رجیستر دستگاه یافت نشد.");
        if (string.IsNullOrWhiteSpace(register.Endpoint))
            throw new InvalidOperationException("مسیر اندپوینت رجیستر دستگاه مشخص نشده است.");
        return register.Endpoint!;
    }

    public int GetIntegrationIdLengthOrDefault()
    {
        var settings = GetOptionsOrThrow();
        return settings.RegisterDevice?.IntegrationIdLength > 0 ? settings.RegisterDevice!.IntegrationIdLength : 4;
    }

    public int ExtractIntegrationIdOrThrow(string serial, int digitCount)
    {
        if (digitCount <= 0) throw new InvalidOperationException("طول شناسه یکپارچه‌سازی باید بزرگ‌تر از صفر باشد.");
        var digits = new string((serial ?? string.Empty).Where(char.IsDigit).ToArray());
        if (digits.Length < digitCount) throw new InvalidOperationException($"سریال باید حداقل {digitCount} رقم داشته باشد.");
        var slice = digits[..digitCount];
        if (!int.TryParse(slice, NumberStyles.None, CultureInfo.InvariantCulture, out var id))
            throw new InvalidOperationException("امکان تبدیل شناسه یکپارچه‌سازی به عدد وجود ندارد.");
        return id;
    }

    public string CombineUrl(string baseUrl, string endpoint)
    {
        if (Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri))
            return new Uri(baseUri, endpoint).ToString();
        throw new InvalidOperationException("آدرس پایه سپیدار معتبر نیست.");
    }

    public string GetUsersLoginEndpoint()
        => _configuration.GetValue<string>("Sepidar:UsersLogin:Endpoint") ?? "/api/users/login";
}
