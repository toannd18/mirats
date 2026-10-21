import { useEffect, useRef, useState } from 'react';
import {
  Button, Tag, Space, App, Typography, Card, Divider, Dropdown, Tooltip,
} from 'antd';
import type { MenuProps } from 'antd';
import {
  PlusOutlined, EditOutlined, DeleteOutlined,
  CheckCircleOutlined, SendOutlined,
  AppstoreOutlined, EnvironmentOutlined, InboxOutlined,
  AlertOutlined, MoreOutlined,
} from '@ant-design/icons';
import { ProList, ProTable } from '@ant-design/pro-components';
import type { ActionType, ProColumns } from '@ant-design/pro-components';
import apiClient from '../../../services/api-client';
import { formatNumber } from '../../../utils/format';
import { consumablesApi } from '../services/consumables.service';
import { useNavigate, useLocation, useSearchParams } from 'react-router-dom';
import { usePermission } from '../../../hooks/usePermission';
import { useIsMobile } from '../../../hooks/useIsMobile';
import ConsumableCheckoutModal from '../components/ConsumableCheckoutModal';
import ConsumableFormModal from '../components/ConsumableFormModal';
import ListRowActions from '../../../shared/components/list/ListRowActions';
import { NowrapText, EllipsisText } from '../../../shared/components/list/ListText';
import { useListColumnsState } from '../../../shared/hooks/useListColumnsState';
import { statusColors, uiColors, cardBadgeGradients } from '../../../theme/designTokens';

const { Text, Title } = Typography;

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

/** The single primary action of a row/card — everything else lives in the ⋯ menu. */
interface RowPrimaryAction {
  key: string;
  label: string;
  icon: React.ReactNode;
  run: () => void;
  disabled?: boolean;
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

// ==================== Helpers ====================

/** Versioned default visibility — bump the storage key when the default set changes. */
const DEFAULT_COLUMN_STATE: Record<string, { show: boolean }> = {
  name: { show: true },
  itemNo: { show: true },
  status: { show: true },
  qty: { show: true },
  remaining: { show: true },
  location: { show: true },
  category: { show: false },
  minAmt: { show: false },
  company: { show: false },
  notes: { show: false },
  actions: { show: true },
};

// ==================== Component ====================

const ConsumableListPage: React.FC = () => {
  const { message, modal } = App.useApp();
  const isMobile = useIsMobile();
  const navigate = useNavigate();
  const location = useLocation();
  const [searchParams, setSearchParams] = useSearchParams();
  const actionRef = useRef<ActionType | undefined>(undefined);
  // Backend serializes enums as strings (JsonStringEnumConverter) — compare the string value only.
  const isConfirmed = (status: string) => status === 'Confirmed';

  const [checkoutOpen, setCheckoutOpen] = useState(false);
  const [checkoutTarget, setCheckoutTarget] = useState<ConsumableDto | null>(null);

  // Form modal (Tạo mới/Sửa) — opened IN PLACE via local state (Task A lesson: never navigate
  // to open a modal). Deep-link routes only set this state on the current page.
  const [formModalOpen, setFormModalOpen] = useState(false);
  const [formModalConsumableId, setFormModalConsumableId] = useState<string | null>(null);

  const columnsState = useListColumnsState('consumable-list-columns-v2', DEFAULT_COLUMN_STATE);

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

  const reload = () => actionRef.current?.reload();

  const handleConfirm = async (record: ConsumableDto) => {
    try {
      await apiClient.put(`/consumables/${record.id}/confirm`);
      void message.success('Đã xác nhận vật tư');
      reload();
    } catch (err: unknown) {
      const e = err as { response?: { data?: { message?: string } } };
      void message.error(e?.response?.data?.message ?? 'Lỗi xác nhận');
      throw new Error('confirm failed');
    }
  };

  const handleDelete = async (record: ConsumableDto) => {
    try {
      await apiClient.delete(`/consumables/${record.id}`);
      void message.success('Đã xóa vật tư');
      reload();
    } catch (err: unknown) {
      const e = err as { response?: { data?: { message?: string } } };
      void message.error(e?.response?.data?.message ?? 'Lỗi xóa');
      throw new Error('delete failed');
    }
  };

  const confirmConsumable = (record: ConsumableDto) => {
    modal.confirm({
      title: 'Xác nhận vật tư này?',
      content: 'Vật tư sẽ chuyển sang trạng thái đã xác nhận.',
      okText: 'Xác nhận',
      cancelText: 'Hủy',
      onOk: () => handleConfirm(record),
    });
  };

  const deleteConsumable = (record: ConsumableDto) => {
    modal.confirm({
      title: 'Xóa vật tư này?',
      content: 'Hành động này không thể hoàn tác.',
      okText: 'Xóa',
      okButtonProps: { danger: true },
      cancelText: 'Hủy',
      onOk: () => handleDelete(record),
    });
  };

  const handleEdit = (record: ConsumableDto) => {
    setFormModalConsumableId(record.id);
    setFormModalOpen(true);
  };

  const openCheckout = (record: ConsumableDto) => {
    setCheckoutTarget(record);
    setCheckoutOpen(true);
  };

  const openView = (record: ConsumableDto) => {
    navigate(`/consumables/${record.id}/view`);
  };

  /** One primary action per lifecycle state; everything else goes into the ⋯ menu. */
  const primaryActionFor = (record: ConsumableDto): RowPrimaryAction | null => {
    if (!isConfirmed(record.status)) {
      if (!canEdit) return null;
      return {
        key: 'confirm',
        label: 'Xác nhận',
        icon: <CheckCircleOutlined />,
        run: () => confirmConsumable(record),
      };
    }
    if (!canCheckout) return null;
    return {
      key: 'checkout',
      label: 'Cấp phát',
      icon: <SendOutlined />,
      run: () => openCheckout(record),
      disabled: record.remaining <= 0,
    };
  };

  const menuItemsFor = (record: ConsumableDto): NonNullable<MenuProps['items']> => {
    const items: NonNullable<MenuProps['items']> = [];
    if (canEdit) {
      items.push({ key: 'edit', label: 'Sửa', icon: <EditOutlined />, onClick: () => handleEdit(record) });
    }
    if (canDelete) {
      items.push({ key: 'delete', label: 'Xóa', icon: <DeleteOutlined />, danger: true, onClick: () => deleteConsumable(record) });
    }
    return items;
  };

  const renderActions = (record: ConsumableDto, stopPropagation = false) => {
    const primary = primaryActionFor(record);
    const menuItems = menuItemsFor(record);

    // ListRowActions has no `disabled` prop — the out-of-stock case renders the very same
    // 104px outlined button (disabled) next to an identical ⋯ menu so rows stay aligned.
    if (primary?.disabled) {
      return (
        <Space size={4} wrap>
          <Button size="small" style={{ width: 104 }} icon={primary.icon} disabled>
            {primary.label}
          </Button>
          {menuItems.length > 0 && (
            <Dropdown menu={{ items: menuItems }} trigger={['click']}>
              <Button
                size="small"
                icon={<MoreOutlined />}
                aria-label="Thao tác khác"
                onClick={(e) => { if (stopPropagation) e.stopPropagation(); }}
              />
            </Dropdown>
          )}
        </Space>
      );
    }

    return (
      <ListRowActions
        primary={primary ? {
          key: primary.key,
          label: primary.label,
          icon: primary.icon,
          onClick: primary.run,
        } : null}
        menuItems={menuItems}
        stopPropagation={stopPropagation}
      />
    );
  };

  const statusTag = (record: ConsumableDto) => (
    <Space size={[0, 4]} wrap>
      <Tag color={isConfirmed(record.status) ? statusColors.active : statusColors.pending} style={{ margin: 0 }}>
        {isConfirmed(record.status) ? 'Đã xác nhận' : 'Chờ xác nhận'}
      </Tag>
      {record.isLowStock && (
        <Tag color="error" icon={<AlertOutlined />} style={{ margin: 0 }}>Tồn kho thấp</Tag>
      )}
    </Space>
  );

  // ──── Search Columns (chỉ dùng cho form tìm kiếm) ────
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

  // ──── Desktop table columns ────
  const desktopColumns = ([
    {
      title: 'Tên',
      dataIndex: 'name',
      key: 'name',
      width: 200,
      // Server-side sort whitelist: name / itemNo / status / createdAt.
      sorter: true,
      render: (_: unknown, record: ConsumableDto) => (
        <Tooltip title={record.name} placement="topLeft">
          <Typography.Link
            onClick={() => openView(record)}
            style={{ display: 'block', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}
          >
            {record.name || '-'}
          </Typography.Link>
        </Tooltip>
      ),
    },
    {
      title: 'Mã vật tư',
      dataIndex: 'itemNo',
      key: 'itemNo',
      width: 140,
      // Natural (numeric-aware) ordering is done in PostgreSQL — no client-side comparator.
      sorter: true,
      render: (_: unknown, record: ConsumableDto) => <NowrapText value={record.itemNo} code />,
    },
    {
      title: 'Trạng thái',
      dataIndex: 'status',
      key: 'status',
      width: 170,
      // Status is a persisted enum column — server-side sortable.
      sorter: true,
      render: (_: unknown, record: ConsumableDto) => statusTag(record),
    },
    {
      title: 'Tổng SL',
      dataIndex: 'qty',
      key: 'qty',
      width: 100,
      align: 'right',
      render: (_: unknown, record: ConsumableDto) => (
        <Text style={{ whiteSpace: 'nowrap' }}>{formatNumber(record.qty)}</Text>
      ),
    },
    {
      title: 'Còn lại',
      dataIndex: 'remaining',
      key: 'remaining',
      width: 120,
      align: 'right',
      render: (_: unknown, record: ConsumableDto) => (
        record.isLowStock ? (
          <Tooltip title={`Ngưỡng tối thiểu: ${record.minAmt}`} placement="topLeft">
            <Text strong type="danger" style={{ whiteSpace: 'nowrap' }}>
              {formatNumber(record.remaining)}
            </Text>
          </Tooltip>
        ) : (
          <Text style={{ whiteSpace: 'nowrap' }}>{formatNumber(record.remaining)}</Text>
        )
      ),
    },
    {
      title: 'Vị trí',
      key: 'location',
      width: 150,
      render: (_: unknown, record: ConsumableDto) => <EllipsisText value={record.location?.name} />,
    },
    {
      title: 'Danh mục',
      key: 'category',
      width: 160,
      render: (_: unknown, record: ConsumableDto) => <EllipsisText value={record.category?.name} />,
    },
    {
      title: 'Ngưỡng tối thiểu',
      dataIndex: 'minAmt',
      key: 'minAmt',
      width: 130,
      align: 'right',
      render: (_: unknown, record: ConsumableDto) => (
        <Text style={{ whiteSpace: 'nowrap' }}>{formatNumber(record.minAmt)}</Text>
      ),
    },
    {
      title: 'Công ty',
      key: 'company',
      width: 170,
      render: (_: unknown, record: ConsumableDto) => <EllipsisText value={record.companyName} />,
    },
    {
      title: 'Ghi chú',
      dataIndex: 'notes',
      key: 'notes',
      width: 240,
      render: (_: unknown, record: ConsumableDto) => <EllipsisText value={record.notes} />,
    },
    {
      title: 'Thao tác',
      key: 'actions',
      width: 150,
      fixed: 'right',
      render: (_: unknown, record: ConsumableDto) => renderActions(record),
    },
  ] as ProColumns<ConsumableDto>[]).map((column) => ({ ...column, hideInSearch: true, search: false }));

  // ──── Render ────

  return (
    <>
      {isMobile ? (
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
            defaultCollapsed: true,
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
              onClick={() => openView(record)}
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
                    <Typography.Link
                      strong
                      style={{ fontSize: 16, lineHeight: 1.4 }}
                      onClick={(e) => { e.stopPropagation(); openView(record); }}
                    >
                      {record.name}
                    </Typography.Link>
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
                  <Text strong style={{ fontSize: 14 }}>{formatNumber(record.qty)}</Text>
                </div>

                <div style={dataRowStyle}>
                  <InboxOutlined style={labelIconStyle} />
                  <Text type="secondary" style={{ fontSize: 12 }}>Còn lại</Text>
                </div>
                <div style={dataRowStyle}>
                  {record.isLowStock ? (
                    <Text strong type="danger" style={{ fontSize: 15, lineHeight: 1 }}>
                      {formatNumber(record.remaining)}
                      <Text type="secondary" style={{ fontSize: 11, marginLeft: 4, fontWeight: 400 }}>
                        (Ngưỡng: {record.minAmt})
                      </Text>
                    </Text>
                  ) : (
                    <Text strong style={{ fontSize: 14 }}>{formatNumber(record.remaining)}</Text>
                  )}
                </div>
              </div>

              {/* ── Divider + Actions ── */}
              <Divider style={{ margin: '12px 0' }} />

              <div style={{ display: 'flex', justifyContent: 'flex-end' }}>
                {renderActions(record, true)}
              </div>
            </Card>
          )}
        />
      ) : (
        <ProTable<ConsumableDto>
          headerTitle={
            <Title level={4} style={{ margin: 0 }}>
              Vật tư tiêu hao
            </Title>
          }
          actionRef={actionRef}
          rowKey="id"
          columns={[...searchColumns, ...desktopColumns]}
          search={{ labelWidth: 'auto', defaultCollapsed: false }}
          options={{ reload: true, density: true, setting: true }}
          columnsState={columnsState}
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
          request={async (params, sort) => {
            try {
              const { current, pageSize, ...rest } = params;
              // Server-side sort (API whitelist: name/itemNo/status/createdAt). ProTable only ever
              // has one active sort key, so the first ascend/descend entry wins.
              const sortEntry = Object.entries(sort ?? {}).find(([, order]) => order === 'ascend' || order === 'descend');
              const sortBy = sortEntry?.[0];
              const sortDir = sortEntry?.[1] === 'descend' ? 'desc' : 'asc';
              const res = await consumablesApi.list({
                ...rest, page: current, pageSize,
                ...(sortBy ? { sortBy, sortDir } : {}),
              });
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
          scroll={{ x: 'max-content' }}
        />
      )}

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
