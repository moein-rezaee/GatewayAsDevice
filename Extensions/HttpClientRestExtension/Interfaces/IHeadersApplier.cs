using System.Net.Http.Headers;

namespace HttpClientRestExtension.Interfaces;

public interface IHeadersApplier
{
    void Apply(HttpRequestMessage message, IDictionary<string, string>? headers);
}

