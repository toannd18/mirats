import { useEffect, useRef, useState } from 'react';
import {
  Button, Tag, Space, Popconfirm, App,
  Typography, Divider, Card,
} from 'antd';
import {
  PlusOutlined, EditOutlined, DeleteOutlined,
  CheckCircleOutlined, EyeOutlined, SendOutlined,
  AppstoreOutlined, EnvironmentOutlined, InboxOutlined,
  AlertOutlined, FileTextOutlined,
} from '@ant-design/icons';
import { ProList } from '@ant-design/pro-components';
import type { ActionType, ProColumns } from '@ant-design/pro-components';
import apiClient from '../../../services/api-client';
import { consumablesApi } from '../services/consumables.service';
import { useNavigate, useLocation, useSearchParams } from 'react-router-dom';
import { usePermission } from '../../../hooks/usePermission';
import ConsumableCheckoutModal from '../components/ConsumableCheckoutModal';
import ConsumableFormModal from '../components/ConsumableFormModal';
import { statusColors, uiColors, cardBadgeGradients } from '../../../theme/designTokens';

const { Text, Title, Paragraph } = Typography;

// ==================== Types ====================

interface ConsumableDto {
  id: string;
  name: string;
  itemNo: string | null;
  notes: string | null;
  qty: number;
  minAmt: number;
  status: string; // "Pending" | "Confirmed" (enum serialized as string — JsonStringEnumConverter)
  remaining: number;
  isLowStock: boolean;
  companyId: string | null;
  companyName: string | null;
  category: { id: string; name: string } | null;
  location: { id: string; name: string } | null;
}

// ==================== Styles ====================

const iconBadgeStyle: React.CSSProperties = {
  width: 48,
  height: 48,
  borderRadius: 12,
  background: cardBadgeGradients.lightBlue,
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
  color: uiColors.labelGray,
  fontSize: 13,
};

// ==================== Component ====================

const ConsumableListPage: React.FC = () => {
  const { message } = App.useApp();
  const navigate = useNavigate();
  const location = useLocation();
  const [searchParams, setSearchParams] = useSearchParams();
  const actionRef = useRef<ActionType>(null);
  // Backend serializes enums as strings (JsonStringEnumConverter) — compare the string value only.
  const isConfirmed = (status: string) => status === 'Confirmed';

  const [checkoutOpen, setCheckoutOpen] = useState(false);
  const [checkoutTarget, setCheckoutTarget] = useState<ConsumableDto | null>(null);

  // Form modal (Tạo mới/Sửa) — opened IN PLACE via local state (Task A lesson: never navigate
  // to open a modal). Deep-link routes only set this state on the current page.
  const [formModalOpen, setFormModalOpen] = useState(false);
  const [formModalConsumableId, setFormModalConsumableId] = useState<string | null>(null);

  // Deep-link support: /consumables/new (create) & /consumables/:id (edit) open the form modal
  // on THIS page by setting local state — no redirect through another page.
  useEffect(() => {
    if (location.pathname === '/consumables/new') {
      setFormModalConsumableId(null);
      setFormModalOpen(true);
    } else {
      const editMatch = location.pathname.match(/^\/consumables\/(?!new$)([^/]+)$/);
      if (editMatch) {
        setFormModalConsumableId(editMatch[1]);
        setFormModalOpen(true);
      }
    }
  }, [location.pathname]);

  // ST6b — permission gating (mirrors backend [Authorize(Policy=...)]).
  const canCreate = usePermission('consumables.create');
  const canEdit = usePermission('consumables.edit');
  const canDelete = usePermission('consumables.delete');
  const canCheckout = usePermission('consumables.checkout');

  // ──── Auto-open checkout modal when navigating from the detail page (?checkout=<id>) ────

  useEffect(() => {
    const checkoutId = searchParams.get('checkout');
    if (!checkoutId) return;
    setSearchParams({}, { replace: true });
    (async () => {
      try {
        const res = await apiClient.get(`/consumables/${checkoutId}`);
        const d = res.data.data;
        const dto: ConsumableDto = {
          id: d.id,
          name: d.name,
          itemNo: d.itemNo,
          notes: d.notes ?? null,
          qty: d.qty,
          minAmt: d.minAmt,
          status: d.status,
          remaining: d.remaining,
          isLowStock: d.isLowStock,
          companyId: d.companyId,
          companyName: d.company?.name ?? null,
          category: d.category ?? null,
          location: d.location ?? null,
        };
        setCheckoutTarget(dto);
        setCheckoutOpen(true);
      } catch {
        void message.error('Không thể mở cấp phát cho vật tư này');
      }
    })();
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  // ──── Handlers ────

  const handleConfirm = async (id: string) => {
    try {
      await apiClient.put(`/consumables/${id}/confirm`);
      void message.success('Đã xác nhận vật tư');
      actionRef.current?.reload();
    } catch (err: unknown) {
      const e = err as { response?: { data?: { message?: string } } };
      void message.error(e?.response?.data?.message ?? 'Lỗi xác nhận');
    }
  };

  const handleDelete = async (id: string) => {
    try {
      await apiClient.delete(`/consumables/${id}`);
      void message.success('Đã xóa vật tư');
      actionRef.current?.reload();
    } catch (err: unknown) {
      const e = err as { response?: { data?: { message?: string } } };
      void message.error(e?.response?.data?.message ?? 'Lỗi xóa');
    }
  };

  const openCheckout = (record: ConsumableDto) => {
    setCheckoutTarget(record);
    setCheckoutOpen(true);
  };

  // ──── Search Columns (ẩn trong bảng, chỉ dùng cho form tìm kiếm) ────
  const searchColumns: ProColumns<ConsumableDto>[] = [
    {
      title: 'Tìm kiếm',
      dataIndex: 'search',
      valueType: 'text',
      hideInTable: true,
      fieldProps: { placeholder: 'Tên, mã vật tư...' },
    },
    {
      title: 'Danh mục',
      dataIndex: 'categoryId',
      valueType: 'select',
      hideInTable: true,
      request: async () => {
        const res = await apiClient.get('/categories');
        const consumableCategories = (res.data.data ?? []).filter((c: { categoryType: number | string }) => c.categoryType === 'Consumable');
        return consumableCategories.map((c: { id: string; name: string }) => ({ label: c.name, value: c.id }));
      },
    },
    {
      title: 'Vị trí',
      dataIndex: 'locationId',
      valueType: 'select',
      hideInTable: true,
      request: async () => {
        const res = await apiClient.get('/locations', { params: { pageSize: 500 } });
        return (res.data.data ?? []).map((l: { id: string; name: string }) => ({ label: l.name, value: l.id }));
      },
    },
  ];

  // ──── Render ────

  return (
    <>
      <ProList<ConsumableDto>
        headerTitle={
          <Title level={4} style={{ margin: 0 }}>
            Vật tư tiêu hao
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
              onClick={() => { setFormModalConsumableId(null); setFormModalOpen(true); }}
            >
              Thêm Vật tư
            </Button>
          ),
        ]}
        request={async (params) => {
          try {
            const { current, pageSize, ...rest } = params;
            const res = await consumablesApi.list({ ...rest, page: current, pageSize });
            return {
              data: res.data.data,
              success: true,
              total: res.data.pagination?.totalItems ?? 0,
            };
          } catch {
            void message.error('Không thể tải danh sách vật tư');
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
            onClick={() => navigate(`/consumables/${record.id}/view`)}
            style={{
              borderRadius: 12,
              marginBottom: 16,
              cursor: 'pointer',
              transition: 'all 0.25s cubic-bezier(0.4, 0, 0.2, 1)',
            }}
            styles={{ body: { padding: '20px 20px 16px' } }}
          >
            {/* ── Header: Icon + Name + ItemNo ── */}
            <div style={{ display: 'flex', alignItems: 'center', gap: 12, marginBottom: 12 }}>
              <div style={iconBadgeStyle}>
                <AppstoreOutlined style={{ fontSize: 22, color: '#1677ff' }} />
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

            {/* ── Tags ── */}
            <Space size={[4, 4]} wrap style={{ marginBottom: 12, paddingLeft: 60 }}>
              {record.category?.name && (
                <Tag color="geekblue" style={{ borderRadius: 4, margin: 0 }}>
                  {record.category.name}
                </Tag>
              )}
              <Tag
                color={isConfirmed(record.status) ? statusColors.active : statusColors.pending}
                style={{ borderRadius: 4, margin: 0 }}
              >
                {isConfirmed(record.status) ? 'Đã xác nhận' : 'Chờ xác nhận'}
              </Tag>
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

            {/* ── Data Grid ── */}
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

            {/* ── Divider + Actions ── */}
            <Divider style={{ margin: '12px 0' }} />

            <Space size="small" wrap style={{ justifyContent: 'flex-end', width: '100%' }}>
              {isConfirmed(record.status) ? (
                <Button
                  size="middle"
                  icon={<EyeOutlined />}
                  onClick={(e) => { e.stopPropagation(); navigate(`/consumables/${record.id}/view`); }}
                >
                  Xem
                </Button>
              ) : (
                <>
                  {canEdit && (
                    <Button
                      size="middle"
                      icon={<EditOutlined />}
                      onClick={(e) => { e.stopPropagation(); setFormModalConsumableId(record.id); setFormModalOpen(true); }}
                    >
                      Sửa
                    </Button>
                  )}
                  {canEdit && (
                    <Popconfirm
                      title="Xác nhận vật tư này?"
                      description="Vật tư sẽ chuyển sang trạng thái đã xác nhận."
                      onConfirm={() => handleConfirm(record.id)}
                      okText="Xác nhận"
                      cancelText="Hủy"
                    >
                      <Button size="middle" type="primary" icon={<CheckCircleOutlined />} onClick={(e) => e.stopPropagation()}>
                        Xác nhận
                      </Button>
                    </Popconfirm>
                  )}
                  {canDelete && (
                    <Popconfirm
                      title="Xóa vật tư này?"
                      description="Hành động này không thể hoàn tác."
                      onConfirm={() => handleDelete(record.id)}
                      okText="Xóa"
                      okButtonProps={{ danger: true }}
                      cancelText="Hủy"
                    >
                      <Button size="middle" danger icon={<DeleteOutlined />} onClick={(e) => e.stopPropagation()}>
                        Xóa
                      </Button>
                    </Popconfirm>
                  )}
                </>
              )}
              {canCheckout && isConfirmed(record.status) && (
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
            </Space>
          </Card>
        )}
      />

      <ConsumableCheckoutModal
        open={checkoutOpen}
        consumable={checkoutTarget}
        onClose={() => {
          setCheckoutOpen(false);
          setCheckoutTarget(null);
        }}
        onSuccess={() => {
          setCheckoutOpen(false);
          setCheckoutTarget(null);
          actionRef.current?.reload();
        }}
      />

      {/* Form modal (Tạo mới/Sửa) — opened in place by local state; deep-link routes
          (/consumables/new, /consumables/:id) also only set state, never redirect. */}
      <ConsumableFormModal
        open={formModalOpen}
        consumableId={formModalConsumableId}
        onClose={() => {
          setFormModalOpen(false);
          setFormModalConsumableId(null);
          navigate('/consumables', { replace: true });
        }}
        onSaved={() => {
          setFormModalOpen(false);
          setFormModalConsumableId(null);
          navigate('/consumables', { replace: true });
          actionRef.current?.reload();
        }}
      />
    </>
  );
};

export default ConsumableListPage;