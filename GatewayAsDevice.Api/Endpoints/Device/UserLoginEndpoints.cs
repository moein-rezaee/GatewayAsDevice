using SepidarGateway.Api.Interfaces;

namespace SepidarGateway.Api.Endpoints.Device;

public static class UserLoginEndpoints
{
    public static void MapUserLoginEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var configuration = endpoints.ServiceProvider.GetRequiredService<IConfiguration>();
        // برای سازگاری نام‌گذاری با RegisterDevice، مسیر Gateway را PascalCase نگه می‌داریم
        var gatewayLoginPath = configuration.GetValue<string>("Gateway:UsersLogin:Route") ?? "/api/Users/Login";
        var route = CombineRoute("/v1", gatewayLoginPath);

        endpoints.MapPost(route, UserLoginAsync)
            .WithName("SepidarUserLogin")
            .WithTags("Device")
            .WithOpenApi(operation =>
            {
                operation.Summary = "یوزر لاگین به سپیدار";
                operation.Description = "از داده‌های کش رجیستر و env استفاده می‌کند و لاجیک در سرویس سپیدار اجرا می‌شود.";
                return operation;
            });
    }

    private static async Task<IResult> UserLoginAsync(
        ISepidarService sepidarService,
        CancellationToken cancellationToken)
    {
        try
        {
            var node = await sepidarService.UserLoginAsync(cancellationToken).ConfigureAwait(false);
            if (node is null)
            {
                return Results.NoContent();
            }
            return Results.Json(node);
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { message = ex.Message });
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
}
