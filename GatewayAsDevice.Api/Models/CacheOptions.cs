using Microsoft.Extensions.Caching.Memory;

namespace SepidarGateway.Api.Models;

// همه پارامترها اختیاری هستند
public sealed record CacheOptions
{
    // اگر true باشد، مقدار به‌صورت امن رمزنگاری و ذخیره می‌شود
    public bool? Secure { get; init; }

    // مطلق نسبت به اکنون (مثلاً 5 دقیقه)
    public TimeSpan? AbsoluteExpirationRelativeToNow { get; init; }

    // انقضای لغزنده؛ با هر دسترسی ریست می‌شود
    public TimeSpan? SlidingExpiration { get; init; }

    // انقضای مطلق در آینده
    public DateTimeOffset? AbsoluteExpiration { get; init; }

    // اولویت حذف
    public CacheItemPriority? Priority { get; init; }

    // اندازه مورد استفاده برای محدودیت اندازه کش (اگر تنظیم شده باشد)
    public long? Size { get; init; }
}

