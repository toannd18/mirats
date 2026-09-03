using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.SystemInfos.Commands;

/// <summary>
/// [Giai đoạn 3] DELETE /api/v1/system-infos/{id} (extracted from SystemInfoController.Delete).
/// Verbatim: company-scope 404; [MC-7a delete-guard] POSITION_IN_USE_BY_CHECKLIST; [BUG-C
/// delete-guard] SYSTEM_IN_USE_BY_CAMPAIGN (completed campaigns = immutable history, FK
/// RESTRICT would leak raw 500 — soft 400 first, AR-2/MC-7a pattern). ILoggableCommand with
/// CompanyId = s.CompanyId.
/// </summary>
public record DeleteSystemInfoCommand(Guid Id, Guid CurrentUserId)
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
            ActionType = ActionType.Delete,
            CreatedBy = CurrentUserId,
            CompanyId = response.CompanyId,
            Note = $"Xóa hệ thống \"{response.Name}\""
        };
    }
}

public class DeleteSystemInfoCommandHandler : IRequestHandler<DeleteSystemInfoCommand, SystemInfoResult>
{
    private readonly IApplicationDbContext _context;
    private readonly ICompanyScopeService _companyScope;

    public DeleteSystemInfoCommandHandler(IApplicationDbContext context, ICompanyScopeService companyScope)
    {
        _context = context;
        _companyScope = companyScope;
    }

    public async Task<SystemInfoResult> Handle(DeleteSystemInfoCommand request, CancellationToken cancellationToken)
    {
        var s = await _context.SystemInfos.FindAsync(request.Id);
        if (s == null)
            return new SystemInfoResult(false, "Not found.", "NOT_FOUND");

        // Company scoping: a regular user may only delete systems of their own company (or floater).
        var userCompanyIdDelete = await _companyScope.GetCurrentUserCompanyIdAsync();
        if (userCompanyIdDelete.HasValue && s.CompanyId.HasValue && s.CompanyId.Value != userCompanyIdDelete.Value)
            return new SystemInfoResult(false, "Not found.", "NOT_FOUND");

        // [MC-7a delete-guard] Nếu có vị trí thuộc hệ thống này đang được ChecklistItem của template
        // bảo dưỡng tham chiếu → chặn (FK RESTRICT ở DB sẽ chặn cascade; guard trước để trả 400 mềm,
        // không để lộ 500 FK thô). Mirror delete-guard Company (AR-2).
        var posIds = await _context.SystemPositions.AsNoTracking()
            .Where(p => p.SystemInfoId == request.Id)
            .Select(p => p.Id)
            .ToListAsync(cancellationToken);
        if (posIds.Count > 0
            && await _context.MaintenanceChecklistItemPositions.AsNoTracking()
                .AnyAsync(ip => posIds.Contains(ip.SystemPositionId), cancellationToken))
            return new SystemInfoResult(false,
                "Hệ thống có vị trí đang được ChecklistItem của template bảo dưỡng tham chiếu — không thể xóa.",
                "POSITION_IN_USE_BY_CHECKLIST");

        // [BUG-C delete-guard] Campaign (kể cả Completed — lịch sử bất biến) tham chiếu SystemInfo qua
        // FK RESTRICT → xóa system sẽ lộ 500 FK thô (reproduced trong audit backend 2026-08-30).
        // Chặn trước bằng 400 mềm, cùng pattern AR-2/MC-7a: delete-guard by usage history.
        var campaignCount = await _context.MaintenanceCampaigns.AsNoTracking()
            .CountAsync(c => c.SystemInfoId == request.Id, cancellationToken);
        if (campaignCount > 0)
            return new SystemInfoResult(false,
                $"Hệ thống đã có {campaignCount} đợt bảo dưỡng (lịch sử bất biến) — không thể xóa.",
                "SYSTEM_IN_USE_BY_CAMPAIGN");

        _context.SystemInfos.Remove(s);
        await _context.SaveChangesAsync(cancellationToken);

        return new SystemInfoResult(true, "Deleted.", Id: request.Id, Name: s.Name, CompanyId: s.CompanyId);
    }
}
