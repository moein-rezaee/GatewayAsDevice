namespace SepidarGateway.Api.Endpoints.Gateway;

public static class HealthEndpoints
{
    public static void MapHealthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var configuration = endpoints.ServiceProvider.GetRequiredService<IConfiguration>();
        var healthPath = configuration.GetValue<string>("Gateway:Health:Path") ?? "/health";

        endpoints.MapGet(healthPath, GetHealth)
        .WithTags("Gateway");
    }

    // Handler: GET /health
    private static IResult GetHealth()
        => Results.Ok(new
        {
            status = "Healthy",
            timestamp = DateTimeOffset.UtcNow
        });
}
