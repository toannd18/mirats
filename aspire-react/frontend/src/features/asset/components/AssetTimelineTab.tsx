import { useCallback, useEffect, useState } from 'react';
import { Alert, App, Button, Empty, Grid, Skeleton, Tag, Timeline, Tooltip, Typography, theme, Segmented } from 'antd';
import {
  AuditOutlined, CheckCircleOutlined, CheckOutlined, ClockCircleOutlined, CloseOutlined,
  CloudUploadOutlined, DeleteOutlined, DiffOutlined, DisconnectOutlined, EditOutlined, ExportOutlined,
  ImportOutlined, InboxOutlined, LockOutlined, PlusCircleOutlined,
  PrinterOutlined, RollbackOutlined, SafetyCertificateOutlined, ScheduleOutlined, SendOutlined, StopOutlined,
  ToolOutlined, UndoOutlined, UnlockOutlined, UserOutlined,
} from '@ant-design/icons';
import { Link } from 'react-router-dom';
import { assetService } from '../services/asset.service';
import { formatDateTimeShort } from '../../../utils/format';
import { usePermission } from '../../../hooks/usePermission';
import AssetInstallationCorrectionModal from './AssetInstallationCorrectionModal';
import { timelineRefRoute, type AssetTimelineEvent, type AssetTimelineResponse } from '../types/timeline';

const { Text } = Typography;

/** Số sự kiện mỗi trang khi bấm "Tải thêm" (LL-4 yêu cầu 6). */
const PAGE_SIZE = 20;

/**
 * [LL-4b] Nhóm "Sự kiện chính" — 4 loại riêng của lý lịch, gửi lên server qua tham số `types`.
 * Giá trị là danh sách `eventType` ngăn bằng dấu phẩy (hợp đồng: docs/API.md § Asset Timeline).
 */
const MAIN_EVENT_TYPES = 'INSTALL,REMOVE,MAINTENANCE,MAINTENANCE_CAMPAIGN';

type TimelineFilter = 'main' | 'all';

/** Trạng thái lỗi đã tách sẵn: mô tả (KHÔNG lặp tiêu đề) + mã lỗi/HTTP để hiển thị riêng. */
interface TimelineError {
  description: string;
  code: string | null;
}

type TimelineToken = ReturnType<typeof theme.useToken>['token'];

/**
 * Icon + màu cho từng `eventType`. Màu lấy từ theme token của antd (`theme.useToken()`), KHÔNG hard-code
 * hex — đổi theme là đổi theo. Giá trị lạ (eventType mới do server thêm sau này) rơi vào nhánh mặc định
 * (đồng hồ, màu chữ phụ) ⇒ không bao giờ trắng màn hình / không ném lỗi.
 *
 * Nhãn tiếng Việt KHÔNG lặp lại ở đây: `title` do server ghép sẵn (nguồn sự thật duy nhất) để màn Lý lịch
 * và màn Lịch sử không lệch nhau.
 */
function eventMeta(eventType: string, token: TimelineToken): { color: string; icon: React.ReactNode } {
  switch (eventType) {
    // ── Sự kiện riêng của lý lịch (không đến từ ActionLog) ──
    case 'INSTALL': return { color: token.colorSuccess, icon: <ToolOutlined /> };
    case 'REMOVE': return { color: token.colorWarning, icon: <DisconnectOutlined /> };
    case 'MAINTENANCE': return { color: token.colorPrimary, icon: <ToolOutlined /> };
    case 'MAINTENANCE_CAMPAIGN': return { color: token.colorInfo, icon: <ScheduleOutlined /> };
    // ── Sự kiện đến từ ActionLog (tên ActionType dạng SCREAMING_SNAKE) ──
    case 'CREATE': return { color: token.colorSuccess, icon: <PlusCircleOutlined /> };
    case 'UPDATE': return { color: token.colorPrimary, icon: <EditOutlined /> };
    case 'UPDATE_REJECTED': return { color: token.colorError, icon: <StopOutlined /> };
    case 'DELETE': return { color: token.colorError, icon: <DeleteOutlined /> };
    case 'CHECKOUT': return { color: token.colorPrimary, icon: <SendOutlined /> };
    case 'CHECKIN': return { color: token.colorWarning, icon: <RollbackOutlined /> };
    case 'AUDIT': return { color: token.colorInfo, icon: <AuditOutlined /> };
    case 'ARCHIVE': return { color: token.colorWarning, icon: <InboxOutlined /> };
    case 'UNARCHIVE': return { color: token.colorSuccess, icon: <UndoOutlined /> };
    case 'CONFIRM': return { color: token.colorSuccess, icon: <CheckCircleOutlined /> };
    case 'ACCEPT': return { color: token.colorSuccess, icon: <CheckOutlined /> };
    case 'DECLINE': return { color: token.colorError, icon: <CloseOutlined /> };
    case 'IMPORT': return { color: token.colorInfo, icon: <ImportOutlined /> };
    case 'EXPORT': return { color: token.colorTextTertiary, icon: <ExportOutlined /> };
    case 'STOCK_IN': return { color: token.colorSuccess, icon: <ImportOutlined /> };
    case 'MARK_DAMAGED': return { color: token.colorError, icon: <StopOutlined /> };
    case 'DISPOSE': return { color: token.colorError, icon: <DeleteOutlined /> };
    case 'CLOSE': return { color: token.colorTextTertiary, icon: <LockOutlined /> };
    case 'REOPEN': return { color: token.colorWarning, icon: <UnlockOutlined /> };
    case 'INSPECT': return { color: token.colorInfo, icon: <SafetyCertificateOutlined /> };
    case 'PUBLISH': return { color: token.colorPrimary, icon: <CloudUploadOutlined /> };
    case 'COMPLETE': return { color: token.colorSuccess, icon: <CheckCircleOutlined /> };
    // [LL-5] Sự kiện ĐÍNH CHÍNH lý lịch lắp đặt — icon/màu RIÊNG (không rơi vào nhánh mặc định).
    case 'CORRECT': return { color: token.colorInfo, icon: <DiffOutlined /> };
    default: return { color: token.colorTextTertiary, icon: <ClockCircleOutlined /> };
  }
}

/**
 * Tab "Lý lịch" của trang chi tiết thiết bị — dòng thời gian dọc (mới nhất trước) từ
 * `GET /api/v1/assets/{id}/timeline` (hợp đồng: docs/API.md).
 *
 * [LL-4b] Bộ chọn nhóm sự kiện: "Sự kiện chính" (mặc định — INSTALL/REMOVE/MAINTENANCE/MAINTENANCE_CAMPAIGN,
 * lọc ở SERVER qua `types`) hoặc "Tất cả". Đổi nhóm ⇒ tải lại từ trang đầu.
 *
 * Trạng thái: đang tải (Skeleton) · rỗng · lỗi (mô tả + mã lỗi, nút thử lại). Phân trang bằng con trỏ:
 * nút "Tải thêm" chỉ hiện khi server còn trả `nextCursor`; nối trang và khử trùng theo `id` sự kiện do
 * server cấp (`AssetTimelineEvent.id`) — KHÔNG ghép khoá ở client.
 */
export default function AssetTimelineTab({ assetId }: { assetId: string }) {
  const { token } = theme.useToken();
  const { message } = App.useApp();
  const screens = Grid.useBreakpoint();
  // < 576px (VD 390px): nút "Thử lại" xuống dòng riêng thay vì nằm cạnh phần mô tả.
  const isNarrow = !screens.sm;
  // [LL-5] Nút "Đính chính" chỉ hiện khi có quyền riêng (không gộp vào assets.edit).
  const canCorrect = usePermission('assets.correct');

  const [filter, setFilter] = useState<TimelineFilter>('main');
  const [events, setEvents] = useState<AssetTimelineEvent[]>([]);
  const [nextCursor, setNextCursor] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [loadingMore, setLoadingMore] = useState(false);
  const [error, setError] = useState<TimelineError | null>(null);
  // Id dòng lý lịch đang đính chính (null = modal đóng).
  const [correctEntryId, setCorrectEntryId] = useState<string | null>(null);

  // `append = false` → tải trang đầu (thay danh sách); `append = true` → nối thêm theo `cursor`.
  const load = useCallback(async (cursor: string | null, append: boolean) => {
    if (append) setLoadingMore(true); else setLoading(true);
    setError(null);
    try {
      const types = filter === 'main' ? MAIN_EVENT_TYPES : undefined;
      const res = await assetService.getTimeline(assetId, cursor ? { limit: PAGE_SIZE, cursor, types } : { limit: PAGE_SIZE, types });
      const body = res.data as AssetTimelineResponse | undefined;
      const page = Array.isArray(body?.data) ? body!.data : [];
      setEvents(prev => {
        const seen = new Set(append ? prev.map(e => e.id) : []);
        const fresh = page.filter(e => !seen.has(e.id));
        return append ? [...prev, ...fresh] : fresh;
      });
      setNextCursor(body?.pagination?.nextCursor ?? null);
    } catch (err: unknown) {
      const e = err as {
        response?: { status?: number; data?: { message?: string; error_code?: string } };
      };
      // `e?.response?.status` là mã HTTP (KHÔNG phải enum nghiệp vụ). Sweep S2 trong
      // `scripts/audit-sweeps.ps1` chỉ bỏ qua dòng có ĐÚNG chuỗi `response?.status`, nên phép so sánh 404
      // phải viết INLINE trên chính dòng đó (không tách ra biến `status`).
      const status = e?.response?.status;
      const errorCode = e?.response?.data?.error_code;
      setError({
        // Mô tả KHÔNG lặp lại tiêu đề của Alert ("Không thể tải lý lịch thiết bị").
        description: e?.response?.status === 404
          ? 'Thiết bị không tồn tại hoặc nằm ngoài phạm vi công ty của bạn.'
          : (e?.response?.data?.message || 'Vui lòng thử lại sau.'),
        code: errorCode ?? (status ? `HTTP ${status}` : null),
      });
      // Trang nối lỗi → giữ nguyên danh sách đã có (chỉ báo lỗi, không xoá dữ liệu đang xem).
      if (!append) { setEvents([]); setNextCursor(null); }
    } finally {
      if (append) setLoadingMore(false); else setLoading(false);
    }
  }, [assetId, filter]);

  useEffect(() => { void load(null, false); }, [load]);

  // [LL-6] Mở trang in RIÊNG ở tab mới, truyền đúng bộ lọc đang chọn (chỉ "Sự kiện chính" mới có `types`).
  const openPrintPage = () => {
    const query = filter === 'main' ? `?types=${MAIN_EVENT_TYPES}` : '';
    window.open(`/assets/${assetId}/ly-lich/in${query}`, '_blank', 'noopener');
  };

  const filterPicker = (
    <div style={{ marginBottom: 12, display: 'flex', gap: 8, alignItems: 'center', flexWrap: 'wrap' }}>
      <Tooltip title={`Sự kiện chính: lắp đặt, tháo khỏi vị trí, phiếu bảo dưỡng và đợt bảo dưỡng.`}>
        <Segmented<TimelineFilter>
          value={filter}
          onChange={(value) => setFilter(value)}
          options={[
            { label: 'Sự kiện chính', value: 'main' },
            { label: 'Tất cả', value: 'all' },
          ]}
        />
      </Tooltip>
      <Button icon={<PrinterOutlined />} onClick={openPrintPage}>
        In lý lịch
      </Button>
    </div>
  );

  if (loading) return <Skeleton active paragraph={{ rows: 5 }} />;

  if (error) {
    return (
      <Alert
        type="error"
        showIcon
        title="Không thể tải lý lịch thiết bị"
        description={
          <div>
            <div>{error.description}</div>
            {error.code && (
              <div style={{ marginTop: 4, fontSize: 12, color: token.colorTextSecondary }}>
                Mã lỗi: {error.code}
              </div>
            )}
            {isNarrow && (
              <div style={{ marginTop: 8 }}>
                <Button size="small" onClick={() => void load(null, false)}>Thử lại</Button>
              </div>
            )}
          </div>
        }
        action={isNarrow ? undefined : <Button size="small" onClick={() => void load(null, false)}>Thử lại</Button>}
      />
    );
  }

  if (events.length === 0) {
    return (
      <div>
        {filterPicker}
        <Empty
          image={Empty.PRESENTED_IMAGE_SIMPLE}
          description={filter === 'main' ? 'Chưa có sự kiện chính nào — chọn "Tất cả" để xem toàn bộ.' : 'Chưa có sự kiện lý lịch'}
        />
      </div>
    );
  }

  return (
    <div>
      {filterPicker}
      <Timeline
        items={events.map(event => {
          const meta = eventMeta(event.eventType, token);
          const route = timelineRefRoute(event.refType, event.refId);
          // [LL-5] Chỉ sự kiện của nguồn lý lịch lắp đặt mới đính chính được; cả INSTALL và REMOVE của
          // CÙNG một dòng mở CÙNG một form (dùng chung `refId` = id dòng).
          const correctableEntryId = canCorrect && event.refType === 'assetInstallation' ? event.refId : null;
          return {
            // [LL-4b] `id` do server cấp, ổn định giữa các trang ⇒ dùng thẳng làm key.
            key: event.id,
            // antd 6: Timeline item.dot deprecated → `icon` (màu giữ ở span ngoài theo theme token).
            icon: <span style={{ color: meta.color, fontSize: 14 }}>{meta.icon}</span>,
            content: (
              <div style={{ wordBreak: 'break-word' }}>
                <div style={{ display: 'flex', alignItems: 'baseline', gap: 6, flexWrap: 'wrap' }}>
                  {route
                    ? <Link to={route}><Text strong>{event.title}</Text></Link>
                    : <Text strong>{event.title}</Text>}
                  {event.isEstimated && (
                    <Tooltip title="Thời điểm này được suy ra khi chuyển dữ liệu cũ sang lý lịch (backfill), không phải mốc do người dùng ghi nhận.">
                      <Tag color="warning" style={{ marginInlineEnd: 0 }}>Thời điểm ước tính</Tag>
                    </Tooltip>
                  )}
                  {event.isCorrection && (
                    <Tooltip title="Bản ghi trước đã bị thay thế, xem lý do ở sự kiện Đính chính.">
                      <Tag color="processing" style={{ marginInlineEnd: 0 }}>Đã đính chính</Tag>
                    </Tooltip>
                  )}
                  {correctableEntryId && (
                    <Button
                      type="link"
                      size="small"
                      icon={<DiffOutlined />}
                      style={{ marginInlineStart: 'auto', padding: 0 }}
                      onClick={() => setCorrectEntryId(correctableEntryId)}
                    >
                      Đính chính
                    </Button>
                  )}
                </div>
                <div style={{ fontSize: 12, color: token.colorTextSecondary }}>
                  {formatDateTimeShort(event.occurredAt)}
                  {event.actorName && <span> · <UserOutlined /> {event.actorName}</span>}
                </div>
                {event.eventType === 'MAINTENANCE' && (
                  <div style={{ marginTop: 2 }}>
                    {event.maintenanceCompletionDate
                      ? <Text style={{ fontSize: 12 }}>Hoàn thành: {formatDateTimeShort(event.maintenanceCompletionDate)}</Text>
                      : <Tag style={{ marginInlineEnd: 0 }}>Chưa hoàn thành</Tag>}
                  </div>
                )}
                {event.details && (
                  <div style={{ fontSize: 13, marginTop: 2, color: token.colorText }}>{event.details}</div>
                )}
              </div>
            ),
          };
        })}
      />

      {nextCursor && (
        <div style={{ textAlign: 'center', marginTop: 4 }}>
          <Button loading={loadingMore} onClick={() => void load(nextCursor, true)}>Tải thêm</Button>
        </div>
      )}

      {/* [LL-5] Modal đính chính: mở từ nút trên sự kiện INSTALL/REMOVE của CÙNG một dòng lý lịch. */}
      <AssetInstallationCorrectionModal
        open={correctEntryId !== null}
        assetId={assetId}
        entryId={correctEntryId}
        onClose={() => setCorrectEntryId(null)}
        onSuccess={(msg) => {
          setCorrectEntryId(null);
          void message.success(msg);
          // Tải lại TRANG 1 và GIỮ NGUYÊN bộ lọc đang chọn.
          void load(null, false);
        }}
      />
    </div>
  );
}
