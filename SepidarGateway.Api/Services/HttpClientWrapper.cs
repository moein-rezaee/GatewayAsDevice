using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using SepidarGateway.Api.Interfaces;

namespace SepidarGateway.Api.Services;

public class HttpClientWrapper : IHttpClientWrapper
{
    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _serializerOptions;

    public HttpClientWrapper(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _serializerOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
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
        response.EnsureSuccessStatusCode();

        if (response.Content is null)
        {
            return default;
        }

        var payload = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
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
