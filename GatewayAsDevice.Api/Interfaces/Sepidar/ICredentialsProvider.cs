namespace SepidarGateway.Api.Interfaces.Sepidar;

public interface ICredentialsProvider
{
    string GetUsernameOrThrow();
    string GetPasswordOrThrow();
}

