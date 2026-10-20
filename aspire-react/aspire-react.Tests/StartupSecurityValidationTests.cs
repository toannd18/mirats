using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Infrastructure.Authentication;
using aspire_react.Server.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace aspire_react.Tests;

/// <summary>
/// [READY GĐ0] Startup fail-fast policy:
/// - Auth:SigningKey must be at least 32 bytes measured as UTF-8 (exactly how TokenService and
///   JwtBearer consume it) — a shorter key must abort startup, not silently weaken HS256.
/// - Auth:BootstrapAdminPassword must be >= 12 characters and different from the username ONLY when
///   the first admin is created on a blank database. Existing admins are never re-validated.
/// </summary>
public class StartupSecurityValidationTests
{
    private static IConfiguration Config(params (string Key, string? Value)[] pairs)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(pairs.ToDictionary(p => p.Key, p => p.Value))
            .Build();

    private static IConfiguration AuthConfig(string? signingKey)
        => Config(
            ("Auth:Issuer", "test-issuer"),
            ("Auth:Audience", "test-audience"),
            ("Auth:SigningKey", signingKey));

    private static AppDbContext NewDb() => TestHelpers.CreateContext(Guid.NewGuid().ToString());

    private static IConfiguration BootstrapConfig(string password, string username = "admin", string email = "admin@localhost")
        => Config(
            ("Auth:BootstrapAdminPassword", password),
            ("INITIAL_ADMIN_USERNAME", username),
            ("INITIAL_ADMIN_EMAIL", email));

    // ==================== Auth:SigningKey ====================

    [Fact]
    public void SigningKey_16Bytes_ThrowsAtRegistration()
    {
        var services = new ServiceCollection();

        var ex = Assert.Throws<InvalidOperationException>(
            () => services.AddAppAuthentication(AuthConfig("0123456789abcdef")));

        Assert.Contains("32 bytes", ex.Message);
        Assert.Contains("16 bytes", ex.Message);
    }

    [Fact]
    public void SigningKey_Missing_ThrowsAtRegistration()
    {
        var services = new ServiceCollection();

        var ex = Assert.Throws<InvalidOperationException>(() => services.AddAppAuthentication(AuthConfig(null)));

        Assert.Contains("Auth:SigningKey is not configured", ex.Message);
    }

    [Fact]
    public void SigningKey_MeasuresUtf8Bytes_NotCharacters()
    {
        // 11 characters × 3 UTF-8 bytes = 33 bytes → valid even though char count < 32.
        var key = new string('ẩ', 11);
        Assert.Equal(33, System.Text.Encoding.UTF8.GetByteCount(key));

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAppAuthentication(AuthConfig(key)); // must not throw
    }

    [Fact]
    public void SigningKey_MultibyteUnder32Bytes_Throws()
    {
        // 10 characters × 3 UTF-8 bytes = 30 bytes → rejected, proving byte-based measurement.
        var key = new string('ẩ', 10);
        var services = new ServiceCollection();

        var ex = Assert.Throws<InvalidOperationException>(() => services.AddAppAuthentication(AuthConfig(key)));

        Assert.Contains("30 bytes", ex.Message);
    }

    // ==================== Auth:BootstrapAdminPassword ====================

    [Fact]
    public void Bootstrap_BlankDb_11Chars_Throws()
    {
        using var db = NewDb();

        var ex = Assert.Throws<BootstrapAdminConfigurationException>(
            () => BootstrapAdminSeeder.Ensure(db, BootstrapConfig("12345678901"), NullLogger.Instance));

        Assert.Contains("12 characters", ex.Message);
        Assert.Empty(db.Users);
    }

    [Fact]
    public void Bootstrap_BlankDb_PasswordEqualsUsername_Throws()
    {
        using var db = NewDb();

        var ex = Assert.Throws<BootstrapAdminConfigurationException>(
            () => BootstrapAdminSeeder.Ensure(
                db, BootstrapConfig("Administrator", username: "administrator"), NullLogger.Instance));

        Assert.Contains("INITIAL_ADMIN_USERNAME", ex.Message);
        Assert.Empty(db.Users);
    }

    [Fact]
    public void Bootstrap_BlankDb_12Chars_CreatesAdmin_ForcedPasswordChange()
    {
        using var db = NewDb();

        BootstrapAdminSeeder.Ensure(db, BootstrapConfig("StrongPass12x"), NullLogger.Instance);

        var admin = Assert.Single(db.Users);
        Assert.Equal("admin", admin.Username);
        Assert.True(admin.IsSuperUser);
        Assert.True(admin.IsActive);
        Assert.True(admin.MustChangePassword);
        Assert.NotNull(admin.PasswordHash);
        Assert.NotEqual("StrongPass12x", admin.PasswordHash);
    }

    [Fact]
    public void Bootstrap_ExistingAdmin_WeakPassword_IsNotValidated()
    {
        using var db = NewDb();
        db.Users.Add(new User
        {
            Username = "admin",
            Email = "admin@localhost",
            PasswordHash = "existing-hash",
            IsActive = true
        });
        db.SaveChanges();

        BootstrapAdminSeeder.Ensure(db, BootstrapConfig("short"), NullLogger.Instance);

        var admin = Assert.Single(db.Users);
        Assert.Equal("existing-hash", admin.PasswordHash);
    }

    [Fact]
    public void Bootstrap_ExistingAdminWithoutHash_WeakPassword_SeedsWithoutValidation()
    {
        using var db = NewDb();
        db.Users.Add(new User { Username = "admin", Email = "admin@localhost", IsActive = true });
        db.SaveChanges();

        BootstrapAdminSeeder.Ensure(db, BootstrapConfig("short"), NullLogger.Instance);

        var admin = Assert.Single(db.Users);
        Assert.NotNull(admin.PasswordHash);
        Assert.NotEqual("short", admin.PasswordHash);
    }
}
