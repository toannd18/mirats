using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Interfaces;
using aspire_react.Server.Infrastructure.Authentication.WebAuthn;
using aspire_react.Server.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Xunit;

namespace aspire_react.Tests;

/// <summary>
/// [AUTH Phase 3] Ceremony unit tests with a software authenticator (TestAuthenticator) —
/// registration persists a passkey, assertion verifies + advances the counter, clone detection
/// rejects stale counters, user-handle ownership is enforced, flag gating works at command level.
/// </summary>
public class WebAuthnCeremonyTests
{
    private static (Fido2Service svc, AppDbContext db, IMemoryCache cache) NewService()
    {
        var db = TestHelpers.CreateContext($"wa-{Guid.NewGuid():N}");
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Auth:WebAuthn:RPId"] = "localhost",
                ["Auth:WebAuthn:Origins"] = "https://localhost:5173"
            })
            .Build();
        var svc = new Fido2Service(config, new MemoryCache(Options.Create(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions())), db);
        return (svc, db, null!);
    }

    private static User NewUser() => new()
    {
        Username = "wa-user",
        Email = "wa-user@test.local",
        FirstName = "Web",
        LastName = "Authn",
        IsActive = true
    };

    [Fact]
    public async Task Register_Ceremony_Persists_Passkey()
    {
        var (svc, db, _) = NewService();
        var user = NewUser();
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var optionsJson = await svc.BeginRegistrationAsync(user);
        var options = TestAuthenticator.ParseOptions(optionsJson);
        var cred = TestAuthenticator.CreateCredential();

        var passkey = await svc.CompleteRegistrationAsync(user.Id, TestAuthenticator.AttestationResponse(options, cred), "YubiKey test");

        Assert.Equal(TestAuthenticator.ToB64Url(cred.CredentialId), passkey.CredentialId);
        Assert.Equal("YubiKey test", passkey.Name);
        var row = await db.UserPasskeys.AsNoTracking().SingleAsync(p => p.Id == passkey.Id);
        Assert.Equal(0u, row.SignCount);
        Assert.False(string.IsNullOrEmpty(row.PublicKey));
    }

    [Fact]
    public async Task Register_DuplicateCredentialId_Rejected()
    {
        var (svc, db, _) = NewService();
        var user = NewUser();
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var optionsJson = await svc.BeginRegistrationAsync(user);
        var cred = TestAuthenticator.CreateCredential();
        await svc.CompleteRegistrationAsync(user.Id, TestAuthenticator.AttestationResponse(TestAuthenticator.ParseOptions(optionsJson), cred), "first");

        // Same credentialId again (fresh options ceremony) → uniqueness callback rejects
        // (translated to the layer-neutral PASSKEY_VERIFICATION_FAILED code).
        var options2 = await svc.BeginRegistrationAsync(user);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.CompleteRegistrationAsync(user.Id, TestAuthenticator.AttestationResponse(TestAuthenticator.ParseOptions(options2), cred), "second"));
    }

    [Fact]
    public async Task Assertion_Ceremony_Verifies_And_Advances_Counter()
    {
        var (svc, db, _) = NewService();
        var user = NewUser();
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var cred = TestAuthenticator.CreateCredential();
        var regOptions = TestAuthenticator.ParseOptions(await svc.BeginRegistrationAsync(user));
        await svc.CompleteRegistrationAsync(user.Id, TestAuthenticator.AttestationResponse(regOptions, cred), "key1");

        var assertOptions = TestAuthenticator.ParseOptions(await svc.BeginAssertionAsync(user.Username.ToLower()));
        var identity = await svc.CompleteAssertionAsync(TestAuthenticator.AssertionResponse(assertOptions, cred, user.Id.ToByteArray()));

        Assert.Equal(user.Id, identity.User.Id);
        var row = await db.UserPasskeys.AsNoTracking().SingleAsync(p => p.Id == identity.Passkey.Id);
        Assert.Equal(1u, row.SignCount);
        Assert.NotNull(row.LastUsedAt);
    }

    [Fact]
    public async Task Assertion_CloneDetection_StaleCounter_Rejected()
    {
        var (svc, db, _) = NewService();
        var user = NewUser();
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var cred = TestAuthenticator.CreateCredential();
        await svc.CompleteRegistrationAsync(user.Id,
            TestAuthenticator.AttestationResponse(TestAuthenticator.ParseOptions(await svc.BeginRegistrationAsync(user)), cred), "key1");

        // First assertion: counter 1 — OK.
        var o1 = TestAuthenticator.ParseOptions(await svc.BeginAssertionAsync(user.Username.ToLower()));
        await svc.CompleteAssertionAsync(TestAuthenticator.AssertionResponse(o1, cred, user.Id.ToByteArray()));

        // Clone attack: signature valid but counter NOT advanced (still 1, stored = 1) → rejected.
        var o2 = TestAuthenticator.ParseOptions(await svc.BeginAssertionAsync(user.Username.ToLower()));
        var stale = TestAuthenticator.AssertionResponse(o2, cred, user.Id.ToByteArray(), forcedCount: 1);
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.CompleteAssertionAsync(stale));
    }

    [Fact]
    public async Task Assertion_WrongUserHandle_Rejected()
    {
        var (svc, db, _) = NewService();
        var user = NewUser();
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var cred = TestAuthenticator.CreateCredential();
        await svc.CompleteRegistrationAsync(user.Id,
            TestAuthenticator.AttestationResponse(TestAuthenticator.ParseOptions(await svc.BeginRegistrationAsync(user)), cred), "key1");

        // A different user handle claiming this credential → ownership callback fails.
        var otherHandle = Guid.NewGuid().ToByteArray();
        var options = TestAuthenticator.ParseOptions(await svc.BeginAssertionAsync(user.Username.ToLower()));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.CompleteAssertionAsync(TestAuthenticator.AssertionResponse(options, cred, otherHandle)));
    }

    [Fact]
    public async Task Assertion_Discoverable_NoUsername_WorksWithUserHandle()
    {
        var (svc, db, _) = NewService();
        var user = NewUser();
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var cred = TestAuthenticator.CreateCredential();
        await svc.CompleteRegistrationAsync(user.Id,
            TestAuthenticator.AttestationResponse(TestAuthenticator.ParseOptions(await svc.BeginRegistrationAsync(user)), cred), "key1");

        // Username-less login: no allowCredentials — identity resolved from userHandle alone.
        var options = TestAuthenticator.ParseOptions(await svc.BeginAssertionAsync(null));
        var identity = await svc.CompleteAssertionAsync(TestAuthenticator.AssertionResponse(options, cred, user.Id.ToByteArray()));
        Assert.Equal(user.Id, identity.User.Id);
    }

    [Fact]
    public async Task IsEnabled_DefaultFalse_SystemSettingTrue_Enables()
    {
        var (svc, db, _) = NewService();
        Assert.False(await svc.IsEnabledAsync()); // default

        db.SystemSettings.Add(new SystemSetting
        {
            Key = IWebAuthnService.PasskeysEnabledSettingKey,
            Value = "true",
            Description = "flag"
        });
        await db.SaveChangesAsync();
        Assert.True(await svc.IsEnabledAsync());
    }
}
