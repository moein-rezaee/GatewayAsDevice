namespace SepidarGateway.Api.Models;

public sealed class SepidarSession
{
    public string Serial { get; init; } = string.Empty;
    public int IntegrationID { get; init; }
    public int GenerationVersion { get; init; }

    public string? PublicKeyXml { get; init; }
    public string? RsaModulusB64 { get; init; }
    public string? RsaExponentB64 { get; init; }

    public string? Authorization { get; init; }
}

