using System.Text.Json.Nodes;

namespace SepidarGateway.Api.Models;

public sealed class RegisterDeviceCacheEntry
{
    public string Serial { get; init; } = string.Empty;
    public JsonNode? Response { get; init; }
    public DateTimeOffset CachedAt { get; init; } = DateTimeOffset.UtcNow;
}

