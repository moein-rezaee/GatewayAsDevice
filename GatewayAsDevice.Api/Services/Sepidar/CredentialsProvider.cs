using SepidarGateway.Api.Interfaces.Sepidar;

namespace SepidarGateway.Api.Services.Sepidar;

public class CredentialsProvider : ICredentialsProvider
{
    public string GetUsernameOrThrow()
        => Environment.GetEnvironmentVariable("LOGIN_USERNAME")
           ?? throw new InvalidOperationException("LOGIN_USERNAME تنظیم نشده است.");

    public string GetPasswordOrThrow()
        => Environment.GetEnvironmentVariable("LOGIN_PASSWORD")
           ?? throw new InvalidOperationException("LOGIN_PASSWORD تنظیم نشده است.");
}
