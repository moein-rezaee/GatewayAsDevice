namespace HttpClientRestExtension.Interfaces;

public interface IHttpResponseReader
{
    Task<string> ReadAsStringAsync(HttpResponseMessage response, CancellationToken ct);
}

