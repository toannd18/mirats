using aspire_react.Server.Application.Auth.Commands;
using aspire_react.Server.Domain.Interfaces;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace aspire_react.Server.Web.Controllers;

/// <summary>
/// [AUTH Phase 1] Password/JWT authentication endpoints (Keycloak replacement — see
/// AUTH_MIGRATION_PLAYBOOK). THIN 100%: every endpoint is one IMediator.Send; the ONLY
/// controller-level concerns are (1) refresh-cookie set/clear (HTTP concern — §11.2) and
/// (2) client-IP extraction for the brute-force defense. No hash/token/lockout logic here.
/// Dual-auth: tokens from BOTH schemes (self-signed "App" + legacy Keycloak "Bearer") are
/// accepted by [Authorize] endpoints during the migration.
/// </summary>
[ApiController]
[Route("api/v1/auth")]
public class AuthController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly IAuthCookieService _authCookie;

    public AuthController(IMediator mediator, IAuthCookieService authCookie)
    {
        _mediator = mediator;
        _authCookie = authCookie;
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var result = await _mediator.Send(new LoginCommand(request.Username, request.Password, GetClientIpAddress()));

        if (!result.Success)
            return result.ErrorCode == "ACCOUNT_LOCKED"
                ? BadRequest(new { status = "error", message = "Tài khoản tạm thời bị khóa do đăng nhập sai nhiều lần. Vui lòng thử lại sau.", error_code = result.ErrorCode })
                : BadRequest(new { status = "error", message = "Sai tên đăng nhập hoặc mật khẩu.", error_code = result.ErrorCode });

        _authCookie.SetRefreshCookie(result.RefreshToken!, result.RefreshExpiresAt!.Value);
        return Ok(new { status = "success", accessToken = result.AccessToken, mustChangePassword = result.MustChangePassword });
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<IActionResult> Refresh()
    {
        var raw = _authCookie.GetRefreshCookie();
        if (string.IsNullOrEmpty(raw))
            return Unauthorized(new { status = "error", message = "Phiên đăng nhập đã hết hạn." });

        var result = await _mediator.Send(new RefreshTokenCommand(raw));
        if (!result.Success)
        {
            // Reuse-detection containment already revoked everything — clear the cookie.
            _authCookie.ClearRefreshCookie();
            return Unauthorized(new { status = "error", message = "Phiên đăng nhập đã hết hạn." });
        }

        _authCookie.SetRefreshCookie(result.RefreshToken!, result.RefreshExpiresAt!.Value);
        return Ok(new { status = "success", accessToken = result.AccessToken, mustChangePassword = result.MustChangePassword });
    }

    [HttpPost("logout")]
    [AllowAnonymous]
    public async Task<IActionResult> Logout()
    {
        var raw = _authCookie.GetRefreshCookie();
        if (!string.IsNullOrEmpty(raw))
            await _mediator.Send(new LogoutCommand(raw));
        _authCookie.ClearRefreshCookie();
        return Ok(new { status = "success", message = "Đã đăng xuất." });
    }

    [HttpPost("password")]
    [Authorize]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request)
    {
        var userId = GetCurrentUserId();
        var result = await _mediator.Send(new ChangePasswordCommand(userId, request.CurrentPassword, request.NewPassword));

        if (!result.Success)
            return result.ErrorCode == "USER_NOT_FOUND"
                ? Unauthorized(new { status = "error", message = "Phiên đăng nhập không hợp lệ." })
                : BadRequest(new { status = "error", message = "Mật khẩu hiện tại không đúng.", error_code = result.ErrorCode });

        return Ok(new { status = "success", message = "Đã đổi mật khẩu." });
    }

    // ==================== [AUTH Phase 3] Passkey (WebAuthn) endpoints ====================
    // Ceremony options/attestation travel as RAW JSON (navigator.credentials contract) — the
    // {status,data} envelope does not apply to those two payloads (documented for API.md).

    [HttpGet("passkeys/status")]
    [AllowAnonymous]
    public async Task<IActionResult> PasskeyStatus(CancellationToken ct)
        => Ok(new { status = "success", data = new { enabled = await _mediator.Send(new GetPasskeyStatusQuery(), ct) } });

    [HttpGet("passkeys")]
    [Authorize]
    public async Task<IActionResult> ListPasskeys(CancellationToken ct)
        => Ok(new { status = "success", data = await _mediator.Send(new ListPasskeysQuery(GetCurrentUserId()), ct) });

    [HttpPost("passkeys/register/options")]
    [Authorize]
    public async Task<IActionResult> PasskeyRegisterOptions(CancellationToken ct)
    {
        try
        {
            var options = await _mediator.Send(new GetPasskeyRegisterOptionsQuery(GetCurrentUserId()), ct);
            return new ContentResult { Content = options, ContentType = "application/json", StatusCode = 200 };
        }
        catch (InvalidOperationException ex) when (ex.Message == "USER_NOT_FOUND")
        {
            return Unauthorized(new { status = "error", message = "Phiên đăng nhập không hợp lệ." });
        }
    }

    [HttpPost("passkeys/register")]
    [Authorize]
    public async Task<IActionResult> PasskeyRegister([FromBody] RegisterPasskeyRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(new CompletePasskeyRegistrationCommand(GetCurrentUserId(), request.AttestationJson, request.Name), ct);
        return result.Success
            ? Ok(new { status = "success", data = result.Passkey })
            : BadRequest(new { status = "error", message = PasskeyRegisterErrorText(result.ErrorCode!), error_code = result.ErrorCode });
    }

    [HttpPost("passkeys/login/options")]
    [AllowAnonymous]
    public async Task<IActionResult> PasskeyLoginOptions([FromBody] PasskeyLoginOptionsRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetPasskeyLoginOptionsQuery(request.Username), ct);
        return result.Enabled
            ? new ContentResult { Content = result.OptionsJson, ContentType = "application/json", StatusCode = 200 }
            : StatusCode(403, new { status = "error", message = "Đăng nhập bằng Passkey hiện không được bật.", error_code = "PASSKEYS_DISABLED" });
    }

    [HttpPost("passkeys/login")]
    [AllowAnonymous]
    public async Task<IActionResult> PasskeyLogin([FromBody] PasskeyLoginRequest request)
    {
        var result = await _mediator.Send(new VerifyPasskeyAssertionCommand(request.AssertionJson, GetClientIpAddress()));

        if (!result.Success)
        {
            return result.ErrorCode == "PASSKEYS_DISABLED"
                ? StatusCode(403, new { status = "error", message = "Đăng nhập bằng Passkey hiện không được bật.", error_code = result.ErrorCode })
                : Unauthorized(new { status = "error", message = "Xác thực Passkey không thành công.", error_code = result.ErrorCode });
        }

        _authCookie.SetRefreshCookie(result.RefreshToken!, result.RefreshExpiresAt!.Value);
        return Ok(new { status = "success", accessToken = result.AccessToken, mustChangePassword = result.MustChangePassword });
    }

    [HttpDelete("passkeys/{id:guid}")]
    [Authorize]
    public async Task<IActionResult> DeletePasskey(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new DeletePasskeyCommand(GetCurrentUserId(), id), ct);
        return result.Success
            ? Ok(new { status = "success", message = "Đã xóa passkey." })
            : NotFound(new { status = "error", message = "Passkey không tồn tại." });
    }

    private static string PasskeyRegisterErrorText(string code) => code switch
    {
        "PASSKEY_REGISTRATION_EXPIRED" => "Hết thời gian đăng ký passkey. Vui lòng thử lại.",
        "PASSKEY_ALREADY_REGISTERED" => "Passkey này đã được đăng ký.",
        "PASSKEY_MALFORMED_RESPONSE" => "Dữ liệu passkey không hợp lệ.",
        _ => "Xác thực passkey không thành công."
    };

    private Guid GetCurrentUserId()
    {
        var claim = User?.FindFirst("local_user_id")?.Value;
        return Guid.TryParse(claim, out var id) ? id : Guid.Empty;
    }

    private string GetClientIpAddress()
        => HttpContext?.Connection?.RemoteIpAddress?.ToString() ?? string.Empty;
}

public record LoginRequest(string Username, string Password);

public record ChangePasswordRequest(string CurrentPassword, string NewPassword);

// [AUTH Phase 3] Passkey ceremony payloads — AttestationJson/AssertionJson are the RAW
// navigator.credentials JSON (string), parsed server-side by Fido2Service.
public record RegisterPasskeyRequest(string AttestationJson, string? Name);

public record PasskeyLoginOptionsRequest(string? Username);

public record PasskeyLoginRequest(string AssertionJson);
