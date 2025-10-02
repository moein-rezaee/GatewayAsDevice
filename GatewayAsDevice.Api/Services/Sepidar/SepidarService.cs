using System.Text.Json.Nodes;
using SepidarGateway.Api.Interfaces;
using SepidarGateway.Api.Interfaces.Sepidar;

namespace SepidarGateway.Api.Services.Sepidar;

public class SepidarService : ISepidarService
{
    private readonly IRegisterService _register;
    private readonly ILoginService _login;

    public SepidarService(IRegisterService register, ILoginService login)
    {
        _register = register;
        _login = login;
    }

    public async Task<JsonObject> RegisterDeviceAndLoginAsync(string serial, CancellationToken cancellationToken = default)
    {
        var registerNode = await _register.RegisterAsync(serial, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("پاسخ رجیستر دستگاه خالی است.");

        var loginNode = await _login.LoginAsync(serial, registerNode, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("پاسخ لاگین کاربر خالی است.");

        return new JsonObject
        {
            ["Register"] = registerNode.DeepClone(),
            ["Login"] = loginNode.DeepClone()
        };
    }
}

