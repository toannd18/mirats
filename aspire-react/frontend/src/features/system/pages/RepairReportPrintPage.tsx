import { useCallback, useEffect, useMemo, useState } from 'react';
import { useNavigate, useSearchParams } from 'react-router-dom';
import { Alert, Button, Checkbox, Skeleton, Space, Typography } from 'antd';
import { ArrowLeftOutlined, PrinterOutlined } from '@ant-design/icons';
import apiClient from '../../../services/api-client';
import { getUserInfo } from '../../auth/services/auth';
import { usePermission } from '../../../hooks/usePermission';
import { formatDate, formatDateTime, formatNumber } from '../../../utils/format';
import './RepairReportPrintPage.css';

const { Text } = Typography;

interface Summary {
  failures: number; openCount: number; completedCount: number; mttrDays: number | null;
  totalCost: number; missingCostCount: number; warrantyCount: number; warrantyRate: number;
  unassignedCount: number; invalidInstallationCount: number;
}
interface Group {
  periodKey: string; periodLabel: string; systemId: string | null; systemName: string;
  installationStatus: string; failures: number; openCount: number; completedCount: number;
  mttrDays: number | null; totalCost: number; missingCostCount: number; warrantyCount: number;
  warrantyRate: number;
}
interface Item {
  id: string; assetId: string; assetTag: string; assetName: string; modelName: string | null;
  categoryName: string | null; type: string; startDate: string; completionDate: string | null;
  isOpen: boolean; cost: number | null; isWarranty: boolean; systemId: string | null;
  systemName: string | null; installationStatus: string; periodKey: string;
}
interface Result {
  summary: Summary; groups: Group[]; items: Item[];
  scopeCompanyName: string; filterSummary: string;
}

const statusLabel = (s: string) => s === 'Invalid' ? 'Dữ liệu lý lịch không hợp lệ'
  : s === 'SameDay' ? 'Bắt đầu trong ngày'
    : s === 'AtStart' ? 'Đang lắp lúc 00:00' : 'Không thuộc hệ thống';

const typeLabel = (type: string) => ({
  Maintenance: 'Bảo trì định kỳ', Repair: 'Sửa chữa', Upgrade: 'Nâng cấp',
  HardwareSupport: 'Hỗ trợ phần cứng', SoftwareSupport: 'Hỗ trợ phần mềm',
  PatTest: 'PAT test', Calibration: 'Hiệu chuẩn', IncidentReport: 'Báo cáo sự cố',
} as Record<string, string>)[type] ?? type;

export default function RepairReportPrintPage() {
  const navigate = useNavigate();
  const [search] = useSearchParams();
  const canView = usePermission('reports.view');
  const [data, setData] = useState<Result | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [includeDetails, setIncludeDetails] = useState(search.get('details') !== '0');
  const [printedAt, setPrintedAt] = useState(() => formatDateTime(new Date().toISOString()));
  const [printedBy] = useState(() => {
    const info = getUserInfo();
    return [info.firstName, info.lastName].filter(Boolean).join(' ') || info.username || '—';
  });
  const queryString = useMemo(() => {
    const p = new URLSearchParams(search);
    p.delete('details');
    return p.toString();
  }, [search]);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const res = await apiClient.get(`/reports/repairs?${queryString}`);
      setData((res.data?.data ?? null) as Result | null);
      setPrintedAt(formatDateTime(new Date().toISOString()));
    } catch (err: unknown) {
      const e = err as { response?: { data?: { message?: string } } };
      setError(e?.response?.data?.message || 'Không tải được báo cáo');
    } finally {
      setLoading(false);
    }
  }, [queryString]);

  useEffect(() => { void load(); }, [load]);
  useEffect(() => {
    if (data) document.title = 'Báo cáo hỏng hóc – sửa chữa';
  }, [data]);

  const printWhenReady = async () => {
    try { await document.fonts?.ready; } catch { /* print anyway */ }
    window.print();
  };

  if (!canView) return <Alert type="error" showIcon title="Bạn không có quyền in báo cáo." />;
  if (loading) return <div className="repair-print-page"><Skeleton active paragraph={{ rows: 10 }} /></div>;
  if (error || !data) return <Alert type="error" showIcon title={error ?? 'Không có dữ liệu báo cáo'} />;

  const { summary } = data;

  return (
    <div className="repair-print-page">
      <div className="repair-print-toolbar">
        <Space wrap>
          <Button icon={<ArrowLeftOutlined />} onClick={() => navigate('/reports/repairs')}>Quay lại</Button>
          <Button type="primary" icon={<PrinterOutlined />} onClick={() => void printWhenReady()}>In báo cáo</Button>
          <Checkbox checked={includeDetails} onChange={(e) => setIncludeDetails(e.target.checked)}>
            Kèm chi tiết phiếu
          </Checkbox>
        </Space>
      </div>

      <header className="repair-print-header">
        <div className="repair-print-company">Đơn vị: {data.scopeCompanyName}</div>
        <h1>BÁO CÁO HỎNG HÓC – SỬA CHỮA</h1>
        <div className="repair-print-filter">{data.filterSummary}</div>
      </header>

      <div className="repair-print-kpis">
        <div><span>Số lần hỏng</span><strong>{summary.failures}</strong></div>
        <div><span>Đang mở</span><strong>{summary.openCount}</strong></div>
        <div><span>MTTR (ngày)</span><strong>{summary.mttrDays == null ? '—' : Number(summary.mttrDays).toFixed(1)}</strong></div>
        <div><span>Tổng chi phí</span><strong>{formatNumber(summary.totalCost)}</strong></div>
        <div><span>Thiếu chi phí</span><strong>{summary.missingCostCount}</strong></div>
        <div><span>Bảo hành</span><strong>{(summary.warrantyRate * 100).toFixed(0)}%</strong></div>
      </div>

      <table className="repair-print-table">
        <thead>
          <tr>
            <th>Kỳ</th><th>Hệ thống</th><th>Trạng thái lý lịch</th><th>Số lần hỏng</th>
            <th>Đang mở</th><th>Đã hoàn thành</th><th>MTTR (ngày)</th>
            <th>Tổng chi phí</th><th>Thiếu chi phí</th><th>Bảo hành</th><th>Tỷ lệ</th>
          </tr>
        </thead>
        <tbody>
          {data.groups.map((g) => (
            <tr key={`${g.periodKey}|${g.systemId ?? 'none'}|${g.installationStatus}`}>
              <td>{g.periodLabel}</td>
              <td>{g.systemName}</td>
              <td>{statusLabel(g.installationStatus)}</td>
              <td className="num">{g.failures}</td>
              <td className="num">{g.openCount}</td>
              <td className="num">{g.completedCount}</td>
              <td className="num">{g.mttrDays == null ? '—' : Number(g.mttrDays).toFixed(1)}</td>
              <td className="num">{formatNumber(g.totalCost)}</td>
              <td className="num">{g.missingCostCount}</td>
              <td className="num">{g.warrantyCount}</td>
              <td className="num">{(g.warrantyRate * 100).toFixed(0)}%</td>
            </tr>
          ))}
          <tr className="total-row">
            <td colSpan={3}>TỔNG</td>
            <td className="num">{summary.failures}</td>
            <td className="num">{summary.openCount}</td>
            <td className="num">{summary.completedCount}</td>
            <td className="num">{summary.mttrDays == null ? '—' : Number(summary.mttrDays).toFixed(1)}</td>
            <td className="num">{formatNumber(summary.totalCost)}</td>
            <td className="num">{summary.missingCostCount}</td>
            <td className="num">{summary.warrantyCount}</td>
            <td className="num">{(summary.warrantyRate * 100).toFixed(0)}%</td>
          </tr>
        </tbody>
      </table>

      {includeDetails && (
        <>
          <h2 className="repair-print-subtitle">Chi tiết phiếu sửa chữa</h2>
          <table className="repair-print-table repair-print-detail-table">
            <thead>
              <tr>
                <th>Mã tài sản</th><th>Tên tài sản</th><th>Danh mục / Model</th><th>Loại</th>
                <th>Hệ thống</th><th>Bắt đầu</th><th>Hoàn thành</th><th>Chi phí</th><th>Bảo hành</th>
              </tr>
            </thead>
            <tbody>
              {data.items.map((i) => (
                <tr key={i.id}>
                  <td>{i.assetTag}</td>
                  <td>{i.assetName}</td>
                  <td>{[i.categoryName, i.modelName].filter(Boolean).join(' / ') || '—'}</td>
                  <td>{typeLabel(i.type)}</td>
                  <td>{i.systemName ?? statusLabel(i.installationStatus)}</td>
                  <td>{formatDate(i.startDate)}</td>
                  <td>{i.completionDate ? formatDate(i.completionDate) : 'Đang mở'}</td>
                  <td className="num">{i.cost == null ? '—' : formatNumber(i.cost)}</td>
                  <td>{i.isWarranty ? 'Có' : 'Không'}</td>
                </tr>
              ))}
              <tr className="total-row">
                <td colSpan={7}>TỔNG ({data.items.length} phiếu)</td>
                <td className="num">{formatNumber(data.items.reduce((sum, i) => sum + (i.cost ?? 0), 0))}</td>
                <td />
              </tr>
            </tbody>
          </table>
        </>
      )}

      <div className="repair-print-notes">
        <strong>Ghi chú:</strong> Hệ thống được xác định theo lý lịch lắp đặt tại 00:00 ngày bắt đầu (giờ VN);
        nếu không có, lấy khoảng bắt đầu trong ngày. Bảo hành tính theo cờ “Bảo hành” của phiếu.
        MTTR tính theo ngày, chỉ gồm phiếu đã hoàn thành. “Đang mở” không tính vào MTTR.
      </div>

      <section className="repair-signature-block">
        <div className="repair-signature-date">........, ngày .... tháng .... năm .......</div>
        <div className="repair-signature-grid">
          <div><div className="signature-title">Người lập</div><div className="signature-hint">(Ký, ghi rõ họ tên)</div><div className="signature-space" /><div>Họ và tên: .................................</div></div>
          <div><div className="signature-title">Phụ trách đơn vị</div><div className="signature-hint">(Ký, ghi rõ họ tên)</div><div className="signature-space" /><div>Họ và tên: .................................</div></div>
        </div>
      </section>

      <footer className="repair-print-footer">
        <span>Người in: {printedBy}</span>
        <span>Thời điểm in: {printedAt}</span>
      </footer>

      <Text className="repair-print-hint">In A4 ngang, tỉ lệ 100%, tắt “Vừa trang”.</Text>
    </div>
  );
}
