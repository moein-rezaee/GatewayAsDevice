using System.Text.Json.Nodes;

namespace SepidarGateway.Api.Interfaces;

public interface ISepidarService
{
    Task<JsonNode?> RegisterDeviceAsync(string serial, CancellationToken cancellationToken = default);
}
