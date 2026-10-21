import { useEffect, useState } from 'react';
import { Card, Empty, Space, Spin, Table, Tag, Typography } from 'antd';
import { Link } from 'react-router-dom';
import apiClient from '../../../services/api-client';
import { useIsMobile } from '../../../hooks/useIsMobile';
import { formatNumber } from '../../../utils/format';

const { Title, Text } = Typography;

interface LowStockItem {
  id: string;
  name: string;
  qty: number;
  minAmt: number;
  remaining: number;
  type: string;
}

const TYPE_LABELS: Record<string, string> = {
  Component: 'Linh kiện',
  Consumable: 'Vật tư',
  Accessory: 'Phụ kiện',
};

const TYPE_COLORS: Record<string, string> = {
  Component: 'blue',
  Consumable: 'cyan',
  Accessory: 'purple',
};

function detailPath(item: LowStockItem): string {
  if (item.type === 'Component') return `/components/${item.id}`;
  if (item.type === 'Consumable') return `/consumables/${item.id}/view`;
  return `/accessories/${item.id}/view`;
}

export default function LowStockPage() {
  const isMobile = useIsMobile();
  const [items, setItems] = useState<LowStockItem[]>([]);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    let alive = true;
    apiClient.get('/dashboard/low-stock')
      .then((res) => { if (alive) setItems((res.data?.data ?? []) as LowStockItem[]); })
      .catch(() => { if (alive) setItems([]); })
      .finally(() => { if (alive) setLoading(false); });
    return () => { alive = false; };
  }, []);

  if (loading) return <Spin style={{ display: 'block', margin: '80px auto' }} />;

  return (
    <Card>
      <Space orientation="vertical" size={2} style={{ marginBottom: 16 }}>
        <Title level={4} style={{ margin: 0 }}>Vật tư dưới mức tồn tối thiểu</Title>
        <Text type="secondary">
          Danh sách được lọc theo mức tồn tối thiểu của từng mục trong kho chung.
        </Text>
      </Space>

      {isMobile ? (
        items.length === 0 ? (
          <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description="Không có mục nào dưới mức tồn tối thiểu." />
        ) : (
          <Space orientation="vertical" size={12} style={{ width: '100%' }}>
            {items.map((item) => (
              <Card key={`${item.type}-${item.id}`} size="small" style={{ borderRadius: 10 }}>
                <Space size={[6, 6]} wrap style={{ marginBottom: 6 }}>
                  <Tag color={TYPE_COLORS[item.type] ?? 'default'}>{TYPE_LABELS[item.type] ?? item.type}</Tag>
                  <Link to={detailPath(item)}>{item.name}</Link>
                </Space>
                <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '4px 12px', fontSize: 12 }}>
                  <span><Text type="secondary">Tồn hiện tại:</Text> <Text strong type="danger">{formatNumber(item.remaining)}</Text></span>
                  <span><Text type="secondary">Mức tối thiểu:</Text> {formatNumber(item.minAmt)}</span>
                </div>
              </Card>
            ))}
          </Space>
        )
      ) : (
        <Table<LowStockItem>
          rowKey={(item) => `${item.type}-${item.id}`}
          dataSource={items}
          pagination={false}
          scroll={{ x: true }}
          locale={{ emptyText: <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description="Không có mục nào dưới mức tồn tối thiểu." /> }}
          columns={[
            {
              title: 'Loại', dataIndex: 'type', width: 130,
              render: (value: string) => <Tag color={TYPE_COLORS[value] ?? 'default'}>{TYPE_LABELS[value] ?? value}</Tag>,
            },
            {
              title: 'Tên mục', dataIndex: 'name',
              render: (_: unknown, item: LowStockItem) => <Link to={detailPath(item)}>{item.name}</Link>,
            },
            {
              title: 'Tồn hiện tại', dataIndex: 'remaining', width: 130, align: 'right' as const,
              render: (value: number) => <Text strong type="danger">{formatNumber(value)}</Text>,
            },
            {
              title: 'Mức tối thiểu', dataIndex: 'minAmt', width: 130, align: 'right' as const,
              render: (value: number) => formatNumber(value),
            },
          ]}
        />
      )}
    </Card>
  );
}
