namespace SepidarGateway.Api.Models;

public sealed record EncryptedCacheValue
{
    public string Alg { get; init; } = "AESGCM256"; // الگوریتم پیش‌فرض
    public string NonceB64 { get; init; } = string.Empty; // 12 بایت Base64
    public string TagB64 { get; init; } = string.Empty;   // 16 بایت Base64
    public string CipherB64 { get; init; } = string.Empty; // متن رمز Base64
}

