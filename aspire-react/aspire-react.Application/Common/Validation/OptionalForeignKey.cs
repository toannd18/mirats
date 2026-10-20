namespace aspire_react.Server.Application.Common.Validation;

/// <summary>
/// [FIX-GUID-EMPTY phần 2 — 2026-10-20] Quy ước cho trường **KHÓA NGOẠI TÙY CHỌN** trong lệnh Update
/// (patch semantics). Ba trạng thái phải phân biệt được:
/// <list type="bullet">
///   <item><c>null</c> (field VẮNG MẶT trong payload) → <b>giữ nguyên</b> liên kết hiện tại.</item>
///   <item><c>Guid.Empty</c> (chuỗi <c>00000000-0000-0000-0000-000000000000</c>) → <b>XÓA liên kết</b>
///     (gán <c>null</c>). FE gửi giá trị này khi người dùng bấm X (allowClear) trên ô select.</item>
///   <item>id thật → đặt liên kết; id phải <b>TỒN TẠI</b>, nếu không → 400 <c>RESOURCE_NOT_FOUND</c>
///     (không để Postgres 23503 nổ ra 500).</item>
/// </list>
/// <para>
/// Cách dùng trong handler:
/// <code>
/// if (request.SupplierId is not null)
///     entity.SupplierId = OptionalForeignKey.Cleared(request.SupplierId);   // null = xóa liên kết
/// </code>
/// và trước khi gán (hoặc trước khi lưu) kiểm tồn tại trên giá trị ĐÃ chuẩn hoá:
/// <code>
/// var supplierId = OptionalForeignKey.Cleared(request.SupplierId);
/// if (supplierId is not null &amp;&amp; !await _context.Suppliers.AnyAsync(s => s.Id == supplierId, ct))
///     return ... "RESOURCE_NOT_FOUND";
/// </code>
/// </para>
/// <para>
/// Ngược lại, trường khóa ngoại **BẮT BUỘC** trên entity (VD <c>system_positions.SystemInfoId</c>,
/// <c>asset_maintenances.CompanyId</c>) KHÔNG được phép xóa: <c>Guid.Empty</c> ở đó là KHÔNG HỢP LỆ →
/// 400 (handler/validator trả mã lỗi tương ứng), không bao giờ gán <c>null</c>.
/// </para>
/// </summary>
public static class OptionalForeignKey
{
    /// <summary>Chuẩn hoá giá trị client gửi: <c>Guid.Empty</c> ⇒ <c>null</c> (xóa liên kết); còn lại giữ nguyên.</summary>
    public static Guid? Cleared(Guid? sent) => sent == Guid.Empty ? null : sent;

    /// <summary>True khi client gửi <c>Guid.Empty</c> (yêu cầu XÓA liên kết) — dùng cho nhánh cần phân biệt.</summary>
    public static bool IsClearRequested(Guid? sent) => sent == Guid.Empty;

    /// <summary>
    /// Thông điệp + mã lỗi chuẩn khi id tham chiếu không tồn tại (đồng bộ với
    /// <c>ModelValidation</c> / <c>UserReferenceScope</c>).
    /// </summary>
    public static string NotFoundMessage(string field) => $"Trường tham chiếu không tồn tại: {field}.";
}
