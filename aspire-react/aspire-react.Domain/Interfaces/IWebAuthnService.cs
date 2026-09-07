namespace aspire_react.Server.Domain.Interfaces;

/// <summary>
/// [AUTH Phase 3] WebAuthn/passkey ceremonies contract (§11.1 mapping): ceremony orchestration
/// lives behind this thin Domain contract; ALL framework-heavy fido2-net-lib work (CBOR parsing,
/// attestation/assertion verification, challenge encoding) is in
/// Infrastructure/Authentication/WebAuthn/Fido2Service. Application handlers call this contract
/// only. Challenge state between the two ceremony halves is an Infrastructure concern (cache).
/// </summary>
public interface IWebAuthnService
{
    /// <summary>SystemSetting key gating every passkey feature (default: disabled).</summary>
    public const string PasskeysEnabledSettingKey = "auth.passkeys.enabled";

    /// <summary>Current value of the auth.passkeys.enabled flag (SystemSetting; default false).</summary>
    Task<bool> IsEnabledAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Registration ceremony — first half. Builds PublicKeyCredentialCreationOptions for the user
    /// (existing passkeys excluded) and returns the options as raw JSON for the browser
    /// navigator.credentials.create() call. Challenge state cached internally (short TTL).
    /// </summary>
    Task<string> BeginRegistrationAsync(Domain.Entities.User user, CancellationToken cancellationToken = default);

    /// <summary>
    /// Registration ceremony — second half. Verifies the attestation response against the cached
    /// options and persists a new UserPasskey row. Throws on verification failure or duplicate
    /// credential (PASSKEY_ALREADY_REGISTERED).
    /// </summary>
    Task<Domain.Entities.UserPasskey> CompleteRegistrationAsync(
        Guid userId, string attestationResponseJson, string? friendlyName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Assertion (login) ceremony — first half. <paramref name="usernameLower"/> null/empty →
    /// discoverable-credential login (no allowCredentials — the authenticator picks the passkey).
    /// Returns AssertionOptions as raw JSON for navigator.credentials.get().
    /// </summary>
    Task<string> BeginAssertionAsync(string? usernameLower, CancellationToken cancellationToken = default);

    /// <summary>
    /// Assertion ceremony — second half. Verifies the assertion (signature, sign-counter clone
    /// detection, user-handle ownership), updates the stored counter/LastUsedAt and returns the
    /// authenticated user. Throws on any verification failure (caller maps to INVALID_CREDENTIALS).
    /// </summary>
    Task<PasskeyLoginIdentity> CompleteAssertionAsync(string assertionResponseJson, CancellationToken cancellationToken = default);
}

/// <summary>Identity resolved by a successful passkey assertion.</summary>
public record PasskeyLoginIdentity(Domain.Entities.User User, Domain.Entities.UserPasskey Passkey);

/// <summary>Passkey projection for API responses (never exposes the public key bytes).</summary>
public record PasskeyDto(Guid Id, string Name, DateTime CreatedAt, DateTime? LastUsedAt);
