using System.Net.Http;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using DotNetEnv;
using MMLib.SwaggerForOcelot.DependencyInjection;
using Ocelot.DependencyInjection;
using Ocelot.Middleware;
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
    .AddJsonFile("ocelot.json", optional: false, reloadOnChange: true)
    .AddJsonFile($"ocelot.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true)
    .AddEnvironmentVariables();

var swaggerSection = builder.Configuration.GetSection("Gateway:Swagger");
var swaggerRoutePrefix = swaggerSection.GetValue<string>("RoutePrefix") ?? "swagger";
var swaggerDocTitle = swaggerSection.GetValue<string>("DocumentTitle") ?? "Sepidar Gateway";
var swaggerGeneratorPath = swaggerSection.GetValue<string>("GeneratorPath") ?? "/swagger/docs";

var healthSection = builder.Configuration.GetSection("Gateway:Health");
var healthPath = healthSection.GetValue<string>("Path") ?? "/health";

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddOcelot(builder.Configuration);
builder.Services.AddSwaggerForOcelot(builder.Configuration, setup =>
{
    setup.GenerateDocsForGatewayItSelf = true;
    setup.DownstreamDocsCacheExpire = TimeSpan.Zero;
});
builder.Services.AddHttpClient<IHttpClientWrapper, HttpClientWrapper>();
builder.Services.Configure<SepidarOptions>(builder.Configuration.GetSection("Sepidar"));
builder.Services.AddScoped<ISepidarService, SepidarService>();

var app = builder.Build();

if (builder.Configuration.GetValue<bool>("Gateway:UseHttpsRedirection", true))
{
    app.UseHttpsRedirection();
}

app.UseSwagger();
app.UseSwaggerForOcelotUI(opt =>
{
    opt.PathToSwaggerGenerator = swaggerGeneratorPath;
}, uiOpt =>
{
    uiOpt.DocumentTitle = swaggerDocTitle;
    uiOpt.RoutePrefix = swaggerRoutePrefix;
});

app.MapGet(healthPath, () => Results.Ok(new
{
    status = "Healthy",
    timestamp = DateTimeOffset.UtcNow
}));

app.MapPost("/gateway/sepidar/device/register", async (
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
        var response = await sepidarService.RegisterDeviceAsync(request.Serial, cancellationToken).ConfigureAwait(false);
        if (response is null)
        {
            return Results.NoContent();
        }

        return Results.Content(response.RootElement.GetRawText(), "application/json");
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
.WithOpenApi(operation =>
{
    operation.Summary = "ثبت دستگاه در سپیدار";
    operation.Description = "این اندپوینت فقط سریال دستگاه را دریافت می‌کند و مقادیر موردنیاز رجیستر دستگاه سپیدار را در پس‌زمینه محاسبه و پروکسی می‌کند.";
    return operation;
});

app.MapGet("/gateway/sepidar/device/register", () => Results.BadRequest(new { message = "برای ثبت دستگاه از متد POST استفاده کنید." }))
    .ExcludeFromDescription();

app.MapFallback(() => Results.Problem("اندپوینت مورد نظر یافت نشد."));

await app.UseOcelot();

await app.RunAsync();
