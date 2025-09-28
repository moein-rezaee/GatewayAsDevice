using System.Text.Json;

namespace SepidarGateway.Api.Interfaces;

public interface ISepidarService
{
    Task<JsonDocument?> RegisterDeviceAsync(string serial, CancellationToken cancellationToken = default);
}
