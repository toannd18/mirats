using System.Security.Claims;
using System.Text;
using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Domain.Interfaces;
using aspire_react.Server.Infrastructure.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace aspire_react.Server.Infrastructure.Authentication;

/// <summary>
/// [AUTH Phase 1] DUAL-AUTH registration — the migration allows BOTH token types concurrently:
///  - scheme "App": SELF-SIGNED JWT (Auth:Issuer/Audience/SigningKey) — the new password-login
///    tokens; Issuer/Audience VALIDATED (unlike the Keycloak config which disabled both checks).
///  - scheme "Keycloak": the pre-existing Keycloak JwtBearer (Authority-based) — kept verbatim so
///    users whose local password hasn't been reset yet keep working during the transition.
/// BOTH schemes stamp the local_user_id claim (the App scheme receives it embedded at issuance;
/// the Keycloak scheme resolves it via JIT provisioning exactly as before). Default scheme is
/// "App" so new tokens win when both would validate.
/// Phase 5 removes the "Keycloak" scheme entirely (AUTH_MIGRATION_PLAYBOOK).
/// </summary>
public static class AuthenticationServiceCollectionExtensions
{
    public static IServiceCollection AddKeycloakAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        var keycloakUrl = configuration["Keycloak:Authority"]
            ?? "https://localhost:8080/realms/aspire-react";

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme /* default = "Bearer"... see note */)
            .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                options.Authority = keycloakUrl;
                options.RequireHttpsMetadata = false;

                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = false,
                    ValidateAudience = false,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromMinutes(1)
                };

                options.Events = new JwtBearerEvents
                {
                    OnTokenValidated = async context =>
                    {
                        var provisioning = context.HttpContext.RequestServices
                            .GetRequiredService<IJitUserProvisioningService>();
                        var localUserId = await provisioning.ProvisionAsync(context.Principal);
                        if (localUserId.HasValue && context.Principal?.Identity is ClaimsIdentity identity)
                        {
                            identity.AddClaim(new Claim("local_user_id", localUserId.Value.ToString()));
                        }
                    }
                };
            })
            // [AUTH Phase 1] New self-signed scheme. Named "App"; registered as the DEFAULT scheme
            // below so self-signed tokens win. Same OnTokenValidated contract: verify the local
            // user still exists AND is active (a user disabled mid-session is rejected immediately).
            // MapInboundClaims=false keeps the SHORT OIDC claim names (preferred_username, email...)
            // verbatim on the principal — same reason TokenService clears the outbound map; and the
            // JWT inbound map would otherwise rewrite email/given_name/family_name into ClaimTypes
            // URIs, breaking FindFirst("...") parity with the old Keycloak flow.
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
                    IssuerSigningKey = new SymmetricSecurityKey(
                        Encoding.UTF8.GetBytes(configuration["Auth:SigningKey"]
                            ?? throw new InvalidOperationException("Auth:SigningKey is not configured."))),
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
                        // [AUTH Phase 1 gap fix] pwd_change enforcement là Middleware riêng
                        // (PasswordChangeGateMiddleware) — KHÔNG gate ở đây vì context.Fail()
                        // chỉ ra 401 challenge, trong khi §4.4 yêu cầu 403 MUST_CHANGE_PASSWORD.
                    }
                };
            });

        // Default scheme: the "Policy" forwarder — inspects the JWT issuer and FORWARDS to the
        // right concrete scheme (self-signed "App" OR legacy Keycloak). Without this, a Keycloak
        // token hits the "App" scheme first and is rejected without trying the legacy path.
        // NOTE: AddPolicyScheme is an AuthenticationBuilder extension → chained on the builder.
        services.AddAuthentication(AuthSchemes.Policy) // default scheme = policy forwarder
            .AddPolicyScheme(AuthSchemes.Policy, AuthSchemes.Policy, options =>
            {
                options.ForwardDefaultSelector = context =>
                {
                    var bearer = context.Request.Headers.Authorization.ToString();
                    if (bearer.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                    {
                        var token = bearer["Bearer ".Length..].Trim();
                        // Self-signed tokens carry our Issuer — Keycloak tokens carry the realm URL.
                        // Read WITHOUT validation (this is routing, not authentication).
                        try
                        {
                            var jwt = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().ReadJwtToken(token);
                            if (jwt.Issuer == (configuration["Auth:Issuer"] ?? "aspire-react"))
                                return AuthSchemes.App;
                        }
                        catch { /* malformed → let the default (Keycloak) attempt handle/reject it */ }
                    }
                    return AuthSchemes.Keycloak;
                };
            });

        return services;
    }
}

/// <summary>Scheme names for the dual-auth transition (AUTH Phase 1).</summary>
public static class AuthSchemes
{
    /// <summary>Issuer-inspecting forwarder — routes each Bearer token to App or Keycloak.</summary>
    public const string Policy = "AuthPolicy";

    /// <summary>Self-signed JWT (new — password login). Default scheme during the migration.</summary>
    public const string App = "App";

    /// <summary>Keycloak JwtBearer (legacy — kept until Phase 5 removes it).</summary>
    public const string Keycloak = JwtBearerDefaults.AuthenticationScheme; // "Bearer"
}
