using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.Consumables.Commands;

/// <summary>
/// [Giai đoạn 3 — Nặng] POST /api/v1/consumables/{id}/checkout (extracted from
/// ComponentsController-analog ConsumablesController.Checkout). Transaction boundary moved
/// VERBATIM from RunTransactional: strategy.ExecuteAsync → BeginTransaction → CheckoutAsync →
/// !Success → Rollback (controller maps 400) / Commit. The service's checkout writes the
/// ActionLog via the same SaveChanges (atomic inside this boundary).
/// </summary>
public record CheckoutConsumableCommand(Guid ConsumableId, Guid? UserId, int Quantity, string? Note, Guid CurrentUserId)
    : IRequest<Domain.Interfaces.ConsumableCheckoutResult>;

public class CheckoutConsumableCommandHandler : IRequestHandler<CheckoutConsumableCommand, Domain.Interfaces.ConsumableCheckoutResult>
{
    private readonly IApplicationDbContext _context;
    private readonly IConsumableAllocationService _allocationService;

    public CheckoutConsumableCommandHandler(IApplicationDbContext context, IConsumableAllocationService allocationService)
    {
        _context = context;
        _allocationService = allocationService;
    }

    public async Task<Domain.Interfaces.ConsumableCheckoutResult> Handle(CheckoutConsumableCommand request, CancellationToken cancellationToken)
    {
        var strategy = _context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync<Domain.Interfaces.ConsumableCheckoutResult>(async () =>
        {
            await using var tx = await _context.Database.BeginTransactionAsync(cancellationToken);
            var result = await _allocationService.CheckoutAsync(
                request.ConsumableId, request.UserId, request.Quantity, request.Note, request.CurrentUserId, cancellationToken);
            if (!result.Success)
            {
                await tx.RollbackAsync(cancellationToken);
                return result;
            }
            await tx.CommitAsync(cancellationToken);
            return result;
        });
    }
}
