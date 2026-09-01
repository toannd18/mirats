using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using MediatR;

namespace aspire_react.Server.Application.Locations.Commands;

/// <summary>
/// [Giai đoạn 2] POST /api/v1/locations (extracted from AdminController.CreateLocation).
/// ⚠️ TODO SECURITY BUG-G: NO company-scoping validation and NO input validation here — the
/// pre-migration controller had none, so a regular user can create a location for ANY company
/// (cross-company creation) and with an empty name. Behavior preserved verbatim for parity;
/// registered as BUG-G (SECURITY/HIGH) in docs/BACKLOG.md — fix requires its own approved task.
/// ILoggableCommand only: locations have NO output-cache (no [OutputCache] on GET /locations
/// pre-migration) — ICacheInvalidatingCommand deliberately NOT implemented.
/// </summary>
public record CreateLocationCommand(
    string Name,
    Guid? ParentId,
    Guid? CompanyId,
    Guid? ManagerId,
    string? Address,
    string? City,
    string? State,
    string? Country,
    string? Zip,
    Guid CurrentUserId)
    : IRequest<LocationResult>, ILoggableCommand<LocationResult>
{
    public ActionLogEntry? BuildLogEntry(LocationResult response)
    {
        if (!response.Success)
            return null;

        return new ActionLogEntry
        {
            ItemType = ItemType.Location,
            ItemId = response.LocationId!.Value,
            ActionType = ActionType.Create,
            CreatedBy = CurrentUserId,
            CompanyId = CompanyId,
            Note = $"Tạo địa điểm \"{Name}\""
        };
    }
}

public class CreateLocationCommandHandler : IRequestHandler<CreateLocationCommand, LocationResult>
{
    private readonly IApplicationDbContext _context;

    public CreateLocationCommandHandler(IApplicationDbContext context) => _context = context;

    public async Task<LocationResult> Handle(CreateLocationCommand request, CancellationToken cancellationToken)
    {
        // TODO SECURITY BUG-G: no company-scoping validation, see BACKLOG.md (SECURITY/HIGH).
        // Verbatim pre-migration behavior: no checks at all — parity preserved deliberately.
        var l = new Location
        {
            Name = request.Name,
            ParentId = request.ParentId,
            CompanyId = request.CompanyId,
            ManagerId = request.ManagerId,
            Address = request.Address,
            City = request.City,
            State = request.State,
            Country = request.Country,
            Zip = request.Zip
        };
        _context.Locations.Add(l);
        await _context.SaveChangesAsync(cancellationToken);

        return new LocationResult(true, "Created.", LocationId: l.Id, Name: l.Name, CompanyId: l.CompanyId);
    }
}
