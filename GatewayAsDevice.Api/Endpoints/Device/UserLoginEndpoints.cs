using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SepidarGateway.Api.Interfaces;
using SepidarGateway.Api.Models;
using System.Linq;

namespace SepidarGateway.Api.Endpoints.Device;

public static class UserLoginEndpoints
{
    public static void MapUserLoginEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var configuration = endpoints.ServiceProvider.GetRequiredService<IConfiguration>();
        // برای سازگاری نام‌گذاری با RegisterDevice، مسیر Gateway را PascalCase نگه می‌داریم
        var gatewayLoginPath = configuration.GetValue<string>("Gateway:UsersLogin:Route") ?? "/api/Users/Login";
        var route = CombineRoute("/v1", gatewayLoginPath);

        endpoints.MapPost(route, UserLoginAsync)
            .WithName("SepidarUserLogin")
            .WithTags("Device")
            .WithOpenApi(operation =>
            {
                operation.Summary = "یوزر لاگین به سپیدار";
                operation.Description = "با استفاده از یوزر/پسورد از env و کلید عمومی رجیسترشده در کش، به سرویس /api/users/login لاگین می‌کند.";
                return operation;
            });
    }

    private static async Task<IResult> UserLoginAsync(
        ICacheWrapper cache,
        IHttpClientWrapper http,
        IOptions<SepidarOptions> options,
        IConfiguration configuration,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("Sepidar.UserLogin");
        // 1) بازیابی ورودی‌های لازم
        var entry = cache.Get<RegisterDeviceCacheEntry>("device:register:current");
        if (entry is null || entry.Response is null || string.IsNullOrWhiteSpace(entry.Serial))
        {
            return Results.BadRequest(new { message = "داده‌های رجیستر دستگاه در کش یافت نشد. ابتدا رجیستر دستگاه را فراخوانی کنید." });
        }

        var userName = LoginUserName;
        var password = LoginPassword;

        var settings = options.Value ?? new SepidarOptions();
        // طبق درخواست: IntegrationID همیشه از ارقام ابتدای سریال استخراج می‌شود (پیش‌فرض 4 رقم)
        var integrationIdLen = settings.RegisterDevice?.IntegrationIdLength > 0
            ? settings.RegisterDevice.IntegrationIdLength
            : 4;
        var integrationId = ExtractIntegrationId(entry.Serial, integrationIdLen);

        // 2) آماده‌سازی ArbitraryCode و نسخه API
        var arbitraryGuid = Guid.NewGuid();
        var arbitraryCode = arbitraryGuid.ToString();
        // بر اساس داکیومنت مقدار باید 110 باشد
        var generationVersion = "110";

        // 3) آماده‌سازی کلید عمومی RSA از کش
        if (!TryGetRsaParameters(entry.Response, out var rsaParams))
        {
            return Results.Problem("کلید عمومی برای رمزنگاری در کش یافت نشد. دوباره رجیستر دستگاه را فراخوانی کنید.");
        }

        // 4) رمزنگاری ArbitraryCode با RSA/PKCS1v1.5
        string encArbitraryCode;
        using (var rsa = RSA.Create())
        {
            rsa.ImportParameters(rsaParams);
            // طبق نمونه Python: رمزنگاری روی uuid.bytes (RFC 4122 order)، نه رشته متنی GUID
            var guidBytes = GuidToRfc4122Bytes(arbitraryGuid);
            var enc = rsa.Encrypt(guidBytes, RSAEncryptionPadding.Pkcs1);
            encArbitraryCode = Convert.ToBase64String(enc);
        }

        // 5) محاسبه MD5 پسورد به صورت hex
        var pwdHashBytes = MD5.HashData(Encoding.UTF8.GetBytes(password));
        var passwordHashHex = ConvertToHex(pwdHashBytes);

        // 6) آدرس و هدرها + بدنه
        if (string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            return Results.Problem("آدرس پایه سپیدار تنظیم نشده است.");
        }
        var endpoint = configuration.GetValue<string>("Sepidar:UsersLogin:Endpoint") ?? "/api/users/login";
        var url = CombineUrl(settings.BaseUrl, endpoint);

        var headers = new Dictionary<string, string>
        {
            ["Content-Type"] = "application/json",
            ["GenerationVersion"] = generationVersion,
            ["IntegrationID"] = integrationId.ToString(),
            ["ArbitraryCode"] = arbitraryCode,
            ["EncArbitraryCode"] = encArbitraryCode
        };

        var body = new Dictionary<string, string>
        {
            ["UserName"] = userName,
            ["PasswordHash"] = passwordHashHex
        };

        try
        {
            // 6.5) Logging: curl + headers/body + raw pre-encryption data
            var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                PropertyNamingPolicy = null,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
            var bodyJson = JsonSerializer.Serialize(body, jsonOptions);
            var (curlBash, curlPwsh) = BuildCurlCommands(url, headers, bodyJson);
            logger.LogInformation(
                "درخواست لاگین سپیدار در شُرُوع ارسال\nURL: {Url}\nHeaders:\n{Headers}\nBody (Postman raw JSON):\n{Body}\nCurl (bash/sh):\n{CurlBash}\nCurl (PowerShell):\n{CurlPwsh}\nRaw before encryption:\n  ArbitraryCode: {Arbitrary}\n  IntegrationID: {IntegrationID}\n  UserName: {UserName}\n  PasswordHashHex: {PwdHash}",
                url,
                string.Join("\n", headers.Select(kv => $"  {kv.Key}: {kv.Value}")),
                bodyJson,
                curlBash,
                curlPwsh,
                arbitraryCode,
                integrationId,
                userName,
                passwordHashHex);

            var payload = await http.PostRawAsync(url, body, headers: headers, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(payload))
            {
                return Results.NoContent();
            }

            var node = JsonNode.Parse(payload);
            return node is null ? Results.Content(payload, "application/json") : Results.Json(node);
        }
        catch (HttpRequestException ex)
        {
            return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status502BadGateway, title: "خطا در ارتباط با سپیدار");
        }
        catch (TaskCanceledException)
        {
            return Results.Problem(detail: "مهلت فراخوانی سرویس سپیدار به پایان رسید.", statusCode: StatusCodes.Status504GatewayTimeout, title: "اتمام مهلت ارتباط");
        }
    }

    private static int ExtractIntegrationId(string serial, int digitCount)
    {
        var digits = new string((serial ?? string.Empty).Where(char.IsDigit).ToArray());
        if (digitCount <= 0 || digits.Length < digitCount)
        {
            throw new InvalidOperationException("سریال برای استخراج IntegrationID کافی نیست.");
        }
        return int.Parse(digits[..digitCount]);
    }

    // حذف منطق خواندن IntegrationID از DeviceTitle؛ طبق سناریو فقط از سریال استخراج می‌شود

    private static bool TryGetRsaParameters(JsonNode? responseNode, out RSAParameters rsaParams)
    {
        rsaParams = default;
        if (responseNode is null) return false;

        // مسیر 1: PublicKey.Modulus/Exponent به صورت Base64
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

        // مسیر 2: PublicKeyXml
        if (responseNode["PublicKeyXml"]?.GetValue<string>() is string xml && !string.IsNullOrWhiteSpace(xml))
        {
            try
            {
                var modulus = GetXmlElementValue(xml, "Modulus");
                var exponent = GetXmlElementValue(xml, "Exponent");
                if (!string.IsNullOrWhiteSpace(modulus) && !string.IsNullOrWhiteSpace(exponent))
                {
                    rsaParams = new RSAParameters
                    {
                        Modulus = Convert.FromBase64String(modulus),
                        Exponent = Convert.FromBase64String(exponent)
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

    private static string? GetXmlElementValue(string xml, string element)
    {
        var open = "<" + element + ">";
        var close = "</" + element + ">";
        var i = xml.IndexOf(open, StringComparison.OrdinalIgnoreCase);
        if (i < 0) return null;
        i += open.Length;
        var j = xml.IndexOf(close, i, StringComparison.OrdinalIgnoreCase);
        if (j < 0) return null;
        return xml.Substring(i, j - i).Trim();
    }

    private static string ConvertToHex(byte[] bytes)
    {
        var sb = new StringBuilder(bytes.Length * 2);
        foreach (var b in bytes)
        {
            sb.Append(b.ToString("x2"));
        }
        return sb.ToString();
    }

    private static string CombineRoute(string versionPrefix, string endpoint)
    {
        versionPrefix ??= string.Empty;
        endpoint ??= string.Empty;

        static string Normalize(string s, bool leading)
            => string.IsNullOrWhiteSpace(s)
                ? string.Empty
                : leading
                    ? "/" + s.Trim().Trim('/')
                    : s.Trim().Trim('/');

        var v = Normalize(versionPrefix, leading: true);
        var ep = Normalize(endpoint, leading: false);
        return string.IsNullOrEmpty(ep) ? v : $"{v}/{ep}";
    }

    private static string CombineUrl(string baseUrl, string endpoint)
    {
        var baseUriOk = Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri);
        if (!baseUriOk || baseUri is null) throw new InvalidOperationException("BaseUrl نامعتبر است.");
        return new Uri(baseUri, endpoint).ToString();
    }

    // مطابق PythonSample: uuid.bytes در ترتیب RFC 4122 (big-endian برای سه فیلد آغازین)
    private static byte[] GuidToRfc4122Bytes(Guid guid)
    {
        var b = guid.ToByteArray();
        var r = new byte[16];
        // time_low
        r[0] = b[3]; r[1] = b[2]; r[2] = b[1]; r[3] = b[0];
        // time_mid
        r[4] = b[5]; r[5] = b[4];
        // time_hi_and_version
        r[6] = b[7]; r[7] = b[6];
        // clock_seq_hi_and_reserved, clock_seq_low, node(6)
        Array.Copy(b, 8, r, 8, 8);
        return r;
    }

    private static (string Bash, string PowerShell) BuildCurlCommands(string url, IDictionary<string, string> headers, string bodyJson)
    {
        static string QuoteBash(string s) => "'" + s.Replace("'", "'\"'\"'") + "'";
        static string QuotePwsh(string s) => "\"" + s.Replace("`", "``").Replace("\"", "`\"") + "\"";

        var bash = new StringBuilder();
        bash.Append("curl --location ").Append(QuoteBash(url));
        foreach (var (k, v) in headers)
        {
            bash.Append(' ').Append("--header ").Append(QuoteBash($"{k}: {v}"));
        }
        bash.Append(' ').Append("--header ").Append(QuoteBash("Content-Type: application/json"));
        bash.Append(' ').Append("--data ").Append(QuoteBash(bodyJson));

        var pwsh = new StringBuilder();
        pwsh.Append("curl --location ").Append(QuotePwsh(url));
        foreach (var (k, v) in headers)
        {
            pwsh.Append(' ').Append("--header ").Append(QuotePwsh($"{k}: {v}"));
        }
        pwsh.Append(' ').Append("--header ").Append(QuotePwsh("Content-Type: application/json"));
        pwsh.Append(' ').Append("--data ").Append(QuotePwsh(bodyJson));

        return (bash.ToString(), pwsh.ToString());
    }

    // Read-only properties for credentials from env
    private static string LoginUserName =>
        Environment.GetEnvironmentVariable("LOGIN_USERNAME")
        ?? throw new InvalidOperationException("LOGIN_USERNAME تنظیم نشده است. لطفاً آن را در env قرار دهید.");

    private static string LoginPassword =>
        Environment.GetEnvironmentVariable("LOGIN_PASSWORD")
        ?? throw new InvalidOperationException("LOGIN_PASSWORD تنظیم نشده است. لطفاً آن را در env قرار دهید.");
}
