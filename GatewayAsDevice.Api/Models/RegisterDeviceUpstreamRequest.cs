namespace SepidarGateway.Api.Models;

public sealed record RegisterDeviceUpstreamRequest
{
    public string Cypher { get; init; } = string.Empty;
    public string IV { get; init; } = string.Empty;
    public int IntegrationID { get; init; }
}

