namespace SepidarGateway.Api.Interfaces;

public interface IHttpClientWrapper
{
    Task<TResponse?> GetAsync<TResponse>(
        string url,
        IDictionary<string, string>? headers = null,
        IDictionary<string, string?>? queryParameters = null,
        CancellationToken cancellationToken = default);

    Task<TResponse?> PostAsync<TRequest, TResponse>(
        string url,
        TRequest? body = default,
        IDictionary<string, string>? headers = null,
        IDictionary<string, string?>? queryParameters = null,
        CancellationToken cancellationToken = default);

    Task<TResponse?> PutAsync<TRequest, TResponse>(
        string url,
        TRequest? body = default,
        IDictionary<string, string>? headers = null,
        IDictionary<string, string?>? queryParameters = null,
        CancellationToken cancellationToken = default);

    Task<TResponse?> DeleteAsync<TResponse>(
        string url,
        IDictionary<string, string>? headers = null,
        IDictionary<string, string?>? queryParameters = null,
        CancellationToken cancellationToken = default);
}
