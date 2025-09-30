using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using DotNetEnv;
using Microsoft.OpenApi.Models;
using SepidarGateway.Api.Interfaces;
using SepidarGateway.Api.Models;
using SepidarGateway.Api.Options;
using SepidarGateway.Api.Services;

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

var healthSection = builder.Configuration.GetSection("Gateway:Health");
var healthPath = healthSection.GetValue<string>("Path") ?? "/health";

// Build device route based on Sepidar upstream path with version prefix
var sepidarRegisterPath = builder.Configuration.GetValue<string>("Sepidar:RegisterDevice:Endpoint") ?? "/api/Devices/Register";
var deviceRegisterRouteV1 = CombineRoute("/v1", sepidarRegisterPath);

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

if (builder.Configuration.GetValue<bool>("Gateway:UseHttpsRedirection", true))
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

app.MapGet(healthPath, () => Results.Ok(new
{
    status = "Healthy",
    timestamp = DateTimeOffset.UtcNow
}))
    .WithTags("Gateway");

app.MapPost(deviceRegisterRouteV1, async (
    RegisterDeviceGatewayRequest request,
    ISepidarService sepidarService,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.Serial))
    {
        return Results.BadRequest(new { message = "ارسال سریال دستگاه الزامی است." });
    }

    try
    {
        var responseNode = await sepidarService.RegisterDeviceAsync(request.Serial, cancellationToken).ConfigureAwait(false);
        if (responseNode is null)
        {
            return Results.NoContent();
        }

        return Results.Json(responseNode);
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { message = ex.Message });
    }
    catch (InvalidOperationException ex)
    {
        return Results.Problem(ex.Message);
    }
    catch (HttpRequestException ex)
    {
        return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status502BadGateway, title: "خطا در ارتباط با سپیدار");
    }
    catch (TaskCanceledException)
    {
        return Results.Problem(detail: "مهلت فراخوانی سرویس سپیدار به پایان رسید.", statusCode: StatusCodes.Status504GatewayTimeout, title: "اتمام مهلت ارتباط");
    }
})
.WithName("SepidarRegisterDevice")
.WithTags("Device")
.WithOpenApi(operation =>
{
    operation.Summary = "ثبت دستگاه در سپیدار";
    operation.Description = "این اندپوینت فقط سریال دستگاه را دریافت می‌کند و مقادیر موردنیاز رجیستر دستگاه سپیدار را در پس‌زمینه محاسبه و پروکسی می‌کند.";
    return operation;
});

app.MapGet(deviceRegisterRouteV1, () => Results.BadRequest(new { message = "برای ثبت دستگاه از متد POST استفاده کنید." }))
    .ExcludeFromDescription();

app.MapFallback(() => Results.Problem("اندپوینت مورد نظر یافت نشد."));

await app.RunAsync();

static string CombineRoute(string versionPrefix, string endpoint)
{
    versionPrefix ??= string.Empty;
    endpoint ??= string.Empty;

    string Normalize(string s, bool leading)
        => string.IsNullOrWhiteSpace(s)
            ? string.Empty
            : leading
                ? "/" + s.Trim().Trim('/')
                : s.Trim().Trim('/');

    var v = Normalize(versionPrefix, leading: true);
    var ep = Normalize(endpoint, leading: false);
    return string.IsNullOrEmpty(ep) ? v : $"{v}/{ep}";
}
