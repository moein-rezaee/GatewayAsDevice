using HttpClientRestExtension.Interfaces;

namespace HttpClientRestExtension.Services;

public class HttpRequestExecutor : IHttpRequestExecutor
{
    private readonly HttpClient _http;
    private readonly IRequestUriBuilder _uriBuilder;
    private readonly IHeadersApplier _headersApplier;
    private readonly IHttpContentFactory _contentFactory;
    private readonly IHttpResponseReader _responseReader;

    public HttpRequestExecutor(
        HttpClient http,
        IRequestUriBuilder? uriBuilder = null,
        IHeadersApplier? headersApplier = null,
        IHttpContentFactory? contentFactory = null,
        IHttpResponseReader? responseReader = null)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _uriBuilder = uriBuilder ?? new DefaultRequestUriBuilder();
        _headersApplier = headersApplier ?? new DefaultHeadersApplier();
        _contentFactory = contentFactory ?? new JsonContentFactory(new SystemTextJsonSerializerAdapter());
        _responseReader = responseReader ?? new DefaultHttpResponseReader();
    }

    public Task<string> GetAsync(string url, IDictionary<string, string>? headers = null, IDictionary<string, string?>? query = null, CancellationToken ct = default)
        => SendAsync(new HttpRequestMessage(HttpMethod.Get, _uriBuilder.Build(url, query)), headers, ct);

    public Task<string> PostAsync(string url, object? body = null, IDictionary<string, string>? headers = null, IDictionary<string, string?>? query = null, CancellationToken ct = default)
        => SendWithBodyAsync(HttpMethod.Post, url, body, headers, query, ct);

    public Task<string> PutAsync(string url, object? body = null, IDictionary<string, string>? headers = null, IDictionary<string, string?>? query = null, CancellationToken ct = default)
        => SendWithBodyAsync(HttpMethod.Put, url, body, headers, query, ct);

    public Task<string> PatchAsync(string url, object? body = null, IDictionary<string, string>? headers = null, IDictionary<string, string?>? query = null, CancellationToken ct = default)
        => SendWithBodyAsync(new HttpMethod("PATCH"), url, body, headers, query, ct);

    public Task<string> DeleteAsync(string url, IDictionary<string, string>? headers = null, IDictionary<string, string?>? query = null, CancellationToken ct = default)
        => SendAsync(new HttpRequestMessage(HttpMethod.Delete, _uriBuilder.Build(url, query)), headers, ct);

    private async Task<string> SendWithBodyAsync(HttpMethod method, string url, object? body, IDictionary<string, string>? headers, IDictionary<string, string?>? query, CancellationToken ct)
    {
        var msg = new HttpRequestMessage(method, _uriBuilder.Build(url, query))
        {
            Content = _contentFactory.CreateJson(body)
        };
        return await SendAsync(msg, headers, ct).ConfigureAwait(false);
    }

    private async Task<string> SendAsync(HttpRequestMessage msg, IDictionary<string, string>? headers, CancellationToken ct)
    {
        using (msg)
        {
            _headersApplier.Apply(msg, headers);
            using var res = await _http.SendAsync(msg, ct).ConfigureAwait(false);
            return await _responseReader.ReadAsStringAsync(res, ct).ConfigureAwait(false);
        }
    }
}

