import { useCallback, useEffect, useMemo, useState } from 'react';
import { Button, Card, Col, Empty, Progress, Row, Space, Spin, Table, Tag, Timeline, Tooltip, Typography } from 'antd';
import {
  AuditOutlined, CheckCircleOutlined, ExperimentOutlined, FileProtectOutlined,
  InboxOutlined, KeyOutlined, LaptopOutlined, ReloadOutlined, ToolOutlined,
} from '@ant-design/icons';
import { Link, useNavigate } from 'react-router-dom';
import apiClient from '../../../services/api-client';
import { assetStatusColors, statusColors, textColors, uiColors } from '../../../theme/designTokens';
import { ASSET_STATUS_LABELS } from '../../asset/types/asset';
import { ACTION_TYPE_TAGS } from '../../../shared/components/ActionLogTable';
import { formatDate, formatDateTime, formatMoney, formatNumber } from '../../../utils/format';
import { useIsMobile } from '../../../hooks/useIsMobile';

const { Text } = Typography;

interface DashboardAlertItem {
  id: string;
  name: string;
  code: string | null;
  dueAt: string | null;
  detail: string | null;
}

interface DashboardAlert {
  key: string;
  count: number;
  items: DashboardAlertItem[];
}

interface DashboardOverview {
  alerts: DashboardAlert[];
  numbers: {
    totalAssets: number;
    deployedAssets: number;
    pendingAssets: number;
    totalAssetValue: number;
  };
  activeCampaigns: {
    id: string;
    name: string;
    systemName: string;
    completed: number;
    total: number;
    endDate: string | null;
  }[];
  byStatus: { status: string; count: number }[];
  byCategory: { category: string; color?: string | null; count: number }[];
  recentActivity: RecentActivity[];
  updatedAt: string;
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

const ACTION_TYPE_VALUES: Record<string, number> = {
  Create: 1, Update: 2, Delete: 3, Checkout: 4, Checkin: 5, Audit: 6,
  Import: 7, Export: 8, Accept: 9, Decline: 10, Confirm: 11, Archive: 12,
  Unarchive: 13, UpdateRejected: 14, StockIn: 15, MarkDamaged: 16, Dispose: 17,
  Close: 18, Reopen: 19, Inspect: 20,
};

function actionTag(actionType: string): { color: string; label: string } {
  return ACTION_TYPE_TAGS[ACTION_TYPE_VALUES[actionType]] ?? { color: 'default', label: actionType };
}

const ITEM_TYPE_LABELS: Record<string, string> = {
  Asset: 'Tài sản', Consumable: 'Vật tư', Accessory: 'Phụ kiện', Component: 'Linh kiện',
  License: 'Bản quyền', ComponentUnit: 'Đơn vị linh kiện', AssetMaintenance: 'Bảo trì',
  PermissionGroup: 'Nhóm', User: 'Người dùng', Model: 'Model', Manufacturer: 'Nhà SX',
  Supplier: 'Nhà cung cấp', Location: 'Địa điểm', Company: 'Công ty', Department: 'Phòng ban',
  SystemInfo: 'Hệ thống', SystemPosition: 'Vị trí hệ thống',
};

function statusLabel(status: string): string {
  return ASSET_STATUS_LABELS[status as keyof typeof ASSET_STATUS_LABELS] ?? 'Không xác định';
}

function statusColor(status: string): string {
  return assetStatusColors[status] ?? statusColors.closed;
}

function compactMoney(value: number): string {
  if (value >= 1_000_000_000) return `${formatNumber(Math.round(value / 100_000_000) / 10)} tỷ ₫`;
  if (value >= 1_000_000) return `${formatNumber(Math.round(value / 1_000_000))} triệu ₫`;
  if (value >= 1_000) return `${formatNumber(Math.round(value / 1_000))} nghìn ₫`;
  return `${formatNumber(value)} ₫`;
}

const ALERT_META: Record<string, {
  title: string;
  icon: React.ReactNode;
  color: string;
  path: string;
}> = {
  lowStock: {
    title: 'Vật tư dưới mức tồn',
    icon: <ToolOutlined />,
    color: uiColors.warningAmber,
    path: '/inventory/low-stock',
  },
  overdueMaintenance: {
    title: 'Bảo dưỡng quá hạn',
    icon: <ExperimentOutlined />,
    color: statusColors.overdue,
    path: '/dashboard/alerts/overdueMaintenance',
  },
  overdueAudits: {
    title: 'Kiểm kê quá hạn',
    icon: <AuditOutlined />,
    color: '#d46b08',
    path: '/dashboard/alerts/overdueAudits',
  },
  expiringLicenses: {
    title: 'Bản quyền sắp/đã hết hạn',
    icon: <KeyOutlined />,
    color: '#cf1322',
    path: '/dashboard/alerts/expiringLicenses',
  },
  unconfirmedAssets: {
    title: 'Tài sản chưa xác nhận',
    icon: <FileProtectOutlined />,
    color: '#722ed1',
    path: '/dashboard/alerts/unconfirmedAssets',
  },
};

export default function DashboardPage() {
  const navigate = useNavigate();
  const isMobile = useIsMobile();
  const [overview, setOverview] = useState<DashboardOverview | null>(null);
  const [loading, setLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);

  const load = useCallback(async (refresh = false) => {
    if (refresh) setRefreshing(true);
    try {
      const res = await apiClient.get('/dashboard/overview');
      setOverview(res.data?.data as DashboardOverview);
    } catch {
      /* keep last successful snapshot */
    } finally {
      setLoading(false);
      setRefreshing(false);
    }
  }, []);

  useEffect(() => { void load(); }, [load]);

  const updatedTime = useMemo(() => {
    if (!overview?.updatedAt) return '--:--';
    return formatDateTime(overview.updatedAt).split(' ')[1] ?? '--:--';
  }, [overview?.updatedAt]);

  if (loading) return <Spin size="large" />;

  const statusTotal = (overview?.byStatus ?? []).reduce((sum, row) => sum + row.count, 0);
  const categoryTotal = (overview?.byCategory ?? []).reduce((sum, row) => sum + row.count, 0);

  const alerts = Object.keys(ALERT_META).map((key) => {
    const found = overview?.alerts.find((a) => a.key === key);
    return { ...ALERT_META[key], key, count: found?.count ?? 0 };
  });

  const numbers = [
    { key: 'total', title: 'Tổng tài sản', value: overview?.numbers.totalAssets ?? 0, icon: <LaptopOutlined /> },
    { key: 'deployed', title: 'Đã cấp phát', value: overview?.numbers.deployedAssets ?? 0, icon: <CheckCircleOutlined /> },
    { key: 'pending', title: 'Chờ cấp phát', value: overview?.numbers.pendingAssets ?? 0, icon: <InboxOutlined /> },
    { key: 'value', title: 'Tổng giá trị', value: overview?.numbers.totalAssetValue ?? 0, icon: <FileProtectOutlined /> },
  ];

  const attentionCampaigns = [...(overview?.activeCampaigns ?? [])]
    .sort((a, b) => {
      const now = Date.now();
      const aOverdue = !!a.endDate && new Date(a.endDate).getTime() < now;
      const bOverdue = !!b.endDate && new Date(b.endDate).getTime() < now;
      if (aOverdue !== bOverdue) return aOverdue ? -1 : 1;
      const aEnd = a.endDate ? new Date(a.endDate).getTime() : Number.MAX_SAFE_INTEGER;
      const bEnd = b.endDate ? new Date(b.endDate).getTime() : Number.MAX_SAFE_INTEGER;
      if (aEnd !== bEnd) return aEnd - bEnd;
      const aRatio = a.total > 0 ? a.completed / a.total : 0;
      const bRatio = b.total > 0 ? b.completed / b.total : 0;
      return aRatio - bRatio;
    })
    .slice(0, 5);

  return (
    <div>
      <div style={{ display: 'flex', justifyContent: 'flex-end', alignItems: 'center', gap: 8, flexWrap: 'wrap', marginBottom: 12 }}>
        <Text type="secondary" style={{ fontSize: 12 }}>Cập nhật lúc {updatedTime}</Text>
        <Button icon={<ReloadOutlined />} loading={refreshing} onClick={() => void load(true)}>Làm mới</Button>
      </div>

      <Card size="small" title="Cảnh báo" style={{ marginBottom: 16 }}>
        <div style={{ display: 'grid', gridTemplateColumns: isMobile ? '1fr 1fr' : 'repeat(5, minmax(0, 1fr))', gap: 12 }}>
          {alerts.map((alert) => {
            const inactive = alert.count <= 0;
            return (
              <button
                key={alert.key}
                type="button"
                onClick={() => { if (!inactive) navigate(alert.path); }}
                style={{
                  textAlign: 'left', border: `1px solid ${inactive ? '#f0f0f0' : alert.color}`, borderRadius: 10, padding: 12,
                  background: inactive ? '#fafafa' : '#fff', cursor: inactive ? 'default' : 'pointer',
                  minWidth: 0,
                }}
              >
                <Space size={6} style={{ color: inactive ? textColors.secondary : alert.color, marginBottom: 4 }}>
                  {alert.icon}
                  <Text type={inactive ? 'secondary' : undefined} style={{ fontSize: 12 }}>{alert.title}</Text>
                </Space>
                {inactive ? (
                  <Space size={4} style={{ color: statusColors.closed }}>
                    <CheckCircleOutlined />
                    <Text type="secondary" style={{ fontSize: 14 }}>Không có</Text>
                  </Space>
                ) : (
                  <div style={{ fontSize: 24, fontWeight: 700, lineHeight: 1.2, color: alert.color }}>
                    {alert.count}
                  </div>
                )}
              </button>
            );
          })}
        </div>
      </Card>

      <div style={{ display: 'grid', gridTemplateColumns: isMobile ? '1fr 1fr' : 'repeat(4, minmax(0, 1fr))', gap: 16, marginBottom: 16 }}>
        {numbers.map((item) => {
          const full = item.key === 'value' ? formatMoney(item.value) : formatNumber(item.value);
          const display = item.key === 'value' ? compactMoney(item.value) : formatNumber(item.value);
          return (
            <Card key={item.key} size="small" style={{ height: '100%' }} styles={{ body: { minHeight: 88 } }}>
              <Space size={6} style={{ color: textColors.secondary, marginBottom: 8 }}>
                {item.icon}
                <Text type="secondary" style={{ fontSize: 12 }}>{item.title}</Text>
              </Space>
              <Tooltip title={full}>
                <div style={{
                  fontSize: isMobile ? 18 : 24, fontWeight: 600, lineHeight: 1.25,
                  overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap',
                }}>
                  {display}
                </div>
              </Tooltip>
            </Card>
          );
        })}
      </div>

      <Card
        title="Đợt bảo dưỡng cần chú ý"
        size="small"
        style={{ marginBottom: 16 }}
        extra={<Link to="/maintenance/campaigns">Xem tất cả</Link>}
      >
        {attentionCampaigns.length === 0 ? (
          <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description="Không có đợt bảo dưỡng đang thực hiện" />
        ) : (
          <Space orientation="vertical" size={10} style={{ width: '100%' }}>
            {attentionCampaigns.map((campaign) => {
              const overdue = !!campaign.endDate && new Date(campaign.endDate).getTime() < Date.now();
              const noChecklist = campaign.total === 0;
              const percent = campaign.total > 0 ? Math.round((campaign.completed / campaign.total) * 100) : 0;
              return (
                <div key={campaign.id} style={{ padding: '8px 0', borderBottom: '1px solid #f0f0f0' }}>
                  <div style={{ display: 'flex', justifyContent: 'space-between', gap: 8, alignItems: 'center', flexWrap: 'wrap' }}>
                    <div style={{ minWidth: 0, flex: 1 }}>
                      <Space size={6} wrap>
                        <Text strong>{campaign.name}</Text>
                        {overdue && <Tag color="red">Quá hạn</Tag>}
                        {noChecklist && <Tag color="orange">Chưa có checklist</Tag>}
                      </Space>
                      <div style={{ fontSize: 12, color: textColors.secondary }}>{campaign.systemName}</div>
                    </div>
                    <Text strong style={{ color: overdue ? statusColors.overdue : uiColors.warningAmber }}>
                      {campaign.completed}/{campaign.total}
                    </Text>
                  </div>
                  <Progress
                    percent={percent}
                    showInfo={false}
                    strokeColor={overdue ? statusColors.overdue : uiColors.warningAmber}
                    style={{ margin: '4px 0 2px' }}
                  />
                  <Text type="secondary" style={{ fontSize: 11 }}>
                    {campaign.endDate ? `Kết thúc: ${formatDate(campaign.endDate)}` : 'Chưa đặt ngày kết thúc'}
                  </Text>
                </div>
              );
            })}
          </Space>
        )}
      </Card>

      <Row gutter={[16, 16]}>
        <Col xs={24} lg={12}>
          <Card title="Tài sản theo trạng thái" size="small">
            {isMobile ? (
              <Space orientation="vertical" size={6} style={{ width: '100%' }}>
                {overview?.byStatus.map((row) => (
                  <div key={row.status} style={{ display: 'flex', justifyContent: 'space-between' }}>
                    <Tag color={statusColor(row.status)}>{statusLabel(row.status)}</Tag>
                    <Text strong>{row.count}</Text>
                  </div>
                ))}
                <div style={{ display: 'flex', justifyContent: 'space-between', paddingTop: 6, borderTop: '1px solid #f0f0f0' }}>
                  <Text strong>Tổng</Text>
                  <Text strong>{statusTotal}</Text>
                </div>
              </Space>
            ) : (
              <Table
                dataSource={overview?.byStatus ?? []}
                rowKey="status"
                pagination={false}
                size="small"
                columns={[
                  { title: 'Trạng thái', dataIndex: 'status', render: (value: string) => <Tag color={statusColor(value)}>{statusLabel(value)}</Tag> },
                  { title: 'Số lượng', dataIndex: 'count' },
                ]}
                scroll={{ x: 'max-content' }}
                summary={() => (
                  <Table.Summary.Row>
                    <Table.Summary.Cell index={0}><Text strong>Tổng</Text></Table.Summary.Cell>
                    <Table.Summary.Cell index={1}><Text strong>{statusTotal}</Text></Table.Summary.Cell>
                  </Table.Summary.Row>
                )}
              />
            )}
          </Card>
        </Col>
        <Col xs={24} lg={12}>
          <Card title="Tài sản theo danh mục" size="small">
            {isMobile ? (
              <Space orientation="vertical" size={6} style={{ width: '100%' }}>
                {overview?.byCategory.map((row) => (
                  <div key={row.category} style={{ display: 'flex', justifyContent: 'space-between' }}>
                    <Tag color={row.color ?? undefined}>{row.category}</Tag>
                    <Text strong>{row.count}</Text>
                  </div>
                ))}
                <div style={{ display: 'flex', justifyContent: 'space-between', paddingTop: 6, borderTop: '1px solid #f0f0f0' }}>
                  <Text strong>Tổng</Text>
                  <Text strong>{categoryTotal}</Text>
                </div>
              </Space>
            ) : (
              <Table
                dataSource={overview?.byCategory ?? []}
                rowKey="category"
                pagination={false}
                size="small"
                columns={[
                  { title: 'Danh mục', dataIndex: 'category', render: (value: string, row) => <Tag color={row.color ?? undefined}>{value}</Tag> },
                  { title: 'Số lượng', dataIndex: 'count' },
                ]}
                scroll={{ x: 'max-content' }}
                summary={() => (
                  <Table.Summary.Row>
                    <Table.Summary.Cell index={0}><Text strong>Tổng</Text></Table.Summary.Cell>
                    <Table.Summary.Cell index={1}><Text strong>{categoryTotal}</Text></Table.Summary.Cell>
                  </Table.Summary.Row>
                )}
              />
            )}
          </Card>
        </Col>
      </Row>

      <Card
        title="Hoạt động gần đây"
        size="small"
        style={{ marginTop: 16 }}
        extra={<Link to="/system-history">Xem tất cả</Link>}
      >
        <Timeline items={(overview?.recentActivity ?? []).slice(0, 8).map((activity) => {
          const tag = actionTag(activity.actionType);
          const objectLabel = activity.itemName
            ? `${ITEM_TYPE_LABELS[activity.itemType] ?? activity.itemType}: ${activity.itemName}`
            : ITEM_TYPE_LABELS[activity.itemType] ?? activity.itemType;
          return {
            content: (
              <div>
                <Tag color={tag.color}>{tag.label}</Tag>
                {objectLabel && <span style={{ fontSize: 12, marginInlineStart: 4 }}>{objectLabel}</span>}
                <span style={{ fontSize: 12, color: textColors.secondary, marginInlineStart: 8 }}>
                  {formatDateTime(activity.actionDate)}
                </span>
                <div style={{ fontSize: 11, color: textColors.secondary }}>
                  bởi {[activity.creator.firstName, activity.creator.lastName].filter(Boolean).join(' ') || activity.creator.username}
                </div>
              </div>
            ),
          };
        })} />
      </Card>
    </div>
  );
}
