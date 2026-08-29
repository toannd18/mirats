import { useEffect, useState } from 'react';
import { Card, Row, Col, Statistic, Timeline, Table, Tag, Spin } from 'antd';
import {
  LaptopOutlined, CheckCircleOutlined,
  WarningOutlined, InboxOutlined,
} from '@ant-design/icons';
import apiClient from '../../../services/api-client';
import { assetStatusColors, statusColors, textColors, uiColors } from '../../../theme/designTokens';
import { ASSET_STATUS_LABELS } from '../../asset/types/asset';
import { ACTION_TYPE_TAGS } from '../../../shared/components/ActionLogTable';
import { formatMoney, formatDateTime } from '../../../utils/format';

interface DashboardSummary {
  totalAssets: number;
  deployedAssets: number;
  rtdAssets: number;
  overdueAudits: number;
  archivedAssets: number;
  lowStockCount: number;
  systemsOverdueMaintenance: number;
  totalAssetValue: number;
}

interface RecentActivity {
  id: string;
  itemType: string;
  itemId: string;
  actionType: string;
  itemName?: string | null;
  note?: string;
  actionDate: string;
  creator: { id: string; username: string; firstName: string; lastName: string };
}

interface ChartData {
  status?: string;
  category?: string;
  color?: string;
  count: number;
}

interface LowStockItem {
  id: string;
  name: string;
  qty: number;
  minAmt: number;
  remaining: number;
  type: string;
}

// ActionType enum (backend) → số → dùng ACTION_TYPE_TAGS (1 nguồn màu/label hợp nhất ở T5).
const ACTION_TYPE_VALUES: Record<string, number> = {
  Create: 1, Update: 2, Delete: 3, Checkout: 4, Checkin: 5, Audit: 6,
  Import: 7, Export: 8, Accept: 9, Decline: 10, Confirm: 11, Archive: 12,
  Unarchive: 13, UpdateRejected: 14, StockIn: 15, MarkDamaged: 16, Dispose: 17,
  Close: 18, Reopen: 19, Inspect: 20,
};

function actionTag(actionType: string): { color: string; label: string } {
  const num = ACTION_TYPE_VALUES[actionType];
  return ACTION_TYPE_TAGS[num] ?? { color: 'default', label: actionType };
}

// ItemType enum (backend) → nhãn tiếng Việt (dùng cho Recent Activity).
const ITEM_TYPE_LABELS: Record<string, string> = {
  Asset: 'Tài sản', Consumable: 'Vật tư', Accessory: 'Phụ kiện', Component: 'Linh kiện',
  License: 'Bản quyền', ComponentUnit: 'Đơn vị linh kiện', AssetMaintenance: 'Bảo trì',
  PermissionGroup: 'Nhóm', User: 'Người dùng', Model: 'Model', Manufacturer: 'Nhà SX',
  Supplier: 'Nhà cung cấp', Location: 'Địa điểm', Company: 'Công ty', Department: 'Phòng ban',
  SystemInfo: 'Hệ thống', SystemPosition: 'Vị trí hệ thống',
};

// AssetStatus → nhãn tiếng Việt (dùng ASSET_STATUS_LABELS đã có); status lạ (VD "6" dữ liệu rác) → "Không xác định".
function statusLabel(status: string): string {
  const s = status as keyof typeof ASSET_STATUS_LABELS;
  return ASSET_STATUS_LABELS[s] ?? 'Không xác định';
}

function statusColor(status: string): string {
  return assetStatusColors[status] ?? statusColors.closed;
}

export default function DashboardPage() {
  const [summary, setSummary] = useState<DashboardSummary | null>(null);
  const [activity, setActivity] = useState<RecentActivity[]>([]);
  const [byStatus, setByStatus] = useState<ChartData[]>([]);
  const [byCategory, setByCategory] = useState<ChartData[]>([]);
  const [lowStock, setLowStock] = useState<LowStockItem[]>([]);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    const load = async () => {
      try {
        const [s, a, st, ca, ls] = await Promise.all([
          apiClient.get('/dashboard/summary'),
          apiClient.get('/dashboard/recent-activity'),
          apiClient.get('/dashboard/assets-by-status'),
          apiClient.get('/dashboard/assets-by-category'),
          apiClient.get('/dashboard/low-stock'),
        ]);
        setSummary(s.data.data);
        setActivity(a.data.data);
        setByStatus(st.data.data);
        setByCategory(ca.data.data);
        setLowStock(ls.data.data);
      } catch { /* handled silently */ }
      setLoading(false);
    };
    load();
  }, []);

  if (loading) return <Spin size="large" />;

  return (
    <div>
      <Row gutter={[16, 16]}>
        <Col xs={12} sm={8} md={4}>
          <Card><Statistic title="Tổng tài sản" value={summary?.totalAssets ?? 0} prefix={<LaptopOutlined />} /></Card>
        </Col>
        <Col xs={12} sm={8} md={4}>
          <Card><Statistic title="Đã cấp phát" value={summary?.deployedAssets ?? 0} prefix={<CheckCircleOutlined />} /></Card>
        </Col>
        <Col xs={12} sm={8} md={4}>
          <Card><Statistic title="Sẵn sàng" value={summary?.rtdAssets ?? 0} prefix={<InboxOutlined />} /></Card>
        </Col>
        <Col xs={12} sm={8} md={4}>
          <Card><Statistic title="Sắp hết" value={summary?.lowStockCount ?? 0} styles={{ content: { color: uiColors.warningAmber } }} prefix={<WarningOutlined />} /></Card>
        </Col>
        <Col xs={12} sm={8} md={4}>
          <Card><Statistic title="Bảo dưỡng quá hạn" value={summary?.systemsOverdueMaintenance ?? 0} styles={{ content: { color: statusColors.overdue } }} prefix={<WarningOutlined />} /></Card>
        </Col>
        <Col xs={12} sm={8} md={4}>
          <Card><Statistic title="Tổng giá trị" value={formatMoney(summary?.totalAssetValue)} /></Card>
        </Col>
      </Row>

      <Row gutter={[16, 16]} style={{ marginTop: 16 }}>
        <Col xs={24} lg={12}>
          <Card title="Hoạt động gần đây" size="small">
            <Timeline items={activity.slice(0, 10).map(a => {
              const tag = actionTag(a.actionType);
              const obj = a.itemName ? `${ITEM_TYPE_LABELS[a.itemType] ?? a.itemType}: ${a.itemName}` : (a.itemName ?? '');
              return {
                // antd 6: Timeline items.children đã deprecated → dùng content (runtime warning xác nhận 2026-08-22).
                content: (
                  <div>
                    <Tag color={tag.color}>{tag.label}</Tag>
                    {obj && <span style={{ fontSize: 12, marginInlineStart: 4 }}>{obj}</span>}
                    <span style={{ fontSize: 12, color: textColors.secondary, marginInlineStart: 8 }}>{formatDateTime(a.actionDate)}</span>
                    <div style={{ fontSize: 11, color: textColors.secondary }}>
                      bởi {[a.creator.firstName, a.creator.lastName].filter(Boolean).join(' ') || a.creator.username}
                    </div>
                  </div>
                ),
              };
            })} />
          </Card>
        </Col>
        <Col xs={24} lg={12}>
          <Card title="Tài sản theo trạng thái" size="small">
            <Table
              dataSource={byStatus}
              rowKey="status"
              pagination={false}
              size="small"
              scroll={{ x: 'max-content' }}
              columns={[
                { title: 'Trạng thái', dataIndex: 'status', key: 'status', render: (text: string) => <Tag color={statusColor(text)}>{statusLabel(text)}</Tag> },
                { title: 'Số lượng', dataIndex: 'count', key: 'count' },
              ]}
            />
          </Card>
        </Col>
      </Row>

      <Row gutter={[16, 16]} style={{ marginTop: 16 }}>
        <Col xs={24} lg={12}>
          <Card title="Tài sản theo danh mục" size="small">
            <Table
              dataSource={byCategory}
              rowKey="category"
              pagination={false}
              size="small"
              scroll={{ x: 'max-content' }}
              columns={[
                { title: 'Danh mục', dataIndex: 'category', key: 'category', render: (text: string, r: ChartData) => <Tag color={r.color}>{text}</Tag> },
                { title: 'Số lượng', dataIndex: 'count', key: 'count' },
              ]}
            />
          </Card>
        </Col>
        <Col xs={24} lg={12}>
          <Card title="Cảnh báo sắp hết" size="small">
            <Table
              dataSource={lowStock}
              rowKey="id"
              pagination={false}
              size="small"
              scroll={{ x: 'max-content' }}
              columns={[
                { title: 'Tên', dataIndex: 'name', key: 'name' },
                { title: 'Loại', dataIndex: 'type', key: 'type', render: (t: string) => <Tag>{ITEM_TYPE_LABELS[t] ?? t}</Tag> },
                { title: 'Còn lại', dataIndex: 'remaining', key: 'remaining', render: (v: number, r: LowStockItem) => <span style={{ color: v <= 0 ? 'red' : 'orange' }}>{v} / {r.qty}</span> },
              ]}
            />
          </Card>
        </Col>
      </Row>
    </div>
  );
}
