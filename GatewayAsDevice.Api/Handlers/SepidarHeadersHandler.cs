using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Sepidar.Extension.Services;
using SepidarGateway.Api.Interfaces;
using SepidarGateway.Api.Models;

namespace SepidarGateway.Api.Handlers;

public class SepidarHeadersHandler : DelegatingHandler
{
    private readonly ICacheService _cache;
    private readonly PublicKeyProcessor _pk;
    private readonly ILogger<SepidarHeadersHandler> _logger;
    private readonly ICurlBuilder _curlBuilder;
    private static readonly HashSet<string> _noSessionPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "/v1/api/General/GenerationVersion"
    };

    public SepidarHeadersHandler(ICacheService cache, ILogger<SepidarHeadersHandler> logger, ICurlBuilder curlBuilder)
    {
        _cache = cache;
        _pk = new PublicKeyProcessor();
        _logger = logger;
        _curlBuilder = curlBuilder;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // Paths that don't require session/headers should pass through untouched
        var rawPath = request.RequestUri?.AbsolutePath ?? string.Empty;
        var normalizedPath = NormalizePath(rawPath);

        var requiresSession = !_noSessionPaths.Contains(normalizedPath);

        SepidarSession? session = null;
        if (requiresSession && (!_cache.TryGet<SepidarSession>("Sepidar:Session", out session) || session is null))
        {
            _logger.LogWarning("Sepidar session not found for downstream request {Method} {Path}.", request.Method, normalizedPath);
            var content = new StringContent(JsonSerializer.Serialize(new { message = "ابتدا دستگاه را رجیستر و لاگین کنید." }));
            var resp = new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = content };
            return resp;
        }

        if (requiresSession)
        {
            // Generate new arbitrary code per request and encrypt with RSA
            var arbitrary = Guid.NewGuid();
            if (!TryBuildRsaParams(session!, out var rsaParams))
            {
                _logger.LogWarning("Failed to build RSA parameters from cached session for downstream request {Method} {Path}.", request.Method, normalizedPath);
                var content = new StringContent(JsonSerializer.Serialize(new { message = "کلید عمومی نامعتبر است. رجیستر را بررسی کنید." }));
                return new HttpResponseMessage(HttpStatusCode.BadGateway) { Content = content };
            }

            var encArbitrary = _pk.EncryptGuidArbitraryCode(arbitrary, rsaParams);

            // Inject headers downstream (only when required)
            request.Headers.Remove("GenerationVersion");
            request.Headers.Add("GenerationVersion", session!.GenerationVersion.ToString());
            request.Headers.Remove("IntegrationID");
            request.Headers.Add("IntegrationID", session!.IntegrationID.ToString());
            request.Headers.Remove("ArbitraryCode");
            request.Headers.Add("ArbitraryCode", arbitrary.ToString());
            request.Headers.Remove("EncArbitraryCode");
            request.Headers.Add("EncArbitraryCode", encArbitrary);
            if (!string.IsNullOrWhiteSpace(session!.Authorization))
            {
                request.Headers.Remove("Authorization");
                request.Headers.Add("Authorization", session!.Authorization!);
            }
            _logger.LogInformation("Injected Sepidar headers for downstream request {Method} {Path}.", request.Method, normalizedPath);
        }

        await LogDownstreamCurlAsync(request, cancellationToken).ConfigureAwait(false);

        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private static bool TryBuildRsaParams(SepidarGateway.Api.Models.SepidarSession session, out RSAParameters rsa)
    {
        try
        {
            rsa = default;
            if (!string.IsNullOrWhiteSpace(session.RsaModulusB64) && !string.IsNullOrWhiteSpace(session.RsaExponentB64))
            {
                rsa = new RSAParameters
                {
                    Modulus = Convert.FromBase64String(session.RsaModulusB64!),
                    Exponent = Convert.FromBase64String(session.RsaExponentB64!)
                };
                return true;
            }
            if (!string.IsNullOrWhiteSpace(session.PublicKeyXml))
            {
                var pk = new PublicKeyProcessor();
                var (mod, exp) = pk.TryParseRsaXml(session.PublicKeyXml!);
                if (!string.IsNullOrWhiteSpace(mod) && !string.IsNullOrWhiteSpace(exp))
                {
                    rsa = new RSAParameters
                    {
                        Modulus = Convert.FromBase64String(mod),
                        Exponent = Convert.FromBase64String(exp)
                    };
                    return true;
                }
            }
        }
        catch { }
        rsa = default;
        return false;
    }

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        return path.TrimEnd('/');
    }

    private async Task LogDownstreamCurlAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (!_logger.IsEnabled(LogLevel.Information)) return;

        try
        {
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var header in request.Headers)
            {
                headers[header.Key] = string.Join(", ", header.Value);
            }
            if (request.Content?.Headers is not null)
            {
                foreach (var header in request.Content.Headers)
                {
                    headers[header.Key] = string.Join(", ", header.Value);
                }
            }

            if (headers.TryGetValue("Authorization", out var auth) && !string.IsNullOrWhiteSpace(auth))
            {
                headers["Authorization"] = MaskAuthorization(auth);
            }

            string? bodyJson = null;
            if (request.Content is StringContent stringContent)
            {
                bodyJson = await stringContent.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            }

            var uri = request.RequestUri?.ToString() ?? string.Empty;
            var curl = _curlBuilder.Build(uri, headers, bodyJson, request.Method);
            _logger.LogInformation("Downstream curl ready: {Curl}", curl);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to log downstream curl request.");
        }
    }

    private static string MaskAuthorization(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return value;
        const string bearerPrefix = "Bearer ";
        if (value.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var token = value[bearerPrefix.Length..];
            if (token.Length <= 4)
            {
                return bearerPrefix + "***";
            }
            return bearerPrefix + token[..4] + "...";
        }

        if (value.Length <= 6) return "***";
        return value[..3] + "...";
    }
}
