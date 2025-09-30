using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace SepidarGateway.Api.Endpoints.Gateway;

public static class HealthEndpoints
{
    public static void MapHealthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var configuration = endpoints.ServiceProvider.GetRequiredService<IConfiguration>();
        var healthPath = configuration.GetValue<string>("Gateway:Health:Path") ?? "/health";

        endpoints.MapGet(healthPath, () => Results.Ok(new
        {
            status = "Healthy",
            timestamp = DateTimeOffset.UtcNow
        }))
        .WithTags("Gateway");
    }
}

