import { useCallback, useEffect, useState } from 'react';
import { Alert, Button, Empty, Skeleton, Tag, Timeline, Tooltip, Typography, theme } from 'antd';
import {
  AuditOutlined, CheckCircleOutlined, CheckOutlined, ClockCircleOutlined, CloseOutlined,
  CloudUploadOutlined, DeleteOutlined, DisconnectOutlined, EditOutlined, ExportOutlined,
  ImportOutlined, InboxOutlined, LockOutlined, PlusCircleOutlined,
  RollbackOutlined, SafetyCertificateOutlined, ScheduleOutlined, SendOutlined, StopOutlined,
  ToolOutlined, UndoOutlined, UnlockOutlined, UserOutlined,
} from '@ant-design/icons';
import { Link } from 'react-router-dom';
import { assetService } from '../services/asset.service';
import { formatDateTimeShort } from '../../../utils/format';
import {
  timelineEventKey, timelineRefRoute,
  type AssetTimelineEvent, type AssetTimelineResponse,
} from '../types/timeline';

const { Text } = Typography;

/** Số sự kiện mỗi trang khi bấm "Tải thêm" (LL-4 yêu cầu 6). */
const PAGE_SIZE = 20;

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
    default: return { color: token.colorTextTertiary, icon: <ClockCircleOutlined /> };
  }
}

/**
 * Tab "Lý lịch" của trang chi tiết thiết bị — dòng thời gian dọc (mới nhất trước) từ
 * `GET /api/v1/assets/{id}/timeline` (hợp đồng: docs/API.md).
 *
 * Trạng thái: đang tải (Skeleton) · rỗng ("Chưa có sự kiện lý lịch") · lỗi (thông báo + nút thử lại).
 * Phân trang bằng con trỏ: nút "Tải thêm" chỉ hiện khi server còn trả `nextCursor`; nối trang và khử
 * trùng theo khoá sự kiện (xem `timelineEventKey`).
 */
export default function AssetTimelineTab({ assetId }: { assetId: string }) {
  const { token } = theme.useToken();
  const [events, setEvents] = useState<AssetTimelineEvent[]>([]);
  const [nextCursor, setNextCursor] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [loadingMore, setLoadingMore] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // `append = false` → tải trang đầu (thay danh sách); `append = true` → nối thêm theo `cursor`.
  const load = useCallback(async (cursor: string | null, append: boolean) => {
    if (append) setLoadingMore(true); else setLoading(true);
    setError(null);
    try {
      const res = await assetService.getTimeline(assetId, cursor ? { limit: PAGE_SIZE, cursor } : { limit: PAGE_SIZE });
      const body = res.data as AssetTimelineResponse | undefined;
      const page = Array.isArray(body?.data) ? body!.data : [];
      setEvents(prev => {
        const seen = new Set(append ? prev.map(timelineEventKey) : []);
        const fresh = page.filter(e => !seen.has(timelineEventKey(e)));
        return append ? [...prev, ...fresh] : fresh;
      });
      setNextCursor(body?.pagination?.nextCursor ?? null);
    } catch (err: unknown) {
      const e = err as { response?: { status?: number; data?: { message?: string } } };
      // `e?.response?.status` là mã HTTP (KHÔNG phải enum phân quyền) — sweep S2 trong
      // `scripts/audit-sweeps.ps1` bỏ qua đúng dạng so sánh này (ngoại lệ đã ghi ở đầu sweep).
      setError(
        e?.response?.status === 404
          ? 'Không tìm thấy thiết bị (hoặc thiết bị ngoài phạm vi công ty của bạn).'
          : e?.response?.data?.message || 'Không thể tải lý lịch thiết bị.',
      );
      // Trang nối lỗi → giữ nguyên danh sách đã có (chỉ báo lỗi, không xoá dữ liệu đang xem).
      if (!append) { setEvents([]); setNextCursor(null); }
    } finally {
      if (append) setLoadingMore(false); else setLoading(false);
    }
  }, [assetId]);

  useEffect(() => { void load(null, false); }, [load]);

  if (loading) return <Skeleton active paragraph={{ rows: 5 }} />;

  if (error) {
    return (
      <Alert
        type="error"
        showIcon
        title="Không thể tải lý lịch thiết bị"
        description={error}
        action={<Button size="small" onClick={() => void load(null, false)}>Thử lại</Button>}
      />
    );
  }

  if (events.length === 0) {
    return <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description="Chưa có sự kiện lý lịch" />;
  }

  return (
    <div>
      <Timeline
        items={events.map(event => {
          const meta = eventMeta(event.eventType, token);
          const route = timelineRefRoute(event.refType, event.refId);
          return {
            key: timelineEventKey(event),
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
    </div>
  );
}
