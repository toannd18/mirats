using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.Departments.Commands;

/// <summary>
/// [Giai đoạn 1 — pilot MediatR] DELETE /api/v1/departments/{id}. Rule order preserved:
/// 404 → scope 404 → DEPARTMENT_IN_USE guard (users / assignment history — hard-delete blocked
/// per delete-guard-by-usage-history convention). ActionLog (thin entry, same fields as the old
/// manual Log(entry)) is persisted by ActionLogBehavior inside the same transaction as the removal.
/// </summary>
public record DeleteDepartmentCommand(Guid Id, Guid CurrentUserId)
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
            ActionType = ActionType.Delete,
            CreatedBy = CurrentUserId,
            CompanyId = response.CompanyId,
            Note = response.Note
        };
    }
}

public class DeleteDepartmentCommandHandler : IRequestHandler<DeleteDepartmentCommand, DepartmentResult>
{
    private readonly IApplicationDbContext _context;
    private readonly ICompanyScopeService _companyScope;

    public DeleteDepartmentCommandHandler(IApplicationDbContext context, ICompanyScopeService companyScope)
    {
        _context = context;
        _companyScope = companyScope;
    }

    public async Task<DepartmentResult> Handle(DeleteDepartmentCommand request, CancellationToken cancellationToken)
    {
        var d = await _context.Departments.FindAsync(request.Id);
        if (d == null)
            return new DepartmentResult(false, "Not found.", "NOT_FOUND");

        // Company scoping: a regular user may only delete departments of their own company (or floater).
        var userCompanyId = await _companyScope.GetCurrentUserCompanyIdAsync();
        if (userCompanyId.HasValue && d.CompanyId.HasValue && d.CompanyId.Value != userCompanyId.Value)
            return new DepartmentResult(false, "Not found.", "NOT_FOUND");

        // Delete guard: a department still referenced by users or by an allocation/checkout target
        // must not be hard-deleted (would orphan the references / lose history).
        if (await _context.Users.AnyAsync(u => u.DepartmentId == request.Id, cancellationToken)
            || await _context.Assignments.IgnoreQueryFilters().AnyAsync(
                a => a.TargetType == AssignmentTargetType.Department && a.TargetId == request.Id, cancellationToken))
            return new DepartmentResult(false,
                "Phòng ban đang được người dùng / lịch sử cấp phát sử dụng — không thể xóa.",
                "DEPARTMENT_IN_USE");

        _context.Departments.Remove(d);
        await _context.SaveChangesAsync(cancellationToken);

        return new DepartmentResult(true, "Deleted.", DepartmentId: request.Id, Name: d.Name, CompanyId: d.CompanyId,
            Note: $"Xóa phòng ban \"{d.Name}\"");
    }
}
