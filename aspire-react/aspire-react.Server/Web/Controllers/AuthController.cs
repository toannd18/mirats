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
