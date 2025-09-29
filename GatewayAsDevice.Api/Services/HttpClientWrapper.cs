using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using System.Text.Encodings.Web;
using SepidarGateway.Api.Interfaces;

namespace SepidarGateway.Api.Services;

public class HttpClientWrapper : IHttpClientWrapper
{
    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _serializerOptions;

    public HttpClientWrapper(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _serializerOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            // Preserve property names as defined in models (e.g., Cypher, IV, IntegrationID)
            PropertyNamingPolicy = null,
            // Avoid escaping '+' and '/' in Base64 strings
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
    }

    public Task<TResponse?> GetAsync<TResponse>(
        string url,
        IDictionary<string, string>? headers = null,
        IDictionary<string, string?>? queryParameters = null,
        CancellationToken cancellationToken = default) =>
        SendAsync<object?, TResponse>(HttpMethod.Get, url, default, headers, queryParameters, cancellationToken);

    public Task<TResponse?> PostAsync<TRequest, TResponse>(
        string url,
        TRequest? body = default,
        IDictionary<string, string>? headers = null,
        IDictionary<string, string?>? queryParameters = null,
        CancellationToken cancellationToken = default) =>
        SendAsync<TRequest?, TResponse>(HttpMethod.Post, url, body, headers, queryParameters, cancellationToken);

    public Task<TResponse?> PutAsync<TRequest, TResponse>(
        string url,
        TRequest? body = default,
        IDictionary<string, string>? headers = null,
        IDictionary<string, string?>? queryParameters = null,
        CancellationToken cancellationToken = default) =>
        SendAsync<TRequest?, TResponse>(HttpMethod.Put, url, body, headers, queryParameters, cancellationToken);

    public Task<TResponse?> DeleteAsync<TResponse>(
        string url,
        IDictionary<string, string>? headers = null,
        IDictionary<string, string?>? queryParameters = null,
        CancellationToken cancellationToken = default) =>
        SendAsync<object?, TResponse>(HttpMethod.Delete, url, default, headers, queryParameters, cancellationToken);

    public async Task<string?> PostRawAsync<TRequest>(
        string url,
        TRequest? body = default,
        IDictionary<string, string>? headers = null,
        IDictionary<string, string?>? queryParameters = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            throw new ArgumentException("URL cannot be null or whitespace.", nameof(url));
        }

        var targetUrl = AppendQueryString(url, queryParameters);
        using var request = new HttpRequestMessage(HttpMethod.Post, targetUrl);

        ApplyHeaders(request, headers);

        if (body is not null)
        {
            request.Content = CreateContent(body);
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

        var payload = response.Content is null
            ? string.Empty
            : await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var statusCode = (int)response.StatusCode;
            var reason = response.ReasonPhrase ?? string.Empty;
            var message = $"Upstream error {statusCode} {reason}. Body: {payload}";
            throw new HttpRequestException(message);
        }

        return string.IsNullOrWhiteSpace(payload) ? null : payload;
    }

    private async Task<TResponse?> SendAsync<TRequest, TResponse>(
        HttpMethod method,
        string url,
        TRequest? body,
        IDictionary<string, string>? headers,
        IDictionary<string, string?>? queryParameters,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            throw new ArgumentException("URL cannot be null or whitespace.", nameof(url));
        }

        var targetUrl = AppendQueryString(url, queryParameters);
        using var request = new HttpRequestMessage(method, targetUrl);

        ApplyHeaders(request, headers);

        if (body is not null && method != HttpMethod.Get)
        {
            request.Content = CreateContent(body);
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

        var payload = response.Content is null
            ? string.Empty
            : await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var statusCode = (int)response.StatusCode;
            var reason = response.ReasonPhrase ?? string.Empty;
            var message = $"Upstream error {statusCode} {reason}. Body: {payload}";
            throw new HttpRequestException(message);
        }

        if (string.IsNullOrWhiteSpace(payload))
        {
            return default;
        }

        return JsonSerializer.Deserialize<TResponse>(payload, _serializerOptions);
    }

    private static void ApplyHeaders(HttpRequestMessage request, IDictionary<string, string>? headers)
    {
        if (headers is null)
        {
            return;
        }

        foreach (var (key, value) in headers)
        {
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            if (!request.Headers.TryAddWithoutValidation(key, value))
            {
                request.Content ??= new StringContent(string.Empty);
                request.Content.Headers.TryAddWithoutValidation(key, value);
            }
        }
    }

    private HttpContent CreateContent<TRequest>(TRequest body)
    {
        if (body is HttpContent httpContent)
        {
            return httpContent;
        }

        if (body is string stringBody)
        {
            return new StringContent(stringBody, Encoding.UTF8, "application/json");
        }

        var serialized = JsonSerializer.Serialize(body, _serializerOptions);
        return new StringContent(serialized, Encoding.UTF8, "application/json");
    }

    private static string AppendQueryString(string url, IDictionary<string, string?>? queryParameters)
    {
        if (queryParameters is null || queryParameters.Count == 0)
        {
            return url;
        }

        var filtered = queryParameters
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Key))
            .ToDictionary(pair => pair.Key, pair => pair.Value);

        return filtered.Count == 0 ? url : QueryHelpers.AddQueryString(url, filtered);
    }
}
