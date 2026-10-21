import { useEffect, useState } from 'react';
import { Card, Empty, Space, Spin, Table, Tag, Typography } from 'antd';
import { Link, useParams } from 'react-router-dom';
import apiClient from '../../../services/api-client';
import { useIsMobile } from '../../../hooks/useIsMobile';
import { formatDate } from '../../../utils/format';

const { Title, Text } = Typography;

interface AlertItem {
  id: string;
  name: string;
  code: string | null;
  dueAt: string | null;
  detail: string | null;
}

interface Alert {
  key: string;
  count: number;
  items: AlertItem[];
}

const ALERT_TITLES: Record<string, string> = {
  lowStock: 'Vật tư dưới mức tồn',
  overdueMaintenance: 'Bảo dưỡng quá hạn',
  overdueAudits: 'Kiểm kê quá hạn',
  expiringLicenses: 'Bản quyền sắp/đã hết hạn',
  unconfirmedAssets: 'Tài sản chưa xác nhận',
};

function itemPath(key: string, id: string): string {
  if (key === 'overdueMaintenance') return `/systems/${id}`;
  if (key === 'expiringLicenses') return `/licenses/${id}`;
  return `/assets/${id}`;
}

export default function DashboardAlertPage() {
  const { key = '' } = useParams<{ key: string }>();
  const isMobile = useIsMobile();
  const [alert, setAlert] = useState<Alert | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    let alive = true;
    apiClient.get('/dashboard/overview')
      .then((res) => {
        if (!alive) return;
        const alerts = (res.data?.data?.alerts ?? []) as Alert[];
        setAlert(alerts.find((a) => a.key === key) ?? null);
      })
      .catch(() => { if (alive) setAlert(null); })
      .finally(() => { if (alive) setLoading(false); });
    return () => { alive = false; };
  }, [key]);

  if (loading) return <Spin style={{ display: 'block', margin: '80px auto' }} />;

  const title = ALERT_TITLES[key] ?? 'Cảnh báo';
  const items = alert?.items ?? [];

  return (
    <Card>
      <Space orientation="vertical" size={2} style={{ marginBottom: 16 }}>
        <Title level={4} style={{ margin: 0 }}>{title}</Title>
        <Text type="secondary">{alert?.count ?? 0} mục cần chú ý</Text>
      </Space>

      {items.length === 0 ? (
        <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description="Không có mục nào." />
      ) : isMobile ? (
        <Space orientation="vertical" size={12} style={{ width: '100%' }}>
          {items.map((item) => (
            <Card key={`${key}-${item.id}`} size="small" style={{ borderRadius: 10 }}>
              <div style={{ fontWeight: 600, marginBottom: 6 }}>
                <Link to={itemPath(key, item.id)}>{item.name}</Link>
              </div>
              <Space size={[6, 6]} wrap>
                {item.code && <Tag>{item.code}</Tag>}
                {item.dueAt && <Tag color="orange">Hạn: {formatDate(item.dueAt)}</Tag>}
                {item.detail && <Tag>{item.detail}</Tag>}
              </Space>
            </Card>
          ))}
        </Space>
      ) : (
        <Table<AlertItem>
          rowKey="id"
          dataSource={items}
          pagination={false}
          scroll={{ x: true }}
          columns={[
            {
              title: 'Tên mục', dataIndex: 'name',
              render: (_: unknown, item: AlertItem) => <Link to={itemPath(key, item.id)}>{item.name}</Link>,
            },
            {
              title: 'Mã', dataIndex: 'code', width: 200,
              render: (value: string | null) => value ?? '—',
            },
            {
              title: 'Hạn / Trạng thái', dataIndex: 'dueAt', width: 180,
              render: (_: unknown, item: AlertItem) => item.dueAt
                ? formatDate(item.dueAt)
                : (item.detail ?? '—'),
            },
            {
              title: 'Chi tiết', dataIndex: 'detail',
              render: (value: string | null, item: AlertItem) => item.dueAt
                ? (value ?? '—')
                : '—',
            },
          ]}
        />
      )}
    </Card>
  );
}
