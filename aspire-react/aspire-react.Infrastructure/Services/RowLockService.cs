using aspire_react.Server.Domain.Interfaces;
using aspire_react.Server.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace aspire_react.Server.Infrastructure.Services;

/// <summary>
/// [FIX-J 2026-10-02 — N13 + N19] PostgreSQL row locks. See <see cref="IRowLockService"/> for the
/// contract; the SQL/parameter typing lives here (Infrastructure) so Application stays
/// provider-agnostic.
/// </summary>
public class RowLockService : IRowLockService
{
    private readonly AppDbContext _context;

    public RowLockService(AppDbContext context)
    {
        _context = context;
    }

    public async Task LockSystemInfoRowAsync(Guid systemInfoId, CancellationToken cancellationToken = default)
    {
        // InMemory (unit tests) cannot run raw SQL — verbatim convention of the original call site.
        if (!_context.Database.IsRelational()) return;

        // Explicit typed parameter: raw SQL with an interpolated Guid cannot infer its type.
        var sysParam = new NpgsqlParameter("sysId", NpgsqlDbType.Uuid) { Value = systemInfoId };
        await _context.SystemInfos
            .FromSqlRaw(@"SELECT * FROM public.""system_infos"" WHERE ""Id"" = @sysId FOR UPDATE", sysParam)
            .FirstOrDefaultAsync(cancellationToken);
        // Drop the FOR UPDATE snapshot — the tracked system state must stay what the pre-read loaded
        // (verbatim from CreateCampaignCommand, where the campaign graph is built before the lock).
        _context.ChangeTracker.Clear();
    }

    public async Task LockUserCredentialRowAsync(string tokenHash, CancellationToken cancellationToken = default)
    {
        if (!_context.Database.IsRelational()) return;

        var hashParam = new NpgsqlParameter("hash", NpgsqlDbType.Text) { Value = tokenHash };
        await _context.UserCredentials
            .FromSqlRaw(@"SELECT * FROM public.""user_credentials"" WHERE ""TokenHash"" = @hash FOR UPDATE", hashParam)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
