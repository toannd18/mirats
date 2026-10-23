import { useCallback, useEffect, useMemo, useState } from 'react';
import { Link } from 'react-router-dom';
import dayjs from 'dayjs';
import {
  Alert, App, Button, Card, Col, Empty, Pagination, Row, Select, Space, Statistic, Table, Tabs, Tag, Typography,
} from 'antd';
import { FileExcelOutlined, ReloadOutlined } from '@ant-design/icons';
import apiClient from '../../../services/api-client';
import { useIsMobile } from '../../../hooks/useIsMobile';
import { usePermission } from '../../../hooks/usePermission';
import { formatDate, formatNumber } from '../../../utils/format';
import RepairReportPage from './RepairReportPage';

const { Text, Title } = Typography;

interface RefOption { id: string; name: string }

interface DepreciationReportRow {
  id: string;
  assetTag: string;
  name: string;
  purchaseCost?: number | null;
  purchaseDate?: string | null;
  model?: string | null;
  depreciation?: string | null;
  monthsTotal?: number;
  monthsUsed?: number;
  monthsRemaining?: number | null;
  currentBookValue?: number | null;
}

interface DepreciationResponse {
  items: DepreciationReportRow[];
  total: number;
  page: number;
  pageSize: number;
  hasMore: boolean;
}

interface AuditReportData {
  totalAudited: number;
  notAudited: number;
  overdueAudit: number;
  totalAssets: number;
}

interface ReportFilters {
  companyId?: string;
  categoryId?: string;
  year?: number;
}

const PAGE_SIZE = 20;
const EMPTY_DEP: DepreciationResponse = { items: [], total: 0, page: 1, pageSize: PAGE_SIZE, hasMore: false };

/** Approved report shell: shared filters, empty-state guidance and no silent truncation. */
export default function ReportsPage() {
  const { message } = App.useApp();
  const isMobile = useIsMobile();
  const canView = usePermission('reports.view');
  const canExport = usePermission('export');

  const [activeKey, setActiveKey] = useState('repairs');
  const [companies, setCompanies] = useState<RefOption[]>([]);
  const [categories, setCategories] = useState<RefOption[]>([]);
  const [filters, setFilters] = useState<ReportFilters>({});
  const [depLoading, setDepLoading] = useState(false);
  const [depData, setDepData] = useState<DepreciationResponse>(EMPTY_DEP);
  const [depPage, setDepPage] = useState(1);
  const [auditLoading, setAuditLoading] = useState(false);
  const [auditData, setAuditData] = useState<AuditReportData | null>(null);

  const yearOptions = useMemo(() => {
    const current = dayjs().year();
    return Array.from({ length: 5 }, (_, index) => current - index)
      .map(year => ({ value: year, label: String(year) }));
  }, []);

  useEffect(() => {
    void Promise.all([
      apiClient.get('/companies'),
      apiClient.get('/categories'),
    ]).then(([companyRes, categoryRes]) => {
      setCompanies((companyRes.data?.data ?? []) as RefOption[]);
      setCategories((categoryRes.data?.data ?? []) as RefOption[]);
    }).catch(() => {
      // Filters remain usable when the optional reference endpoints are not accessible.
    });
  }, []);

  const loadDepreciation = useCallback(async (page = 1) => {
    setDepLoading(true);
    try {
      const res = await apiClient.get('/reports/depreciation', {
        params: {
          page,
          pageSize: PAGE_SIZE,
          companyId: filters.companyId,
          categoryId: filters.categoryId,
          year: filters.year,
        },
      });
      setDepData((res.data?.data ?? EMPTY_DEP) as DepreciationResponse);
      setDepPage(page);
    } catch {
      setDepData(EMPTY_DEP);
      message.error('Không tải được báo cáo khấu hao');
    } finally {
      setDepLoading(false);
    }
  }, [filters, message]);

  const loadAudit = useCallback(async () => {
    setAuditLoading(true);
    try {
      const res = await apiClient.get('/reports/audit', {
        params: {
          companyId: filters.companyId,
          categoryId: filters.categoryId,
          year: filters.year,
        },
      });
      setAuditData((res.data?.data ?? null) as AuditReportData | null);
    } catch {
      setAuditData(null);
      message.error('Không tải được báo cáo kiểm kê');
    } finally {
      setAuditLoading(false);
    }
  }, [filters, message]);

  useEffect(() => {
    if (!canView) return;
    if (activeKey === 'dep') void loadDepreciation(1);
    if (activeKey === 'audit') void loadAudit();
  }, [activeKey, canView, loadAudit, loadDepreciation]);

  const exportAssets = () => apiClient.get('/export/assets', { responseType: 'blob' }).then(r => {
    const url = URL.createObjectURL(r.data as Blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = 'assets.xlsx';
    a.click();
    URL.revokeObjectURL(url);
  }).catch(() => {
    message.error('Không xuất được dữ liệu tài sản');
  });

  const filterFields = (
    <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(180px, 1fr))', gap: 12 }}>
      <div>
        <Text strong style={{ display: 'block', marginBottom: 4 }}>Công ty</Text>
        <Select
          allowClear
          style={{ width: '100%' }}
          placeholder="Tất cả công ty"
          value={filters.companyId}
          onChange={(value?: string) => setFilters(prev => ({ ...prev, companyId: value }))}
          options={companies.map(c => ({ value: c.id, label: c.name }))}
        />
      </div>
      <div>
        <Text strong style={{ display: 'block', marginBottom: 4 }}>Danh mục</Text>
        <Select
          allowClear
          style={{ width: '100%' }}
          placeholder="Tất cả danh mục"
          value={filters.categoryId}
          onChange={(value?: string) => setFilters(prev => ({ ...prev, categoryId: value }))}
          options={categories.map(c => ({ value: c.id, label: c.name }))}
        />
      </div>
      <div>
        <Text strong style={{ display: 'block', marginBottom: 4 }}>Năm mua</Text>
        <Select
          allowClear
          style={{ width: '100%' }}
          placeholder="Tất cả các năm"
          value={filters.year}
          onChange={(value?: number) => setFilters(prev => ({ ...prev, year: value }))}
          options={yearOptions}
        />
      </div>
    </div>
  );

  const depColumns = [
    { title: 'Mã tài sản', dataIndex: 'assetTag', width: 120 },
    { title: 'Tên tài sản', dataIndex: 'name', width: 180, ellipsis: true },
    { title: 'Model tài sản', dataIndex: 'model', width: 140, ellipsis: true },
    { title: 'Đơn giá', dataIndex: 'purchaseCost', width: 110, align: 'right' as const,
      render: (value: number | null | undefined) => value == null ? '—' : formatNumber(value) },
    { title: 'Ngày mua', dataIndex: 'purchaseDate', width: 100,
      render: (value: string | null | undefined) => value ? formatDate(value) : '—' },
    { title: 'Đã sử dụng (tháng)', dataIndex: 'monthsUsed', width: 120, align: 'right' as const },
    { title: 'Còn lại (tháng)', dataIndex: 'monthsRemaining', width: 100, align: 'right' as const },
    { title: 'Giá trị còn lại', dataIndex: 'currentBookValue', width: 130, align: 'right' as const,
      render: (value: number | null | undefined) => value == null ? '—' : formatNumber(value) },
  ];

  const depEmpty = (
    <Empty
      image={Empty.PRESENTED_IMAGE_SIMPLE}
      description="Chưa có tài sản đủ dữ liệu khấu hao. Hãy gán model có cấu hình khấu hao, sau đó nhập đơn giá và ngày mua cho tài sản."
    />
  );

  const depreciationTab = (
    <>
      <Card size="small" style={{ marginBottom: 16 }}>
        <Space style={{ width: '100%', justifyContent: 'space-between', marginBottom: 12 }} wrap>
          <Text strong>Bộ lọc báo cáo khấu hao</Text>
          <Button icon={<ReloadOutlined />} loading={depLoading} onClick={() => void loadDepreciation(depPage)}>
            Tải lại
          </Button>
        </Space>
        {filterFields}
      </Card>
      {isMobile ? (
        <>
          {depData.items.length === 0 ? <Card>{depEmpty}</Card> : depData.items.map(row => (
            <Card key={row.id} size="small" style={{ marginBottom: 12 }}>
              <Space style={{ width: '100%', justifyContent: 'space-between' }} wrap>
                <Text strong>{row.assetTag}</Text>
                <Tag>{row.model || 'Chưa có model'}</Tag>
              </Space>
              <Text style={{ display: 'block', margin: '4px 0 8px' }}>{row.name}</Text>
              <div style={{ display: 'grid', gridTemplateColumns: 'auto 1fr', gap: '4px 12px', fontSize: 13 }}>
                <Text type="secondary">Đơn giá</Text><Text>{row.purchaseCost == null ? '—' : formatNumber(row.purchaseCost)}</Text>
                <Text type="secondary">Ngày mua</Text><Text>{row.purchaseDate ? formatDate(row.purchaseDate) : '—'}</Text>
                <Text type="secondary">Đã sử dụng</Text><Text>{row.monthsUsed ?? 0} tháng</Text>
                <Text type="secondary">Còn lại</Text><Text>{row.monthsRemaining ?? 0} tháng</Text>
                <Text type="secondary">Giá trị còn lại</Text><Text>{row.currentBookValue == null ? '—' : formatNumber(row.currentBookValue)}</Text>
              </div>
            </Card>
          ))}
          <Pagination
            style={{ textAlign: 'center' }}
            current={depData.page}
            pageSize={PAGE_SIZE}
            total={depData.total}
            showSizeChanger={false}
            onChange={page => void loadDepreciation(page)}
          />
        </>
      ) : (
        <Table
          rowKey="id"
          size="small"
          loading={depLoading}
          columns={depColumns}
          dataSource={depData.items}
          scroll={{ x: 'max-content' }}
          locale={{ emptyText: depEmpty }}
          pagination={{
            current: depData.page,
            pageSize: PAGE_SIZE,
            total: depData.total,
            showSizeChanger: false,
            showTotal: (total, range) => `${range[0]}–${range[1]} / ${total} tài sản`,
            onChange: page => void loadDepreciation(page),
          }}
        />
      )}
      {depData.total > 0 && (
        <Text type="secondary" style={{ display: 'block', marginTop: 8, fontSize: 12 }}>
          Đang hiển thị {depData.items.length}/{depData.total} tài sản. Dùng phân trang để xem phần còn lại.
        </Text>
      )}
    </>
  );

  const auditTab = (
    <>
      <Card size="small" style={{ marginBottom: 16 }}>
        <Space style={{ width: '100%', justifyContent: 'space-between', marginBottom: 12 }} wrap>
          <Text strong>Bộ lọc báo cáo kiểm kê</Text>
          <Button icon={<ReloadOutlined />} loading={auditLoading} onClick={() => void loadAudit()}>
            Tải lại
          </Button>
        </Space>
        {filterFields}
      </Card>
      {!auditData || auditData.totalAssets === 0 ? (
        <Card>
          <Empty
            image={Empty.PRESENTED_IMAGE_SIMPLE}
            description="Chưa có dữ liệu kiểm kê trong phạm vi đã chọn. Hãy kiểm tra lại bộ lọc hoặc thực hiện kiểm kê tài sản trước."
          />
        </Card>
      ) : (
        <Row gutter={[12, 12]}>
          <Col xs={24} sm={8}><Card><Statistic title="Đã kiểm kê" value={auditData.totalAudited} suffix={`/ ${auditData.totalAssets}`} /></Card></Col>
          <Col xs={24} sm={8}><Card><Statistic title="Chưa kiểm kê" value={auditData.notAudited} /></Card></Col>
          <Col xs={24} sm={8}><Card><Statistic title="Quá hạn kiểm kê" value={auditData.overdueAudit} styles={{ content: { color: auditData.overdueAudit > 0 ? '#cf1322' : undefined } }} /></Card></Col>
        </Row>
      )}
    </>
  );

  const exportTab = (
    <Card size="small">
      <Title level={5} style={{ marginTop: 0 }}>Xuất dữ liệu tài sản</Title>
      <Text type="secondary">
        Tệp Excel xuất danh sách tài sản theo phạm vi công ty của bạn. Nếu dữ liệu lớn, hãy lọc theo danh mục/công ty
        hoặc liên hệ quản trị viên; tệp hiện được tạo tối đa 1.000 bản ghi mỗi lần để tránh quá tải.
      </Text>
      <div style={{ marginTop: 16 }}>
        <Button type="primary" icon={<FileExcelOutlined />} disabled={!canExport} onClick={() => void exportAssets()}>
          Xuất Excel tài sản
        </Button>
        {!canExport && <Text type="secondary" style={{ marginLeft: 8 }}>Bạn không có quyền xuất dữ liệu.</Text>}
      </div>
      <div style={{ marginTop: 12 }}>
        <Link to="/admin/import">Mở trang Nhập Excel →</Link>
      </div>
    </Card>
  );

  if (!canView) {
    return (
      <div style={{ padding: 24 }}>
        <Alert type="error" showIcon title="Bạn không có quyền xem báo cáo." />
      </div>
    );
  }

  return (
    <div>
      <Tabs
        activeKey={activeKey}
        onChange={setActiveKey}
        items={[
          { key: 'repairs', label: 'Sửa chữa', children: <RepairReportPage embedded /> },
          { key: 'dep', label: 'Khấu hao', children: depreciationTab },
          { key: 'audit', label: 'Kiểm kê', children: auditTab },
          { key: 'export', label: 'Xuất dữ liệu', children: exportTab },
        ]}
      />
    </div>
  );
}
