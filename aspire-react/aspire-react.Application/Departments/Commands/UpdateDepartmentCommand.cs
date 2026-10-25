using aspire_react.Server.Application.Common.ActionLogs;
using aspire_react.Server.Application.Common.CompanyScope;
using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Application.Common.Validation;
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

        // [DF-8] Non-superuser cannot move to another company or clear to floater.
        if (!await CompanyAssignmentPolicy.CanChangeAsync(_companyScope, request.CompanyId))
            return new DepartmentResult(false, "Bạn không được đổi công ty của phòng ban.", "FORBIDDEN");

        // [BUG-E FIX] Name is only required WHEN SENT (patch semantics): blank sent → 400 (same as
        // the old full-PUT rule); absent → keep the stored name.
        if (request.Name != null && string.IsNullOrWhiteSpace(request.Name))
            return new DepartmentResult(false, "Tên phòng ban không được để trống.");

        // [BUG-E FIX] Dup-check only when the name is actually CHANGED (same rule as Create).
        var nameChanged = request.Name != null && request.Name != d.Name;
        if (nameChanged && await _context.Departments.AnyAsync(x => x.Name == request.Name && x.Id != request.Id, cancellationToken))
            return new DepartmentResult(false, "Tên phòng ban đã tồn tại.");

        var before = new { d.Name, d.CompanyId, d.ManagerId, d.Phone, d.Fax };
        // [FIX-GUID-EMPTY phần 2 — 2026-10-20] FK TÙY CHỌN: vắng = giữ; Guid.Empty = xóa liên kết (null);
        // id thật phải TỒN TẠI (trước đây ⇒ Postgres 23503 ⇒ 500).
        var newCompanyId = OptionalForeignKey.Cleared(request.CompanyId);
        var newManagerId = OptionalForeignKey.Cleared(request.ManagerId);
        if (newCompanyId is not null
            && !await _context.Companies.AnyAsync(x => x.Id == newCompanyId, cancellationToken))
            return new DepartmentResult(false, OptionalForeignKey.NotFoundMessage("companyId"), "RESOURCE_NOT_FOUND");
        if (newManagerId is not null
            && !await _context.Users.AnyAsync(x => x.Id == newManagerId, cancellationToken))
            return new DepartmentResult(false, OptionalForeignKey.NotFoundMessage("managerId"), "RESOURCE_NOT_FOUND");
        // [AUDIT-LOG B6a] helper dùng chung: tên field snake_case, CHỈ field thực sự đổi.
        var changes = new ActionLogChanges()
            .Track("name", before.Name)
            .Track("companyId", before.CompanyId)
            .Track("managerId", before.ManagerId)
            .Track("phone", before.Phone)
            .Track("fax", before.Fax);
        if (request.Name != null) d.Name = request.Name;
        if (request.CompanyId.HasValue) d.CompanyId = newCompanyId;
        if (request.ManagerId.HasValue) d.ManagerId = newManagerId;
        if (request.Phone is not null) d.Phone = request.Phone;
        if (request.Fax is not null) d.Fax = request.Fax;
        await _context.SaveChangesAsync(cancellationToken);

        var logMeta = changes
            .Set("name", d.Name)
            .Set("companyId", d.CompanyId)
            .Set("managerId", d.ManagerId)
            .Set("phone", d.Phone)
            .Set("fax", d.Fax)
            .ToLogMeta();

        return new DepartmentResult(
            true, "Updated.",
            DepartmentId: d.Id, Name: d.Name, CompanyId: d.CompanyId,
            LogMeta: logMeta, Note: $"Cập nhật phòng ban \"{d.Name}\"");
    }
}
