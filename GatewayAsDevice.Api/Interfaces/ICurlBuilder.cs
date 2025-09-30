using System.Net.Http;

namespace SepidarGateway.Api.Interfaces;

public interface ICurlBuilder
{
    // Toggle next build to PowerShell format; default is bash/sh
    ICurlBuilder PowerShell();

    // Build a single curl command string
    string Build(string url, IDictionary<string, string>? headers = null, string? bodyJson = null, HttpMethod? method = null);
}

