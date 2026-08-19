using aspire_react.Server.Application;
using aspire_react.Server.Infrastructure;
using aspire_react.Server.Infrastructure.Authentication;
using aspire_react.Server.Infrastructure.Authorization;
using aspire_react.Server.Infrastructure.Caching;
using aspire_react.Server.Infrastructure.Persistence;
using aspire_react.Server.Web.ExceptionHandlers;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

// Add service defaults & Aspire client integrations.
builder.AddServiceDefaults();

// EF Core (Postgres) + Postgres health check.
builder.AddPersistence();

// Redis-backed output caching for reference-data endpoints (Task P). Registers IOutputCacheStore
// + its own Redis health check; consumed via [OutputCache] attributes + app.UseOutputCache().
builder.AddRedisCaching();

// Application layer: MediatR + FluentValidation + ValidationBehavior (Task L).
builder.Services.AddApplicationServices();

// Infrastructure: Keycloak admin API, JIT user provisioning, app services, lockout guard.
builder.Services.AddInfrastructureServices(builder.Configuration);

// Authentication: Keycloak JWT bearer (OnTokenValidated delegates to IJitUserProvisioningService).
builder.Services.AddKeycloakAuthentication(builder.Configuration);

// Authorization: permission policies from PermissionCatalog + PermissionHandler.
builder.Services.AddPermissionAuthorization();

// CORS for frontend — origins come from CORS_ALLOWED_ORIGINS env (comma-separated, set in
// docker-compose.yml), falling back to the dev origin for local `dotnet run` (safe, not a secret).
var corsOrigins = (builder.Configuration["CORS_ALLOWED_ORIGINS"] ?? "http://localhost:5173")
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
    {
        policy.WithOrigins(corsOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

// Add controllers (API endpoints)
builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// Add services to the container.
builder.Services.AddProblemDetails();
// Maps FluentValidation.ValidationException (from ValidationBehavior) to a clean 400 response.
builder.Services.AddExceptionHandler<ValidationExceptionHandler>();
builder.Services.AddOpenApi();

var app = builder.Build();

// Migration + seed default system groups + legacy superuser migration (extracted from Program.cs, Task Q).
StartupDataSeeder.Seed(app.Services);

// Configure the HTTP request pipeline.
app.UseExceptionHandler();
app.UseCors("Frontend");

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseAuthentication();
app.UseAuthorization();

// Output cache (Task P) — AFTER UseAuthorization so unauthorized requests short-circuit (403)
// before ever touching the cache; [OutputCache] endpoints are reference-data only.
app.UseOutputCache();

// Map controllers (all [ApiController] routes)
app.MapControllers();

// Map health check endpoints (includes /health and /alive from ServiceDefaults)
app.MapDefaultEndpoints();

// API routes placeholder
var api = app.MapGroup("/api/v1");

// Health endpoint (anonymous)
api.MapGet("health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }))
   .WithName("ApiHealth")
   .ExcludeFromDescription()
   .AllowAnonymous();

app.UseFileServer();

app.Run();
