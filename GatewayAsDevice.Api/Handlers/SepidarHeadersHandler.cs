using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using Sepidar.Extension.Services;
using SepidarGateway.Api.Interfaces;
using SepidarGateway.Api.Models;

namespace SepidarGateway.Api.Handlers;

public class SepidarHeadersHandler : DelegatingHandler
{
    private readonly ICacheService _cache;
    private readonly PublicKeyProcessor _pk;
    private static readonly HashSet<string> _noSessionPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "/v1/api/General/GenerationVersion"
    };

    public SepidarHeadersHandler(ICacheService cache)
    {
        _cache = cache;
        _pk = new PublicKeyProcessor();
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
        }

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
}
