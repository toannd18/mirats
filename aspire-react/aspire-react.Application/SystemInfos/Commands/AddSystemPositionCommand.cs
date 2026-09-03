using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Application.SystemInfos;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.SystemInfos.Commands;

/// <summary>
/// [Giai đoạn 3] POST /api/v1/system-infos/{systemInfoId}/positions (extracted from
/// SystemInfoController.AddPosition). Verbatim: code normalize/regex/name-required/dup-code;
/// system 404; [Task L2] scope → 404 hide-existence (position inherits parent system's company).
/// ILoggableCommand with CompanyId = sys.CompanyId.
/// </summary>
public record AddSystemPositionCommand(Guid SystemInfoId, string? Code, string? Name, string? Description, Guid CurrentUserId)
    : IRequest<SystemInfoResult>, ILoggableCommand<SystemInfoResult>
{
    public ActionLogEntry? BuildLogEntry(SystemInfoResult response)
    {
        if (!response.Success)
            return null;

        return new ActionLogEntry
        {
            ItemType = ItemType.SystemPosition,
            ItemId = response.Id!.Value,
            ActionType = ActionType.Create,
            CreatedBy = CurrentUserId,
            CompanyId = response.CompanyId,
            Note = $"Tạo vị trí \"{response.Name}\" trong hệ thống \"{response.Note}\""
        };
    }
}

public class AddSystemPositionCommandHandler : IRequestHandler<AddSystemPositionCommand, SystemInfoResult>
{
    private readonly IApplicationDbContext _context;
    private readonly ICompanyScopeService _companyScope;

    public AddSystemPositionCommandHandler(IApplicationDbContext context, ICompanyScopeService companyScope)
    {
        _context = context;
        _companyScope = companyScope;
    }

    public async Task<SystemInfoResult> Handle(AddSystemPositionCommand request, CancellationToken cancellationToken)
    {
        var code = SystemCodeRules.Normalize(request.Code) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(code) || !SystemCodeRules.CodeRegex.IsMatch(code))
            return new SystemInfoResult(false, "Mã vị trí phải đúng định dạng XXX(X)-YYYY-ZZZ (3-4 chữ hoa, viết hoa).");
        // [SEC-FIX P1] Code/Name nullable on the shared DTO — Create still requires both.
        if (string.IsNullOrWhiteSpace(request.Name))
            return new SystemInfoResult(false, "Tên vị trí không được để trống.");
        if (await _context.SystemPositions.AnyAsync(x => x.Code == code, cancellationToken))
            return new SystemInfoResult(false, "Mã vị trí đã tồn tại.");
        var sys = await _context.SystemInfos.FindAsync(new object?[] { request.SystemInfoId }, cancellationToken);
        if (sys == null)
            return new SystemInfoResult(false, "System not found.", "NOT_FOUND");

        // [Task L2 — COMPANY-SCOPING on Create] A position inherits its parent system's CompanyId,
        // so a regular user may only add positions to systems in their own company scope (or a
        // company-less system). Superuser bypasses. Out-of-scope → 404 (hide existence, Task I).
        var userCompanyIdAddPos = await _companyScope.GetCurrentUserCompanyIdAsync();
        if (userCompanyIdAddPos.HasValue && sys.CompanyId.HasValue && sys.CompanyId.Value != userCompanyIdAddPos.Value)
            return new SystemInfoResult(false, "System not found.", "NOT_FOUND");

        var pos = new SystemPosition { SystemInfoId = request.SystemInfoId, Code = code.ToUpper(), Name = request.Name!, Description = request.Description };
        _context.SystemPositions.Add(pos);
        await _context.SaveChangesAsync(cancellationToken);

        return new SystemInfoResult(true, "Created",
            Id: pos.Id, Code: pos.Code, Name: pos.Name, CompanyId: sys.CompanyId,
            Note: sys.Name);
    }
}
