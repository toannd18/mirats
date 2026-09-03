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
/// [Giai đoạn 3] PUT /api/v1/system-infos/{id} (extracted from SystemInfoController.Update).
/// Verbatim: company-scope 404 hide-existence; PATCH-AWARE ([SEC-FIX P1]/Task F/M1) — Code
/// normalized/validated only when actually sent; Name assigned only when non-whitespace;
/// Description only when sent; CompanyId only when sent. FIELD_LOCKED on Company change when
/// positions or license seats reference the system. LogMeta ×4 (code/name/description/companyId).
/// ILoggableCommand with CompanyId = s.CompanyId (post-update value, verbatim).
/// </summary>
public record UpdateSystemInfoCommand(Guid Id, string? Code, string? Name, string? Description, Guid? CompanyId, Guid CurrentUserId)
    : IRequest<SystemInfoResult>, ILoggableCommand<SystemInfoResult>
{
    public ActionLogEntry? BuildLogEntry(SystemInfoResult response)
    {
        if (!response.Success)
            return null;

        return new ActionLogEntry
        {
            ItemType = ItemType.SystemInfo,
            ItemId = Id,
            ActionType = ActionType.Update,
            CreatedBy = CurrentUserId,
            CompanyId = response.CompanyId,
            LogMeta = response.LogMeta,
            Note = response.Note
        };
    }
}

public class UpdateSystemInfoCommandHandler : IRequestHandler<UpdateSystemInfoCommand, SystemInfoResult>
{
    private readonly IApplicationDbContext _context;
    private readonly ICompanyScopeService _companyScope;

    public UpdateSystemInfoCommandHandler(IApplicationDbContext context, ICompanyScopeService companyScope)
    {
        _context = context;
        _companyScope = companyScope;
    }

    public async Task<SystemInfoResult> Handle(UpdateSystemInfoCommand request, CancellationToken cancellationToken)
    {
        var s = await _context.SystemInfos.FindAsync(request.Id);
        if (s == null)
            return new SystemInfoResult(false, "Not found.", "NOT_FOUND");

        // Company scoping: a regular user may only edit systems of their own company (or floater).
        var userCompanyIdUpdate = await _companyScope.GetCurrentUserCompanyIdAsync();
        if (userCompanyIdUpdate.HasValue && s.CompanyId.HasValue && s.CompanyId.Value != userCompanyIdUpdate.Value)
            return new SystemInfoResult(false, "Not found.", "NOT_FOUND");

        // Patch-aware validation: Code is only normalized/validated when it was ACTUALLY sent
        // (an absent field must not fail validation nor be overwritten — Task F/M1 pattern).
        var normalizedCode = SystemCodeRules.Normalize(request.Code);
        if (normalizedCode != null)
        {
            if (!SystemCodeRules.CodeRegex.IsMatch(normalizedCode))
                return new SystemInfoResult(false, "Mã hệ thống phải đúng định dạng XXX(X)-YYYY-ZZZ (3-4 chữ hoa, viết hoa).");
            if (await _context.SystemInfos.AnyAsync(x => x.Code == normalizedCode && x.Id != request.Id, cancellationToken))
                return new SystemInfoResult(false, "Mã hệ thống đã tồn tại.");
        }

        // [SEC-FIX P1] FIELD_LOCKED CompanyId — once the system is referenced by a child Position
        // (where Assets can be parked) or targeted by a LicenseSeat, moving it to another company
        // would silently re-scope those references. Mirrors Consumable-có-lịch-sử
        // (ConsumablesController FIELD_LOCKED) / License convention. Patch-aware: only
        // triggers when CompanyId is EXPLICITLY sent and DIFFERS — re-saving the same value passes.
        if (request.CompanyId.HasValue && request.CompanyId.Value != s.CompanyId
            && (await _context.SystemPositions.AnyAsync(p => p.SystemInfoId == request.Id, cancellationToken)
                || await _context.LicenseSeats.AnyAsync(ls => ls.SystemInfoId == request.Id, cancellationToken)))
            return new SystemInfoResult(false,
                "Hệ thống đã có vị trí hoặc seat license tham chiếu — không thể đổi công ty.",
                "FIELD_LOCKED");

        // ─── Patch semantics (Task F/M1 pattern): only fields explicitly sent are applied. ───
        var before = new { s.Code, s.Name, s.Description, s.CompanyId };
        if (normalizedCode != null) s.Code = normalizedCode.ToUpper();
        if (!string.IsNullOrWhiteSpace(request.Name)) s.Name = request.Name;
        if (request.Description is not null) s.Description = request.Description;
        if (request.CompanyId.HasValue) s.CompanyId = request.CompanyId;
        await _context.SaveChangesAsync(cancellationToken);

        var logMeta = JsonSerializer.Serialize(new
        {
            changes = new
            {
                code = new { old = before.Code, @new = s.Code },
                name = new { old = before.Name, @new = s.Name },
                description = new { old = before.Description, @new = s.Description },
                companyId = new { old = before.CompanyId, @new = s.CompanyId }
            }
        });

        return new SystemInfoResult(true, "Updated.",
            Id: s.Id, Name: s.Name, CompanyId: s.CompanyId,
            LogMeta: logMeta, Note: $"Cập nhật hệ thống \"{s.Name}\"");
    }
}
