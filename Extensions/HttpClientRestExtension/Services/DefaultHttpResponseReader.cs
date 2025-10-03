using HttpClientRestExtension.Interfaces;

namespace HttpClientRestExtension.Services;

public class DefaultHttpResponseReader : IHttpResponseReader
{
    public async Task<string> ReadAsStringAsync(HttpResponseMessage response, CancellationToken ct)
    {
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
    }
}

