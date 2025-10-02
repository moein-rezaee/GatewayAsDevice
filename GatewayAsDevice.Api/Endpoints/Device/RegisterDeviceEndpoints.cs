using System.Text.Json.Nodes;
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
            operation.Summary = "ثبت دستگاه و ورود کاربر در سپیدار";
            operation.Description = "سریال دستگاه را دریافت می‌کند، رجیستر دستگاه را انجام می‌دهد و در ادامه فرآیند لاگین کاربر را نیز تکمیل می‌کند.";
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
            var registerNode = await sepidarService.RegisterDeviceAsync(request.Serial, cancellationToken).ConfigureAwait(false);
            if (registerNode is null)
            {
                throw new InvalidOperationException("پاسخ رجیستر دستگاه خالی است.");
            }

            var loginNode = await sepidarService.UserLoginAsync(cancellationToken).ConfigureAwait(false);
            if (loginNode is null)
            {
                throw new InvalidOperationException("پاسخ لاگین کاربر خالی است.");
            }

            var response = new JsonObject
            {
                ["Register"] = registerNode.DeepClone(),
                ["Login"] = loginNode.DeepClone()
            };

            return Results.Json(response);
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
