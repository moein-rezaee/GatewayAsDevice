using SepidarGateway.Api.Models;

namespace SepidarGateway.Api.Interfaces.Sepidar;

public interface IConfigService
{
    SepidarOptions GetOptionsOrThrow();
    string GetBaseUrlForRegisterOrThrow();
    string GetBaseUrlOrThrow();
    string GetRegisterEndpointOrThrow();
    int GetIntegrationIdLengthOrDefault();
    int ExtractIntegrationIdOrThrow(string serial, int digitCount);
    string CombineUrl(string baseUrl, string endpoint);
    string GetUsersLoginEndpoint();
}

