using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Infrastructure.Authentication;

/// <summary>
/// [AUTH Phase 1] Brute-force defense implementation (AUTH_MIGRATION_PLAYBOOK §4.5). Per-username
/// escalating lockout reads/writes auth_login_attempts (Postgres-backed — counters survive
/// restarts); the per-IP burst limit uses an in-memory sliding cache (cheap, process-local — a
/// restart resets it which is acceptable for a burst limiter).
/// </summary>
public class AuthAttemptService : IAuthAttemptService
{
    private const int WindowMinutes = 15;
    private const int LockoutThreshold = 5;
    private const int MaxLockoutSeconds = 900; // 15 minutes
    private const int IpBurstLimit = 30;       // failures / minute
    private const int CleanupOlderThanDays = 30;

    private readonly IApplicationDbContext _context;

    // Static so the limiter survives DI scope changes (per-request scoped service).
    private static readonly object IpCacheLock = new();
    private static readonly Dictionary<string, Queue<DateTime>> IpFailures = new();

    public AuthAttemptService(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<int> GetConsecutiveFailuresAsync(string usernameLower, CancellationToken cancellationToken = default)
    {
        var windowStart = DateTime.UtcNow.AddMinutes(-WindowMinutes);

        // Consecutive = failed attempts after the LAST success in the window (last-success wins).
        var lastSuccess = await _context.AuthLoginAttempts.AsNoTracking()
            .Where(a => a.Username == usernameLower && a.Success && a.CreatedAt >= windowStart)
            .OrderByDescending(a => a.CreatedAt)
            .Select(a => (DateTime?)a.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        var query = _context.AuthLoginAttempts.AsNoTracking()
            .Where(a => a.Username == usernameLower && !a.Success && a.CreatedAt >= windowStart);

        if (lastSuccess.HasValue)
            query = query.Where(a => a.CreatedAt > lastSuccess.Value);

        return await query.CountAsync(cancellationToken);
    }

    public async Task<DateTime?> GetLastFailureAtAsync(string usernameLower, CancellationToken cancellationToken = default)
    {
        var windowStart = DateTime.UtcNow.AddMinutes(-WindowMinutes);
        return await _context.AuthLoginAttempts.AsNoTracking()
            .Where(a => a.Username == usernameLower && !a.Success && a.CreatedAt >= windowStart)
            .OrderByDescending(a => a.CreatedAt)
            .Select(a => (DateTime?)a.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public int LockoutSeconds(int consecutiveFailures)
    {
        if (consecutiveFailures < LockoutThreshold) return 0;
        if (consecutiveFailures == LockoutThreshold) return 60; // 5th failure → 1 minute
        var seconds = (int)Math.Pow(2, consecutiveFailures);    // 6th → 64s, 7th → 128s ...
        return Math.Min(seconds, MaxLockoutSeconds);
    }

    public async Task RecordAttemptAsync(string usernameLower, string ipAddress, bool success, CancellationToken cancellationToken = default)
    {
        _context.AuthLoginAttempts.Add(new Domain.Entities.AuthLoginAttempt
        {
            Username = usernameLower,
            IpAddress = ipAddress ?? string.Empty,
            Success = success,
            CreatedAt = DateTime.UtcNow
        });

        // Failures also feed the in-memory per-IP burst limiter.
        // [FIX-N17] Guard the key: a missing/unparseable client IP used to be passed straight into
        // the dictionary (CS8604) — a null key throws ArgumentNullException at runtime.
        if (!success && !string.IsNullOrEmpty(ipAddress))
        {
            lock (IpCacheLock)
            {
                var queue = IpFailures.TryGetValue(ipAddress, out var q) ? q : IpFailures[ipAddress] = new Queue<DateTime>();
                queue.Enqueue(DateTime.UtcNow);
                // Trim entries older than 1 minute (the burst window).
                while (queue.Count > 0 && queue.Peek() < DateTime.UtcNow.AddMinutes(-1)) queue.Dequeue();
            }
        }

        await _context.SaveChangesAsync(cancellationToken);

        // Opportunistic cleanup (~10% of logins) — no background service required.
        if (Random.Shared.Next(0, 10) == 0)
        {
            await CleanupOldAsync(cancellationToken);
        }
    }

    public Task<bool> IsIpBlockedAsync(string ipAddress, CancellationToken cancellationToken = default)
    {
        lock (IpCacheLock)
        {
            if (!IpFailures.TryGetValue(ipAddress, out var queue)) return Task.FromResult(false);
            while (queue.Count > 0 && queue.Peek() < DateTime.UtcNow.AddMinutes(-1)) queue.Dequeue();
            return Task.FromResult(queue.Count > IpBurstLimit);
        }
    }

    public async Task CleanupOldAsync(CancellationToken cancellationToken = default)
    {
        // Load-then-remove (NOT ExecuteDeleteAsync — InMemory tests don't support it).
        var cutoff = DateTime.UtcNow.AddDays(-CleanupOlderThanDays);
        var old = await _context.AuthLoginAttempts
            .Where(a => a.CreatedAt < cutoff)
            .ToListAsync(cancellationToken);
        if (old.Count == 0) return;
        _context.AuthLoginAttempts.RemoveRange(old);
        await _context.SaveChangesAsync(cancellationToken);
    }
}
