using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Sepidar.Extension.Services;
using Sepidar.Extension.Interfaces;
using SepidarGateway.Api.Interfaces;

namespace SepidarGateway.Api.Services;

public class SepidarService : ISepidarService
{
    private readonly IConfiguration _configuration;

    public SepidarService(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public async Task<JsonObject> RegisterDeviceAndLoginAsync(string serial, CancellationToken cancellationToken = default)
    {
        var client = new SepidarClient();
        var opts = BuildOptions(serial);
        var register = await client.RegisterAsync(opts, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("پاسخ رجیستر دستگاه خالی است.");
        var login = await client.LoginAsync(opts, register, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("پاسخ لاگین کاربر خالی است.");

        return new JsonObject
        {
            ["Register"] = register.DeepClone(),
            ["Login"] = login.DeepClone(),
            ["GenerationVersion"] = opts.GenerationVersion,
        };
    }

    private SepidarClientOptions BuildOptions(string serial)
    {
        var baseUrl = _configuration.GetValue<string>("Sepidar:BaseUrl") ?? throw new InvalidOperationException("BaseUrl");
        var registerEndpoint = _configuration.GetValue<string>("Sepidar:RegisterDevice:Endpoint") ?? "/api/Devices/Register";
        var usersLoginEndpoint = _configuration.GetValue<string>("Sepidar:UsersLogin:Endpoint") ?? "/api/users/login";
        var integrationLen = _configuration.GetValue<int?>("Sepidar:RegisterDevice:IntegrationIdLength") ?? 4;
        var username = Environment.GetEnvironmentVariable("LOGIN_USERNAME") ?? string.Empty;
        var password = Environment.GetEnvironmentVariable("LOGIN_PASSWORD") ?? string.Empty;
        return new SepidarClientOptions
        {
            BaseUrl = baseUrl,
            RegisterEndpoint = registerEndpoint,
            UsersLoginEndpoint = usersLoginEndpoint,
            IntegrationIdLength = integrationLen,
            Serial = serial?.Trim() ?? string.Empty,
            Username = username,
            Password = password
        };
    }
}
