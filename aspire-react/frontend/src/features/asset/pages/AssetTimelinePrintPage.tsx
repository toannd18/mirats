import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { Link, useParams, useSearchParams } from 'react-router-dom';
import { Alert, Button, Result, Skeleton } from 'antd';
import { PrinterOutlined, ReloadOutlined } from '@ant-design/icons';
import { getUserInfo } from '../../auth/services/auth';
import { assetService } from '../services/asset.service';
import { ASSET_STATUS_LABELS, normalizeAssetStatus, type AssetDetailDto } from '../types/asset';
import type { AssetTimelineEvent } from '../types/timeline';
import { formatDate, formatDateTimeShort, formatMoney } from '../../../utils/format';
import {
  PRINT_MAX_EVENTS,
  PRINT_PAGE_LIMIT,
  TimelineTooLargeError,
  formatEventTime,
  loadAllTimelineEvents,
  type TimelinePageLike,
} from '../utils/timelinePrint';
import './AssetTimelinePrintPage.css';

/**
 * [LL-6] Trang in RIÊNG "LÝ LỊCH THIẾT BỊ" — `/assets/:id/ly-lich/in?types=…`.
 *
 * - Mở ở TAB MỚI từ nút "In lý lịch" ở tab Lịch sử; KHÔNG có sidebar/menu (route ngoài AppLayout).
 * - Tự tải TOÀN BỘ sự kiện (limit 200/trang, cursor, đảo CŨ → MỚI) rồi **tự gọi `window.print()`**.
 * - Vượt 5.000 sự kiện ⇒ **KHÔNG in**, hiển thị thông báo gợi ý chọn "Sự kiện chính".
 * - Bố cục A4: tiêu đề, khối thông tin thiết bị, bảng sự kiện CŨ → MỚI, dấu ≈ cho thời điểm ước tính,
 *   [Đã đính chính], nhãn bộ lọc, footer (người in + thời điểm in + "Trang x / y" bằng @page margin box).
 * - Quyền: `assets.view`. Asset ngoài phạm vi/không tồn tại ⇒ trang "không tìm thấy".
 */
type PrintState =
  | { kind: 'loading' }
  | { kind: 'notFound' }
  | { kind: 'error'; message: string; code?: string }
  | { kind: 'tooLarge'; message: string }
  | { kind: 'ready'; asset: AssetDetailDto; events: AssetTimelineEvent[]; requests: number; loadedAt: string };

function InfoItem({ label, value, nowrap }: { label: string; value: string; nowrap?: boolean }) {  return (
    <div className="asset-info-item">
      <span className="asset-info-label">{label}:</span>
      <span className={`asset-info-value${nowrap ? ' nowrap' : ''}`}>{value || '—'}</span>
    </div>
  );
}

/** Nhãn người thực hiện trên bản in: dòng ước tính (backfill) không có người ghi → "Chuyển dữ liệu tự động". */
function actorLabel(event: AssetTimelineEvent): string {
  const name = event.actorName?.trim();
  if (name) return name;
  return event.isEstimated ? 'Chuyển dữ liệu tự động' : '—';
}

export default function AssetTimelinePrintPage() {
  const { id: assetId } = useParams<{ id: string }>();
  const [search] = useSearchParams();
  const types = search.get('types') ?? undefined;
  const filterLabel = types ? 'Sự kiện chính' : 'Tất cả sự kiện';

  const [state, setState] = useState<PrintState>({ kind: 'loading' });
  const [reloadKey, setReloadKey] = useState(0);
  const autoPrintedRef = useRef(false);

  const printer = useMemo(() => {
    const info = getUserInfo();
    const fullName = `${info.firstName ?? ''} ${info.lastName ?? ''}`.trim();
    return fullName || info.username || '—';
  }, []);

  const load = useCallback(async () => {
    if (!assetId) {
      setState({ kind: 'notFound' });
      return;
    }
    autoPrintedRef.current = false;
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

      const loadedAt = formatDateTimeShort(new Date().toISOString());
      setState({ kind: 'ready', asset, events: result.events, requests: result.requests, loadedAt });
      document.title = `Lý lịch thiết bị ${asset.assetTag}`;
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

  // [LL-6] In khi dữ liệu + layout + FONT đã sẵn sàng. `document.fonts.ready` đảm bảo font (Segoe UI /
  // ký tự Việt) tải xong TRƯỚC khi trình duyệt phân trang — in sớm có thể bị fallback font ⇒ sai số trang
  // và ngắt dòng. Dùng chung cho auto-print và nút "In lại".
  const printWhenReady = useCallback(async () => {
    try {
      await document.fonts?.ready;
    } catch {
      // Font Loading API không khả dụng → vẫn in (không chặn tính năng).
    }
    window.print();
  }, []);

  useEffect(() => {
    void load();
  }, [load, reloadKey]);

  // Tự mở hộp thoại in SAU khi dữ liệu + layout đã sẵn sàng (mỗi lần tải một lần).
  useEffect(() => {
    if (state.kind !== 'ready' || autoPrintedRef.current) return undefined;
    autoPrintedRef.current = true;
    const timer = window.setTimeout(() => {
      void printWhenReady();
    }, 500);
    return () => window.clearTimeout(timer);
  }, [state.kind, printWhenReady]);

  if (state.kind === 'loading') {
    return (
      <div className="asset-print-page">
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
      <div className="asset-print-page">
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
      <div className="asset-print-page">
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

  const { asset, events, loadedAt } = state;
  const statusLabel = ASSET_STATUS_LABELS[normalizeAssetStatus(asset.status)] ?? String(asset.status);
  const hasEstimated = events.some((event) => event.isEstimated);
  const assignedTo = asset.assignedTo?.name ?? '—';
  const modelLabel = [asset.model?.name, asset.model?.modelNumber].filter(Boolean).join(' · ');

  return (
    <div className="asset-print-page">
      <div className="print-toolbar">
        <Link to={`/assets/${assetId}`}>Quay lại thiết bị</Link>
        <Button type="primary" icon={<PrinterOutlined />} onClick={() => void printWhenReady()}>
          In lại
        </Button>
      </div>

      <header className="print-header">
        <h1>LÝ LỊCH THIẾT BỊ</h1>
        <div className="print-subtitle">
          <span>Mã: <strong>{asset.assetTag}</strong></span>
          <span aria-hidden="true">·</span>
          <span>{asset.name}</span>
          <span aria-hidden="true">·</span>
          <span>Bộ lọc: <strong>{filterLabel}</strong></span>
          <span aria-hidden="true">·</span>
          <span><strong>{events.length}</strong> sự kiện</span>
        </div>
      </header>

      <section className="asset-info">
        <div className="asset-info-grid">
          <InfoItem label="Mã tài sản" value={asset.assetTag} />
          <InfoItem label="Tên thiết bị" value={asset.name} />
          <InfoItem label="Số serial" value={asset.serial ?? ''} />
          <InfoItem label="Model" value={modelLabel} />
          <InfoItem label="Danh mục" value={asset.category?.name ?? ''} />
          <InfoItem label="Hãng sản xuất" value={asset.manufacturer?.name ?? ''} />
          <InfoItem label="Nhà cung cấp" value={asset.supplier?.name ?? ''} />
          <InfoItem label="Công ty" value={asset.company?.name ?? ''} />
          <InfoItem label="Vị trí" value={asset.location?.name ?? ''} />
          <InfoItem label="Trạng thái" value={asset.isConfirmed ? statusLabel : `${statusLabel} (chưa xác nhận)`} />
          <InfoItem label="Đang giữ" value={assignedTo} />
          <InfoItem label="Ngày mua" value={asset.purchaseDate ? formatDate(asset.purchaseDate) : "—"} nowrap />
          <InfoItem label="Giá mua" value={asset.purchaseCost != null ? formatMoney(asset.purchaseCost) : "—"} nowrap />
          <InfoItem label="Bảo hành" value={asset.warrantyMonths ? `${asset.warrantyMonths} tháng` : ''} />
          <InfoItem label="Số đơn hàng" value={asset.orderNumber ?? ''} />
          <InfoItem label="Cấp phát gần nhất" value={asset.lastCheckout ? formatDateTimeShort(asset.lastCheckout) : "—"} nowrap />
          <InfoItem label="Thu hồi gần nhất" value={asset.lastCheckin ? formatDateTimeShort(asset.lastCheckin) : "—"} nowrap />
          <InfoItem label="Kiểm kê kế tiếp" value={asset.nextAuditDate ? formatDate(asset.nextAuditDate) : "—"} nowrap />
        </div>
        {asset.notes && (
          <div className="asset-notes">
            <strong>Ghi chú:</strong> {asset.notes}
          </div>
        )}
      </section>

      <table className="timeline-print-table">
        <thead>
          <tr>
            <th className="col-time">Thời điểm</th>
            <th className="col-event">Sự kiện</th>
            <th>Chi tiết</th>
            <th className="col-actor">Người thực hiện</th>
          </tr>
        </thead>
        <tbody>
          {events.map((event) => (
            <tr key={event.id}>
              <td className="col-time">{formatEventTime(event)}</td>
              <td className="col-event">
                {event.title}
                {event.isCorrection && <span className="correction-tag"> [Đã đính chính]</span>}
              </td>
              <td>{event.details ?? ''}</td>
              <td className="col-actor">{actorLabel(event)}</td>
            </tr>
          ))}
        </tbody>
      </table>

      {hasEstimated && (
        <p className="print-footnote">
          ≈ Thời điểm ước tính — được suy ra khi chuyển dữ liệu cũ sang lý lịch (backfill), không phải mốc do
          người dùng ghi nhận.
        </p>
      )}

      {/* [LL-6] Khối chữ ký cuối bản in — `break-inside: avoid` để không bị tách khỏi trang cuối. */}
      <section className="signature-block">
        <div className="signature-date">…………, ngày …… tháng …… năm ………</div>
        <div className="signature-grid">
          <div className="signature-cell">
            <div className="signature-title">Người lập</div>
            <div className="signature-hint">(Ký, ghi rõ họ tên)</div>
            <div className="signature-space" />
            <div className="signature-name">Họ và tên: .................................</div>
          </div>
          <div className="signature-cell">
            <div className="signature-title">Phụ trách đơn vị</div>
            <div className="signature-hint">(Ký, ghi rõ họ tên)</div>
            <div className="signature-space" />
            <div className="signature-name">Họ và tên: .................................</div>
          </div>
        </div>
      </section>

      <footer className="print-footer">
        <span>Người in: {printer}</span>
        <span>Thời điểm in: {loadedAt}</span>
        <span>Bộ lọc: {filterLabel}</span>
      </footer>
    </div>
  );
}
