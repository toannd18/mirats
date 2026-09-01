using System.Text.Json;
using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.Departments.Commands;

/// <summary>
/// [Giai đoạn 1 — pilot MediatR] PUT /api/v1/departments/{id}. Full-PUT semantics preserved
/// EXACTLY from the pre-migration controller: Name/CompanyId/ManagerId/Phone/Fax are ALL
/// assigned unconditionally (a field absent from the payload clears it — this is the historical
/// behavior, deliberately NOT "improved" to patch semantics in a pure-migration task).
/// Rule order preserved: 404 → scope 404 → empty-name 400 → duplicate-name 400.
/// The ActionLog changes-snapshot (LogMeta old→new) is built in the handler where the tracked
/// entity's before-values live, and carried to ActionLogBehavior via the response.
/// </summary>
public record UpdateDepartmentCommand(
    Guid Id,
    string Name,
    Guid? CompanyId,
    Guid? ManagerId,
    string? Phone,
    string? Fax,
    Guid CurrentUserId)
    : IRequest<DepartmentResult>, ILoggableCommand<DepartmentResult>
{
    public ActionLogEntry? BuildLogEntry(DepartmentResult response)
    {
        if (!response.Success)
            return null;

        return new ActionLogEntry
        {
            ItemType = ItemType.Department,
            ItemId = Id,
            ActionType = ActionType.Update,
            CreatedBy = CurrentUserId,
            CompanyId = response.CompanyId,
            LogMeta = response.LogMeta,
            Note = response.Note
        };
    }
}

public class UpdateDepartmentCommandHandler : IRequestHandler<UpdateDepartmentCommand, DepartmentResult>
{
    private readonly IApplicationDbContext _context;
    private readonly ICompanyScopeService _companyScope;

    public UpdateDepartmentCommandHandler(IApplicationDbContext context, ICompanyScopeService companyScope)
    {
        _context = context;
        _companyScope = companyScope;
    }

    public async Task<DepartmentResult> Handle(UpdateDepartmentCommand request, CancellationToken cancellationToken)
    {
        var d = await _context.Departments.FindAsync(request.Id);
        if (d == null)
            return new DepartmentResult(false, "Not found.", "NOT_FOUND");

        // Company scoping: a regular user may only edit departments of their own company (or floater).
        var userCompanyId = await _companyScope.GetCurrentUserCompanyIdAsync();
        if (userCompanyId.HasValue && d.CompanyId.HasValue && d.CompanyId.Value != userCompanyId.Value)
            return new DepartmentResult(false, "Not found.", "NOT_FOUND");

        if (string.IsNullOrWhiteSpace(request.Name))
            return new DepartmentResult(false, "Tên phòng ban không được để trống.");

        if (await _context.Departments.AnyAsync(x => x.Name == request.Name && x.Id != request.Id, cancellationToken))
            return new DepartmentResult(false, "Tên phòng ban đã tồn tại.");

        var before = new { d.Name, d.CompanyId, d.ManagerId, d.Phone, d.Fax };
        d.Name = request.Name;
        d.CompanyId = request.CompanyId;
        d.ManagerId = request.ManagerId;
        d.Phone = request.Phone;
        d.Fax = request.Fax;
        await _context.SaveChangesAsync(cancellationToken);

        var logMeta = JsonSerializer.Serialize(new
        {
            changes = new
            {
                name = new { old = before.Name, @new = d.Name },
                companyId = new { old = before.CompanyId, @new = d.CompanyId },
                managerId = new { old = before.ManagerId, @new = d.ManagerId },
                phone = new { old = before.Phone, @new = d.Phone },
                fax = new { old = before.Fax, @new = d.Fax }
            }
        });

        return new DepartmentResult(
            true, "Updated.",
            DepartmentId: d.Id, Name: d.Name, CompanyId: d.CompanyId,
            LogMeta: logMeta, Note: $"Cập nhật phòng ban \"{d.Name}\"");
    }
}
