using System.Text.Json;
using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.Departments.Commands;

/// <summary>
/// [Giai đoạn 1 — pilot MediatR] PUT /api/v1/departments/{id}.
/// [BUG-E FIX 2026-09-05] PATCH-SAFE semantics (Task M1/M2 pattern — behavior change approved):
/// all fields are NULLABLE and assigned ONLY when actually sent (`is not null`); an absent field
/// no longer clears the stored value. Previously full-PUT (Name/CompanyId/ManagerId/Phone/Fax ALL
/// assigned unconditionally — a missing field silently wiped data). Validation order preserved:
/// 404 → scope 404 → empty-name 400 (only when Name IS sent and blank) → duplicate-name 400 (only
/// when the name is actually CHANGED — mirrors Create's dup-check). LogMeta old→new pairs now
/// naturally reflect only real changes (unchanged fields yield old==new).
/// </summary>
public record UpdateDepartmentCommand(
    Guid Id,
    string? Name,
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

        // [BUG-E FIX] Name is only required WHEN SENT (patch semantics): blank sent → 400 (same as
        // the old full-PUT rule); absent → keep the stored name.
        if (request.Name != null && string.IsNullOrWhiteSpace(request.Name))
            return new DepartmentResult(false, "Tên phòng ban không được để trống.");

        // [BUG-E FIX] Dup-check only when the name is actually CHANGED (same rule as Create).
        var nameChanged = request.Name != null && request.Name != d.Name;
        if (nameChanged && await _context.Departments.AnyAsync(x => x.Name == request.Name && x.Id != request.Id, cancellationToken))
            return new DepartmentResult(false, "Tên phòng ban đã tồn tại.");

        var before = new { d.Name, d.CompanyId, d.ManagerId, d.Phone, d.Fax };
        if (request.Name != null) d.Name = request.Name;
        if (request.CompanyId.HasValue) d.CompanyId = request.CompanyId;
        if (request.ManagerId.HasValue) d.ManagerId = request.ManagerId;
        if (request.Phone is not null) d.Phone = request.Phone;
        if (request.Fax is not null) d.Fax = request.Fax;
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
