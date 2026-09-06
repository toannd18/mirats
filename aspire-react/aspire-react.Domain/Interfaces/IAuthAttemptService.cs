namespace aspire_react.Server.Domain.Interfaces;

/// <summary>
/// [AUTH Phase 1] Brute-force defense for the login endpoint (AUTH_MIGRATION_PLAYBOOK §4.5) —
/// escalating per-username lockout over a rolling window plus a per-IP burst rate limit. Both
/// counters read/write the auth_login_attempts table (per-IP uses an in-memory cache). The login
/// endpoint MUST NOT go live before this defense is active.
/// </summary>
public interface IAuthAttemptService
{
    /// <summary>Number of consecutive FAILED attempts for the (lowercased) username within the
    /// 15-minute window, after the most recent success.</summary>
    Task<int> GetConsecutiveFailuresAsync(string usernameLower, CancellationToken cancellationToken = default);

    /// <summary>Timestamp (UTC) of the most recent FAILED attempt for the username within the
    /// window (null = no failure in window) — anchor for the escalating lockout countdown.</summary>
    Task<DateTime?> GetLastFailureAtAsync(string usernameLower, CancellationToken cancellationToken = default);

    /// <summary>Lockout seconds for the current failure count: 1–4 → 0 (no lock); 5 → 60s;
    /// 6–9 → 2^attempt seconds capped at 900s (15 minutes).</summary>
    int LockoutSeconds(int consecutiveFailures);

    /// <summary>Records one attempt (success or failure). Success resets the username counter.</summary>
    Task RecordAttemptAsync(string usernameLower, string ipAddress, bool success, CancellationToken cancellationToken = default);

    /// <summary>True when the IP exceeded the burst limit (> 30 failed attempts / minute) —
    /// the login endpoint answers 429 and the caller must wait out the 5-minute window.</summary>
    Task<bool> IsIpBlockedAsync(string ipAddress, CancellationToken cancellationToken = default);

    /// <summary>Deletes attempts older than 30 days. Called probabilistically (~10%) on login —
    /// no background service required.</summary>
    Task CleanupOldAsync(CancellationToken cancellationToken = default);
}
