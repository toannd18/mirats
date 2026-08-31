using System.Security.Claims;
using aspire_react.Server.Infrastructure.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace aspire_react.Server.Infrastructure.Authentication;

/// <summary>
/// Registers Keycloak JWT bearer authentication. The JIT provisioning business logic that used to
/// live inline in <c>OnTokenValidated</c> is now delegated to <see cref="IJitUserProvisioningService"/>
/// (Task Q) so it is unit-testable independently; the handler only stamps the <c>local_user_id</c> claim.
/// </summary>
public static class AuthenticationServiceCollectionExtensions
{
    public static IServiceCollection AddKeycloakAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        // Authority from Aspire service discovery or configuration
        var keycloakUrl = configuration["Keycloak:Authority"]
            ?? "https://localhost:8080/realms/aspire-react";

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
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
                        // Resolve the scoped JIT provisioning service from the current request's
                        // service provider (OnTokenValidated executes per-request, so the scoped
                        // factory is available).
                        var provisioning = context.HttpContext.RequestServices
                            .GetRequiredService<IJitUserProvisioningService>();

                        var localUserId = await provisioning.ProvisionAsync(context.Principal);

                        // Augment claims principal with local user ID. This allows controllers/services
                        // to read the local DB ID directly as "local_user_id" claim, avoiding repeated DB lookups.
                        if (localUserId.HasValue && context.Principal?.Identity is ClaimsIdentity identity)
                        {
                            identity.AddClaim(new Claim("local_user_id", localUserId.Value.ToString()));
                        }
                    }
                };
            });

        return services;
    }
}
