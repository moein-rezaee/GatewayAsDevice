using System.Text.Json.Nodes;
using Sepidar.Extension.Services;

namespace Sepidar.Extension.Interfaces;

public interface ISepidarClient
{
    Task<JsonObject?> RegisterAsync(SepidarClientOptions options, CancellationToken ct = default);
    Task<JsonObject?> LoginAsync(SepidarClientOptions options, JsonObject registerNode, CancellationToken ct = default);
}
