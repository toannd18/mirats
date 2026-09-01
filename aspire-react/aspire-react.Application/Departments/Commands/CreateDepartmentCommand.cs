using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.Departments.Commands;

/// <summary>
/// [Giai đoạn 1 — pilot MediatR] POST /api/v1/departments. Business rules moved verbatim from
/// DepartmentsController.Create, in the SAME order: scope mismatch → COMPANY_MISMATCH; empty name;
/// duplicate name (global, no company scoping — same as before). Soft-fail responses keep the old
/// custom 400 bodies (no FluentValidation here — see playbook: custom message/error_code bodies
/// must NOT be reshaped into the standard "Validation failed." envelope).
/// ActionLog is persisted by ActionLogBehavior (ILoggableCommand) — thin entry, identical to the
/// old manual Log(entry) fields (no LogMeta on create).
/// </summary>
public record CreateDepartmentCommand(
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
            ItemId = response.DepartmentId!.Value,
            ActionType = ActionType.Create,
            CreatedBy = CurrentUserId,
            CompanyId = CompanyId,
            Note = $"Tạo phòng ban \"{Name}\""
        };
    }
}

public class CreateDepartmentCommandHandler : IRequestHandler<CreateDepartmentCommand, DepartmentResult>
{
    private readonly IApplicationDbContext _context;
    private readonly ICompanyScopeService _companyScope;

    public CreateDepartmentCommandHandler(IApplicationDbContext context, ICompanyScopeService companyScope)
    {
        _context = context;
        _companyScope = companyScope;
    }

    public async Task<DepartmentResult> Handle(CreateDepartmentCommand request, CancellationToken cancellationToken)
    {
        // [Task L2] Company-scoping on CREATE (order preserved): a regular user may only create
        // departments for their own company (or floater); superuser may create for any company.
        var userCompanyId = await _companyScope.GetCurrentUserCompanyIdAsync();
        if (userCompanyId.HasValue && request.CompanyId.HasValue && request.CompanyId.Value != userCompanyId.Value)
            return new DepartmentResult(false, "Bạn chỉ được tạo phòng ban cho công ty của mình.", "COMPANY_MISMATCH");

        if (string.IsNullOrWhiteSpace(request.Name))
            return new DepartmentResult(false, "Tên phòng ban không được để trống.");

        if (await _context.Departments.AnyAsync(x => x.Name == request.Name, cancellationToken))
            return new DepartmentResult(false, "Tên phòng ban đã tồn tại.");

        var d = new Department
        {
            Name = request.Name,
            CompanyId = request.CompanyId,
            ManagerId = request.ManagerId,
            Phone = request.Phone,
            Fax = request.Fax
        };
        _context.Departments.Add(d);
        await _context.SaveChangesAsync(cancellationToken);

        return new DepartmentResult(true, "Created.", DepartmentId: d.Id, Name: d.Name, CompanyId: d.CompanyId);
    }
}
