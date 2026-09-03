using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.SystemInfos.Commands;

/// <summary>
/// [Giai đoạn 3] DELETE /api/v1/system-infos/{systemInfoId}/positions/{posId} (extracted from
/// SystemInfoController.DeletePosition). Verbatim: scope → 404; [MC-7a delete-guard]
/// POSITION_IN_USE_BY_CHECKLIST (FK RESTRICT would leak raw 500 — soft 400 first).
/// ILoggableCommand with CompanyId = pos.SystemInfo.CompanyId.
/// </summary>
public record DeleteSystemPositionCommand(Guid SystemInfoId, Guid PosId, Guid CurrentUserId)
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
            ActionType = ActionType.Delete,
            CreatedBy = CurrentUserId,
            CompanyId = response.CompanyId,
            Note = $"Xóa vị trí \"{response.Name}\""
        };
    }
}

public class DeleteSystemPositionCommandHandler : IRequestHandler<DeleteSystemPositionCommand, SystemInfoResult>
{
    private readonly IApplicationDbContext _context;
    private readonly ICompanyScopeService _companyScope;

    public DeleteSystemPositionCommandHandler(IApplicationDbContext context, ICompanyScopeService companyScope)
    {
        _context = context;
        _companyScope = companyScope;
    }

    public async Task<SystemInfoResult> Handle(DeleteSystemPositionCommand request, CancellationToken cancellationToken)
    {
        var pos = await _context.SystemPositions.Include(p => p.SystemInfo)
            .FirstOrDefaultAsync(p => p.Id == request.PosId && p.SystemInfoId == request.SystemInfoId, cancellationToken);
        if (pos == null)
            return new SystemInfoResult(false, "Position not found.", "NOT_FOUND");

        // Company scoping: a regular user may only delete positions of a system in their own company.
        var userCompanyIdDeletePos = await _companyScope.GetCurrentUserCompanyIdAsync();
        var posCompanyIdDelete = pos.SystemInfo?.CompanyId;
        if (userCompanyIdDeletePos.HasValue && posCompanyIdDelete.HasValue && posCompanyIdDelete.Value != userCompanyIdDeletePos.Value)
            return new SystemInfoResult(false, "Position not found.", "NOT_FOUND");

        // [MC-7a delete-guard] Vị trí đang được ChecklistItem của template bảo dưỡng tham chiếu →
        // chặn xóa (FK RESTRICT ở DB sẽ chặn; guard trước để trả 400 mềm, không lộ 500 FK thô).
        if (await _context.MaintenanceChecklistItemPositions.AsNoTracking()
                .AnyAsync(ip => ip.SystemPositionId == request.PosId, cancellationToken))
            return new SystemInfoResult(false,
                "Vị trí đang được ChecklistItem của template bảo dưỡng tham chiếu — không thể xóa. Hãy điều chỉnh template (version mới) trước.",
                "POSITION_IN_USE_BY_CHECKLIST");

        _context.SystemPositions.Remove(pos);
        await _context.SaveChangesAsync(cancellationToken);

        return new SystemInfoResult(true, "Position deleted.",
            Id: request.PosId, Name: pos.Name, CompanyId: pos.SystemInfo?.CompanyId);
    }
}
