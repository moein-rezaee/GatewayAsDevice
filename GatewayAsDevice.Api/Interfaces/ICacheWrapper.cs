using SepidarGateway.Api.Models;

namespace SepidarGateway.Api.Interfaces;

public interface ICacheWrapper
{
    // ذخیره مدل جنریک با کلید دلخواه و تنظیمات اختیاری
    void Set<T>(string key, T value, CacheOptions? options = null);

    // دریافت مدل جنریک از کش؛ در صورت نبود، null برمی‌گرداند
    T? Get<T>(string key);

    // اگر نبود، بساز و در کش ذخیره کن
    T GetOrCreate<T>(string key, Func<T> factory, CacheOptions? options = null);

    // حذف دستی کلید از کش
    void Remove(string key);
}

