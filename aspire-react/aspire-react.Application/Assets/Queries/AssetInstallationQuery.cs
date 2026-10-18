using aspire_react.Server.Application.Common.Interfaces;
using aspire_react.Server.Domain.Enums;
using aspire_react.Server.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace aspire_react.Server.Application.Assets.Queries;

/// <summary>
/// [LL-5 Bước 4] Đọc MỘT dòng lý lịch lắp đặt của MỘT thiết bị.
/// <para>
/// Vì sao cần endpoint riêng: form "Đính chính" cần ĐỦ dữ liệu dòng gốc — <c>startedAt</c>, <c>endedAt</c>,
/// vị trí (`systemPositionId`), `note`, `source` — mà DTO timeline chỉ có 1 mốc (`occurredAt` = StartedAt
/// hoặc EndedAt tuỳ sự kiện) và không có `note`/`source`/`systemPositionId`.
/// </para>
/// <para>
/// Company-scope (mirror <c>GetAssetByIdQueryHandler</c>): asset ngoài phạm vi công ty coi như KHÔNG tồn tại ⇒
/// <c>Success = false</c> ⇒ controller trả 404 (hide-existence). Bản ghi không thuộc asset cũng vậy.
/// </para>
/// </summary>
public record AssetInstallationQuery(Guid AssetId, Guid EntryId) : IRequest<AssetInstallationResult>;

/// <summary>Kết quả đọc dòng lý lịch. <c>Success = false</c> = không tìm thấy (404).</summary>
public record AssetInstallationResult(bool Success, AssetInstallationDetailDto? Installation = null);

/// <summary>Dữ liệu đầy đủ của một dòng lý lịch lắp đặt cho form đính chính.</summary>
/// <param name="StartedAt">[UTC có `Z`] Mốc bắt đầu gắn.</param>
/// <param name="EndedAt">[UTC có `Z`] Mốc tháo/điều chuyển; <c>null</c> = dòng ĐANG MỞ.</param>
/// <param name="Source">Nguồn bản ghi (`Api` | `Import` | `Backfill` | `Correction`).</param>
/// <param name="IsEstimated"><c>true</c> khi mốc thời gian là ƯỚC TÍNH (chỉ dòng <c>Backfill</c>).</param>
/// <param name="IsCorrection"><c>true</c> khi chính dòng này là một BẢN ĐÍNH CHÍNH.</param>
/// <param name="SupersededAt">[UTC] <c>null</c> = dòng đang hiệu lực; khác null = đã bị đính chính (không sửa được nữa).</param>
public record AssetInstallationDetailDto(
    Guid Id,
    Guid AssetId,
    Guid? SystemPositionId,
    string? SystemPositionName,
    Guid? SystemInfoId,
    string? SystemInfoName,
    DateTime StartedAt,
    DateTime? EndedAt,
    string? Note,
    AssetInstallationSource Source,
    bool IsEstimated,
    bool IsCorrection,
    Guid? CorrectsEntryId,
    DateTime? SupersededAt,
    string? LoggedByName);

public class AssetInstallationQueryHandler : IRequestHandler<AssetInstallationQuery, AssetInstallationResult>
{
    private readonly IApplicationDbContext _context;
    private readonly ICompanyScopeService _companyScope;

    public AssetInstallationQueryHandler(IApplicationDbContext context, ICompanyScopeService companyScope)
    {
        _context = context;
        _companyScope = companyScope;
    }

    public async Task<AssetInstallationResult> Handle(AssetInstallationQuery request, CancellationToken cancellationToken)
    {
        var userCompanyId = await _companyScope.GetCurrentUserCompanyIdAsync();
        var asset = await _context.Assets.AsNoTracking()
            .Where(a => a.Id == request.AssetId)
            .Select(a => new { a.Id, a.CompanyId })
            .FirstOrDefaultAsync(cancellationToken);

        if (asset == null
            || (userCompanyId.HasValue && asset.CompanyId.HasValue && asset.CompanyId.Value != userCompanyId.Value))
            return new AssetInstallationResult(false);

        var row = await _context.AssetInstallations.AsNoTracking()
            .Where(i => i.Id == request.EntryId && i.AssetId == asset.Id)
            .Select(i => new
            {
                i.Id,
                i.AssetId,
                i.SystemPositionId,
                i.SystemPositionName,
                i.SystemInfoId,
                i.SystemInfoName,
                i.StartedAt,
                i.EndedAt,
                i.Note,
                i.Source,
                i.CorrectsEntryId,
                i.SupersededAt,
                i.LoggedByName
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (row == null)
            return new AssetInstallationResult(false);

        return new AssetInstallationResult(true, new AssetInstallationDetailDto(
            row.Id,
            row.AssetId,
            row.SystemPositionId,
            row.SystemPositionName,
            row.SystemInfoId,
            row.SystemInfoName,
            // Cột StartedAt/EndedAt là `timestamp with time zone`; chuẩn hoá Kind=Utc để JSON có hậu tố `Z`
            // (cùng quy ước với AssetTimelineQueryHandler — FE tự format theo giờ máy người dùng).
            AssetTimelineQueryHandler.ToApiKind(row.StartedAt),
            row.EndedAt.HasValue ? AssetTimelineQueryHandler.ToApiKind(row.EndedAt.Value) : null,
            row.Note,
            row.Source,
            row.Source == AssetInstallationSource.Backfill,
            row.CorrectsEntryId != null,
            row.CorrectsEntryId,
            row.SupersededAt.HasValue ? AssetTimelineQueryHandler.ToApiKind(row.SupersededAt.Value) : null,
            row.LoggedByName));
    }
}
