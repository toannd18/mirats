import { useCallback, useEffect, useMemo, useState } from 'react';
import {
  Descriptions, Tag, Spin, Button, Space, App, Table, Card,
  Typography, Tabs, Empty,
} from 'antd';
import {
  ArrowLeftOutlined, EditOutlined, AppstoreOutlined,
  InboxOutlined, AlertOutlined,
  CalendarOutlined, DollarOutlined, UserOutlined,
  UserSwitchOutlined, SendOutlined, HistoryOutlined,
} from '@ant-design/icons';
import { ProTable } from '@ant-design/pro-components';
import type { ProColumns } from '@ant-design/pro-components';
import { useNavigate, useParams } from 'react-router-dom';
import apiClient from '../../../services/api-client';
import { usePermission } from '../../../hooks/usePermission';
import ConsumableCheckoutModal from '../components/ConsumableCheckoutModal';
import ConsumableFormModal from '../components/ConsumableFormModal';
import { ACTION_TYPE_TAGS } from '../../../shared/components/ActionLogTable';
import { formatDate, formatMoney } from '../../../utils/format';

const { Title, Text } = Typography;

// ==================== Types ====================

interface ConsumableDetail {
  id: string;
  name: string;
  itemNo: string | null;
  qty: number;
  minAmt: number;
  remaining: number;
  percentRemaining: number;
  isLowStock: boolean;
  modelNumber: string | null;
  orderNumber: string | null;
  purchaseDate: string | null;
  purchaseCost: number | null;
  notes: string | null;
  categoryId: string | null;
  manufacturerId: string | null;
  supplierId: string | null;
  locationId: string | null;
  companyId: string | null;
  category: { id: string; name: string } | null;
  manufacturer: { id: string; name: string } | null;
  supplier: { id: string; name: string } | null;
  location: { id: string; name: string } | null;
  company: { id: string; name: string } | null;
}

interface CheckoutRecord {
  id: string;
  consumableId: string;
  userId: string;
  userName: string | null;
  firstName: string | null;
  lastName: string | null;
  createdByName: string | null;
  createdByFirstName: string | null;
  createdByLastName: string | null;
  quantity: number;
  note: string | null;
  createdAt: string;
}

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

const ConsumableDetailPage: React.FC = () => {
  const { message } = App.useApp();
  const navigate = useNavigate();
  const { id } = useParams<{ id: string }>();

  const [detail, setDetail] = useState<ConsumableDetail | null>(null);
  const [checkouts, setCheckouts] = useState<CheckoutRecord[]>([]);
  const [loading, setLoading] = useState(true);
  const [checkoutModalOpen, setCheckoutModalOpen] = useState(false);
  // Form modal (Sửa) — opened IN PLACE via local state (Task A lesson: no navigate to open modal).
  const [editModalOpen, setEditModalOpen] = useState(false);

  // ST6b — permission gating (mirrors backend [Authorize(Policy=...)]).
  const canEdit = usePermission('consumables.edit');
  const canCheckout = usePermission('consumables.checkout');

  const loadData = useCallback(async () => {
    if (!id) return;
    setLoading(true);
    try {
      const [detailRes, checkoutRes] = await Promise.all([
        apiClient.get(`/consumables/${id}`),
        apiClient.get(`/consumables/${id}/checkouts`).catch(() => ({ data: { data: [] } })),
      ]);
      setDetail(detailRes.data.data);
      setCheckouts(checkoutRes.data.data ?? []);
    } catch {
      void message.error('Không thể tải thông tin vật tư');
    } finally {
      setLoading(false);
    }
  }, [id, message]);

  useEffect(() => {
    void loadData();
  }, [loadData]);

  // Stable reference for the shared checkout modal (avoids reloading users on every parent render).
  const checkoutConsumable = useMemo(
    () => (detail ? {
      id: detail.id,
      name: detail.name,
      companyId: detail.companyId,
      companyName: detail.company?.name ?? null,
      remaining: detail.remaining,
    } : null),
    [detail],
  );

  if (loading) {
    return (
      <div style={{ display: 'flex', justifyContent: 'center', padding: 80 }}>
        <Spin size="large" />
      </div>
    );
  }

  if (!detail) {
    return (
      <div style={{ textAlign: 'center', padding: 80 }}>
        <Text type="secondary">Không tìm thấy vật tư.</Text>
      </div>
    );
  }

  const getFullName = (firstName: string | null, lastName: string | null, userName: string | null) =>
    [firstName, lastName].filter(Boolean).join(' ') || userName || '-';

  const checkoutColumns = [
    {
      title: 'Ngày',
      dataIndex: 'createdAt',
      key: 'createdAt',
      width: 110,
      render: (v: string) => formatDate(v),
    },
    {
      title: 'Người nhận',
      dataIndex: 'userName',
      key: 'receiver',
      width: 160,
      render: (_v: string | null, record: CheckoutRecord) => (
        <Space size={4}>
          <UserOutlined style={{ color: '#8c8c8c' }} />
          <Text>{getFullName(record.firstName, record.lastName, record.userName)}</Text>
        </Space>
      ),
    },
    {
      title: 'Người cấp phát',
      dataIndex: 'createdByName',
      key: 'issuer',
      width: 160,
      render: (_v: string | null, record: CheckoutRecord) => (
        <Space size={4}>
          <UserSwitchOutlined style={{ color: '#8c8c8c' }} />
          <Text>{getFullName(record.createdByFirstName, record.createdByLastName, record.createdByName)}</Text>
        </Space>
      ),
    },
    {
      title: 'SL',
      dataIndex: 'quantity',
      key: 'quantity',
      width: 60,
      align: 'center' as const,
      render: (v: number) => <Text strong>{v.toLocaleString('vi-VN')}</Text>,
    },
    {
      title: 'Ghi chú',
      dataIndex: 'note',
      key: 'note',
      ellipsis: true,
      render: (v: string | null) => v || '-',
    },
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
      title: 'Nội dung',
      dataIndex: 'note',
      key: 'note',
      ellipsis: true,
      render: (_: unknown, record: ActionLogItem) => record.note || '-',
    },
  ];

  // ──── Tab Items ────

  const tabItems = [
    {
      key: 'checkouts',
      label: (
        <span>
          <InboxOutlined style={{ marginRight: 6 }} />
          Lịch sử cấp phát
        </span>
      ),
      children: checkouts.length === 0 ? (
        <Empty description="Chưa có lịch sử cấp phát nào" style={{ padding: '32px 0' }} />
      ) : (
        <Table
          columns={checkoutColumns}
          dataSource={checkouts}
          rowKey="id"
          size="small"
          pagination={{ pageSize: 10, showTotal: (total) => `${total} lần cấp phát` }}
          scroll={{ x: 'max-content' }}
        />
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
          ghost
          pagination={{ defaultPageSize: 15, showSizeChanger: false, showTotal: (t) => `${t} sự kiện` }}
          request={async () => {
            try {
              const res = await apiClient.get('/action-logs', { params: { itemType: 2, itemId: id } });
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
    <div style={{ maxWidth: 960 }}>
      {/* Header */}
      <div style={{ display: 'flex', alignItems: 'center', gap: 16, marginBottom: 24 }}>
        <Button
          icon={<ArrowLeftOutlined />}
          onClick={() => navigate('/consumables')}
          size="middle"
        >
          Quay lại
        </Button>
        <Title level={4} style={{ margin: 0 }}>
          Chi tiết vật tư
        </Title>
        {detail.isLowStock && (
          <Tag color="error" icon={<AlertOutlined />} style={{ borderRadius: 4 }}>
            Tồn kho thấp
          </Tag>
        )}
        <div style={{ flex: 1 }} />
        <Space>
          {canCheckout && (
            <Button type="primary" ghost icon={<SendOutlined />}
              onClick={() => setCheckoutModalOpen(true)}
              disabled={detail.remaining <= 0}>
              Cấp phát
            </Button>
          )}
          {canEdit && <Button icon={<EditOutlined />} onClick={() => setEditModalOpen(true)}>Sửa</Button>}
        </Space>
      </div>

      {/* Stock Summary Cards */}
      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(160px, 1fr))', gap: 16, marginBottom: 24 }}>
        <Card size="small" style={{ borderRadius: 8, textAlign: 'center', background: '#f6ffed', borderColor: '#b7eb8f' }}>
          <Text type="secondary" style={{ fontSize: 12 }}>Tổng số lượng</Text>
          <div><Text strong style={{ fontSize: 24 }}>{detail.qty.toLocaleString('vi-VN')}</Text></div>
        </Card>
        <Card
          size="small"
          style={{
            borderRadius: 8,
            textAlign: 'center',
            background: detail.isLowStock ? '#fff2f0' : '#e6f4ff',
            borderColor: detail.isLowStock ? '#ffccc7' : '#91caff',
          }}
        >
          <Text type="secondary" style={{ fontSize: 12 }}>Còn lại</Text>
          <div>
            <Text strong style={{ fontSize: 24, color: detail.isLowStock ? '#ff4d4f' : undefined }}>
              {detail.remaining.toLocaleString('vi-VN')}
            </Text>
          </div>
          <Text type="secondary" style={{ fontSize: 12 }}>
            ({detail.percentRemaining}%)
          </Text>
        </Card>
        <Card size="small" style={{ borderRadius: 8, textAlign: 'center', background: '#fffbe6', borderColor: '#ffe58f' }}>
          <Text type="secondary" style={{ fontSize: 12 }}>Ngưỡng cảnh báo</Text>
          <div><Text strong style={{ fontSize: 24 }}>{detail.minAmt.toLocaleString('vi-VN')}</Text></div>
        </Card>
      </div>

      {/* Detail Info */}
      <Card
        title={
          <Space>
            <AppstoreOutlined />
            <span>Thông tin vật tư</span>
          </Space>
        }
        style={{ borderRadius: 12, marginBottom: 24 }}
      >
        <Descriptions bordered size="small" column={{ xs: 1, sm: 2 }}>
          <Descriptions.Item label="Tên vật tư">
            <Text strong>{detail.name}</Text>
          </Descriptions.Item>
          <Descriptions.Item label="Mã vật tư">
            <Text code>{detail.itemNo || '-'}</Text>
          </Descriptions.Item>
          <Descriptions.Item label="Danh mục">
            {detail.category?.name ? (
              <Tag color="geekblue">{detail.category.name}</Tag>
            ) : '-'}
          </Descriptions.Item>
          <Descriptions.Item label="Vị trí">
            {detail.location?.name ?? '-'}
          </Descriptions.Item>
          <Descriptions.Item label="Công ty">
            {detail.company?.name ?? '-'}
          </Descriptions.Item>
          <Descriptions.Item label="Nhà sản xuất">
            {detail.manufacturer?.name ?? '-'}
          </Descriptions.Item>
          <Descriptions.Item label="Nhà cung cấp">
            {detail.supplier?.name ?? '-'}
          </Descriptions.Item>
          <Descriptions.Item label="Model No.">
            {detail.modelNumber || '-'}
          </Descriptions.Item>
          <Descriptions.Item label="Order No.">
            {detail.orderNumber || '-'}
          </Descriptions.Item>
          <Descriptions.Item label="Ngày mua">
            <CalendarOutlined style={{ marginRight: 4 }} />
            {formatDate(detail.purchaseDate)}
          </Descriptions.Item>
          <Descriptions.Item label="Đơn giá">
            <DollarOutlined style={{ marginRight: 4 }} />
            {formatMoney(detail.purchaseCost)}
          </Descriptions.Item>
          <Descriptions.Item label="Ghi chú" span={{ xs: 1, sm: 2 }}>
            {detail.notes || '-'}
          </Descriptions.Item>
        </Descriptions>
      </Card>

      {/* Tabs: Checkout History + Action Logs */}
      <Card style={{ borderRadius: 12 }} styles={{ body: { paddingTop: 8 } }}>
        <Tabs defaultActiveKey="checkouts" items={tabItems} size="middle" />
      </Card>

      {/* Checkout Modal — opens in place (fix: no more navigate to the list page) */}
      <ConsumableCheckoutModal
        open={checkoutModalOpen}
        consumable={checkoutConsumable}
        onClose={() => setCheckoutModalOpen(false)}
        onSuccess={() => {
          setCheckoutModalOpen(false);
          void loadData();
        }}
      />

      {/* Form modal (Sửa) — opens in place via local state, no navigation */}
      <ConsumableFormModal
        open={editModalOpen}
        consumableId={detail.id}
        onClose={() => setEditModalOpen(false)}
        onSaved={() => {
          setEditModalOpen(false);
          void loadData();
        }}
      />
    </div>
  );
};

export default ConsumableDetailPage;
