namespace HttpClientRestExtension.Interfaces;

public interface IHttpContentFactory
{
    HttpContent CreateJson(object? body);
}

