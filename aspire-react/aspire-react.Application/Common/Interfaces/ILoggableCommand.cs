using aspire_react.Server.Domain.Entities;

namespace aspire_react.Server.Application.Common.Interfaces;

/// <summary>
/// [Giai đoạn 0.2 — M1] Opt-in marker for commands whose ActionLog is persisted automatically by
/// <see cref="Common.Behaviors.ActionLogBehavior{TRequest,TResponse}"/> instead of a manual
/// <c>_actionLogService.LogAction(...)</c> call inside the handler.
/// </para>
/// <para>
/// Placement rationale (4-project structure, Giai đoạn 0.1): this is an APPLICATION-pipeline
/// contract — implemented by commands and consumed by a pipeline behavior, both in the Application
/// project — so it lives here, NOT in Domain (Domain must stay pure; and the generic response-type
/// parameter is an orchestration concern). Domain types (ItemType/ActionType/ActionLogEntry) are
/// referenced from it, which keeps the dependency direction legal (Application → Domain).
/// </para>
/// <para>
/// Design note (single source of truth): ItemType/ActionType/ItemId/CreatedBy/CompanyId are NOT
/// declared as separate marker properties because <see cref="ActionLogEntry"/> already declares
/// them <c>required</c> — the compiler enforces their presence inside <see cref="BuildLogEntry"/>.
/// Duplicating them as interface properties would create two places that can disagree.
/// </para>
/// <para>
/// OPT-IN ONLY: commands that do not implement this interface pass through the behavior untouched
/// (manual logging inside the handler keeps working). Commands that manage their OWN transaction
/// (checkout/checkin — the FOR UPDATE patterns) must NOT implement this interface: the behavior
/// wraps the handler in an ambient transaction, and nested BeginTransaction would throw. Those
/// handlers keep their manual, transaction-aware logging.
/// </para>
/// </summary>
/// <typeparam name="TResponse">The handler's response type (same as the command's IRequest&lt;TResponse&gt;).</typeparam>
public interface ILoggableCommand<TResponse>
{
    /// <summary>
    /// Builds the log entry AFTER the handler returned successfully. Returned entries are enriched
    /// (RemoteIp/UserAgent/ActionSource/LocationName/SystemInfo snapshot — identical to the manual
    /// LogAction path) and persisted by the behavior inside the SAME transaction as the handler's
    /// writes. Return <c>null</c> to skip logging for soft-fail responses (e.g. COMPANY_MISMATCH),
    /// mirroring the old early-return-before-LogAction behavior. If the handler throws, this method
    /// is never invoked and nothing is logged.
    /// </summary>
    ActionLogEntry? BuildLogEntry(TResponse response);
}
