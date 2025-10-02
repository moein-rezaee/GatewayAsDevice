using System.Net.Http.Headers;
using HttpClientRestExtension.Interfaces;

namespace HttpClientRestExtension.Services;

public class DefaultHeadersApplier : IHeadersApplier
{
    public void Apply(HttpRequestMessage message, IDictionary<string, string>? headers)
    {
        if (headers is null) return;
        foreach (var kv in headers)
        {
            if (string.Equals(kv.Key, "Content-Type", StringComparison.OrdinalIgnoreCase))
            {
                if (message.Content != null)
                {
                    message.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(kv.Value);
                }
            }
            else
            {
                message.Headers.TryAddWithoutValidation(kv.Key, kv.Value);
            }
        }
    }
}

