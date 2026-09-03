using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.Components.Commands;

/// <summary>
/// [Giai đoạn 3 — Nặng] Transaction-delegating commands — each handler owns the Npgsql execution
/// strategy + explicit transaction boundary moved VERBATIM from ComponentsController.RunTransactional
/// (strategy.ExecuteAsync → BeginTransaction → service call → Success? Commit : Rollback+unsuccessful
/// result). The allocation service relies on this boundary (its FOR UPDATE lock + SaveChanges commit
/// happen inside the caller's transaction).
/// </summary>
public record AssignComponentCommand(Guid ComponentId, Guid AssetId, int AssignedQty, string? Note, Guid CurrentUserId)
    : IRequest<ComponentOperationResult>;

public class AssignComponentCommandHandler : TransactionalComponentOperationHandler<AssignComponentCommand>
{
    public AssignComponentCommandHandler(IApplicationDbContext context, IComponentAllocationService allocationService)
        : base(context, (request, ct) => allocationService.AllocateAsync(request.ComponentId, request.AssetId, request.AssignedQty, null, request.Note, request.CurrentUserId, ct))
    {
    }
}

public record StockInUnitsCommand(Guid ComponentId, List<string> SerialNumbers, string? Note, Guid CurrentUserId)
    : IRequest<ComponentOperationResult>;

public class StockInUnitsCommandHandler : TransactionalComponentOperationHandler<StockInUnitsCommand>
{
    public StockInUnitsCommandHandler(IApplicationDbContext context, IComponentAllocationService allocationService)
        : base(context, (request, ct) => allocationService.StockInAsync(request.ComponentId, request.SerialNumbers, request.Note, request.CurrentUserId, ct))
    {
    }
}

public record CheckoutComponentCommand(Guid ComponentId, Guid AssetId, int Quantity, string? SerialNo, string? Note, Guid CurrentUserId)
    : IRequest<ComponentOperationResult>;

public class CheckoutComponentCommandHandler : TransactionalComponentOperationHandler<CheckoutComponentCommand>
{
    public CheckoutComponentCommandHandler(IApplicationDbContext context, IComponentAllocationService allocationService)
        : base(context, (request, ct) => allocationService.AllocateAsync(request.ComponentId, request.AssetId, request.Quantity, request.SerialNo, request.Note, request.CurrentUserId, ct))
    {
    }
}

public record CheckinComponentCommand(Guid ComponentId, Guid? AssetId, int Quantity, string? SerialNo, string? Note, Guid CurrentUserId)
    : IRequest<ComponentOperationResult>;

public class CheckinComponentCommandHandler : TransactionalComponentOperationHandler<CheckinComponentCommand>
{
    public CheckinComponentCommandHandler(IApplicationDbContext context, IComponentAllocationService allocationService)
        : base(context, (request, ct) => allocationService.ReturnAsync(request.ComponentId, request.AssetId, request.Quantity, request.SerialNo, request.Note, request.CurrentUserId, ct))
    {
    }
}

/// <summary>
/// Shared base implementing the RunTransactional pattern verbatim: strategy.ExecuteAsync →
/// BeginTransaction → operation → !Success → Rollback + (unsuccessful result) / Commit.
/// Failure is returned as an UNSUCCESSFUL ComponentOperationResult (not an exception) so the
/// controller maps it exactly like the pre-migration switch did.
/// </summary>
public abstract class TransactionalComponentOperationHandler<TRequest> : IRequestHandler<TRequest, ComponentOperationResult>
    where TRequest : IRequest<ComponentOperationResult>
{
    private readonly IApplicationDbContext _context;
    private readonly Func<TRequest, CancellationToken, Task<ComponentOperationResult>> _operation;

    protected TransactionalComponentOperationHandler(
        IApplicationDbContext context,
        Func<TRequest, CancellationToken, Task<ComponentOperationResult>> operation)
    {
        _context = context;
        _operation = operation;
    }

    public async Task<ComponentOperationResult> Handle(TRequest request, CancellationToken cancellationToken)
    {
        // Npgsql's retrying execution strategy requires the transaction to run inside CreateExecutionStrategy
        // (moved verbatim from ComponentsController.RunTransactional — the boundary the allocation
        // service's FOR UPDATE lock + SaveChanges commit rely on).
        var strategy = _context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync<ComponentOperationResult>(async () =>
        {
            await using var tx = await _context.Database.BeginTransactionAsync(cancellationToken);
            var result = await _operation(request, CancellationToken.None);
            if (!result.Success)
            {
                await tx.RollbackAsync(cancellationToken);
                return result; // un-successful result → controller maps 400 + error_code
            }
            await tx.CommitAsync(cancellationToken);
            return result;
        });
    }
}
