namespace HttpClientRestExtension.Interfaces;

public interface IHttpRequestExecutor
{
    Task<string> GetAsync(string url, IDictionary<string, string>? headers = null, IDictionary<string, string?>? query = null, CancellationToken ct = default);
    Task<string> PostAsync(string url, object? body = null, IDictionary<string, string>? headers = null, IDictionary<string, string?>? query = null, CancellationToken ct = default);
    Task<string> PutAsync(string url, object? body = null, IDictionary<string, string>? headers = null, IDictionary<string, string?>? query = null, CancellationToken ct = default);
    Task<string> PatchAsync(string url, object? body = null, IDictionary<string, string>? headers = null, IDictionary<string, string?>? query = null, CancellationToken ct = default);
    Task<string> DeleteAsync(string url, IDictionary<string, string>? headers = null, IDictionary<string, string?>? query = null, CancellationToken ct = default);
}

