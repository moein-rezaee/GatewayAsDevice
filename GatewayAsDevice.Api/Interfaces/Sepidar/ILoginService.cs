using System.Text.Json.Nodes;

namespace SepidarGateway.Api.Interfaces.Sepidar;

public interface ILoginService
{
    Task<JsonNode?> LoginAsync(string serial, JsonObject registerNode, CancellationToken cancellationToken);
}

