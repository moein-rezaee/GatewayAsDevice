using System.Text.Json.Nodes;

namespace SepidarGateway.Api.Interfaces;

public interface ISepidarService
{
    Task<JsonObject> RegisterDeviceAndLoginAsync(string serial, CancellationToken cancellationToken = default);
}

