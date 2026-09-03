using aspire_react.Server.Domain.Interfaces;
using MediatR;

namespace aspire_react.Server.Application.ComponentUnits.Commands;

/// <summary>
/// [Giai đoạn 3] DELETE /api/v1/component-units/{unitId} (extracted from
/// ComponentUnitsController.Delete). DELEGATES to IComponentAllocationService (DeleteUnitAsync) —
/// soft-delete + allocation-history guard + Qty decrement + ActionLog + company-scoping ALL stay
/// in the Infrastructure service untouched. NO ILoggableCommand (service writes its own log).
/// </summary>
public record DeleteComponentUnitCommand(Guid UnitId, Guid CurrentUserId) : IRequest<ComponentOperationResult>;

public class DeleteComponentUnitCommandHandler : IRequestHandler<DeleteComponentUnitCommand, ComponentOperationResult>
{
    private readonly IComponentAllocationService _allocationService;

    public DeleteComponentUnitCommandHandler(IComponentAllocationService allocationService)
        => _allocationService = allocationService;

    public Task<ComponentOperationResult> Handle(DeleteComponentUnitCommand request, CancellationToken cancellationToken)
        => _allocationService.DeleteUnitAsync(request.UnitId, request.CurrentUserId, cancellationToken);
}
