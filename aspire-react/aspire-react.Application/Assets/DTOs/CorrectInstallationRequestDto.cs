namespace aspire_react.Server.Application.Assets.DTOs;

/// <summary>
/// [LL-5] Body của <c>POST /api/v1/assets/{id}/installations/{entryId}/correct</c>.
/// <para>
/// Patch-semantics: MỌI trường optional (trừ <see cref="Reason"/>) — chỉ trường được GỬI mới bị coi là "sửa";
/// không gửi (null) = giữ nguyên giá trị cũ. Không có thay đổi thực sự ⇒ 400 <c>CORRECTION_NO_CHANGES</c>.
/// </para>
/// </summary>
/// <param name="StartedAt">Mốc bắt đầu gắn (UTC). null = giữ nguyên.</param>
/// <param name="EndedAt">
/// Mốc tháo/điều chuyển (UTC). CHỈ nhận khi dòng ĐÃ ĐÓNG; dòng đang mở ⇒ 400 <c>INSTALLATION_CLOSE_USE_CHECKIN</c>
/// (muốn đóng dòng đang mở thì dùng Thu hồi/Lưu trữ).
/// </param>
/// <param name="SystemPositionId">
/// Vị trí (dùng khi ghi nhầm vị trí). CHỈ nhận khi dòng ĐÃ ĐÓNG; dòng đang mở ⇒ 400
/// <c>INSTALLATION_OPEN_POSITION_LOCKED</c> (đổi vị trí hiện tại = Thu hồi + Cấp phát lại).
/// </param>
/// <param name="Note">Ghi chú lý lịch của bản mới. null = giữ nguyên; chuỗi rỗng = xoá.</param>
/// <param name="Reason">LÝ DO đính chính — BẮT BUỘC (10..500 ký tự), lưu vào ActionLog (audit).</param>
public record CorrectInstallationRequestDto(
    DateTime? StartedAt,
    DateTime? EndedAt,
    Guid? SystemPositionId,
    string? Note,
    string? Reason);
