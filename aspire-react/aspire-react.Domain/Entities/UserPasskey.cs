namespace aspire_react.Server.Domain.Entities;

/// <summary>
/// [AUTH Phase 1] One registered WebAuthn passkey of a user (optional secondary login method,
/// gated by the SystemSetting flag auth.passkeys.enabled). Follows the WebAuthn credential model:
/// the credentialId is the browser-stored identifier, the public key verifies assertions, and
/// SignCount detects cloned authenticators.
/// </summary>
public class UserPasskey
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }

    /// <summary>Owning user (no reverse collection on User — passkeys are queried by UserId directly).</summary>
    public User? User { get; set; }

    /// <summary>WebAuthn credential ID (base64url). Unique per authenticator registration.</summary>
    public string CredentialId { get; set; } = string.Empty;

    /// <summary>COSE public key (base64) used to verify login assertions.</summary>
    public string PublicKey { get; set; } = string.Empty;

    /// <summary>WebAuthn signature counter — monotonic per credential; a decrease signals a cloned token.</summary>
    public uint SignCount { get; set; }

    /// <summary>Authenticator Attachment GUID (AAGUID) — identifies the authenticator model, may be empty.</summary>
    public string? Aaguid { get; set; }

    /// <summary>User-friendly label ("MacBook Touch ID", "YubiKey 5C"...).</summary>
    public string? Name { get; set; }

    /// <summary>Allowed transports ("internal", "hybrid", "usb", "nfc", "ble") — hint for the browser call.</summary>
    public string? Transports { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? LastUsedAt { get; set; }
}
