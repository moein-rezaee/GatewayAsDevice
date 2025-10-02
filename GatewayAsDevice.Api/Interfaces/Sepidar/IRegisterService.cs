using System.Text.Json.Nodes;

namespace SepidarGateway.Api.Interfaces.Sepidar;

public interface IRegisterService
{
    Task<JsonObject?> RegisterAsync(string serial, CancellationToken cancellationToken);
}

