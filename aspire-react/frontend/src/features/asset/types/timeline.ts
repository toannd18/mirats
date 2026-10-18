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
  /**
   * [LL-4b] Id ỔN ĐỊNH do SERVER sinh (`"<nguồn>:<id bản ghi gốc>"`, VD `install:…`, `actionLog:…`) — dùng
   * làm React key và khoá khử trùng khi nối trang. Client KHÔNG tự dựng lại id; id phải giữ nguyên giữa
   * các trang nên so sánh chuỗi là đủ.
   */
  id: string;
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
  /**
   * [LL-5] `true` khi sự kiện thuộc một BẢN ĐÍNH CHÍNH (`CorrectsEntryId != null`) — FE gắn Tag
   * "Đã đính chính". Bản bị thay thế bị ẩn khỏi timeline (xem lý do ở sự kiện `CORRECT`).
   */
  isCorrection: boolean;
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

// ==================== [LL-5] Đính chính lý lịch lắp đặt ====================

/** Nguồn của một dòng lý lịch (enum backend serialize thành chuỗi). */
export type AssetInstallationSource = 'Api' | 'Import' | 'Backfill' | 'Correction';

/**
 * Chi tiết MỘT dòng lý lịch lắp đặt — nguồn dữ liệu cho form "Đính chính"
 * (`GET /api/v1/assets/{assetId}/installations/{entryId}`).
 *
 * Vì sao cần endpoint riêng: DTO timeline chỉ có 1 mốc (`occurredAt`) và KHÔNG có `note`/`source`/
 * `systemPositionId` — không đủ để dựng form đính chính.
 */
export interface AssetInstallationDetail {
  id: string;
  assetId: string;
  systemPositionId: string | null;
  systemPositionName: string | null;
  systemInfoId: string | null;
  systemInfoName: string | null;
  /** [UTC có `Z`] Mốc bắt đầu gắn. */
  startedAt: string;
  /** [UTC có `Z`] `null` = dòng ĐANG MỞ (chỉ sửa được mốc bắt đầu + ghi chú). */
  endedAt: string | null;
  note: string | null;
  source: AssetInstallationSource;
  /** `true` = mốc thời gian ƯỚC TÍNH (dòng backfill) ⇒ sau khi đính chính sẽ mất nhãn ước tính. */
  isEstimated: boolean;
  /** `true` = chính dòng này là một bản đính chính. */
  isCorrection: boolean;
  correctsEntryId: string | null;
  /** [UTC] `null` = dòng đang hiệu lực; khác null = đã bị đính chính (không sửa được nữa). */
  supersededAt: string | null;
  loggedByName: string | null;
}
