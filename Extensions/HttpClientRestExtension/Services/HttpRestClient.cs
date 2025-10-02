using HttpClientRestExtension.Interfaces;

namespace HttpClientRestExtension.Services;

public class HttpRestClient : IHttpRestClient
{
    private readonly HttpClient _http;

    public HttpRestClient(HttpClient? httpClient = null)
    {
        _http = httpClient ?? new HttpClient();
    }

    public Task<string> GetAsync(string url, IDictionary<string, string>? headers = null, IDictionary<string, string?>? query = null, CancellationToken ct = default)
        => HttpClientRestExtension.GetAsync(_http, url, headers, query, ct);

    public Task<string> PostAsync(string url, object? body = null, IDictionary<string, string>? headers = null, IDictionary<string, string?>? query = null, CancellationToken ct = default)
        => HttpClientRestExtension.PostAsync(_http, url, body, headers, query, ct);

    public Task<string> PutAsync(string url, object? body = null, IDictionary<string, string>? headers = null, IDictionary<string, string?>? query = null, CancellationToken ct = default)
        => HttpClientRestExtension.PutAsync(_http, url, body, headers, query, ct);

    public Task<string> PatchAsync(string url, object? body = null, IDictionary<string, string>? headers = null, IDictionary<string, string?>? query = null, CancellationToken ct = default)
        => HttpClientRestExtension.PatchAsync(_http, url, body, headers, query, ct);

    public Task<string> DeleteAsync(string url, IDictionary<string, string>? headers = null, IDictionary<string, string?>? query = null, CancellationToken ct = default)
        => HttpClientRestExtension.DeleteAsync(_http, url, headers, query, ct);
}
