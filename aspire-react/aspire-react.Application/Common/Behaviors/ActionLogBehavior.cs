using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.Common.Behaviors;

/// <summary>
/// [Giai đoạn 0.2 — M1] Pipeline behavior that persists the ActionLog for commands implementing
/// <see cref="ILoggableCommand{TResponse}"/> — same convention as <see cref="ValidationBehavior{TRequest,TResponse}"/>
/// (registered via AddOpenBehavior right after it). Commands NOT implementing the marker pass
/// through untouched; their handlers keep their manual logging.
/// </summary>
/// <para>
/// Pipeline order (registration order = pre-phase order): ValidationBehavior (outer) validates and
/// short-circuits invalid requests BEFORE this behavior runs; then this behavior opens its
/// transaction and invokes the handler; the log entry is built and persisted only AFTER the handler
/// returned successfully — if the handler throws, the log is never built and the ambient
/// transaction rolls back whatever the handler already wrote.
/// </para>
/// <para>
/// Transaction invariant (workflow doc §3.2 — "log persisted in the SAME transaction as the data
/// change"): the behavior opens ONE ambient transaction around the handler call. The handler's
/// internal <c>SaveChangesAsync</c> joins that transaction without committing it; the behavior then
/// stages the log (via the enriched <c>IActionLogService.LogAction</c> — identical enrichment to
/// the manual path: RemoteIp/UserAgent/ActionSource/LocationName/SystemInfo snapshot) and saves it
/// INSIDE the same transaction before committing once. Net effect for a simple command: data + log
/// commit atomically, exactly like the old single-SaveChanges manual pattern. For EF InMemory
/// (unit tests) Begin/Commit are no-ops, so tests are unaffected.
/// </para>
/// <para>
/// Limitations (deliberate, keep manual logging for these): (1) commands managing their own
/// transaction (checkout/checkin FOR UPDATE patterns) must not implement the marker — nested
/// BeginTransaction would throw; (2) log entries needing the ActionDate override are not supported
/// by the enriched LogAction path — those call sites keep the manual <c>Log(entry)</c> form.
/// </para>
/// </summary>
public sealed class ActionLogBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly IActionLogService _actionLogService;
    private readonly IApplicationDbContext _context;

    public ActionLogBehavior(IActionLogService actionLogService, IApplicationDbContext context)
    {
        _actionLogService = actionLogService;
        _context = context;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        // Opt-in: non-marked requests are 100% pass-through (zero behavioral impact).
        if (request is not ILoggableCommand<TResponse> loggable)
            return await next();

        TResponse? response = default;

        var strategy = _context.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _context.Database.BeginTransactionAsync(cancellationToken);

            // Handler runs INSIDE the ambient transaction; its SaveChanges does not commit here.
            response = await next();

            // Reached ONLY when the handler returned successfully (exceptions propagate and
            // roll back the transaction — nothing is logged on failure).
            var entry = loggable.BuildLogEntry(response!);
            if (entry != null)
            {
                // Enriched staging — same service path the manual call sites use.
                _actionLogService.LogAction(
                    itemType: entry.ItemType,
                    itemId: entry.ItemId,
                    actionType: entry.ActionType,
                    loggedByUserId: entry.CreatedBy,
                    targetType: entry.TargetType,
                    targetId: entry.TargetId,
                    note: entry.Note,
                    logMeta: entry.LogMeta,
                    locationId: entry.LocationId,
                    companyId: entry.CompanyId,
                    fileName: entry.FileName);

                await _context.SaveChangesAsync(cancellationToken);
            }

            await tx.CommitAsync(cancellationToken);
        });

        return response!;
    }
}
