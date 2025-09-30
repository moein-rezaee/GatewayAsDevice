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
});
builder.Services.AddHttpClient<IHttpClientWrapper, HttpClientWrapper>();
builder.Services.Configure<SepidarOptions>(builder.Configuration.GetSection("Sepidar"));
builder.Services.AddScoped<ISepidarService, SepidarService>();
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<ICacheWrapper, MemoryCacheWrapper>();

var app = builder.Build();

if (builder.Configuration.GetValue("Gateway:UseHttpsRedirection", true))
{
    app.UseHttpsRedirection();
}

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
app.MapUserLoginEndpoints();

app.MapFallback(() => Results.Problem("اندپوینت مورد نظر یافت نشد."));

await app.RunAsync();

// CombineRoute moved to endpoints where needed
