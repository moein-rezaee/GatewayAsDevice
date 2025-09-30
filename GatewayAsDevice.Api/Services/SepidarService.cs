using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Encodings.Web;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Configuration;
using SepidarGateway.Api.Interfaces;
using SepidarGateway.Api.Models;
using System.Text.Json.Nodes;
using System.Xml.Linq;

namespace SepidarGateway.Api.Services;

public class SepidarService : ISepidarService
{
    private readonly IHttpClientWrapper _httpClientWrapper;
    private readonly IOptions<SepidarOptions> _options;
    private readonly ICacheWrapper _cache;
    private readonly IConfiguration _configuration;

    public SepidarService(
        IHttpClientWrapper httpClientWrapper,
        IOptions<SepidarOptions> options,
        ICacheWrapper cache,
        IConfiguration configuration)
    {
        _httpClientWrapper = httpClientWrapper;
        _options = options;
        _cache = cache;
        _configuration = configuration;
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

        var keyPreview = BuildKeyFromSerial(rawSerial);
        var enc = EncryptIntegrationId(integrationId, keyPreview);

        var request = new RegisterDeviceUpstreamRequest
        {
            Cypher = enc.Cipher,
            IV = enc.Iv,
            IntegrationID = integrationId
        };

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
                    // Try multiple key-derivation strategies to match Sepidar docs differences
                    var (ok, xml, usedStrategy) = TryDecryptPublicKeyWithStrategies(cypherB64, ivB64, rawSerial);
                    if (!ok)
                    {
                        throw new InvalidOperationException("رمزگشایی کلید عمومی از پاسخ رجیستر ناموفق بود. احتمالاً کلید AES نادرست مشتق شده است.");
                    }

                    // Attempt to parse XML for RSA parameters
                    var (modulus, exponent) = TryParseRsaXml(xml);

                    // Attach results next to the found Cypher/IV
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

            // Cache register result for later use (login)
            try
            {
                var entry = new RegisterDeviceCacheEntry
                {
                    Serial = rawSerial,
                    Response = root,
                    CachedAt = DateTimeOffset.UtcNow
                };
                var expMin = _configuration.GetValue<int?>("Gateway:Cache:RegisterDevice:ExpirationMinutes") ?? 10;
                var opt = new CacheOptions { Secure = true, AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(expMin) };
                _cache.Set(BuildRegisterCacheKey(rawSerial), entry, opt);
                _cache.Set(BuildRegisterCurrentKey(), entry, opt);
            }
            catch
            {
                // ignore
            }

            return root;
        }
        catch (Exception)
        {
            throw;
        }
    }

    // Curl building handled by ICurlBuilder

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

    private static (bool Ok, string Xml, string Strategy) TryDecryptPublicKeyWithStrategies(string cypherB64, string ivB64, string serial)
    {
        foreach (var (k, name) in GenerateCandidateKeys(serial))
        {
            try
            {
                var xml = DecryptToXmlString(cypherB64, ivB64, k);
                if (LooksLikeRsaXml(xml))
                {
                    return (true, xml, name);
                }
            }
            catch
            {
                // try next
            }
        }
        return (false, string.Empty, string.Empty);
    }

    private static IEnumerable<(string Key, string Name)> GenerateCandidateKeys(string serial)
    {
        serial = (serial ?? string.Empty).Trim();
        var digits = new string(serial.Where(char.IsDigit).ToArray());

        // 1) serial+serial then cut to 16
        yield return (CutOrRepeat(serial + serial, 16), "Serial+Serial Left16");
        // 2) Left 16 chars of serial
        yield return (CutOrRepeat(serial, 16), "Serial Left16");
        // 3) Digits-only left 16 or repeat to 16
        if (!string.IsNullOrEmpty(digits))
        {
            yield return (CutOrRepeat(digits, 16), "Digits Left/Repeat16");
        }
        // 4) Repeat serial to 16 (exact repeat)
        yield return (RepeatToLength(serial, 16), "Serial RepeatTo16");
        // 5) Repeat digits to 16
        if (!string.IsNullOrEmpty(digits))
        {
            yield return (RepeatToLength(digits, 16), "Digits RepeatTo16");
        }
        // 6) Uppercase variations
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

    // Keys used for caching register response
    private static string BuildRegisterCacheKey(string serial) => $"device:register:{serial}";
    private static string BuildRegisterCurrentKey() => "device:register:current";

    // User login with cached register info
    public async Task<JsonNode?> UserLoginAsync(CancellationToken cancellationToken = default)
    {
        var entry = _cache.Get<RegisterDeviceCacheEntry>(BuildRegisterCurrentKey());
        if (entry is null || entry.Response is null || string.IsNullOrWhiteSpace(entry.Serial))
        {
            throw new InvalidOperationException("داده‌های رجیستر دستگاه در کش یافت نشد. ابتدا رجیستر دستگاه را فراخوانی کنید.");
        }

        var settings = _options.Value ?? throw new InvalidOperationException("تنظیمات سپیدار مقداردهی نشده است.");
        if (string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            throw new InvalidOperationException("آدرس پایه سپیدار تنظیم نشده است.");
        }

        var integrationIdLen = settings.RegisterDevice?.IntegrationIdLength > 0 ? settings.RegisterDevice.IntegrationIdLength : 4;
        var integrationId = ExtractIntegrationId(entry.Serial, integrationIdLen);

        // Credentials from env
        var userName = Environment.GetEnvironmentVariable("LOGIN_USERNAME")
            ?? throw new InvalidOperationException("LOGIN_USERNAME تنظیم نشده است.");
        var password = Environment.GetEnvironmentVariable("LOGIN_PASSWORD")
            ?? throw new InvalidOperationException("LOGIN_PASSWORD تنظیم نشده است.");

        // Extract RSA params from cached response
        if (!TryGetRsaParameters(entry.Response, out var rsaParams))
        {
            throw new InvalidOperationException("کلید عمومی برای رمزنگاری یافت نشد. ابتدا رجیستر دستگاه را فراخوانی کنید.");
        }

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

        var passwordHashHex = ConvertToHex(MD5.HashData(Encoding.UTF8.GetBytes(password)));

        var endpoint = _configuration.GetValue<string>("Sepidar:UsersLogin:Endpoint") ?? "/api/users/login";
        var url = CombineUrl(settings.BaseUrl, endpoint);

        var headers = new Dictionary<string, string>
        {
            ["Content-Type"] = "application/json",
            ["GenerationVersion"] = "110",
            ["IntegrationID"] = integrationId.ToString(),
            ["ArbitraryCode"] = arbitraryCode,
            ["EncArbitraryCode"] = encArbitraryCode
        };

        var body = new Dictionary<string, string>
        {
            ["UserName"] = userName,
            ["PasswordHash"] = passwordHashHex
        };

        // no logging

        try
        {
            var payload = await _httpClientWrapper.PostRawAsync(url, body, headers: headers, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(payload))
            {
                return null;
            }
            return JsonNode.Parse(payload);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
        throw;
        }
    }

    private static bool TryGetRsaParameters(JsonNode? responseNode, out RSAParameters rsaParams)
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

    private static string ConvertToHex(byte[] bytes)
    {
        var sb = new StringBuilder(bytes.Length * 2);
        foreach (var b in bytes)
        {
            sb.Append(b.ToString("x2"));
        }
        return sb.ToString();
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
}









