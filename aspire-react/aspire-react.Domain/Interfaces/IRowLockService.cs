namespace aspire_react.Server.Domain.Interfaces;

/// <summary>
/// [FIX-J 2026-10-02 — N13 + N19] Row-level locks (PostgreSQL <c>SELECT ... FOR UPDATE</c>) that must
/// be taken INSIDE the caller's transaction (the lock lives until that transaction ends).
///
/// WHY the abstraction: the raw SQL and its provider-specific parameter typing used to sit directly
/// in Application handlers (<c>CreateCampaignCommand</c> referenced <c>Npgsql.NpgsqlParameter</c>,
/// which drags a provider dependency into the Application layer — audit N13). Moving it here keeps
/// Application provider-agnostic while the concrete SQL stays in Infrastructure.
///
/// CONTRACT: implementations MUST be a no-op on non-relational providers (EF InMemory in unit
/// tests), matching the convention already documented for the checkout/checkin handlers. Callers
/// still wrap the work in <c>Database.CreateExecutionStrategy()</c> + <c>BeginTransactionAsync</c>
/// themselves (Task O/O-FIX pattern).
/// </summary>
public interface IRowLockService
{
    /// <summary>Locks the <c>system_infos</c> row — serializes concurrent maintenance-campaign creation.</summary>
    Task LockSystemInfoRowAsync(Guid systemInfoId, CancellationToken cancellationToken = default);

    /// <summary>Locks the <c>user_credentials</c> row identified by token hash — serializes refresh rotation.</summary>
    Task LockUserCredentialRowAsync(string tokenHash, CancellationToken cancellationToken = default);
}
