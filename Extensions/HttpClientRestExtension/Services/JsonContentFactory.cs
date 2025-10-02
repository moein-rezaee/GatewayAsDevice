using System.Text;
using HttpClientRestExtension.Interfaces;

namespace HttpClientRestExtension.Services;

public class JsonContentFactory : IHttpContentFactory
{
    private readonly IJsonSerializerAdapter _serializer;
    public JsonContentFactory(IJsonSerializerAdapter serializer)
    {
        _serializer = serializer;
    }
    public HttpContent CreateJson(object? body)
    {
        var json = _serializer.Serialize(body);
        return new StringContent(json, Encoding.UTF8, "application/json");
    }
}

