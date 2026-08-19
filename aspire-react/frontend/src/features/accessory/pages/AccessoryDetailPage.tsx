import { useEffect, useState } from 'react';
import {
  Descriptions, Tag, Spin, Button, Space, App, Table, Card,
  Typography, Tabs, Badge, Empty,
} from 'antd';
import {
  ArrowLeftOutlined, EditOutlined, GiftOutlined,
  InboxOutlined, AlertOutlined,
  CalendarOutlined, DollarOutlined, UserSwitchOutlined,
  RollbackOutlined, SendOutlined, HistoryOutlined,
} from '@ant-design/icons';
import { ProTable } from '@ant-design/pro-components';
import type { ProColumns } from '@ant-design/pro-components';
import { useNavigate, useParams } from 'react-router-dom';
import { accessoriesApi, checkoutTypeToLabel, checkoutTypeToColor } from '../services/accessories.service';
import type { AccessoryDetail, AccessoryCheckoutDto } from '../services/accessories.service';
import AccessoryCheckoutModal from '../components/AccessoryCheckoutModal';
import AccessoryCheckinModal from '../components/AccessoryCheckinModal';
import { usePermission } from '../../../hooks/usePermission';
import { ACTION_TYPE_TAGS } from '../../../shared/components/ActionLogTable';
import { formatDate, formatMoney } from '../../../utils/format';

const { Title, Text } = Typography;

// ==================== ProTable DTO ====================

interface ActionLogItem {
  id: string;
  itemType: string;
  itemId: string;
  actionType: string;
  actionTypeValue: number;
  targetType: string | null;
  targetId: string | null;
  targetName: string | null;
  creatorName: string | null;
  note: string | null;
  logMeta: string | null;
  actionDate: string;
}

// ==================== Component ====================

const AccessoryDetailPage: React.FC = () => {
  const { message } = App.useApp();
  const navigate = useNavigate();
  const { id } = useParams<{ id: string }>();

  const [detail, setDetail] = useState<AccessoryDetail | null>(null);
  const [checkouts, setCheckouts] = useState<AccessoryCheckoutDto[]>([]);
  const [loading, setLoading] = useState(true);

  const [checkoutModalOpen, setCheckoutModalOpen] = useState(false);
  const [checkinModalOpen, setCheckinModalOpen] = useState(false);
  const [checkinTarget, setCheckinTarget] = useState<AccessoryCheckoutDto | null>(null);

  // ST6b — permission gating (mirrors backend [Authorize(Policy=...)]).
  const canEdit = usePermission('accessories.edit');
  const canCheckout = usePermission('accessories.checkout'); // covers both checkout & checkin

  const loadMasterData = async () => {
    if (!id) return;
    setLoading(true);
    try {
      const [detailRes, checkoutRes] = await Promise.all([
        accessoriesApi.get(id),
        accessoriesApi.getCheckouts(id).catch(() => ({ data: { data: [] } })),
      ]);
      setDetail(detailRes.data.data);
      setCheckouts(checkoutRes.data.data ?? []);
    } catch {
      void message.error('Không thể tải thông tin phụ kiện');
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    void loadMasterData();
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [id]);

  // ──── Helpers ────

  const getFullName = (firstName: string | null, lastName: string | null, userName: string | null) =>
    [firstName, lastName].filter(Boolean).join(' ') || userName || '-';

  // ──── Active checkouts ────
  const activeCheckouts = checkouts.filter((c) => c.remainingOut > 0);

  // ──── Active Checkout Columns ────

  const activeCheckoutColumns = [
    { title: 'Ngày cấp', dataIndex: 'checkedOutAt', key: 'checkedOutAt', width: 110,
      render: (v: string) => formatDate(v) },
    { title: 'Loại', dataIndex: 'checkoutType', key: 'checkoutType', width: 100,
      render: (v: number | string) => <Tag color={checkoutTypeToColor(v)}>{checkoutTypeToLabel(v)}</Tag> },
    { title: 'Đối tượng nhận', dataIndex: 'targetName', key: 'targetName', width: 170,
      render: (v: string | null) => v ?? '-' },
    { title: 'Đã cấp', dataIndex: 'assignedQty', key: 'assignedQty', width: 70, align: 'center' as const,
      render: (v: number) => <Text strong>{v.toLocaleString('vi-VN')}</Text> },
    { title: 'Đã thu', dataIndex: 'returnedQty', key: 'returnedQty', width: 70, align: 'center' as const,
      render: (v: number) => <Text>{v.toLocaleString('vi-VN')}</Text> },
    { title: 'Còn lại', dataIndex: 'remainingOut', key: 'remainingOut', width: 70, align: 'center' as const,
      render: (v: number) => <Text type="warning" strong>{v.toLocaleString('vi-VN')}</Text> },
    { title: 'Người cấp', dataIndex: 'createdByName', key: 'issuer', width: 130,
      render: (_v: string | null, record: AccessoryCheckoutDto) => (
        <Space size={4}><UserSwitchOutlined style={{ color: '#8c8c8c' }} /><Text>{getFullName(record.createdByFirstName, record.createdByLastName, record.createdByName)}</Text></Space>
      ) },
    { title: 'Ghi chú', dataIndex: 'note', key: 'note', ellipsis: true, width: 100,
      render: (v: string | null) => v || '-' },
    { title: '', key: 'actions', width: 90, fixed: 'right' as const,
      render: (_: unknown, record: AccessoryCheckoutDto) => (
        canCheckout ? (
          <Button size="small" type="primary" ghost icon={<RollbackOutlined />}
            onClick={() => { setCheckinTarget(record); setCheckinModalOpen(true); }}>
            Thu hồi
          </Button>
        ) : null
      ) },
  ];

  // ──── ProTable Columns: Action Logs ────

  const actionLogColumns: ProColumns<ActionLogItem>[] = [
    {
      title: 'Thời gian',
      dataIndex: 'actionDate',
      key: 'actionDate',
      valueType: 'dateTime',
      width: 160,
    },
    {
      title: 'Hành động',
      dataIndex: 'actionTypeValue',
      key: 'actionTypeValue',
      width: 110,
      render: (_, record) => {
        const info = ACTION_TYPE_TAGS[record.actionTypeValue] ?? { label: record.actionType, color: 'default' };
        return <Tag color={info.color}>{info.label}</Tag>;
      },
    },
    {
      title: 'Người thực hiện',
      dataIndex: 'creatorName',
      key: 'creatorName',
      width: 160,
      ellipsis: true,
      render: (_, record) => (
        <Space size={4}>
          <UserSwitchOutlined style={{ color: '#8c8c8c' }} />
          <Text>{record.creatorName || '-'}</Text>
        </Space>
      ),
    },
    {
      title: 'Đối tượng liên quan',
      dataIndex: 'targetName',
      key: 'targetName',
      width: 160,
      ellipsis: true,
      search: false,
      render: (_, record) => record.targetName || '-',
    },
    {
      title: 'Chi tiết / Ghi chú',
      key: 'detail',
      width: 280,
      ellipsis: true,
      search: false,
      render: (_, record) => {
        const parts: string[] = [];
        if (record.note) parts.push(record.note);
        if (record.logMeta) {
          try {
            const meta = JSON.parse(record.logMeta);
            if (meta.changes && typeof meta.changes === 'object') {
              // New format (from Checkout/CheckinAccessoryCommand): { changes: { field: { old, new } } }
              const c = meta.changes as Record<string, { old?: unknown; new?: unknown }>;
              const label: Record<string, string> = {
                quantity: 'SL', checkout_type: 'Loại', return_qty: 'Đã trả',
              };
              for (const [k, v] of Object.entries(c)) {
                if (k === 'quantity') parts.push(`SL: ${v?.new}`);
                else if (k === 'return_qty') parts.push(`Đã trả: ${v?.new}`);
                else if (k === 'checkout_type') parts.push(`Loại: ${v?.new}`);
                else parts.push(`${label[k] ?? k}: ${v?.new ?? ''}`);
              }
            } else {
              // Legacy format (raw top-level): { quantity, returnQty, remaining, checkoutType }
              if (meta.quantity != null) parts.push(`SL: ${meta.quantity}`);
              if (meta.returnQty != null) parts.push(`Đã trả: ${meta.returnQty}`);
              if (meta.remaining != null) parts.push(`Còn: ${meta.remaining}`);
              if (meta.checkoutType) parts.push(`Loại: ${meta.checkoutType}`);
            }
          } catch {
            parts.push(record.logMeta.substring(0, 80));
          }
        }
        return <Text type="secondary">{parts.join(' · ') || '-'}</Text>;
      },
    },
  ];

  // ──── Loading / Error ────

  if (loading) {
    return <div style={{ display: 'flex', justifyContent: 'center', padding: 80 }}><Spin size="large" /></div>;
  }

  if (!detail) {
    return <div style={{ textAlign: 'center', padding: 80 }}><Text type="secondary">Không tìm thấy phụ kiện.</Text></div>;
  }

  const accessoryDtoForModal = {
    id: detail.id, name: detail.name, itemNo: detail.itemNo,
    qty: detail.qty, minAmt: detail.minAmt, remaining: detail.remaining,
    checkedOutQty: detail.checkedOutQty, isLowStock: detail.isLowStock,
    companyId: detail.companyId, companyName: detail.company?.name ?? null,
    category: detail.category, location: detail.location,
  };

  // ──── Tab Items ────

  const tabItems = [
    {
      key: 'active',
      label: (
        <span>
          <InboxOutlined style={{ marginRight: 6 }} />
          Đang cấp phát
          {activeCheckouts.length > 0 && (
            <Badge count={activeCheckouts.length} size="small"
              style={{ marginLeft: 8, backgroundColor: '#fa8c16' }} />
          )}
        </span>
      ),
      children: activeCheckouts.length === 0 ? (
        <Empty description="Không có phụ kiện nào đang được cấp phát" style={{ padding: '32px 0' }} />
      ) : (
        <Table columns={activeCheckoutColumns} dataSource={activeCheckouts} rowKey="id" size="small"
          pagination={activeCheckouts.length > 10 ? { pageSize: 10, showTotal: (t) => `${t} bản ghi` } : false}
          scroll={{ x: 'max-content' }} />
      ),
    },
    {
      key: 'logs',
      label: (
        <span>
          <HistoryOutlined style={{ marginRight: 6 }} />
          Lịch sử hoạt động
        </span>
      ),
      children: (
        <ProTable<ActionLogItem>
          rowKey="id"
          columns={actionLogColumns}
          search={false}
          toolBarRender={false}
          options={false}
          scroll={{ x: 'max-content' }}
          ghost
          pagination={{ defaultPageSize: 15, showSizeChanger: false, showTotal: (t) => `${t} sự kiện` }}
          request={async () => {
            try {
              const res = await accessoriesApi.getLogs(id!);
              return { data: res.data.data ?? [], success: true, total: res.data.data?.length ?? 0 };
            } catch {
              void message.error('Không thể tải lịch sử hoạt động');
              return { data: [], success: false, total: 0 };
            }
          }}
          locale={{
            emptyText: <Empty description="Chưa có hoạt động nào" />,
          }}
        />
      ),
    },
  ];

  return (
    <div style={{ maxWidth: 1000 }}>
      {/* Header */}
      <div style={{ display: 'flex', alignItems: 'center', gap: 16, marginBottom: 24 }}>
        <Button icon={<ArrowLeftOutlined />} onClick={() => navigate('/accessories')} size="middle">Quay lại</Button>
        <Title level={4} style={{ margin: 0 }}>Chi tiết phụ kiện</Title>
        {detail.isLowStock && (
          <Tag color="error" icon={<AlertOutlined />} style={{ borderRadius: 4 }}>Tồn kho thấp</Tag>
        )}
        <div style={{ flex: 1 }} />
        <Space>
          {canCheckout && (
            <Button type="primary" ghost icon={<SendOutlined />}
              onClick={() => setCheckoutModalOpen(true)} disabled={detail.remaining <= 0}>
              Cấp phát
            </Button>
          )}
          {canEdit && <Button icon={<EditOutlined />} onClick={() => navigate(`/accessories/${detail.id}`)}>Sửa</Button>}
        </Space>
      </div>

      {/* Stock Summary Cards */}
      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(160px, 1fr))', gap: 16, marginBottom: 24 }}>
        <Card size="small" style={{ borderRadius: 8, textAlign: 'center', background: '#f6ffed', borderColor: '#b7eb8f' }}>
          <Text type="secondary" style={{ fontSize: 12 }}>Tổng số lượng</Text>
          <div><Text strong style={{ fontSize: 24 }}>{detail.qty.toLocaleString('vi-VN')}</Text></div>
        </Card>
        <Card size="small" style={{ borderRadius: 8, textAlign: 'center',
          background: detail.isLowStock ? '#fff2f0' : '#e6f4ff',
          borderColor: detail.isLowStock ? '#ffccc7' : '#91caff' }}>
          <Text type="secondary" style={{ fontSize: 12 }}>Còn lại</Text>
          <div><Text strong style={{ fontSize: 24, color: detail.isLowStock ? '#ff4d4f' : undefined }}>{detail.remaining.toLocaleString('vi-VN')}</Text></div>
          <Text type="secondary" style={{ fontSize: 12 }}>({detail.percentRemaining}%)</Text>
        </Card>
        <Card size="small" style={{ borderRadius: 8, textAlign: 'center', background: '#f0e6ff', borderColor: '#d4baff' }}>
          <Text type="secondary" style={{ fontSize: 12 }}>Đang cấp phát</Text>
          <div><Text strong style={{ fontSize: 24 }}>{detail.checkedOutQty.toLocaleString('vi-VN')}</Text></div>
        </Card>
        <Card size="small" style={{ borderRadius: 8, textAlign: 'center', background: '#fffbe6', borderColor: '#ffe58f' }}>
          <Text type="secondary" style={{ fontSize: 12 }}>Ngưỡng cảnh báo</Text>
          <div><Text strong style={{ fontSize: 24 }}>{detail.minAmt.toLocaleString('vi-VN')}</Text></div>
        </Card>
      </div>

      {/* Detail Info */}
      <Card title={<Space><GiftOutlined /><span>Thông tin phụ kiện</span></Space>} style={{ borderRadius: 12, marginBottom: 24 }}>
        <Descriptions bordered size="small" column={{ xs: 1, sm: 2 }}>
          <Descriptions.Item label="Tên phụ kiện"><Text strong>{detail.name}</Text></Descriptions.Item>
          <Descriptions.Item label="Mã phụ kiện"><Text code>{detail.itemNo || '-'}</Text></Descriptions.Item>
          <Descriptions.Item label="Danh mục">{detail.category?.name ? <Tag color="purple">{detail.category.name}</Tag> : '-'}</Descriptions.Item>
          <Descriptions.Item label="Vị trí">{detail.location?.name ?? '-'}</Descriptions.Item>
          <Descriptions.Item label="Công ty">{detail.company?.name ?? '-'}</Descriptions.Item>
          <Descriptions.Item label="Nhà sản xuất">{detail.manufacturer?.name ?? '-'}</Descriptions.Item>
          <Descriptions.Item label="Nhà cung cấp">{detail.supplier?.name ?? '-'}</Descriptions.Item>
          <Descriptions.Item label="Model No.">{detail.modelNumber || '-'}</Descriptions.Item>
          <Descriptions.Item label="Order No.">{detail.orderNumber || '-'}</Descriptions.Item>
          <Descriptions.Item label="Ngày mua"><CalendarOutlined style={{ marginRight: 4 }} />{formatDate(detail.purchaseDate)}</Descriptions.Item>
          <Descriptions.Item label="Đơn giá"><DollarOutlined style={{ marginRight: 4 }} />{formatMoney(detail.purchaseCost)}</Descriptions.Item>
          <Descriptions.Item label="Ghi chú" span={{ xs: 1, sm: 2 }}>{detail.notes || '-'}</Descriptions.Item>
        </Descriptions>
      </Card>

      {/* Tabs: Active Checkouts + Action Logs */}
      <Card style={{ borderRadius: 12 }} styles={{ body: { paddingTop: 8 } }}>
        <Tabs defaultActiveKey="active" items={tabItems} size="middle" />
      </Card>

      {/* Checkout Modal */}
      <AccessoryCheckoutModal open={checkoutModalOpen} accessory={accessoryDtoForModal}
        onClose={() => setCheckoutModalOpen(false)}
        onSuccess={() => { setCheckoutModalOpen(false); void loadMasterData(); }} />

      {/* Checkin Modal */}
      <AccessoryCheckinModal open={checkinModalOpen} checkout={checkinTarget}
        onClose={() => { setCheckinModalOpen(false); setCheckinTarget(null); }}
        onSuccess={() => { setCheckinModalOpen(false); setCheckinTarget(null); void loadMasterData(); }} />
    </div>
  );
};

export default AccessoryDetailPage;
