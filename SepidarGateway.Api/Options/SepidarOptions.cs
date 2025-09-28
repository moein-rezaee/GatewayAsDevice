namespace SepidarGateway.Api.Options;

public class SepidarOptions
{
    public string BaseUrl { get; set; } = string.Empty;
    public SepidarRegisterDeviceOptions RegisterDevice { get; set; } = new();
}

public class SepidarRegisterDeviceOptions
{
    public string Endpoint { get; set; } = "/api/Devices/Register";
    public int IntegrationIdLength { get; set; } = 4;
}
