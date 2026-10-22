import { useCallback, useEffect, useMemo, useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import dayjs from 'dayjs';
import {
  Alert, App, Button, Card, Col, Empty, Row, Select, Space, Spin, Statistic, Table, Tag, Typography,
} from 'antd';
import { ArrowLeftOutlined, ReloadOutlined } from '@ant-design/icons';
import apiClient from '../../../services/api-client';
import { formatDate, formatNumber } from '../../../utils/format';
import { usePermission } from '../../../hooks/usePermission';
import { MAINTENANCE_TYPE_LABELS } from '../../maintenance/components/MaintenanceTable';

const { Title, Text } = Typography;

type Period = 'month' | 'quarter' | 'year';
type TypeGroup = 'repair_incident' | 'repair_incident_hardware' | 'all';

interface RefOption { id: string; name: string }

interface RepairSummary {
  failures: number;
  openCount: number;
  completedCount: number;
  mttrDays: number | null;
  totalCost: number;
  missingCostCount: number;
  warrantyCount: number;
  warrantyRate: number;
  unassignedCount: number;
  invalidInstallationCount: number;
}

interface RepairGroup {
  periodKey: string;
  periodLabel: string;
  systemId: string | null;
  systemName: string;
  installationStatus: string;
  failures: number;
  openCount: number;
  completedCount: number;
  mttrDays: number | null;
  totalCost: number;
  missingCostCount: number;
  warrantyCount: number;
  warrantyRate: number;
}

interface RepairItem {
  id: string;
  assetId: string;
  assetTag: string;
  assetName: string;
  modelName: string | null;
  categoryName: string | null;
  type: string;
  startDate: string;
  completionDate: string | null;
  isOpen: boolean;
  cost: number | null;
  isWarranty: boolean;
  systemId: string | null;
  systemName: string | null;
  installationStatus: string;
  periodKey: string;
}

interface RepairResponse {
  summary: RepairSummary;
  groups: RepairGroup[];
  items: RepairItem[];
}

const emptyResponse: RepairResponse = {
  summary: {
    failures: 0, openCount: 0, completedCount: 0, mttrDays: null, totalCost: 0,
    missingCostCount: 0, warrantyCount: 0, warrantyRate: 0, unassignedCount: 0,
    invalidInstallationCount: 0,
  },
  groups: [],
  items: [],
};

function statusLabel(status: string): string {
  if (status === 'Invalid') return 'Dữ liệu lý lịch không hợp lệ';
  if (status === 'SameDay') return 'Bắt đầu trong ngày';
  if (status === 'AtStart') return 'Đang lắp lúc 00:00';
  return 'Không thuộc hệ thống';
}

function statusColor(status: string): string {
  if (status === 'Invalid') return 'red';
  if (status === 'Unassigned') return 'default';
  return 'blue';
}

export default function RepairReportPage() {
  const { message } = App.useApp();
  const navigate = useNavigate();
  const canView = usePermission('reports.view');

  const now = dayjs();
  const [period, setPeriod] = useState<Period>('quarter');
  const [year, setYear] = useState(now.year());
  const [month, setMonth] = useState(0);
  const [quarter, setQuarter] = useState(0);
  const [systemMode, setSystemMode] = useState<string>('all');
  const [typeGroup, setTypeGroup] = useState<TypeGroup>('repair_incident');
  const [categoryId, setCategoryId] = useState<string | undefined>();
  const [modelId, setModelId] = useState<string | undefined>();
  const [data, setData] = useState<RepairResponse>(emptyResponse);
  const [loading, setLoading] = useState(false);
  const [systems, setSystems] = useState<RefOption[]>([]);
  const [categories, setCategories] = useState<RefOption[]>([]);
  const [models, setModels] = useState<RefOption[]>([]);

  useEffect(() => {
    void Promise.all([
      apiClient.get('/system-infos'),
      apiClient.get('/categories'),
      apiClient.get('/models', { params: { pageSize: 500 } }),
    ]).then(([sys, cat, mod]) => {
      setSystems((sys.data?.data ?? []) as RefOption[]);
      setCategories((cat.data?.data ?? []) as RefOption[]);
      setModels((mod.data?.data ?? []) as RefOption[]);
    }).catch(() => {
      // Filters remain usable even if a reference list cannot be loaded.
    });
  }, []);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const params: Record<string, unknown> = {
        period,
        year,
        typeGroup,
        unassignedOnly: systemMode === 'unassigned',
      };
      if (period === 'month') params.month = month;
      if (period === 'quarter') params.quarter = quarter;
      if (systemMode !== 'all' && systemMode !== 'unassigned') params.systemId = systemMode;
      if (categoryId) params.categoryId = categoryId;
      if (modelId) params.modelId = modelId;
      const res = await apiClient.get('/reports/repairs', { params });
      setData((res.data?.data ?? emptyResponse) as RepairResponse);
    } catch (err: unknown) {
      const e = err as { response?: { data?: { message?: string } } };
      void message.error(e?.response?.data?.message || 'Không tải được báo cáo sửa chữa');
      setData(emptyResponse);
    } finally {
      setLoading(false);
    }
  }, [period, year, month, quarter, systemMode, typeGroup, categoryId, modelId, message]);

  useEffect(() => { void load(); }, [load]);

  const groups = useMemo(() => data.groups.map((g) => ({
    ...g,
    key: `${g.periodKey}|${g.systemId ?? 'none'}|${g.installationStatus}`,
  })), [data.groups]);

  const maxFailures = Math.max(1, ...groups.map((g) => g.failures));

  const detailFor = (g: RepairGroup) =>
    data.items.filter((i) =>
      i.periodKey === g.periodKey
      && i.systemId === g.systemId
      && i.installationStatus === g.installationStatus,
    );

  const groupColumns = [
    { title: 'Kỳ', dataIndex: 'periodLabel', width: 100 },
    { title: 'Hệ thống', key: 'system', render: (_: unknown, g: RepairGroup) => (
      <Space size={4} wrap>
        <span>{g.systemName}</span>
        <Tag color={statusColor(g.installationStatus)}>{statusLabel(g.installationStatus)}</Tag>
      </Space>
    ) },
    { title: 'Số lần hỏng', dataIndex: 'failures', width: 110, align: 'right' as const },
    { title: 'Đang mở', dataIndex: 'openCount', width: 90, align: 'right' as const },
    { title: 'MTTR (ngày)', key: 'mttr', width: 110, align: 'right' as const,
      render: (_: unknown, g: RepairGroup) => g.mttrDays == null ? '—' : formatNumber(g.mttrDays) },
    { title: 'Chi phí', key: 'cost', width: 130, align: 'right' as const,
      render: (_: unknown, g: RepairGroup) => formatNumber(g.totalCost) },
    { title: 'Thiếu chi phí', dataIndex: 'missingCostCount', width: 110, align: 'right' as const },
    { title: 'Bảo hành', key: 'warranty', width: 100, align: 'right' as const,
      render: (_: unknown, g: RepairGroup) => `${(g.warrantyRate * 100).toFixed(0)}%` },
  ];

  const detailColumns = [
    { title: 'Mã tài sản', key: 'tag', width: 150,
      render: (_: unknown, r: RepairItem) => <Link to={`/assets/${r.assetId}`}>{r.assetTag}</Link> },
    { title: 'Tên', dataIndex: 'assetName', ellipsis: true },
    { title: 'Loại', key: 'type', width: 130,
      render: (_: unknown, r: RepairItem) => MAINTENANCE_TYPE_LABELS[Number(
        Object.entries({
          Maintenance: 1, Repair: 2, Upgrade: 3, HardwareSupport: 4, SoftwareSupport: 5,
          PatTest: 6, Calibration: 7, IncidentReport: 8,
        }).find(([k]) => k === r.type)?.[1] ?? 0,
      )] ?? r.type },
    { title: 'Bắt đầu', dataIndex: 'startDate', width: 120, render: (v: string) => formatDate(v) },
    { title: 'Hoàn thành', dataIndex: 'completionDate', width: 120,
      render: (v: string | null) => v ? formatDate(v) : <Tag color="orange">Đang mở</Tag> },
    { title: 'Chi phí', dataIndex: 'cost', width: 120, align: 'right' as const,
      render: (v: number | null) => v == null ? '—' : formatNumber(v) },
    { title: 'Bảo hành', dataIndex: 'isWarranty', width: 90,
      render: (v: boolean) => v ? <Tag color="green">Có</Tag> : <Tag>Không</Tag> },
  ];

  if (!canView) {
    return (
      <div style={{ padding: 24 }}>
        <Alert type="error" showIcon title="Bạn không có quyền xem báo cáo." />
      </div>
    );
  }

  return (
    <div>
      <Space style={{ width: '100%', justifyContent: 'space-between', marginBottom: 16 }} wrap>
        <Space>
          <Button icon={<ArrowLeftOutlined />} onClick={() => navigate('/reports')}>Quay lại</Button>
          <Title level={4} style={{ margin: 0 }}>Báo cáo sửa chữa</Title>
        </Space>
        <Button icon={<ReloadOutlined />} onClick={() => void load()} loading={loading}>Tải lại</Button>
      </Space>

      <Card size="small" style={{ marginBottom: 16 }}>
        <Space wrap size="middle">
          <span>
            <Text strong>Kỳ</Text>
            <Select style={{ width: 120, marginLeft: 8 }} value={period}
              onChange={(v: Period) => setPeriod(v)}
              options={[{ value: 'month', label: 'Tháng' }, { value: 'quarter', label: 'Quý' }, { value: 'year', label: 'Năm' }]} />
          </span>
          <span>
            <Text strong>Năm</Text>
            <Select style={{ width: 100, marginLeft: 8 }} value={year}
              onChange={setYear}
              options={[year - 2, year - 1, year, year + 1, year + 2].map((y) => ({ value: y, label: String(y) }))} />
          </span>
          {period === 'month' && (
            <span>
              <Text strong>Tháng</Text>
              <Select style={{ width: 100, marginLeft: 8 }} value={month} onChange={setMonth}
                options={[{ value: 0, label: 'Cả năm' }, ...Array.from({ length: 12 }, (_, i) => ({ value: i + 1, label: `${i + 1}` }))]} />
            </span>
          )}
          {period === 'quarter' && (
            <span>
              <Text strong>Quý</Text>
              <Select style={{ width: 100, marginLeft: 8 }} value={quarter} onChange={setQuarter}
                options={[{ value: 0, label: 'Cả năm' }, ...[1, 2, 3, 4].map((q) => ({ value: q, label: `Q${q}` }))]} />
            </span>
          )}
          <span>
            <Text strong>Hệ thống</Text>
            <Select style={{ width: 230, marginLeft: 8 }} value={systemMode} onChange={setSystemMode}
              options={[
                { value: 'all', label: 'Tất cả hệ thống' },
                { value: 'unassigned', label: 'Không thuộc hệ thống' },
                ...systems.map((s) => ({ value: s.id, label: s.name })),
              ]} />
          </span>
          <span>
            <Text strong>Nhóm loại</Text>
            <Select style={{ width: 250, marginLeft: 8 }} value={typeGroup}
              onChange={(v: TypeGroup) => setTypeGroup(v)}
              options={[
                { value: 'repair_incident', label: 'Sửa chữa + Sự cố' },
                { value: 'repair_incident_hardware', label: 'Sửa chữa + Sự cố + Hỗ trợ phần cứng' },
                { value: 'all', label: 'Tất cả loại bảo trì' },
              ]} />
          </span>
          <span>
            <Text strong>Danh mục</Text>
            <Select allowClear style={{ width: 180, marginLeft: 8 }} value={categoryId} onChange={setCategoryId}
              options={categories.map((c) => ({ value: c.id, label: c.name }))} />
          </span>
          <span>
            <Text strong>Model</Text>
            <Select allowClear style={{ width: 180, marginLeft: 8 }} value={modelId} onChange={setModelId}
              options={models.map((m) => ({ value: m.id, label: m.name }))} />
          </span>
        </Space>
        <div style={{ marginTop: 8 }}>
          <Text type="secondary">
            Hệ thống được xác định theo lý lịch lắp đặt tại 00:00 ngày bắt đầu (giờ VN); nếu không có, lấy khoảng bắt đầu trong ngày.
            Bảo hành tính theo cờ “Bảo hành” của phiếu.
          </Text>
        </div>
      </Card>

      {(data.summary.openCount > 0 || data.summary.missingCostCount > 0
        || data.summary.unassignedCount > 0 || data.summary.invalidInstallationCount > 0) && (
        <Alert
          type="warning"
          showIcon
          style={{ marginBottom: 16 }}
          title="Chất lượng dữ liệu cần lưu ý"
          description={[
            data.summary.openCount > 0 ? `${data.summary.openCount} phiếu chưa hoàn thành (không tính MTTR)` : null,
            data.summary.missingCostCount > 0 ? `${data.summary.missingCostCount} phiếu thiếu chi phí` : null,
            data.summary.unassignedCount > 0 ? `${data.summary.unassignedCount} sự cố không thuộc hệ thống` : null,
            data.summary.invalidInstallationCount > 0 ? `${data.summary.invalidInstallationCount} sự cố có lý lịch không hợp lệ` : null,
          ].filter(Boolean).join(' · ')}
        />
      )}

      <Row gutter={[12, 12]} style={{ marginBottom: 16 }}>
        <Col xs={12} sm={8} lg={4}><Card size="small"><Statistic title="Số lần hỏng" value={data.summary.failures} /></Card></Col>
        <Col xs={12} sm={8} lg={4}><Card size="small"><Statistic title="Đang mở" value={data.summary.openCount} /></Card></Col>
        <Col xs={12} sm={8} lg={4}><Card size="small"><Statistic title="MTTR (ngày)" value={data.summary.mttrDays ?? '—'} precision={data.summary.mttrDays == null ? undefined : 1} /></Card></Col>
        <Col xs={12} sm={8} lg={4}><Card size="small"><Statistic title="Tổng chi phí" value={data.summary.totalCost} formatter={(v) => formatNumber(Number(v))} /></Card></Col>
        <Col xs={12} sm={8} lg={4}><Card size="small"><Statistic title="Thiếu chi phí" value={data.summary.missingCostCount} /></Card></Col>
        <Col xs={12} sm={8} lg={4}><Card size="small"><Statistic title="Bảo hành" value={(data.summary.warrantyRate * 100).toFixed(0)} suffix="%" /></Card></Col>
      </Row>

      <Card title="Số lần hỏng theo kỳ" size="small" style={{ marginBottom: 16 }}>
        {groups.length === 0 ? <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} /> : (
          <div style={{ display: 'grid', gap: 8 }}>
            {groups.slice(0, 12).map((g) => (
              <div key={g.key} style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
                <Text style={{ width: 260, flexShrink: 0 }} ellipsis>{g.periodLabel} · {g.systemName}</Text>
                <div style={{ flex: 1, background: '#f0f0f0', height: 18, borderRadius: 4, overflow: 'hidden' }}>
                  <div style={{ width: `${(g.failures / maxFailures) * 100}%`, height: '100%', background: '#1677ff' }} />
                </div>
                <Text style={{ width: 32, textAlign: 'right' }}>{g.failures}</Text>
              </div>
            ))}
          </div>
        )}
      </Card>

      <Card title="Chi tiết theo kỳ × hệ thống" size="small">
        {loading ? <Spin style={{ display: 'block', margin: '40px auto' }} /> : (
          <Table
            rowKey="key"
            size="small"
            columns={groupColumns}
            dataSource={groups}
            pagination={false}
            scroll={{ x: 'max-content' }}
            locale={{ emptyText: <Empty description="Chưa có sự cố trong kỳ đã chọn" /> }}
            expandable={{
              expandedRowRender: (g) => (
                <Table
                  rowKey="id"
                  size="small"
                  columns={detailColumns}
                  dataSource={detailFor(g)}
                  pagination={false}
                  scroll={{ x: 'max-content' }}
                />
              ),
            }}
          />
        )}
      </Card>
    </div>
  );
}
