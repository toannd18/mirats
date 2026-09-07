using System.Text;
using System.Text.Json;
using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Domain.Interfaces;
using Fido2NetLib;
using Fido2NetLib.Objects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;

namespace aspire_react.Server.Infrastructure.Authentication.WebAuthn;

/// <summary>
/// [AUTH Phase 3] fido2-net-lib-backed IWebAuthnService (framework-heavy half of the §11.1
/// mapping). Ceremony challenges are cached in IMemoryCache with a short TTL — the two halves
/// of each ceremony MUST complete against the same instance (single-server dev/one-pod prod;
/// a sticky requirement already implied by in-process JWT signing config).
/// Config: Auth:WebAuthn:RPId (default "localhost"), Auth:WebAuthn:Origins (default the two
/// dev origins — the browser origin for WebAuthn is the FRONTEND origin, not the API's).
/// </summary>
public class Fido2Service : IWebAuthnService
{
    private const int ChallengeStateTtlMinutes = 5;
    private const string RegistrationCachePrefix = "wa-reg:";
    private const string AssertionCachePrefix = "wa-assert:";

    private readonly Fido2 _fido2;
    private readonly IMemoryCache _cache;
    private readonly IApplicationDbContext _context;

    public Fido2Service(IConfiguration configuration, IMemoryCache cache, IApplicationDbContext context)
    {
        _cache = cache;
        _context = context;

        var rpId = configuration["Auth:WebAuthn:RPId"] ?? "localhost";
        var originsRaw = configuration["Auth:WebAuthn:Origins"]
            ?? "https://localhost:5173,https://localhost:7314";
        var origins = originsRaw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet();

        _fido2 = new Fido2(new Fido2Configuration
        {
            ServerDomain = rpId,
            ServerName = "Mirats",
            Origins = origins,
            Timeout = 60_000,
            TimestampDriftTolerance = 60_000, // ms of clock drift tolerated between halves
            MDSCacheDirPath = Path.Combine(Path.GetTempPath(), "fido2-mds")
        });
    }

    private static string CredentialIdToB64Url(byte[] credentialId)
        => Convert.ToBase64String(credentialId).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] B64UrlToBytes(string b64Url)
        => Convert.FromBase64String(b64Url.Replace('-', '+').Replace('_', '/').PadRight(b64Url.Length + (4 - b64Url.Length % 4) % 4, '='));

    public async Task<bool> IsEnabledAsync(CancellationToken cancellationToken = default)
    {
        var setting = await _context.SystemSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Key == IWebAuthnService.PasskeysEnabledSettingKey, cancellationToken);
        return string.Equals(setting?.Value, "true", StringComparison.OrdinalIgnoreCase);
    }

    public Task<string> BeginRegistrationAsync(Domain.Entities.User user, CancellationToken cancellationToken = default)
    {
        var existing = _context.UserPasskeys.AsNoTracking()
            .Where(p => p.UserId == user.Id)
            .AsEnumerable()
            .Select(p => new PublicKeyCredentialDescriptor(B64UrlToBytes(p.CredentialId)))
            .ToList();

        var fidoUser = new Fido2User
        {
            Id = user.Id.ToByteArray(), // userHandle — stable local identity (never Keycloak sub)
            Name = user.Username,
            DisplayName = string.Join(' ', new[] { user.FirstName, user.LastName }.Where(s => !string.IsNullOrWhiteSpace(s)))
                is { Length: > 0 } displayName ? displayName : user.Username
        };

        var options = _fido2.RequestNewCredential(new RequestNewCredentialParams
        {
            User = fidoUser,
            ExcludeCredentials = existing,
            // Discoverable (resident) credential + required UV → supports username-less login later.
            AuthenticatorSelection = new AuthenticatorSelection
            {
                UserVerification = UserVerificationRequirement.Required,
                ResidentKey = ResidentKeyRequirement.Required
            },
            AttestationPreference = AttestationConveyancePreference.None // no attestation chain verification needed (Phase 3 scope)
        });

        _cache.Set(RegistrationCachePrefix + user.Id, options, TimeSpan.FromMinutes(ChallengeStateTtlMinutes));
        return Task.FromResult(options.ToJson());
    }

    public async Task<Domain.Entities.UserPasskey> CompleteRegistrationAsync(
        Guid userId, string attestationResponseJson, string? friendlyName, CancellationToken cancellationToken = default)
    {
        if (!_cache.TryGetValue(RegistrationCachePrefix + userId, out CredentialCreateOptions? originalOptions) || originalOptions == null)
            throw new InvalidOperationException("PASSKEY_REGISTRATION_EXPIRED");

        var raw = JsonSerializer.Deserialize<AuthenticatorAttestationRawResponse>(
            attestationResponseJson,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("PASSKEY_MALFORMED_RESPONSE");

        // Translate fido2 exceptions into layer-neutral codes — Application must not reference
        // the fido2 package (dependency direction §11.1); it catches InvalidOperationException.
        RegisteredPublicKeyCredential credential;
        try
        {
            credential = await _fido2.MakeNewCredentialAsync(new MakeNewCredentialParams
            {
                AttestationResponse = raw,
                OriginalOptions = originalOptions,
                // Uniqueness: the same credentialId must never be registered twice (any user).
                IsCredentialIdUniqueToUserCallback = async (args, ct) =>
                    !await _context.UserPasskeys.AsNoTracking()
                        .AnyAsync(p => p.CredentialId == CredentialIdToB64Url(args.CredentialId), ct)
            });
        }
        catch (Fido2VerificationException)
        {
            // Attestation/signature/format verification failure — no internals leaked.
            throw new InvalidOperationException("PASSKEY_VERIFICATION_FAILED");
        }

        var passkey = new Domain.Entities.UserPasskey
        {
            UserId = userId,
            CredentialId = CredentialIdToB64Url(credential.Id),
            PublicKey = Convert.ToBase64String(credential.PublicKey),
            SignCount = credential.SignCount,
            Aaguid = credential.AaGuid.ToString(),
            Name = string.IsNullOrWhiteSpace(friendlyName) ? "Passkey" : friendlyName.Trim(),
            CreatedAt = DateTime.UtcNow
        };

        _context.UserPasskeys.Add(passkey);
        _cache.Remove(RegistrationCachePrefix + userId);
        await _context.SaveChangesAsync(cancellationToken);
        return passkey;
    }

    public Task<string> BeginAssertionAsync(string? usernameLower, CancellationToken cancellationToken = default)
    {
        List<PublicKeyCredentialDescriptor> allowedCredentials = [];

        if (!string.IsNullOrWhiteSpace(usernameLower))
        {
            var userExists = _context.Users.AsNoTracking().Any(u => u.Username.ToLower() == usernameLower);
            if (userExists)
            {
                allowedCredentials = _context.UserPasskeys.AsNoTracking()
                    .Where(p => p.UserId == _context.Users.AsNoTracking()
                        .Where(u => u.Username.ToLower() == usernameLower)
                        .Select(u => u.Id)
                        .FirstOrDefault())
                    .AsEnumerable()
                    .Select(p => new PublicKeyCredentialDescriptor(B64UrlToBytes(p.CredentialId)))
                    .ToList();
            }
            // Unknown username → EMPTY allowlist with a VALID challenge (same generic failure
            // semantics as password login: no user enumeration, ceremony still completes).
        }

        var options = _fido2.GetAssertionOptions(new GetAssertionOptionsParams
        {
            AllowedCredentials = allowedCredentials,
            UserVerification = UserVerificationRequirement.Required
        });

        var challengeB64 = CredentialIdToB64Url(options.Challenge);
        _cache.Set(AssertionCachePrefix + challengeB64, options, TimeSpan.FromMinutes(ChallengeStateTtlMinutes));
        return Task.FromResult(options.ToJson());
    }

    public async Task<PasskeyLoginIdentity> CompleteAssertionAsync(string assertionResponseJson, CancellationToken cancellationToken = default)
    {
        var raw = JsonSerializer.Deserialize<AuthenticatorAssertionRawResponse>(
            assertionResponseJson,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("PASSKEY_MALFORMED_RESPONSE");

        // Challenge → cached options. clientDataJSON arrives as base64url bytes on the raw model.
        var clientDataJson = Encoding.UTF8.GetString(raw.Response.ClientDataJson);
        using var clientData = JsonDocument.Parse(clientDataJson);
        var challengeB64 = clientData.RootElement.GetProperty("challenge").GetString()
            ?? throw new InvalidOperationException("PASSKEY_MALFORMED_RESPONSE");

        if (!_cache.TryGetValue(AssertionCachePrefix + challengeB64, out AssertionOptions? originalOptions) || originalOptions == null)
            throw new InvalidOperationException("PASSKEY_ASSERTION_EXPIRED");

        // raw.Id is the credential id as base64url (browser JSON id == rawId).
        var credentialIdB64 = string.IsNullOrEmpty(raw.Id) ? string.Empty : raw.Id;
        var stored = await _context.UserPasskeys
            .FirstOrDefaultAsync(p => p.CredentialId == credentialIdB64, cancellationToken)
            ?? throw new InvalidOperationException("INVALID_CREDENTIALS");

        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.Id == stored.UserId && u.IsActive, cancellationToken)
            ?? throw new InvalidOperationException("INVALID_CREDENTIALS");

        VerifyAssertionResult result;
        try
        {
            result = await _fido2.MakeAssertionAsync(new MakeAssertionParams
            {
                AssertionResponse = raw,
                OriginalOptions = originalOptions,
                StoredPublicKey = Convert.FromBase64String(stored.PublicKey),
                StoredSignatureCounter = stored.SignCount,
                // userHandle must own this credential (Guid bytes equality) — prevents a passkey
                // from asserting for a different user handle than it was registered under.
                IsUserHandleOwnerOfCredentialIdCallback = (args, ct) =>
                    Task.FromResult(new Guid(args.UserHandle) == stored.UserId)
            });
        }
        catch (Fido2VerificationException)
        {
            // Signature failure / clone detection (counter did not advance) / wrong challenge —
            // one generic code, same no-enumeration semantics as password login.
            throw new InvalidOperationException("INVALID_CREDENTIALS");
        }

        // Sign-counter clone detection is performed by the library (StoredSignatureCounter);
        // persist the new counter + last-used timestamp on success.
        stored.SignCount = result.SignCount;
        stored.LastUsedAt = DateTime.UtcNow;

        _cache.Remove(AssertionCachePrefix + challengeB64);
        await _context.SaveChangesAsync(cancellationToken);

        return new PasskeyLoginIdentity(user, stored);
    }
}
