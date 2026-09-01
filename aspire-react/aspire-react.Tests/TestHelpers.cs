using System.Security.Claims;
using System.Text.Json;
using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Domain.Exceptions;
using aspire_react.Server.Domain.Interfaces;
using aspire_react.Server.Infrastructure.Caching;
using aspire_react.Server.Infrastructure.Persistence;
using aspire_react.Server.Infrastructure.Services;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Tests;

/// <summary>
/// Shared fakes + context helpers for the ST9 backend test suite.
/// Mirrors the established AssetMaintenanceTests / LicenseTests / ConsumableTests patterns
/// (xUnit + EF Core InMemory, hand-rolled fakes instead of Moq).
/// </summary>
public static class TestHelpers
{
    /// <summary>Superuser scope so the FMCS global query filters short-circuit (see-all).</summary>
    public sealed class SuperUserScope : ICompanyScopeService
    {
        public bool IsSuperUser() => true;
        public Task<List<Guid>> GetUserCompanyIdsAsync() => Task.FromResult(new List<Guid>());
        public Task<Guid?> GetCurrentUserCompanyIdAsync() => Task.FromResult<Guid?>(null);
        public Task<bool> IsCompanyIdInUserScopeAsync(Guid companyId) => Task.FromResult(true);
    }

    /// <summary>
    /// Configurable company scope mirroring <see cref="CompanyScopeService"/> (SEC-FIX
    /// JIT-COMPANYLESS 2026-08-23): Superuser → null (see everything); regular user with
    /// <see cref="CompanyId"/> → that company; regular user WITHOUT a company → Guid.Empty
    /// sentinel (only company-less records visible, NOT cross-company data).
    /// </summary>
    public sealed class FakeScope : ICompanyScopeService
    {
        public bool Super { get; set; }
        public Guid? CompanyId { get; set; }
        public bool IsSuperUser() => Super;
        public Task<List<Guid>> GetUserCompanyIdsAsync() => Task.FromResult(new List<Guid>());
        public Task<Guid?> GetCurrentUserCompanyIdAsync()
            => Task.FromResult(Super ? (Guid?)null : (CompanyId ?? Guid.Empty));
        public Task<bool> IsCompanyIdInUserScopeAsync(Guid companyId)
            => Task.FromResult(Super || (CompanyId != null && CompanyId == companyId));
    }

    public sealed class FakeCurrentUser : ICurrentUserService
    {
        public Guid UserId { get; } = Guid.NewGuid();
        public Guid GetLocalUserId() => UserId;
    }

    /// <summary>No-op ICacheInvalidator for controller unit tests (no real IOutputCacheStore wired).</summary>
    public sealed class NullCacheInvalidator : ICacheInvalidator
    {
        public Task InvalidateCategoriesAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task InvalidateManufacturersAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task InvalidateSuppliersAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task InvalidateCompaniesAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    /// <summary>[Giai đoạn 1.5] No-op ICacheTagEvictor for pipeline tests (no real output-cache store wired).</summary>
    public sealed class NullCacheTagEvictor : ICacheTagEvictor
    {
        public List<string[]> Evictions { get; } = new();
        public Task EvictTagsAsync(IEnumerable<string> tags, CancellationToken ct = default)
        {
            Evictions.Add(tags.ToArray());
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// In-memory IKeycloakService. Every method is a no-op unless the matching "ShouldThrow"
    /// flag is set (used to simulate a Keycloak outage for the User CRUD handlers).
    /// </summary>
    public sealed class FakeKeycloakService : IKeycloakService
    {
        public bool CreateShouldThrow { get; set; }
        public bool UpdateShouldThrow { get; set; }
        public int CreateCalls { get; private set; }
        public int UpdateCalls { get; private set; }
        public int DisableCalls { get; private set; }
        public int AddToSuperUserGroupCalls { get; private set; }
        public int RemoveFromSuperUserGroupCalls { get; private set; }

        public Task EnsureSuperUserGroupExistsAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task<string> CreateUserAsync(
            string username, string email, string firstName, string lastName,
            bool enabled, CancellationToken ct = default)
        {
            CreateCalls++;
            if (CreateShouldThrow) throw new KeycloakApiException("Keycloak unavailable", "KEYCLOAK_ERROR");
            return Task.FromResult(Guid.NewGuid().ToString());
        }

        public Task UpdateUserAsync(
            string username, string email, string firstName, string lastName,
            bool enabled, CancellationToken ct = default)
        {
            UpdateCalls++;
            if (UpdateShouldThrow) throw new KeycloakApiException("Keycloak sync failed", "KEYCLOAK_SYNC_FAILED");
            return Task.CompletedTask;
        }

        public Task DisableUserAsync(string username, CancellationToken ct = default)
        {
            DisableCalls++;
            return Task.CompletedTask;
        }

        public Task AddUserToSuperUserGroupAsync(string username, CancellationToken ct = default)
        {
            AddToSuperUserGroupCalls++;
            return Task.CompletedTask;
        }

        public Task RemoveUserFromSuperUserGroupAsync(string username, CancellationToken ct = default)
        {
            RemoveFromSuperUserGroupCalls++;
            return Task.CompletedTask;
        }
    }

    /// <summary>IMediator stub that throws on Send — only safe for controller LIST endpoints (no command dispatch).</summary>
    public sealed class ThrowingMediator : IMediator
    {
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("This test does not dispatch MediatR commands.");

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest
            => throw new NotSupportedException("This test does not dispatch MediatR commands.");

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("This test does not dispatch MediatR commands.");

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task Publish(object notification, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification
            => throw new NotSupportedException();
    }

    /// <summary>
    /// InMemory AppDbContext. The InMemory provider has no real transactions, so the
    /// TransactionIgnoredWarning (thrown as an error by default) is suppressed → BeginTransaction
    /// becomes a no-op. This lets the Accessory Checkout/Checkin handlers (which use an ambient
    /// transaction) run end-to-end under InMemory. NOTE: the Asset Checkout/Checkin handlers also
    /// use <c>FromSqlRaw("... FOR UPDATE")</c> which the InMemory provider cannot translate —
    /// those two handlers are exercised via their validator instead (see AssetTests).
    /// </summary>
    public static AppDbContext CreateContext(string name, ICompanyScopeService? scope = null)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(name)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new AppDbContext(options, scope ?? new SuperUserScope());
    }

    /// <summary>
    /// Real ActionLogService. When <paramref name="actorId"/> is given, the HttpContextAccessor is
    /// seeded with a "local_user_id" claim so <c>GetCurrentUserIdAsync()</c> resolves the actor
    /// (the same claim the JIT provisioning hook stamps on real requests).
    /// </summary>
    public static ActionLogService CreateActionLogService(AppDbContext ctx, Guid? actorId = null)
    {
        var accessor = new HttpContextAccessor();
        if (actorId.HasValue)
        {
            accessor.HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    new[] { new Claim("local_user_id", actorId.Value.ToString()) }, "Test"))
            };
        }
        return new ActionLogService(ctx, accessor);
    }

    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    /// <summary>Serializes a controller result value and returns the "data" array's property values.</summary>
    public static List<string> ReadDataStringArray(object? value, string property)
    {
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(value, WebJson));
        var result = new List<string>();
        foreach (var item in doc.RootElement.GetProperty("data").EnumerateArray())
            result.Add(item.GetProperty(property).GetString() ?? string.Empty);
        return result;
    }

    /// <summary>Serializes a controller result value and returns the "data" array length.</summary>
    public static int ReadDataCount(object? value)
    {
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(value, WebJson));
        return doc.RootElement.GetProperty("data").GetArrayLength();
    }
}

