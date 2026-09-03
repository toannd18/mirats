using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using MediatR;

namespace aspire_react.Server.Application.ComponentUnits.Commands;

/// <summary>
/// [Giai đoạn 3] PATCH /api/v1/component-units/{unitId} (extracted from
/// ComponentUnitsController.UpdateStatus). DELEGATES to IComponentAllocationService
/// (SetUnitStatusAsync) — the allocation/lock/ActionLog logic stays in the Infrastructure
/// service untouched. NO ILoggableCommand: the service writes its own ActionLog internally.
/// </summary>
public record UpdateComponentUnitStatusCommand(Guid UnitId, ComponentUnitStatus Status, string? Note, Guid CurrentUserId)
    : IRequest<ComponentOperationResult>;

public class UpdateComponentUnitStatusCommandHandler : IRequestHandler<UpdateComponentUnitStatusCommand, ComponentOperationResult>
{
    private readonly IComponentAllocationService _allocationService;

    public UpdateComponentUnitStatusCommandHandler(IComponentAllocationService allocationService)
        => _allocationService = allocationService;

    public Task<ComponentOperationResult> Handle(UpdateComponentUnitStatusCommand request, CancellationToken cancellationToken)
        => _allocationService.SetUnitStatusAsync(request.UnitId, request.Status, request.Note, request.CurrentUserId, cancellationToken);
}
