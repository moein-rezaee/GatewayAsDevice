using System.Text.Json;
using HttpClientRestExtension.Interfaces;

namespace HttpClientRestExtension.Services;

public class SystemTextJsonSerializerAdapter : IJsonSerializerAdapter
{
    private readonly JsonSerializerOptions _options;
    public SystemTextJsonSerializerAdapter(JsonSerializerOptions? options = null)
    {
        _options = options ?? new JsonSerializerOptions(JsonSerializerDefaults.Web);
    }

    public string Serialize(object? value) => JsonSerializer.Serialize(value, _options);
}

