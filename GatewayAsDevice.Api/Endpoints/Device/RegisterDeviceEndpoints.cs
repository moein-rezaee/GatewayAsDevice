using SepidarGateway.Api.Interfaces;
using SepidarGateway.Api.Models;

namespace SepidarGateway.Api.Endpoints.Device;

public static class RegisterDeviceEndpoints
{
    public static void MapRegisterDeviceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var configuration = endpoints.ServiceProvider.GetRequiredService<IConfiguration>();
        var sepidarRegisterPath = configuration.GetValue<string>("Sepidar:RegisterDevice:Endpoint") ?? "/api/Devices/Register";
        var deviceRegisterRouteV1 = CombineRoute("/v1", sepidarRegisterPath);

        endpoints.MapPost(deviceRegisterRouteV1, RegisterDeviceAsync)
        .WithName("SepidarRegisterDevice")
        .WithTags("Device")
        .WithOpenApi(operation =>
        {
            operation.Summary = "ثبت دستگاه در سپیدار";
            operation.Description = "این اندپوینت فقط سریال دستگاه را دریافت می‌کند و مقادیر موردنیاز رجیستر دستگاه سپیدار را در پس‌زمینه محاسبه و پروکسی می‌کند.";
            return operation;
        });

        endpoints.MapGet(deviceRegisterRouteV1, RequirePostForRegister)
            .ExcludeFromDescription();
    }

    private static string CombineRoute(string versionPrefix, string endpoint)
    {
        versionPrefix ??= string.Empty;
        endpoint ??= string.Empty;

        static string Normalize(string s, bool leading)
            => string.IsNullOrWhiteSpace(s)
                ? string.Empty
                : leading
                    ? "/" + s.Trim().Trim('/')
                    : s.Trim().Trim('/');

        var v = Normalize(versionPrefix, leading: true);
        var ep = Normalize(endpoint, leading: false);
        return string.IsNullOrEmpty(ep) ? v : $"{v}/{ep}";
    }

    // Handler: POST Register Device
    private static async Task<IResult> RegisterDeviceAsync(
        RegisterDeviceGatewayRequest request,
        ISepidarService sepidarService,
        CancellationToken cancellationToken)
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
    }

    // Handler: GET on register route (not allowed)
    private static IResult RequirePostForRegister()
        => Results.BadRequest(new { message = "برای ثبت دستگاه از متد POST استفاده کنید." });

    // caching handled inside SepidarService
}
