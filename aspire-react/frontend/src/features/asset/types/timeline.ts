// ==================== [LL-4] Lý lịch thiết bị (asset timeline) ====================
// Hợp đồng DTO đầy đủ: docs/API.md § "Asset Timeline (Lý lịch thiết bị) — [LL-3]".
//
// Quy ước bắt buộc khi dùng các type này:
//  - `eventType` là chuỗi SCREAMING_SNAKE: hoặc giá trị riêng của sự kiện (INSTALL/REMOVE/MAINTENANCE/
//    MAINTENANCE_CAMPAIGN) hoặc tên `ActionType` (CREATE/UPDATE/CHECKOUT/…/UPDATE_REJECTED). Server bảo
//    đảm trường này luôn có giá trị, nhưng client KHÔNG được giả định tập giá trị là hữu hạn: giá trị lạ
//    phải rơi vào nhánh mặc định (icon/màu mặc định) chứ KHÔNG được ném lỗi.
//  - `occurredAt` / `maintenanceCompletionDate` là ISO-8601 UTC CÓ hậu tố `Z` ⇒ FE tự format theo múi giờ
//    máy người dùng. Không đọc ngày/giờ từ `details` (server KHÔNG bao giờ nhét ngày đã format vào đó).
//  - `refType`/`refId` chỉ để mở link; bản ghi nguồn có thể đã bị xoá cứng ⇒ link phải chịu được 404.

/** Một sự kiện trong dòng thời gian của thiết bị. */
export interface AssetTimelineEvent {
  /** Loại sự kiện — dùng để chọn icon/màu. */
  eventType: string;
  /** Thời điểm sự kiện xảy ra (UTC có `Z`) — KHÔNG phải thời điểm tạo bản ghi. */
  occurredAt: string;
  /** Tiêu đề tiếng Việt ghép sẵn bởi server. */
  title: string;
  /** Chi tiết phụ dạng văn bản (hệ thống/địa điểm/đối tượng/ghi chú), ngăn cách bằng " · ". */
  details: string | null;
  /** Người thực hiện; `null` khi không xác định (VD dòng backfill). */
  actorName: string | null;
  /** Loại bản ghi nguồn: `assetInstallation` | `actionLog` | `assetMaintenance` | `maintenanceCampaign`. */
  refType: string | null;
  /** Id bản ghi nguồn (để mở link) — có thể trỏ tới bản ghi đã bị xoá. */
  refId: string | null;
  /** `true` khi MỐC THỜI GIAN là ước tính (chỉ có ở sự kiện lắp của dòng backfill). */
  isEstimated: boolean;
  /** Ngày hoàn thành phiếu bảo dưỡng (UTC có `Z`); `null` = phiếu chưa hoàn thành. */
  maintenanceCompletionDate: string | null;
}

/** Khối phân trang của response (con trỏ ĐỤC — client chỉ truyền lại nguyên văn). */
export interface AssetTimelinePagination {
  limit: number;
  count: number;
  hasMore: boolean;
  /** `null` ⇒ hết dữ liệu (ẩn nút "Tải thêm"). */
  nextCursor: string | null;
}

/** Body response của `GET /api/v1/assets/{id}/timeline`. */
export interface AssetTimelineResponse {
  status: string;
  data: AssetTimelineEvent[];
  pagination: AssetTimelinePagination;
}

/**
 * Khoá khử trùng của một sự kiện khi nối trang.
 *
 * DTO timeline KHÔNG có trường `id` (mỗi sự kiện là dữ liệu DẪN XUẤT từ 4 nguồn, không có khoá riêng —
 * thêm `id` sẽ là thay đổi backend, ngoài phạm vi LL-4). Bộ tứ `(eventType, refType, refId, occurredAt)`
 * định danh duy nhất một sự kiện: một dòng lý lịch lắp đặt sinh 2 sự kiện khác `eventType`; sự kiện
 * ActionLog/phiếu/đợt có `refId` riêng; mốc thời gian giữ nguyên tick nên so sánh chuỗi là chính xác.
 * Mục đích của khử trùng là chống gọi lặp (double-click "Tải thêm"), không phải che lỗi phân trang.
 */
export function timelineEventKey(event: AssetTimelineEvent): string {
  return `${event.eventType}|${event.refType ?? ''}|${event.refId ?? ''}|${event.occurredAt}`;
}

/**
 * Bảng ánh xạ `refType` → route đã có trong app (đối chiếu `src/App.tsx`).
 *
 * | refType | Màn hình đích | Route |
 * |---------|---------------|-------|
 * | `maintenanceCampaign` | Chi tiết đợt bảo dưỡng | `/maintenance/campaigns/:id` ✔ |
 * | `assetMaintenance` | chỉ có DANH SÁCH (`/maintenances`), không có route chi tiết theo id | — |
 * | `assetInstallation` | không có màn hình riêng (nằm trong chính tab Lý lịch) | — |
 * | `actionLog` | không có màn hình riêng (bảng Lịch sử nằm ở tab Thông tin) | — |
 *
 * `refType` không có route ⇒ KHÔNG render link (LL-4 yêu cầu 5), tuyệt đối không trỏ tới màn hình
 * danh sách kèm filter giả để tránh gây hiểu nhầm là "đã mở đúng bản ghi".
 */
export function timelineRefRoute(refType: string | null, refId: string | null): string | null {
  if (!refType || !refId) return null;
  if (refType === 'maintenanceCampaign') return `/maintenance/campaigns/${refId}`;
  return null;
}
