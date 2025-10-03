using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace HttpClientRestExtension.Services;

public static class HttpClientRestExtension
{
    private static readonly JsonSerializerOptions DefaultJsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task<string> GetAsync(this HttpClient http, string url, IDictionary<string, string>? headers = null, IDictionary<string, string?>? query = null, CancellationToken ct = default)
    {
        var exec = new HttpRequestExecutor(http);
        return await exec.GetAsync(url, headers, query, ct).ConfigureAwait(false);
    }

    public static async Task<string> PostAsync(this HttpClient http, string url, object? body = null, IDictionary<string, string>? headers = null, IDictionary<string, string?>? query = null, CancellationToken ct = default)
    {
        var exec = new HttpRequestExecutor(http);
        return await exec.PostAsync(url, body, headers, query, ct).ConfigureAwait(false);
    }

    public static async Task<string> PutAsync(this HttpClient http, string url, object? body = null, IDictionary<string, string>? headers = null, IDictionary<string, string?>? query = null, CancellationToken ct = default)
    {
        var exec = new HttpRequestExecutor(http);
        return await exec.PutAsync(url, body, headers, query, ct).ConfigureAwait(false);
    }

    public static async Task<string> PatchAsync(this HttpClient http, string url, object? body = null, IDictionary<string, string>? headers = null, IDictionary<string, string?>? query = null, CancellationToken ct = default)
    {
        var exec = new HttpRequestExecutor(http);
        return await exec.PatchAsync(url, body, headers, query, ct).ConfigureAwait(false);
    }

    public static async Task<string> DeleteAsync(this HttpClient http, string url, IDictionary<string, string>? headers = null, IDictionary<string, string?>? query = null, CancellationToken ct = default)
    {
        var exec = new HttpRequestExecutor(http);
        return await exec.DeleteAsync(url, headers, query, ct).ConfigureAwait(false);
    }
}
