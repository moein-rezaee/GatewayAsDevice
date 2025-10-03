using System.Net.Http;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using DotNetEnv;
using Microsoft.OpenApi.Models;
using SepidarGateway.Api.Interfaces;
using SepidarGateway.Api.Models;
using SepidarGateway.Api.Services;
using SepidarGateway.Api.Endpoints.Gateway;
using SepidarGateway.Api.Endpoints.Device;
using Ocelot.DependencyInjection;
using Ocelot.Middleware;
using SepidarGateway.Api.Interfaces;
using SepidarGateway.Api.Services;
using SepidarGateway.Api.Handlers;

var builder = WebApplication.CreateBuilder(args);

var envCandidates = new[]
{
    Path.Combine(builder.Environment.ContentRootPath, ".env"),
    Path.Combine(builder.Environment.ContentRootPath, "..", ".env")
};

foreach (var envFile in envCandidates)
{
    if (File.Exists(envFile))
    {
        Env.Load(envFile, new LoadOptions(setEnvVars: true, clobberExistingVars: false, onlyExactPath: true));
    }
}

builder.Configuration
    .SetBasePath(builder.Environment.ContentRootPath)
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true)
    // Load Ocelot routes
    .AddJsonFile("ocelot.json", optional: false, reloadOnChange: true)
    .AddEnvironmentVariables();

var swaggerSection = builder.Configuration.GetSection("Gateway:Swagger");
var swaggerRoutePrefix = swaggerSection.GetValue<string>("RoutePrefix") ?? "swagger";
var swaggerDocTitle = swaggerSection.GetValue<string>("DocumentTitle") ?? "Sepidar Gateway";

// Endpoints will read their own config values

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = swaggerDocTitle,
        Version = "v1"
    });
    options.DocumentFilter<SepidarGateway.Api.Swagger.OcelotProxyDocumentFilter>();
});
builder.Services.AddHttpClient();
builder.Services.Configure<SepidarOptions>(builder.Configuration.GetSection("Sepidar"));
builder.Services.AddScoped<SepidarGateway.Api.Interfaces.ISepidarService, SepidarGateway.Api.Services.SepidarService>();
builder.Services.AddSingleton<ICurlBuilder, CurlBuilder>();
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<ICacheService, CacheService>();
builder.Services.AddTransient<SepidarHeadersHandler>();
builder.Services
    .AddOcelot()
    .AddDelegatingHandler<SepidarHeadersHandler>();

var app = builder.Build();

if (builder.Configuration.GetValue("Gateway:UseHttpsRedirection", true))
{
    app.UseHttpsRedirection();
}

// Attach Ocelot to the main pipeline (recommended). Internal endpoints remain available.
app.Use(async (ctx, next) =>
{
    // Lightweight trace for /v1/** to aid debugging
    if (ctx.Request.Path.StartsWithSegments("/v1", StringComparison.OrdinalIgnoreCase))
    {
        app.Logger.LogInformation("[OCELOT] → {Method} {Path}", ctx.Request.Method, ctx.Request.Path.Value);
    }
    await next();
    if (ctx.Request.Path.StartsWithSegments("/v1", StringComparison.OrdinalIgnoreCase))
    {
        app.Logger.LogInformation("[OCELOT] ← {Status} {Path}", ctx.Response.StatusCode, ctx.Request.Path.Value);
        if (ctx.Response.StatusCode == StatusCodes.Status404NotFound)
        {
            app.Logger.LogWarning("[OCELOT] 404 (no route matched) for {Method} {Path}. Check ocelot.json mapping.", ctx.Request.Method, ctx.Request.Path.Value);
        }
    }
});

app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", $"{swaggerDocTitle} v1");
    c.DocumentTitle = swaggerDocTitle;
    c.RoutePrefix = swaggerRoutePrefix;
});

// Map endpoints from dedicated classes
app.MapHealthEndpoints();
app.MapRegisterDeviceEndpoints();

// Route /v1/** through Ocelot only; keep /gateway/** for internal endpoints
app.MapWhen(ctx => ctx.Request.Path.StartsWithSegments("/v1", StringComparison.OrdinalIgnoreCase), branch =>
{
    // lightweight tracing
    branch.Use(async (ctx, next) =>
    {
        app.Logger.LogInformation("[OCELOT] → {Method} {Path}", ctx.Request.Method, ctx.Request.Path.Value);
        await next();
        app.Logger.LogInformation("[OCELOT] ← {Status} {Path}", ctx.Response.StatusCode, ctx.Request.Path.Value);
        if (ctx.Response.StatusCode == StatusCodes.Status404NotFound)
        {
            app.Logger.LogWarning("[OCELOT] 404 (no route matched) for {Method} {Path}. Check ocelot.json mapping.", ctx.Request.Method, ctx.Request.Path.Value);
        }
    });
    branch.UseOcelot().Wait();
});

// Fallback فقط برای شاخه داخلی
app.MapFallback("/gateway/{*path}", () => Results.Problem("اندپوینت مورد نظر یافت نشد."));

await app.RunAsync();

// CombineRoute moved to endpoints where needed
