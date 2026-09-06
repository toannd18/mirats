namespace aspire_react.Server.Domain.Entities;

/// <summary>
/// [AUTH Phase 1] One login attempt (success or failure) — dual purpose:
/// (1) brute-force defense input (consecutive failures per username drive the escalating lockout,
/// per-IP failure bursts drive the 429 rate limit), and (2) audit trail of sign-in activity.
/// Rows older than 30 days are opportunistically cleaned (probabilistic, on login).
/// </summary>
public class AuthLoginAttempt
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Normalized (lowercase) username — counters are case-insensitive.</summary>
    public string Username { get; set; } = string.Empty;

    public string IpAddress { get; set; } = string.Empty;

    public bool Success { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
