import { useState } from 'react';
import { Button, message, Table, Tabs } from 'antd';
import apiClient from '../../../services/api-client';
import { usePermission } from '../../../hooks/usePermission';

// Typed report rows (SEC-FIX CI-2: replaces `any` placeholders with explicit shapes).
interface DepreciationReportRow {
  id: string;
  assetTag: string;
  name: string;
  purchaseCost?: number | null;
  monthsUsed?: number;
  monthsRemaining?: number | null;
  currentBookValue?: number | null;
}

interface AuditReportData {
  totalAudited: number;
  notAudited: number;
  overdueAudit: number;
}

export default function ReportsPage() {
  const [loading, setLoading] = useState(false);
  const [depData, setDepData] = useState<DepreciationReportRow[]>([]);
  const [auditData, setAuditData] = useState<AuditReportData | null>(null);
  const canExport = usePermission('export');

  const loadDep = async () => {
    setLoading(true);
    try {
      const r = await apiClient.get('/reports/depreciation');
      setDepData(r.data.data);
    } catch {
      message.error('Lỗi tải báo cáo');
    }
    setLoading(false);
  };

  const loadAudit = async () => {
    setLoading(true);
    try {
      const r = await apiClient.get('/reports/audit');
      setAuditData(r.data.data);
    } catch {
      message.error('Lỗi tải báo cáo');
    }
    setLoading(false);
  };

  const exportAssets = () => apiClient.get('/export/assets', { responseType: 'blob' }).then(r => {
    const url = URL.createObjectURL(r.data);
    const a = document.createElement('a');
    a.href = url;
    a.download = 'assets.csv';
    a.click();
  });

  const depCols = [
    { title: 'Asset Tag', dataIndex: 'assetTag' },
    { title: 'Tên', dataIndex: 'name' },
    { title: 'Giá mua', dataIndex: 'purchaseCost' },
    { title: 'Tháng SD', dataIndex: 'monthsUsed' },
    { title: 'Còn lại', dataIndex: 'monthsRemaining' },
    { title: 'Giá trị hiện tại', dataIndex: 'currentBookValue' },
  ];

  return (
    <div>
      <Tabs items={[
        {
          key: 'dep', label: 'Khấu hao', children: (
            <>
              <Button type="primary" onClick={() => void loadDep()} loading={loading}>Tải báo cáo</Button>
              <Table dataSource={depData} rowKey="id" columns={depCols} loading={loading}
                style={{ marginTop: 16 }} size="small" scroll={{ x: 'max-content' }} />
            </>
          ),
        },
        {
          key: 'audit', label: 'Kiểm kê', children: (
            <>
              <Button type="primary" onClick={() => void loadAudit()} loading={loading}>Tải báo cáo</Button>
              {auditData && <div style={{ marginTop: 16 }}>
                <p>Đã kiểm kê: {auditData.totalAudited}</p>
                <p>Chưa kiểm kê: {auditData.notAudited}</p>
                <p>Quá hạn: {auditData.overdueAudit}</p>
              </div>}
            </>
          ),
        },
        {
          key: 'export', label: 'Xuất CSV', children: (
            canExport ? <Button type="primary" onClick={() => void exportAssets()}>Tải Assets CSV</Button> : null
          ),
        },
      ]} />
    </div>
  );
}
