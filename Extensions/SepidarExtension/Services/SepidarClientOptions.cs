namespace Sepidar.Extension.Services;

public sealed class SepidarClientOptions
{
    public string BaseUrl { get; set; } = string.Empty;
    public string RegisterEndpoint { get; set; } = "/api/Devices/Register";
    public string UsersLoginEndpoint { get; set; } = "/api/users/login";
    public int IntegrationIdLength { get; set; } = 4;
    public int GenerationVersion { get; set; } = 110;

    public string Serial { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}
