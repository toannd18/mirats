using System.Text.Json;
using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Application.SystemInfos;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.SystemInfos.Commands;

/// <summary>
/// [Giai đoạn 3] PUT /api/v1/system-infos/{systemInfoId}/positions/{posId} (extracted from
/// SystemInfoController.UpdatePosition). Verbatim: scope → 404; patch-aware Code/Name/Description
/// ([SEC-FIX P1]); dup-code when changed. Position has NO own CompanyId — inherits parent
/// system's (log CompanyId = pos.SystemInfo.CompanyId verbatim). LogMeta ×3.
/// </summary>
public record UpdateSystemPositionCommand(Guid SystemInfoId, Guid PosId, string? Code, string? Name, string? Description, Guid CurrentUserId)
    : IRequest<SystemInfoResult>, ILoggableCommand<SystemInfoResult>
{
    public ActionLogEntry? BuildLogEntry(SystemInfoResult response)
    {
        if (!response.Success)
            return null;

        return new ActionLogEntry
        {
            ItemType = ItemType.SystemPosition,
            ItemId = PosId,
            ActionType = ActionType.Update,
            CreatedBy = CurrentUserId,
            CompanyId = response.CompanyId,
            LogMeta = response.LogMeta,
            Note = response.Note
        };
    }
}

public class UpdateSystemPositionCommandHandler : IRequestHandler<UpdateSystemPositionCommand, SystemInfoResult>
{
    private readonly IApplicationDbContext _context;
    private readonly ICompanyScopeService _companyScope;

    public UpdateSystemPositionCommandHandler(IApplicationDbContext context, ICompanyScopeService companyScope)
    {
        _context = context;
        _companyScope = companyScope;
    }

    public async Task<SystemInfoResult> Handle(UpdateSystemPositionCommand request, CancellationToken cancellationToken)
    {
        var pos = await _context.SystemPositions.Include(p => p.SystemInfo)
            .FirstOrDefaultAsync(p => p.Id == request.PosId && p.SystemInfoId == request.SystemInfoId, cancellationToken);
        if (pos == null)
            return new SystemInfoResult(false, "Position not found.", "NOT_FOUND");

        // Company scoping: a regular user may only edit positions of a system in their own company.
        var userCompanyIdUpdatePos = await _companyScope.GetCurrentUserCompanyIdAsync();
        var posCompanyId = pos.SystemInfo?.CompanyId;
        if (userCompanyIdUpdatePos.HasValue && posCompanyId.HasValue && posCompanyId.Value != userCompanyIdUpdatePos.Value)
            return new SystemInfoResult(false, "Position not found.", "NOT_FOUND");

        // Patch-aware validation: Code is only normalized/validated when actually sent
        // ([SEC-FIX P1] same class of fix as the parent SystemInfo Update above).
        var normalizedCode = SystemCodeRules.Normalize(request.Code);
        if (normalizedCode != null)
        {
            if (!SystemCodeRules.CodeRegex.IsMatch(normalizedCode))
                return new SystemInfoResult(false, "Mã vị trí phải đúng định dạng XXX(X)-YYYY-ZZZ (3-4 chữ hoa, viết hoa).");
            if (await _context.SystemPositions.AnyAsync(x => x.Code == normalizedCode && x.Id != request.PosId, cancellationToken))
                return new SystemInfoResult(false, "Mã vị trí đã tồn tại.");
        }

        // ─── Patch semantics (Task F/M1 pattern): only fields explicitly sent are applied. A
        // position has no own CompanyId — it inherits its parent SystemInfo's company. ───
        var before = new { pos.Code, pos.Name, pos.Description };
        if (normalizedCode != null) pos.Code = normalizedCode.ToUpper();
        if (!string.IsNullOrWhiteSpace(request.Name)) pos.Name = request.Name;
        if (request.Description is not null) pos.Description = request.Description;
        await _context.SaveChangesAsync(cancellationToken);

        var logMeta = JsonSerializer.Serialize(new
        {
            changes = new
            {
                code = new { old = before.Code, @new = pos.Code },
                name = new { old = before.Name, @new = pos.Name },
                description = new { old = before.Description, @new = pos.Description }
            }
        });

        return new SystemInfoResult(true, "Position updated.",
            Id: pos.Id, Code: pos.Code, Name: pos.Name, CompanyId: pos.SystemInfo?.CompanyId,
            LogMeta: logMeta, Note: $"Cập nhật vị trí \"{pos.Name}\"");
    }
}
