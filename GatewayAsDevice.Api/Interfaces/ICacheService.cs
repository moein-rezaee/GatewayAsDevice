using SepidarGateway.Api.Models;

namespace SepidarGateway.Api.Interfaces;

public interface ICacheService
{
    bool TryGet<T>(string key, out T? value);
    void Set<T>(string key, T value, CacheOptions? options = null);
}
