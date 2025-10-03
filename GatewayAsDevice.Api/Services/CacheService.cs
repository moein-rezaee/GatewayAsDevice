using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Caching.Memory;
using SepidarGateway.Api.Interfaces;
using SepidarGateway.Api.Models;

namespace SepidarGateway.Api.Services;

public sealed class CacheService : ICacheService
{
    private readonly IMemoryCache _cache;
    private readonly byte[] _key;

    public CacheService(IMemoryCache cache)
    {
        _cache = cache;
        var b64 = Environment.GetEnvironmentVariable("CACHE_ENC_KEY");
        if (string.IsNullOrWhiteSpace(b64))
        {
            var seed = Encoding.UTF8.GetBytes($"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid()}");
            using var sha = SHA256.Create();
            _key = sha.ComputeHash(seed);
        }
        else
        {
            _key = Convert.FromBase64String(b64);
            if (_key.Length != 32) throw new InvalidOperationException("CACHE_ENC_KEY باید کلید 32 بایتی Base64 باشد");
        }
    }

    public bool TryGet<T>(string key, out T? value)
    {
        value = default;
        if (!_cache.TryGetValue(key, out object? stored) || stored is null) return false;

        if (stored is EncryptedCacheValue e)
        {
            var json = Decrypt(e);
            value = System.Text.Json.JsonSerializer.Deserialize<T>(json);
            return value is not null;
        }
        else if (stored is byte[] plain)
        {
            var json = Encoding.UTF8.GetString(plain);
            value = System.Text.Json.JsonSerializer.Deserialize<T>(json);
            return value is not null;
        }
        else if (stored is T tv)
        {
            value = tv;
            return true;
        }
        return false;
    }

    public void Set<T>(string key, T value, CacheOptions? options = null)
    {
        var opts = new MemoryCacheEntryOptions
        {
            Priority = options?.Priority ?? CacheItemPriority.NeverRemove,
            AbsoluteExpirationRelativeToNow = options?.AbsoluteExpirationRelativeToNow,
            SlidingExpiration = options?.SlidingExpiration,
            AbsoluteExpiration = options?.AbsoluteExpiration,
            Size = options?.Size
        };

        if (options?.Secure == true)
        {
            var json = System.Text.Json.JsonSerializer.Serialize(value);
            var enc = Encrypt(json);
            _cache.Set(key, enc, opts);
        }
        else
        {
            // بدون رمزنگاری
            var json = System.Text.Json.JsonSerializer.Serialize(value);
            _cache.Set(key, Encoding.UTF8.GetBytes(json), opts);
        }
    }

    private EncryptedCacheValue Encrypt(string plain)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var pt = Encoding.UTF8.GetBytes(plain);
        var ct = new byte[pt.Length];
        var tag = new byte[16];
#pragma warning disable SYSLIB0053
        using var gcm = new AesGcm(_key, tagSizeInBytes: 16);
#pragma warning restore SYSLIB0053
        gcm.Encrypt(nonce, pt, ct, tag);
        return new EncryptedCacheValue
        {
            Alg = "AESGCM256",
            NonceB64 = Convert.ToBase64String(nonce),
            TagB64 = Convert.ToBase64String(tag),
            CipherB64 = Convert.ToBase64String(ct)
        };
    }

    private string Decrypt(EncryptedCacheValue enc)
    {
        var nonce = Convert.FromBase64String(enc.NonceB64);
        var tag = Convert.FromBase64String(enc.TagB64);
        var ct = Convert.FromBase64String(enc.CipherB64);
        var pt = new byte[ct.Length];
#pragma warning disable SYSLIB0053
        using var gcm = new AesGcm(_key, tagSizeInBytes: 16);
#pragma warning restore SYSLIB0053
        gcm.Decrypt(nonce, ct, tag, pt);
        return Encoding.UTF8.GetString(pt);
    }
}
