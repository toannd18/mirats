using System.Text;
using aspire_react.Server.Application.Common.Interfaces;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace aspire_react.Server.Infrastructure.Authentication;

/// <summary>
/// [AUTH Phase 5] Authentication registration — the migration is COMPLETE: the ONLY scheme is
/// "App" (self-signed HS256 JWT from /auth/login + /auth/passkeys/login). The legacy Keycloak
/// bearer scheme, the issuer-inspecting PolicyScheme forwarder, and JIT provisioning were all
/// removed at the end of the campaign.
/// The OnTokenValidated handler keeps one live check: the local user must still exist AND be
/// active (a user disabled mid-session is rejected immediately). The pwd_change gate remains a
/// separate middleware (PasswordChangeGateMiddleware — Program.cs pipeline).
/// </summary>
public static class AuthenticationServiceCollectionExtensions
{
    public static IServiceCollection AddAppAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        // [READY GĐ0] Fail-fast at startup: an HS256 key shorter than 256 bits still "works" at
        // runtime but makes issued tokens brute-forceable. Measure the key EXACTLY the way
        // TokenService/JwtBearer consume it — UTF-8 bytes — and refuse to boot instead of
        // silently weakening authentication.
        var signingKey = configuration["Auth:SigningKey"];
        if (string.IsNullOrWhiteSpace(signingKey))
        {
            throw new InvalidOperationException(
                "Auth:SigningKey is not configured. Set a random key of at least 32 bytes (256 bits), " +
                "e.g. `openssl rand -base64 48`.");
        }

        var signingKeyBytes = Encoding.UTF8.GetBytes(signingKey);
        if (signingKeyBytes.Length < 32)
        {
            throw new InvalidOperationException(
                $"Auth:SigningKey must be at least 32 bytes (256 bits) when UTF-8 encoded; the configured " +
                $"value is only {signingKeyBytes.Length} bytes. Generate a new key with `openssl rand -base64 48` " +
                "and restart the server.");
        }

        services.AddAuthentication(AuthSchemes.App)
            .AddJwtBearer(AuthSchemes.App, options =>
            {
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = configuration["Auth:Issuer"] ?? "aspire-react",
                    ValidateAudience = true,
                    ValidAudience = configuration["Auth:Audience"] ?? "aspire-react-api",
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(signingKeyBytes),
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromMinutes(1)
                };

                options.Events = new JwtBearerEvents
                {
                    OnTokenValidated = async context =>
                    {
                        var localId = context.Principal?.FindFirst("local_user_id")?.Value;
                        if (!Guid.TryParse(localId, out var userId)) return;

                        var db = context.HttpContext.RequestServices
                            .GetRequiredService<IApplicationDbContext>();
                        var active = await db.Users.AsNoTracking()
                            .AnyAsync(u => u.Id == userId && u.IsActive, context.HttpContext.RequestAborted);
                        if (!active)
                        {
                            context.Fail("User is disabled.");
                        }
                        // pwd_change enforcement lives in PasswordChangeGateMiddleware (Program.cs
                        // pipeline) — context.Fail() here could only yield a 401 challenge, while
                        // the designed behavior is 403 MUST_CHANGE_PASSWORD.
                    }
                };
            });

        return services;
    }
}

/// <summary>Authentication scheme names — a single local scheme since the migration completed.</summary>
public static class AuthSchemes
{
    /// <summary>Self-signed JWT (password + passkey login) — the ONLY scheme.</summary>
    public const string App = "App";
}
