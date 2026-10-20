import type { AssetTimelineEvent } from '../types/timeline';
import { formatDateTime } from '../../../utils/format.ts';

/**
 * [LL-6] Tải TOÀN BỘ lý lịch để in. Tách khỏi component để test được logic phân trang/giới hạn.
 *
 * Hợp đồng API `GET /assets/{id}/timeline`:
 *  - `limit` mặc định 50, **tối đa 200** (server kẹp lại); phân trang CURSOR;
 *  - `pagination.hasMore` + `pagination.nextCursor`;
 *  - dữ liệu trả về **MỚI NHẤT TRƯỚC** — bản in cần **CŨ → MỚI** nên loader đảo lại.
 *
 * Quy tắc giới hạn (quyết định 2026-10-20): **KHÔNG BAO GIỜ in bản bị cắt bớt**.
 * Nếu lý lịch vượt `PRINT_MAX_EVENTS` thì ném `TimelineTooLargeError` để trang in hiển thị
 * thông báo gợi ý chọn bộ lọc "Sự kiện chính" — không gọi `window.print()`.
 */

/** Số sự kiện mỗi request khi in (bằng mức tối đa server cho phép). */
export const PRINT_PAGE_LIMIT = 200;

/** Giới hạn trên cho một lần in — vượt mốc này thì KHÔNG in. */
export const PRINT_MAX_EVENTS = 5000;

export interface TimelinePageLike {
  data: AssetTimelineEvent[];
  pagination?: {
    hasMore?: boolean;
    nextCursor?: string | null;
  } | null;
}

/** Hàm tải một trang — nhận cursor (null = trang đầu) + limit. */
export type FetchTimelinePage = (cursor: string | null, limit: number) => Promise<TimelinePageLike>;

export interface LoadAllTimelineOptions {
  /** Số sự kiện/trang (mặc định 200). */
  limit?: number;
  /** Giới hạn trên (mặc định 5.000). */
  maxEvents?: number;
}

export interface LoadAllTimelineResult {
  /** Sự kiện theo thứ tự hiển thị trên bản in: CŨ → MỚI. */
  events: AssetTimelineEvent[];
  /** Tổng số sự kiện đã tải. */
  total: number;
  /** Số request đã thực hiện (để log/chẩn đoán). */
  requests: number;
}

/** Lý lịch vượt giới hạn in — trang in phải hiển thị thông báo, KHÔNG in bản cắt bớt. */
export class TimelineTooLargeError extends Error {
  readonly maxEvents: number;

  constructor(maxEvents: number) {
    super(
      `Lý lịch thiết bị có hơn ${String(maxEvents)} sự kiện nên chưa thể in đầy đủ. ` +
        'Hãy chọn bộ lọc "Sự kiện chính" (hoặc thu hẹp phạm vi) rồi in lại — hệ thống không in bản bị cắt bớt.',
    );
    this.name = 'TimelineTooLargeError';
    this.maxEvents = maxEvents;
  }
}

/**
 * Tải toàn bộ sự kiện qua cursor, đảo về CŨ → MỚI, chặn ở `maxEvents`.
 *
 * @throws TimelineTooLargeError khi tổng số sự kiện vượt `maxEvents`
 * @throws Error khi cursor lặp lại (server trả cùng nextCursor) — tránh vòng lặp vô hạn
 * @throws lỗi mạng/HTTP từ `fetchPage` (trang in hiển thị lỗi, không in)
 */
export async function loadAllTimelineEvents(
  fetchPage: FetchTimelinePage,
  options: LoadAllTimelineOptions = {},
): Promise<LoadAllTimelineResult> {
  const limit = options.limit ?? PRINT_PAGE_LIMIT;
  const maxEvents = options.maxEvents ?? PRINT_MAX_EVENTS;

  if (!Number.isFinite(limit) || limit <= 0) throw new Error('limit phải là số dương.');
  if (!Number.isFinite(maxEvents) || maxEvents <= 0) throw new Error('maxEvents phải là số dương.');

  const collected: AssetTimelineEvent[] = [];
  const seenIds = new Set<string>();
  let cursor: string | null = null;
  let requests = 0;

  // Trần an toàn: mỗi vòng phải lấy được ít nhất 1 sự kiện mới, nếu không thì dừng.
  for (;;) {
    const page = await fetchPage(cursor, limit);
    requests += 1;

    const batch = Array.isArray(page?.data) ? page.data : [];
    for (const event of batch) {
      if (!event || !event.id || seenIds.has(event.id)) continue;
      seenIds.add(event.id);
      collected.push(event);
    }

    const hasMore = page?.pagination?.hasMore === true;
    const nextCursor = page?.pagination?.nextCursor ?? null;

    // Vượt giới hạn (kể cả đúng mốc nhưng còn trang) ⇒ dừng ngay, KHÔNG in bản cắt bớt.
    if (collected.length > maxEvents || (collected.length === maxEvents && hasMore)) {
      throw new TimelineTooLargeError(maxEvents);
    }

    if (!hasMore || !nextCursor) break;
    if (nextCursor === cursor) {
      throw new Error('Máy chủ trả cursor lặp lại — dừng tải để tránh vòng lặp vô hạn.');
    }
    cursor = nextCursor;
  }

  // API trả MỚI NHẤT TRƯỚC; bản in cần CŨ → MỚI.
  const events = [...collected].reverse();
  return { events, total: events.length, requests };
}

/**
 * [LL-6] Nhãn thời điểm của một sự kiện trên bản in: thêm tiền tố `≈` khi mốc thời gian là ƯỚC TÍNH
 * (dòng lý lịch backfill) để người đọc không nhầm là mốc do người dùng ghi nhận.
 *
 * Tự format `dd/MM/yyyy HH:mm` theo giờ máy (giống `utils/format.ts#formatDateTimeShort`) để module này
 * giữ được tính THUẦN — test chạy trực tiếp bằng Node (type stripping) không cần bundler/alias.
 */
export function formatEventTime(event: AssetTimelineEvent): string {
  const formatted = formatDateTime(event.occurredAt);
  const time = formatted === '-' ? String(event.occurredAt) : formatted;
  return event.isEstimated ? `≈ ${time}` : time;
}