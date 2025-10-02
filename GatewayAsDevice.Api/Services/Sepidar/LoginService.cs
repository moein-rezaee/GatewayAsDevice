using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using SepidarGateway.Api.Interfaces;
using SepidarGateway.Api.Interfaces.Sepidar;

namespace SepidarGateway.Api.Services.Sepidar;

public class LoginService : ILoginService
{
    private readonly IHttpClientWrapper _http;
    private readonly IConfigService _config;
    private readonly ICryptoService _crypto;
    private readonly ICredentialsProvider _credentials;

    public LoginService(IHttpClientWrapper http, IConfigService config, ICryptoService crypto, ICredentialsProvider credentials)
    {
        _http = http;
        _config = config;
        _crypto = crypto;
        _credentials = credentials;
    }

    public async Task<JsonNode?> LoginAsync(string serial, JsonObject registerNode, CancellationToken cancellationToken)
    {
        if (registerNode is null)
            throw new InvalidOperationException("پاسخ رجیستر دستگاه نامعتبر است.");

        var baseUrl = _config.GetBaseUrlOrThrow();
        var endpoint = _config.GetUsersLoginEndpoint();
        var url = _config.CombineUrl(baseUrl, endpoint);

        var integrationId = _config.ExtractIntegrationIdOrThrow(serial, _config.GetIntegrationIdLengthOrDefault());

        var userName = _credentials.GetUsernameOrThrow();
        var password = _credentials.GetPasswordOrThrow();

        if (!_crypto.TryGetRsaParameters(registerNode, out var rsaParams))
            throw new InvalidOperationException("کلید عمومی برای رمزنگاری در پاسخ رجیستر یافت نشد.");

        var arbitraryGuid = Guid.NewGuid();
        var arbitraryCode = arbitraryGuid.ToString();
        var guidBytes = _crypto.GuidToRfc4122Bytes(arbitraryGuid);

        string encArbitraryCode;
        using (var rsa = RSA.Create())
        {
            rsa.ImportParameters(rsaParams);
            var enc = rsa.Encrypt(guidBytes, RSAEncryptionPadding.Pkcs1);
            encArbitraryCode = Convert.ToBase64String(enc);
        }

        var passwordHashHex = _crypto.ToHex(MD5.HashData(Encoding.UTF8.GetBytes(password)));

        var headers = new Dictionary<string, string>
        {
            ["Content-Type"] = "application/json",
            ["GenerationVersion"] = "110",
            ["IntegrationID"] = integrationId.ToString(),
            ["ArbitraryCode"] = arbitraryCode,
            ["EncArbitraryCode"] = encArbitraryCode
        };

        var body = new Dictionary<string, string>
        {
            ["UserName"] = userName,
            ["PasswordHash"] = passwordHashHex
        };

        var payload = await _http.PostRawAsync(url, body, headers: headers, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(payload)) return null;
        return JsonNode.Parse(payload);
    }
}
