using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using SepidarGateway.Api.Interfaces;
using SepidarGateway.Api.Models;

namespace SepidarGateway.Api.Services;

public class MemoryCacheWrapper : ICacheWrapper
{
    private readonly IMemoryCache _cache;
    private readonly byte[] _key; // 32 bytes برای AES-GCM-256
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    private const string Alg = "AESGCM256";

    public MemoryCacheWrapper(IMemoryCache cache, IConfiguration config)
    {
        _cache = cache;
        _key = LoadOrCreateKey(config);
    }

    public void Set<T>(string key, T value, CacheOptions? options = null)
    {
        if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("کلید کش نامعتبر است.", nameof(key));

        var secure = options?.Secure == true;
        var payload = secure ? (object)Encrypt(value) : value!;

        if (options is null)
        {
            _cache.Set(key, payload);
            return;
        }

        var entryOptions = ToEntryOptions(options);
        _cache.Set(key, payload, entryOptions);
    }

    public T? Get<T>(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return default;
        if (!_cache.TryGetValue(key, out var obj) || obj is null) return default;

        // مسیر سازگاری: اگر مقدار خام ذخیره شده باشد
        if (obj is T direct) return direct;

        if (obj is EncryptedCacheValue enc)
        {
            try
            {
                return Decrypt<T>(enc);
            }
            catch
            {
                return default;
            }
        }

        return default;
    }

    public T GetOrCreate<T>(string key, Func<T> factory, CacheOptions? options = null)
    {
        if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("کلید کش نامعتبر است.", nameof(key));
        if (factory is null) throw new ArgumentNullException(nameof(factory));

        var existing = Get<T>(key);
        if (existing is not null && !existing.Equals(default(T)))
        {
            return existing;
        }

        var value = factory();
        Set(key, value, options);
        return value;
    }

    public void Remove(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return;
        _cache.Remove(key);
    }

    private EncryptedCacheValue Encrypt<T>(T value)
    {
        var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(value, _json);

        var nonce = RandomNumberGenerator.GetBytes(12);
        var tag = new byte[16];
        var ciphertext = new byte[jsonBytes.Length];
        using var gcm = new AesGcm(_key, tagSizeInBytes: 16);
        gcm.Encrypt(nonce, jsonBytes, ciphertext, tag, associatedData: null);

        return new EncryptedCacheValue
        {
            Alg = Alg,
            NonceB64 = Convert.ToBase64String(nonce),
            TagB64 = Convert.ToBase64String(tag),
            CipherB64 = Convert.ToBase64String(ciphertext)
        };
    }

    private T? Decrypt<T>(EncryptedCacheValue enc)
    {
        if (!string.Equals(enc.Alg, Alg, StringComparison.Ordinal))
        {
            throw new NotSupportedException($"الگوریتم پشتیبانی‌نشده: {enc.Alg}");
        }

        var nonce = Convert.FromBase64String(enc.NonceB64);
        var tag = Convert.FromBase64String(enc.TagB64);
        var ciphertext = Convert.FromBase64String(enc.CipherB64);

        var plaintext = new byte[ciphertext.Length];
        using var gcm = new AesGcm(_key, tagSizeInBytes: 16);
        gcm.Decrypt(nonce, ciphertext, tag, plaintext, associatedData: null);

        return JsonSerializer.Deserialize<T>(plaintext, _json);
    }

    private static MemoryCacheEntryOptions ToEntryOptions(CacheOptions options)
    {
        var entry = new MemoryCacheEntryOptions();
        if (options.AbsoluteExpirationRelativeToNow is TimeSpan a)
        {
            entry.SetAbsoluteExpiration(a);
        }
        if (options.AbsoluteExpiration is DateTimeOffset abs)
        {
            entry.AbsoluteExpiration = abs;
        }
        if (options.SlidingExpiration is TimeSpan s)
        {
            entry.SetSlidingExpiration(s);
        }
        if (options.Priority is CacheItemPriority p)
        {
            entry.Priority = p;
        }
        if (options.Size is long size)
        {
            entry.Size = size;
        }
        return entry;
    }

    private byte[] LoadOrCreateKey(IConfiguration config)
    {
        // ترتیب خواندن: config → env (CACHE_ENCRYPTION_KEY)
        var str = config["Gateway:Cache:EncryptionKey"] ?? Environment.GetEnvironmentVariable("CACHE_ENCRYPTION_KEY");
        if (string.IsNullOrWhiteSpace(str))
        {
            var generated = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            Environment.SetEnvironmentVariable("CACHE_ENCRYPTION_KEY", generated); // فقط برای پروسس جاری
            str = generated;
        }

        // تلاش برای Base64 → Hex → متن خام (SHA256)
        if (TryFromBase64(str, out var key)) return Ensure32(key);
        if (TryFromHex(str, out key)) return Ensure32(key);

        // متن دلخواه: از SHA-256 آن استفاده کن
        var bytes = Encoding.UTF8.GetBytes(str);
        return SHA256.HashData(bytes);
    }

    private static bool TryFromBase64(string s, out byte[] bytes)
    {
        try
        {
            bytes = Convert.FromBase64String(s);
            return true;
        }
        catch
        {
            bytes = Array.Empty<byte>();
            return false;
        }
    }

    private static bool TryFromHex(string s, out byte[] bytes)
    {
        try
        {
            s = s.Trim();
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) s = s[2..];
            if (s.Length % 2 != 0) throw new FormatException("hex length");
            var len = s.Length / 2;
            bytes = new byte[len];
            for (int i = 0; i < len; i++)
            {
                bytes[i] = byte.Parse(s.AsSpan(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            }
            return true;
        }
        catch
        {
            bytes = Array.Empty<byte>();
            return false;
        }
    }

    private static byte[] Ensure32(byte[] input)
    {
        if (input.Length == 32) return input;
        // اگر کوتاه‌تر یا بلندتر بود، با SHA-256 به 32 تبدیل کن
        return SHA256.HashData(input);
    }
}
