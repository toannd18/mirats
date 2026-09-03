using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Application.SystemInfos;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.SystemInfos.Commands;

/// <summary>
/// [Giai đoạn 3] POST /api/v1/system-infos (extracted from SystemInfoController.Create).
/// Verbatim: normalize Code uppercase FIRST (lowercase client input accepted); regex
/// XXX(X)-YYYY-ZZZ; Name required ([SEC-FIX P1]); dup-Code; [Task L2] COMPANY_MISMATCH
/// company-scoping on create. ILoggableCommand with CompanyId = sys.CompanyId (company-scoped
/// resource — unlike reference data).
/// </summary>
public record CreateSystemInfoCommand(string? Code, string? Name, string? Description, Guid? CompanyId, Guid CurrentUserId)
    : IRequest<SystemInfoResult>, ILoggableCommand<SystemInfoResult>
{
    public ActionLogEntry? BuildLogEntry(SystemInfoResult response)
    {
        if (!response.Success)
            return null;

        return new ActionLogEntry
        {
            ItemType = ItemType.SystemInfo,
            ItemId = response.Id!.Value,
            ActionType = ActionType.Create,
            CreatedBy = CurrentUserId,
            CompanyId = response.CompanyId,
            Note = $"Tạo hệ thống \"{response.Name}\""
        };
    }
}

public class CreateSystemInfoCommandHandler : IRequestHandler<CreateSystemInfoCommand, SystemInfoResult>
{
    private readonly IApplicationDbContext _context;
    private readonly ICompanyScopeService _companyScope;

    public CreateSystemInfoCommandHandler(IApplicationDbContext context, ICompanyScopeService companyScope)
    {
        _context = context;
        _companyScope = companyScope;
    }

    public async Task<SystemInfoResult> Handle(CreateSystemInfoCommand request, CancellationToken cancellationToken)
    {
        // Normalize code to uppercase FIRST so a lowercase client input is accepted and stored uppercase
        // (the user does not need to remember the case rule). Validation then runs on the normalized value.
        var code = request.Code?.Trim().ToUpperInvariant() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(code) || !SystemCodeRules.CodeRegex.IsMatch(code))
            return new SystemInfoResult(false, "Mã hệ thống phải đúng định dạng XXX(X)-YYYY-ZZZ (3-4 chữ hoa, viết hoa).");
        // [SEC-FIX P1] Code/Name are now nullable on the shared DTO — Create must still require both.
        if (string.IsNullOrWhiteSpace(request.Name))
            return new SystemInfoResult(false, "Tên hệ thống không được để trống.");
        if (await _context.SystemInfos.AnyAsync(x => x.Code == code, cancellationToken))
            return new SystemInfoResult(false, "Mã hệ thống đã tồn tại.");

        // [Task L2 — COMPANY-SCOPING on Create] A regular user may only create systems for their own
        // company (or a company-less floater). Superuser (GetCurrentUserCompanyIdAsync → null) may
        // create for any company. Never trust the client-supplied CompanyId alone.
        var userCompanyIdCreate = await _companyScope.GetCurrentUserCompanyIdAsync();
        if (userCompanyIdCreate.HasValue && request.CompanyId.HasValue && request.CompanyId.Value != userCompanyIdCreate.Value)
            return new SystemInfoResult(false, "Bạn chỉ được tạo hệ thống cho công ty của mình.", "COMPANY_MISMATCH");

        var sys = new SystemInfo { Code = code.ToUpper(), Name = request.Name!, Description = request.Description, CompanyId = request.CompanyId };
        _context.SystemInfos.Add(sys);
        await _context.SaveChangesAsync(cancellationToken);

        return new SystemInfoResult(true, "Created", Id: sys.Id, Code: sys.Code, Name: sys.Name, CompanyId: sys.CompanyId);
    }
}
