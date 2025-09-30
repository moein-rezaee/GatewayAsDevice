using System.Text.Json.Nodes;

namespace SepidarGateway.Api.Interfaces;

public interface ISepidarService
{
    Task<JsonNode?> RegisterDeviceAsync(string serial, CancellationToken cancellationToken = default);
    Task<JsonNode?> UserLoginAsync(CancellationToken cancellationToken = default);
}
