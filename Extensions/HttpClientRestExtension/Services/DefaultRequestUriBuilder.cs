using System.Text;
using HttpClientRestExtension.Interfaces;

namespace HttpClientRestExtension.Services;

public class DefaultRequestUriBuilder : IRequestUriBuilder
{
    public string Build(string baseUrl, IDictionary<string, string?>? query)
    {
        if (query is null || query.Count == 0) return baseUrl;
        var hasQuery = baseUrl.Contains('?');
        var sb = new StringBuilder(baseUrl);
        sb.Append(hasQuery ? '&' : '?');
        var i = 0;
        foreach (var kv in query)
        {
            if (i++ > 0) sb.Append('&');
            sb.Append(Uri.EscapeDataString(kv.Key));
            sb.Append('=');
            sb.Append(Uri.EscapeDataString(kv.Value ?? string.Empty));
        }
        return sb.ToString();
    }
}

