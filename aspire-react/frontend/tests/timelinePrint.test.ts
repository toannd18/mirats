import test from 'node:test';
import assert from 'node:assert/strict';
import {
  PRINT_MAX_EVENTS,
  PRINT_PAGE_LIMIT,
  TimelineTooLargeError,
  loadAllTimelineEvents,
  type FetchTimelinePage,
  type TimelinePageLike,
} from '../src/features/asset/utils/timelinePrint.ts';
import type { AssetTimelineEvent } from '../src/features/asset/types/timeline.ts';

/**
 * [LL-6] Test hàm tải dữ liệu in (không cần trình duyệt):
 *  - nhiều trang qua cursor (limit 200),
 *  - vượt giới hạn 5.000 ⇒ TimelineTooLargeError (KHÔNG trả bản cắt bớt),
 *  - lỗi mạng ⇒ ném nguyên lỗi,
 *  - cursor lặp ⇒ dừng, chống vòng lặp vô hạn,
 *  - khử trùng id + đảo CŨ → MỚI.
 *
 * Chạy: xem hướng dẫn trong docs (esbuild bundle → node --test).
 */

function makeEvent(index: number): AssetTimelineEvent {
  return {
    id: `actionLog:${index}`,
    eventType: 'UPDATE',
    occurredAt: new Date(Date.UTC(2026, 0, 1, 0, 0, index)).toISOString(),
    title: `Sự kiện ${index}`,
    details: `Chi tiết ${index}`,
    actorName: 'admin',
    refType: 'actionLog',
    refId: `ref-${index}`,
    isEstimated: false,
    isCorrection: false,
    maintenanceCompletionDate: null,
  };
}

/** Giả lập server: trả các trang theo cursor `page-<n>`; API trả MỚI NHẤT TRƯỚC. */
function pagedFetcher(totalEvents: number, pageSize: number) {
  const calls: Array<{ cursor: string | null; limit: number }> = [];
  const fetchPage: FetchTimelinePage = async (cursor, limit) => {
    calls.push({ cursor, limit });
    const pageIndex = cursor ? Number(cursor.replace('page-', '')) : 0;
    const start = pageIndex * pageSize;
    const end = Math.min(start + pageSize, totalEvents);
    // Mô phỏng dữ liệu mới nhất trước: id giảm dần.
    const data: AssetTimelineEvent[] = [];
    for (let i = start; i < end; i += 1) data.push(makeEvent(totalEvents - 1 - i));
    const hasMore = end < totalEvents;
    const page: TimelinePageLike = {
      data,
      pagination: { hasMore, nextCursor: hasMore ? `page-${pageIndex + 1}` : null },
    };
    return page;
  };
  return { fetchPage, calls };
}

test('tải nhiều trang qua cursor, đảo CŨ → MỚI, dùng limit 200', async () => {
  const { fetchPage, calls } = pagedFetcher(450, PRINT_PAGE_LIMIT);
  const result = await loadAllTimelineEvents(fetchPage);

  assert.equal(result.requests, 3);
  assert.equal(result.total, 450);
  assert.deepEqual(calls.map((call) => call.limit), [200, 200, 200]);
  assert.deepEqual(calls.map((call) => call.cursor), [null, 'page-1', 'page-2']);
  // Trang 1 (mới nhất) có id 449..250; sau khi đảo, phần tử đầu là CŨ NHẤT (id 0).
  assert.equal(result.events[0].id, 'actionLog:0');
  assert.equal(result.events.at(-1)?.id, 'actionLog:449');
});

test('đúng 5.000 sự kiện nhưng còn trang ⇒ TimelineTooLargeError (không in bản cắt bớt)', async () => {
  const { fetchPage } = pagedFetcher(PRINT_MAX_EVENTS + 1, PRINT_PAGE_LIMIT);
  await assert.rejects(() => loadAllTimelineEvents(fetchPage), TimelineTooLargeError);
});

test('vượt 5.000 sự kiện ⇒ TimelineTooLargeError', async () => {
  const { fetchPage } = pagedFetcher(PRINT_MAX_EVENTS + 250, PRINT_PAGE_LIMIT);
  await assert.rejects(
    () => loadAllTimelineEvents(fetchPage),
    (error: unknown) => error instanceof TimelineTooLargeError && error.maxEvents === PRINT_MAX_EVENTS,
  );
});

test('đúng 5.000 sự kiện và hết dữ liệu ⇒ in được (không coi là vượt)', async () => {
  const { fetchPage } = pagedFetcher(PRINT_MAX_EVENTS, PRINT_PAGE_LIMIT);
  const result = await loadAllTimelineEvents(fetchPage);
  assert.equal(result.total, PRINT_MAX_EVENTS);
});

test('lỗi mạng/HTTP được ném nguyên trạng để trang in hiển thị lỗi', async () => {
  const failure = new Error('Network Error');
  const fetchPage: FetchTimelinePage = async () => {
    throw failure;
  };
  await assert.rejects(() => loadAllTimelineEvents(fetchPage), (error: unknown) => error === failure);
});

test('cursor lặp lại ⇒ dừng với lỗi (chống vòng lặp vô hạn)', async () => {
  const fetchPage: FetchTimelinePage = async () => ({
    data: [makeEvent(1)],
    pagination: { hasMore: true, nextCursor: 'same' },
  });
  await assert.rejects(() => loadAllTimelineEvents(fetchPage), /cursor lặp lại/i);
});

test('khử trùng id giữa các trang (server trả trùng do race)', async () => {
  let call = 0;
  const fetchPage: FetchTimelinePage = async () => {
    call += 1;
    if (call === 1) {
      return { data: [makeEvent(2), makeEvent(1)], pagination: { hasMore: true, nextCursor: 'page-2' } };
    }
    return { data: [makeEvent(1), makeEvent(0)], pagination: { hasMore: false, nextCursor: null } };
  };
  const result = await loadAllTimelineEvents(fetchPage);
  assert.deepEqual(result.events.map((event) => event.id), ['actionLog:0', 'actionLog:1', 'actionLog:2']);
});
