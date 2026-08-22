import { useEffect, useRef, useState } from 'react';
import {
  Button, Tag, Space, Popconfirm, Modal, InputNumber, App,
  Typography, Divider, Card,
} from 'antd';
import {
  PlusOutlined, EditOutlined, DeleteOutlined,
  EyeOutlined, SendOutlined,
  GiftOutlined, EnvironmentOutlined, InboxOutlined,
  AlertOutlined, RollbackOutlined, FileTextOutlined,
} from '@ant-design/icons';
import { ProList } from '@ant-design/pro-components';
import type { ActionType, ProColumns } from '@ant-design/pro-components';
import apiClient from '../../../services/api-client';
import { accessoriesApi, checkoutTypeToLabel } from '../services/accessories.service';
import type { AccessoryDto, AccessoryCheckoutDto, CheckinRequest } from '../services/accessories.service';
import { useLocation, useNavigate } from 'react-router-dom';
import { usePermission } from '../../../hooks/usePermission';
import AccessoryCheckoutModal from '../components/AccessoryCheckoutModal';
import AccessoryFormModal from '../components/AccessoryFormModal';
import { statusColors } from '../../../theme/designTokens';

const { Text, Title, Paragraph } = Typography;

// ==================== Styles ====================

const iconBadgeStyle: React.CSSProperties = {
  width: 48,
  height: 48,
  borderRadius: 12,
  background: 'linear-gradient(135deg, #f0e6ff 0%, #d4baff 100%)',
  display: 'flex',
  alignItems: 'center',
  justifyContent: 'center',
  flexShrink: 0,
};

const dataGridStyle: React.CSSProperties = {
  display: 'grid',
  gridTemplateColumns: '1fr 1fr',
  gap: '8px 16px',
  padding: '12px 16px',
  background: '#fafafa',
  borderRadius: 8,
  border: '1px solid #f0f0f0',
};

const dataRowStyle: React.CSSProperties = {
  display: 'flex',
  alignItems: 'center',
  gap: 6,
};

const labelIconStyle: React.CSSProperties = {
  color: '#8c8c8c',
  fontSize: 13,
};

// ==================== Component ====================

const AccessoryListPage: React.FC = () => {
  const { message } = App.useApp();
  const navigate = useNavigate();
  const location = useLocation();
  const actionRef = useRef<ActionType>(null);

  const [checkoutModalOpen, setCheckoutModalOpen] = useState(false);
  const [checkoutTarget, setCheckoutTarget] = useState<AccessoryDto | null>(null);

  const [checkinModalOpen, setCheckinModalOpen] = useState(false);
  const [checkinCheckouts, setCheckinCheckouts] = useState<AccessoryCheckoutDto[]>([]);
  const [checkinLoading, setCheckinLoading] = useState(false);
  const [checkinAccessoryId, setCheckinAccessoryId] = useState<string | null>(null);
  const [checkinAccessoryName, setCheckinAccessoryName] = useState('');

  // Form modal (Tạo mới/Sửa) — opened IN PLACE via local state (Task A lesson: never navigate
  // to open a modal). Deep-link routes only set this state on the current page.
  const [formModalOpen, setFormModalOpen] = useState(false);
  const [formModalAccessoryId, setFormModalAccessoryId] = useState<string | null>(null);

  // Deep-link support: /accessories/new (create) & /accessories/:id (edit) open the form modal
  // on THIS page by setting local state — no redirect through another page.
  useEffect(() => {
    if (location.pathname === '/accessories/new') {
      setFormModalAccessoryId(null);
      setFormModalOpen(true);
    } else {
      const editMatch = location.pathname.match(/^\/accessories\/(?!new$)([^/]+)$/);
      if (editMatch) {
        setFormModalAccessoryId(editMatch[1]);
        setFormModalOpen(true);
      }
    }
  }, [location.pathname]);

  // ST6b — permission gating (mirrors backend [Authorize(Policy=...)]).
  const canCreate = usePermission('accessories.create');
  const canEdit = usePermission('accessories.edit');
  const canDelete = usePermission('accessories.delete');
  const canCheckout = usePermission('accessories.checkout'); // covers both checkout & checkin (backend uses accessories.checkout for both)

  // ──── Handlers ────

  const handleDelete = async (id: string) => {
    try {
      await accessoriesApi.delete(id);
      void message.success('Đã xóa phụ kiện');
      actionRef.current?.reload();
    } catch (err: unknown) {
      const e = err as { response?: { data?: { message?: string } } };
      void message.error(e?.response?.data?.message ?? 'Lỗi xóa');
    }
  };

  const openCheckout = (record: AccessoryDto) => {
    setCheckoutTarget(record);
    setCheckoutModalOpen(true);
  };

  const openCheckin = async (record: AccessoryDto) => {
    setCheckinAccessoryId(record.id);
    setCheckinAccessoryName(record.name);
    setCheckinLoading(true);
    setCheckinModalOpen(true);
    setCheckinCheckouts([]);

    try {
      const res = await accessoriesApi.getCheckouts(record.id);
      const allCheckouts = res.data.data as AccessoryCheckoutDto[];
      // Only show checkouts that still have items out (not fully returned)
      const active = allCheckouts.filter((c) => c.remainingOut > 0);
      setCheckinCheckouts(active);

      if (active.length === 0) {
        void message.info('Không có bản ghi cấp phát nào đang hoạt động.');
      }
    } catch {
      void message.error('Lỗi tải danh sách cấp phát');
    } finally {
      setCheckinLoading(false);
    }
  };

  const handleCheckin = async (checkoutId: string, returnQty: number, checkout: AccessoryCheckoutDto) => {
    if (returnQty < 1) {
      void message.warning('Số lượng thu hồi phải > 0');
      return;
    }
    if (returnQty > checkout.remainingOut) {
      void message.error(`Không thể thu hồi quá số lượng đang cấp phát (${checkout.remainingOut.toLocaleString('vi-VN')})`);
      return;
    }

    try {
      const payload: CheckinRequest = { returnQty };
      await accessoriesApi.checkin(checkoutId, payload);
      void message.success('Đã thu hồi phụ kiện');
      // Reload the checkouts list
      if (checkinAccessoryId) {
        const res = await accessoriesApi.getCheckouts(checkinAccessoryId);
        const all = res.data.data as AccessoryCheckoutDto[];
        setCheckinCheckouts(all.filter((c) => c.remainingOut > 0));
      }
      actionRef.current?.reload();
    } catch (err: unknown) {
      const e = err as { response?: { data?: { message?: string } } };
      void message.error(e?.response?.data?.message ?? 'Lỗi thu hồi');
    }
  };

  // ──── Checkin Row Component ────

  const CheckinRow: React.FC<{ checkout: AccessoryCheckoutDto }> = ({ checkout }) => {
    const [returnQty, setReturnQty] = useState(1);
    const [checkinSubmitting, setCheckinSubmitting] = useState(false);

    return (
      <div
        key={checkout.id}
        style={{
          display: 'flex',
          alignItems: 'center',
          gap: 12,
          padding: '10px 0',
          borderBottom: '1px solid #f0f0f0',
          flexWrap: 'wrap',
        }}
      >
        <div style={{ flex: 1, minWidth: 0 }}>
          <div>
            <Tag color="purple" style={{ marginRight: 8 }}>
              {checkoutTypeToLabel(checkout.checkoutType)}
            </Tag>
            <Text strong>{checkout.targetName ?? checkout.targetId}</Text>
          </div>
          <div style={{ marginTop: 4 }}>
            <Text type="secondary" style={{ fontSize: 12 }}>
              Đã cấp: {checkout.assignedQty.toLocaleString('vi-VN')}
              {' · '}
              Đã thu: {checkout.returnedQty.toLocaleString('vi-VN')}
              {' · '}
              Còn lại: <Text type="danger" strong>{checkout.remainingOut.toLocaleString('vi-VN')}</Text>
            </Text>
          </div>
          {checkout.note && (
            <Text type="secondary" style={{ fontSize: 12, display: 'block' }}>
              Ghi chú: {checkout.note}
            </Text>
          )}
        </div>
        <Space size="small">
          <InputNumber
            min={1}
            max={checkout.remainingOut}
            value={returnQty}
            onChange={(v) => setReturnQty(v ?? 1)}
            style={{ width: 80 }}
            size="small"
          />
          <Button
            type="primary"
            ghost
            size="small"
            icon={<RollbackOutlined />}
            loading={checkinSubmitting}
            onClick={async () => {
              setCheckinSubmitting(true);
              await handleCheckin(checkout.id, returnQty, checkout);
              setCheckinSubmitting(false);
            }}
          >
            Thu hồi
          </Button>
        </Space>
      </div>
    );
  };

  // ──── Search Columns ────

  const searchColumns: ProColumns<AccessoryDto>[] = [
    {
      title: 'Tìm kiếm',
      dataIndex: 'search',
      valueType: 'text',
      hideInTable: true,
      fieldProps: { placeholder: 'Tên, mã phụ kiện...' },
    },
    {
      title: 'Danh mục',
      dataIndex: 'categoryId',
      valueType: 'select',
      hideInTable: true,
      request: async () => {
        const res = await apiClient.get('/categories');
        const consumableAndAccessoryCategories = (res.data.data ?? []).filter(
          (c: { categoryType: number | string }) => c.categoryType === 'Consumable' || c.categoryType === 'Accessory'
        );
        return consumableAndAccessoryCategories.map((c: { id: string; name: string }) => ({
          label: c.name,
          value: c.id,
        }));
      },
    },
    {
      title: 'Vị trí',
      dataIndex: 'locationId',
      valueType: 'select',
      hideInTable: true,
      request: async () => {
        const res = await apiClient.get('/locations', { params: { pageSize: 500 } });
        return (res.data.data ?? []).map((l: { id: string; name: string }) => ({
          label: l.name,
          value: l.id,
        }));
      },
    },
  ];

  // ──── Render ────

  return (
    <>
      <ProList<AccessoryDto>
        headerTitle={
          <Title level={4} style={{ margin: 0 }}>
            Phụ kiện
          </Title>
        }
        actionRef={actionRef}
        rowKey="id"
        ghost
        cardProps={false}
        columns={searchColumns}
        grid={{ gutter: 16, xs: 1, sm: 1, md: 2, lg: 2, xl: 3, xxl: 3 }}
        search={{
          labelWidth: 'auto',
          defaultCollapsed: false,
        }}
        toolBarRender={() => [
          canCreate && (
            <Button
              key="create"
              type="primary"
              size="middle"
              icon={<PlusOutlined />}
              onClick={() => { setFormModalAccessoryId(null); setFormModalOpen(true); }}
            >
              Thêm Phụ kiện
            </Button>
          ),
        ]}
        request={async (params) => {
          try {
            const { current, pageSize, ...rest } = params;
            const res = await accessoriesApi.list({ ...rest, page: current, pageSize });
            return {
              data: res.data.data,
              success: true,
              total: res.data.pagination?.totalItems ?? 0,
            };
          } catch {
            void message.error('Không thể tải danh sách phụ kiện');
            return { data: [], success: false, total: 0 };
          }
        }}
        pagination={{
          defaultPageSize: 12,
          showSizeChanger: true,
          showTotal: (total, range) => `${range[0]}-${range[1]} / ${total} mục`,
        }}
        itemRender={(record) => (
          <Card
            hoverable
            onClick={() => navigate(`/accessories/${record.id}/view`)}
            style={{
              borderRadius: 12,
              marginBottom: 16,
              cursor: 'pointer',
              transition: 'all 0.25s cubic-bezier(0.4, 0, 0.2, 1)',
            }}
            styles={{ body: { padding: '20px 20px 16px' } }}
          >
            {/* Header */}
            <div style={{ display: 'flex', alignItems: 'center', gap: 12, marginBottom: 12 }}>
              <div style={iconBadgeStyle}>
                <GiftOutlined style={{ fontSize: 22, color: '#722ed1' }} />
              </div>
              <div style={{ flex: 1, minWidth: 0 }}>
                <div style={{ display: 'flex', alignItems: 'baseline', gap: 8, flexWrap: 'wrap' }}>
                  <Text strong style={{ fontSize: 16, lineHeight: 1.4 }}>
                    {record.name}
                  </Text>
                  {record.itemNo && (
                    <Text
                      type="secondary"
                      style={{
                        fontSize: 12,
                        fontFamily: '"SF Mono", "Fira Code", "Cascadia Code", monospace',
                        background: '#f5f5f5',
                        padding: '1px 8px',
                        borderRadius: 4,
                        border: '1px solid #e8e8e8',
                        whiteSpace: 'nowrap',
                        maxWidth: '100%',
                        overflow: 'hidden',
                        textOverflow: 'ellipsis',
                      }}
                    >
                      {record.itemNo}
                    </Text>
                  )}
                </div>
              </div>
            </div>

            {/* Tags */}
            <Space size={[4, 4]} wrap style={{ marginBottom: 12, paddingLeft: 60 }}>
              {record.category?.name && (
                <Tag color="purple" style={{ borderRadius: 4, margin: 0 }}>
                  {record.category.name}
                </Tag>
              )}
              {record.checkedOutQty > 0 ? (
                <Tag color="orange" style={{ borderRadius: 4, margin: 0 }}>
                  Đang cấp phát
                </Tag>
              ) : (
                <Tag color={statusColors.ready} style={{ borderRadius: 4, margin: 0 }}>
                  Sẵn sàng
                </Tag>
              )}
              {record.isLowStock && (
                <Tag
                  color="error"
                  icon={<AlertOutlined />}
                  style={{ borderRadius: 4, margin: 0, fontWeight: 500 }}
                >
                  Tồn kho thấp
                </Tag>
              )}
            </Space>

            {/* Data Grid */}
            <div style={dataGridStyle}>
              <div style={dataRowStyle}>
                <EnvironmentOutlined style={labelIconStyle} />
                <Text type="secondary" style={{ fontSize: 12 }}>Vị trí</Text>
              </div>
              <div style={dataRowStyle}>
                {record.location?.name ? (
                  <Text style={{ fontSize: 13, fontWeight: 500 }}>{record.location.name}</Text>
                ) : (
                  <Text type="secondary" italic style={{ fontSize: 13 }}>Chưa gán</Text>
                )}
              </div>

              <div style={dataRowStyle}>
                <InboxOutlined style={labelIconStyle} />
                <Text type="secondary" style={{ fontSize: 12 }}>Tổng số lượng</Text>
              </div>
              <div style={dataRowStyle}>
                <Text strong style={{ fontSize: 14 }}>{record.qty.toLocaleString('vi-VN')}</Text>
              </div>

              <div style={dataRowStyle}>
                <InboxOutlined style={labelIconStyle} />
                <Text type="secondary" style={{ fontSize: 12 }}>Còn lại</Text>
              </div>
              <div style={dataRowStyle}>
                {record.isLowStock ? (
                  <Text strong type="danger" style={{ fontSize: 15, lineHeight: 1 }}>
                    {record.remaining.toLocaleString('vi-VN')}
                    <Text type="secondary" style={{ fontSize: 11, marginLeft: 4, fontWeight: 400 }}>
                      (Ngưỡng: {record.minAmt})
                    </Text>
                  </Text>
                ) : (
                  <Text strong style={{ fontSize: 14 }}>{record.remaining.toLocaleString('vi-VN')}</Text>
                )}
              </div>

              {record.notes && (
                <>
                  <div style={{ ...dataRowStyle, gridColumn: '1 / -1' }}>
                    <FileTextOutlined style={labelIconStyle} />
                    <Text type="secondary" style={{ fontSize: 12 }}>Ghi chú</Text>
                  </div>
                  <div style={{ gridColumn: '1 / -1', minWidth: 0 }}>
                    <Paragraph
                      ellipsis={{ rows: 2, tooltip: record.notes }}
                      style={{ fontSize: 13, margin: 0 }}
                    >
                      {record.notes}
                    </Paragraph>
                  </div>
                </>
              )}
            </div>

            {/* Divider + Actions */}
            <Divider style={{ margin: '12px 0' }} />

            <Space size="small" wrap style={{ justifyContent: 'flex-end', width: '100%' }}>
              {canEdit && (
                <Button
                  size="middle"
                  icon={<EditOutlined />}
                  onClick={(e) => { e.stopPropagation(); setFormModalAccessoryId(record.id); setFormModalOpen(true); }}
                >
                  Sửa
                </Button>
              )}
              <Button
                size="middle"
                icon={<EyeOutlined />}
                onClick={(e) => { e.stopPropagation(); navigate(`/accessories/${record.id}/view`); }}
              >
                Xem
              </Button>
              {canCheckout && (
                <Button
                  size="middle"
                  type="primary"
                  ghost
                  icon={<SendOutlined />}
                  onClick={(e) => { e.stopPropagation(); openCheckout(record); }}
                  disabled={record.remaining <= 0}
                >
                  Cấp phát
                </Button>
              )}
              {canCheckout && (
                <Button
                  size="middle"
                  icon={<RollbackOutlined />}
                  onClick={(e) => { e.stopPropagation(); openCheckin(record); }}
                  disabled={record.checkedOutQty <= 0}
                >
                  Thu hồi
                </Button>
              )}
              {canDelete && (
                <Popconfirm
                  title="Xóa phụ kiện này?"
                  description="Hành động này không thể hoàn tác."
                  onConfirm={() => handleDelete(record.id)}
                  okText="Xóa"
                  okButtonProps={{ danger: true }}
                  cancelText="Hủy"
                >
                  <Button size="middle" danger icon={<DeleteOutlined />} onClick={(e) => e.stopPropagation()}
                    disabled={record.checkedOutQty > 0}>
                    Xóa
                  </Button>
                </Popconfirm>
              )}
            </Space>
          </Card>
        )}
      />

      {/* Checkout Modal */}
      <AccessoryCheckoutModal
        open={checkoutModalOpen}
        accessory={checkoutTarget}
        onClose={() => {
          setCheckoutModalOpen(false);
          setCheckoutTarget(null);
        }}
        onSuccess={() => {
          setCheckoutModalOpen(false);
          setCheckoutTarget(null);
          actionRef.current?.reload();
        }}
      />

      {/* Checkin Modal */}
      <Modal
        title={
          <Space>
            <RollbackOutlined />
            <span>Thu hồi phụ kiện — {checkinAccessoryName}</span>
          </Space>
        }
        open={checkinModalOpen}
        onCancel={() => { setCheckinModalOpen(false); setCheckinCheckouts([]); }}
        footer={null}
        width={640}
        destroyOnClose
      >
        {checkinLoading ? (
          <div style={{ textAlign: 'center', padding: 24 }}>
            <Text type="secondary">Đang tải danh sách cấp phát...</Text>
          </div>
        ) : checkinCheckouts.length === 0 ? (
          <div style={{ textAlign: 'center', padding: 24 }}>
            <Text type="secondary">Không có bản ghi cấp phát nào đang hoạt động.</Text>
          </div>
        ) : (
          <div>
            {checkinCheckouts.map((ch) => (
              <CheckinRow key={ch.id} checkout={ch} />
            ))}
          </div>
        )}
      </Modal>

      {/* Form Modal (Tạo mới/Sửa) — mở TẠI CHỖ bằng state cục bộ. Deep-link
          (/accessories/new, /accessories/:id) also only set state, never redirect. */}
      <AccessoryFormModal
        open={formModalOpen}
        accessoryId={formModalAccessoryId}
        onClose={() => {
          setFormModalOpen(false);
          setFormModalAccessoryId(null);
          navigate('/accessories', { replace: true });
        }}
        onSaved={() => {
          setFormModalOpen(false);
          setFormModalAccessoryId(null);
          navigate('/accessories', { replace: true });
          actionRef.current?.reload();
        }}
      />
    </>
  );
};

export default AccessoryListPage;