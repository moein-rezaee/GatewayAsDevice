using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Sepidar.Extension.Services;
using Sepidar.Extension.Interfaces;
using SepidarGateway.Api.Interfaces;

namespace SepidarGateway.Api.Services;

public class SepidarService : ISepidarService
{
    private readonly IConfiguration _configuration;
    private readonly SepidarGateway.Api.Interfaces.ICacheService _cache;

    public SepidarService(IConfiguration configuration, SepidarGateway.Api.Interfaces.ICacheService cache)
    {
        _configuration = configuration;
        _cache = cache;
    }

    public async Task<JsonObject> RegisterDeviceAndLoginAsync(string serial, CancellationToken cancellationToken = default)
    {
        var client = new SepidarClient();
        var opts = BuildOptions(serial);
        var register = await client.RegisterAsync(opts, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("پاسخ رجیستر دستگاه خالی است.");
        var login = await client.LoginAsync(opts, register, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("پاسخ لاگین کاربر خالی است.");

        // Persist session securely (no expiration)
        TryPersistSession(serial, opts, register, login);

        // Build root response and bubble up key fields
        var response = new JsonObject
        {
            ["Register"] = register.DeepClone(),
            ["Login"] = login.DeepClone(),
            ["GenerationVersion"] = opts.GenerationVersion,
        };

        // Try lift IntegrationID & Authorization to root for convenience
        try
        {
            var integrationId = register["IntegrationID"]?.GetValue<int?>() ?? login["IntegrationID"]?.GetValue<int?>();
            if (integrationId.HasValue)
            {
                response["IntegrationID"] = integrationId.Value;
            }
        }
        catch { }
        try
        {
            var authorization = login["Authorization"]?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(authorization))
            {
                response["Authorization"] = authorization;
            }
        }
        catch { }

        return response;
    }

    private SepidarClientOptions BuildOptions(string serial)
    {
        var baseUrl = _configuration.GetValue<string>("Sepidar:BaseUrl") ?? throw new InvalidOperationException("BaseUrl");
        var registerEndpoint = _configuration.GetValue<string>("Sepidar:RegisterDevice:Endpoint") ?? "/api/Devices/Register";
        var usersLoginEndpoint = _configuration.GetValue<string>("Sepidar:UsersLogin:Endpoint") ?? "/api/users/login";
        var integrationLen = _configuration.GetValue<int?>("Sepidar:RegisterDevice:IntegrationIdLength") ?? 4;
        var generationVersion = _configuration.GetValue<int?>("Sepidar:GenerationVersion") ?? 110;
        var username = Environment.GetEnvironmentVariable("LOGIN_USERNAME") ?? string.Empty;
        var password = Environment.GetEnvironmentVariable("LOGIN_PASSWORD") ?? string.Empty;
        return new SepidarClientOptions
        {
            BaseUrl = baseUrl,
            RegisterEndpoint = registerEndpoint,
            UsersLoginEndpoint = usersLoginEndpoint,
            IntegrationIdLength = integrationLen,
            GenerationVersion = generationVersion,
            Serial = serial?.Trim() ?? string.Empty,
            Username = username,
            Password = password
        };
    }

    private void TryPersistSession(string serial, SepidarClientOptions opts, JsonObject register, JsonObject login)
    {
        try
        {
            // Extract public key
            var pkProc = new Sepidar.Extension.Services.PublicKeyProcessor();
            string? mod = null, exp = null, xml = null;
            if (register["PublicKey"] is JsonObject pk)
            {
                mod = pk["Modulus"]?.GetValue<string>();
                exp = pk["Exponent"]?.GetValue<string>();
            }
            xml = register["PublicKeyXml"]?.GetValue<string>();

            // Extract token
            var auth = login["Authorization"]?.GetValue<string>();

            var integrationId = register["IntegrationID"]?.GetValue<int?>() ?? 0;
            if (integrationId == 0 && login["IntegrationID"]?.GetValue<int?>() is int iid) integrationId = iid;

            var session = new SepidarGateway.Api.Models.SepidarSession
            {
                Serial = serial,
                IntegrationID = integrationId,
                GenerationVersion = opts.GenerationVersion,
                PublicKeyXml = xml,
                RsaModulusB64 = mod,
                RsaExponentB64 = exp,
                Authorization = auth
            };
            _cache.Set("Sepidar:Session", session, new SepidarGateway.Api.Models.CacheOptions { Secure = true });
        }
        catch
        {
            // ignore cache failures
        }
    }
}
