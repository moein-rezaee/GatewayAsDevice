namespace SepidarGateway.Api.Options;

public class SepidarOptions
{
    public string BaseUrl { get; set; } = string.Empty;
    public SepidarRegisterDeviceOptions RegisterDevice { get; set; } = new();
}

public enum KeyDerivationMode
{
    Auto,
    Left16Chars,
    Left16Digits,
    RepeatDigitsTo16,
    RepeatCharsTo16
}

public class SepidarRegisterDeviceOptions
{
    public string Endpoint { get; set; } = "/api/Devices/Register";
    public int IntegrationIdLength { get; set; } = 4;
    public KeyDerivationMode KeyMode { get; set; } = KeyDerivationMode.Left16Digits;
}
