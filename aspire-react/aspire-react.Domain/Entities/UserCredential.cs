namespace aspire_react.Server.Domain.Entities;

/// <summary>
/// [AUTH Phase 1] Refresh-token credential row for one user session. Rotation lifecycle: each
/// refresh revokes the old row (RevokedAt + ReplacedById → new row) and issues a fresh one;
/// RE-USING an already-revoked token revokes ALL of the user's rows (stolen-token containment).
/// Only the SHA-256 hash of the raw token is stored — a DB leak never yields usable tokens.
/// </summary>
public class UserCredential
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }

    /// <summary>Owning user (no reverse collection on User — credentials are queried by UserId directly).</summary>
    public User? User { get; set; }

    /// <summary>SHA-256 of the raw refresh token (base64). The raw value is never stored.</summary>
    public string TokenHash { get; set; } = string.Empty;

    public DateTime ExpiresAt { get; set; }

    public DateTime? RevokedAt { get; set; }

    /// <summary>The row that replaced this one during rotation (chain link for auditing).</summary>
    public Guid? ReplacedById { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
