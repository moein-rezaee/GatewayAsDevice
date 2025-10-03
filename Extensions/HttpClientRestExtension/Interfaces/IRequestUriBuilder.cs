namespace HttpClientRestExtension.Interfaces;

public interface IRequestUriBuilder
{
    string Build(string baseUrl, IDictionary<string, string?>? query);
}

