import { useCallback, useEffect, useState } from 'react';
import { Link, useParams, useSearchParams } from 'react-router-dom';
import { Alert, Button, Result, Skeleton } from 'antd';
import { PrinterOutlined, ReloadOutlined } from '@ant-design/icons';
import dayjs from 'dayjs';
import { assetService } from '../services/asset.service';
import type { AssetDetailDto } from '../types/asset';
import type { AssetTimelineEvent } from '../types/timeline';
import {
  PRINT_MAX_EVENTS,
  PRINT_PAGE_LIMIT,
  TimelineTooLargeError,
  loadAllTimelineEvents,
  type TimelinePageLike,
} from '../utils/timelinePrint';

/**
 * [LL-6] Trang in RIÊNG "LÝ LỊCH THIẾT BỊ" — `/assets/:id/ly-lich/in?types=…`.
 *
 * - Mở ở TAB MỚI từ nút "In lý lịch" ở tab Lịch sử; KHÔNG có sidebar/menu (route ngoài AppLayout).
 * - Tự tải TOÀN BỘ sự kiện (limit 200/trang, cursor, đảo CŨ → MỚI) rồi tự gọi `window.print()`.
 * - Vượt 5.000 sự kiện ⇒ **KHÔNG in**, hiển thị thông báo gợi ý chọn "Sự kiện chính"
 *   (không bao giờ in bản bị cắt bớt).
 * - Quyền: `assets.view` (ProtectedRoute). Asset ngoài phạm vi/không tồn tại ⇒ trang "không tìm thấy".
 */
type PrintState =
  | { kind: 'loading' }
  | { kind: 'notFound' }
  | { kind: 'error'; message: string; code?: string }
  | { kind: 'tooLarge'; message: string }
  | { kind: 'ready'; asset: AssetDetailDto; events: AssetTimelineEvent[]; requests: number };

function formatTime(iso: string): string {
  const value = dayjs(iso);
  return value.isValid() ? value.format('DD/MM/YYYY HH:mm') : iso;
}

export default function AssetTimelinePrintPage() {
  const { id: assetId } = useParams<{ id: string }>();
  const [search] = useSearchParams();
  const types = search.get('types') ?? undefined;
  const filterLabel = types ? 'Sự kiện chính' : 'Tất cả sự kiện';

  const [state, setState] = useState<PrintState>({ kind: 'loading' });
  const [reloadKey, setReloadKey] = useState(0);

  const load = useCallback(async () => {
    if (!assetId) {
      setState({ kind: 'notFound' });
      return;
    }
    setState({ kind: 'loading' });
    try {
      const assetResponse = await assetService.get(assetId);
      const asset = assetResponse.data?.data as AssetDetailDto | undefined;
      if (!asset) {
        setState({ kind: 'notFound' });
        return;
      }

      const fetchPage = async (cursor: string | null, limit: number): Promise<TimelinePageLike> => {
        const response = await assetService.getTimeline(assetId, {
          limit,
          ...(cursor ? { cursor } : {}),
          ...(types ? { types } : {}),
        });
        return response.data as TimelinePageLike;
      };

      const result = await loadAllTimelineEvents(fetchPage, {
        limit: PRINT_PAGE_LIMIT,
        maxEvents: PRINT_MAX_EVENTS,
      });

      setState({ kind: 'ready', asset, events: result.events, requests: result.requests });
    } catch (error: unknown) {
      if (error instanceof TimelineTooLargeError) {
        setState({ kind: 'tooLarge', message: error.message });
        return;
      }
      const axiosError = error as {
        response?: { status?: number; data?: { message?: string; error_code?: string } };
        message?: string;
      };
      if (axiosError?.response?.status === 404) {
        setState({ kind: 'notFound' });
        return;
      }
      setState({
        kind: 'error',
        message: axiosError?.response?.data?.message || axiosError?.message || 'Không tải được lý lịch thiết bị.',
        code: axiosError?.response?.data?.error_code,
      });
    }
  }, [assetId, types]);

  useEffect(() => {
    void load();
  }, [load, reloadKey]);

  if (state.kind === 'loading') {
    return (
      <div style={{ maxWidth: 900, margin: '24px auto', padding: '0 16px' }}>
        <Skeleton active paragraph={{ rows: 8 }} />
      </div>
    );
  }

  if (state.kind === 'notFound') {
    return (
      <Result
        status="404"
        title="Không tìm thấy thiết bị"
        subTitle="Thiết bị không tồn tại hoặc bạn không có quyền xem lý lịch này."
        extra={<Link to="/assets">Quay lại danh sách tài sản</Link>}
      />
    );
  }

  if (state.kind === 'tooLarge') {
    return (
      <div style={{ maxWidth: 900, margin: '24px auto', padding: '0 16px' }}>
        <Alert
          type="warning"
          showIcon
          title="Lý lịch quá lớn để in đầy đủ"
          description={
            <div>
              <div>{state.message}</div>
              <div style={{ marginTop: 12 }}>
                <Link to={`/assets/${assetId}`}>Quay lại thiết bị</Link>
              </div>
            </div>
          }
        />
      </div>
    );
  }

  if (state.kind === 'error') {
    return (
      <div style={{ maxWidth: 900, margin: '24px auto', padding: '0 16px' }}>
        <Alert
          type="error"
          showIcon
          title="Không tải được lý lịch thiết bị"
          description={
            <div>
              <div>{state.message}</div>
              {state.code && <div style={{ marginTop: 4, fontSize: 12 }}>Mã lỗi: {state.code}</div>}
              <div style={{ marginTop: 12 }}>
                <Button icon={<ReloadOutlined />} onClick={() => setReloadKey((value) => value + 1)}>
                  Thử lại
                </Button>
              </div>
            </div>
          }
        />
      </div>
    );
  }

  const { asset, events } = state;

  return (
    <div className="asset-print-page" style={{ maxWidth: 1000, margin: '16px auto', padding: '0 16px' }}>
      <div className="print-toolbar" style={{ display: 'flex', justifyContent: 'flex-end', gap: 8, marginBottom: 12 }}>
        <Link to={`/assets/${assetId}`}>Quay lại thiết bị</Link>
        <Button type="primary" icon={<PrinterOutlined />} onClick={() => window.print()}>
          In lại
        </Button>
      </div>

      <h1 style={{ textAlign: 'center', marginBottom: 4 }}>LÝ LỊCH THIẾT BỊ</h1>
      <div style={{ textAlign: 'center', marginBottom: 16 }}>
        Mã: <strong>{asset.assetTag}</strong> · {asset.name} · Bộ lọc: <strong>{filterLabel}</strong>
      </div>

      <table style={{ width: '100%', borderCollapse: 'collapse' }}>
        <thead>
          <tr>
            <th style={{ border: '1px solid #999', padding: 6, textAlign: 'left', width: '16%' }}>Thời điểm</th>
            <th style={{ border: '1px solid #999', padding: 6, textAlign: 'left', width: '24%' }}>Sự kiện</th>
            <th style={{ border: '1px solid #999', padding: 6, textAlign: 'left' }}>Chi tiết</th>
            <th style={{ border: '1px solid #999', padding: 6, textAlign: 'left', width: '18%' }}>Người thực hiện</th>
          </tr>
        </thead>
        <tbody>
          {events.map((event) => (
            <tr key={event.id}>
              <td style={{ border: '1px solid #999', padding: 6, verticalAlign: 'top' }}>
                {event.isEstimated ? '≈ ' : ''}
                {formatTime(event.occurredAt)}
              </td>
              <td style={{ border: '1px solid #999', padding: 6, verticalAlign: 'top' }}>
                {event.title}
                {event.isCorrection ? ' [Đã đính chính]' : ''}
              </td>
              <td style={{ border: '1px solid #999', padding: 6, verticalAlign: 'top' }}>{event.details ?? ''}</td>
              <td style={{ border: '1px solid #999', padding: 6, verticalAlign: 'top' }}>{event.actorName ?? '—'}</td>
            </tr>
          ))}
        </tbody>
      </table>

      <div style={{ marginTop: 12, fontSize: 12 }}>
        Tổng: <strong>{events.length}</strong> sự kiện (đã tải qua {state.requests} request).
        {events.some((event) => event.isEstimated) && (
          <div style={{ marginTop: 4 }}>≈ Thời điểm ước tính (được suy ra khi chuyển dữ liệu cũ sang lý lịch).</div>
        )}
      </div>
    </div>
  );
}
