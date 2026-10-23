import { useEffect, useRef, useState } from 'react';
import {
  Button, Tag, Space, InputNumber, App,
  Typography, Divider, Card, Empty, Tooltip,
} from 'antd';
import type { MenuProps } from 'antd';
import {
  PlusOutlined, EditOutlined, DeleteOutlined,
  SendOutlined,
  GiftOutlined, EnvironmentOutlined, InboxOutlined,
  AlertOutlined, RollbackOutlined,
} from '@ant-design/icons';
import { ProList, ProTable } from '@ant-design/pro-components';
import type { ActionType, ProColumns } from '@ant-design/pro-components';
import apiClient from '../../../services/api-client';
import { accessoriesApi, checkoutTypeToLabel } from '../services/accessories.service';
import type { AccessoryDto, AccessoryCheckoutDto, CheckinRequest } from '../services/accessories.service';
import { formatNumber } from '../../../utils/format';
import { useLocation, useNavigate } from 'react-router-dom';
import { usePermission } from '../../../hooks/usePermission';
import { useIsMobile } from '../../../hooks/useIsMobile';
import AccessoryCheckoutModal from '../components/AccessoryCheckoutModal';
import AccessoryFormModal from '../components/AccessoryFormModal';
import { statusColors, uiColors, cardBadgeGradients } from '../../../theme/designTokens';
import ListRowActions from '../../../shared/components/list/ListRowActions';
import ModalFormShell from '../../../shared/components/ModalFormShell';
import type { ListPrimaryAction } from '../../../shared/components/list/ListRowActions';
import { EllipsisText, NowrapText } from '../../../shared/components/list/ListText';
import { useListColumnsState } from '../../../shared/hooks/useListColumnsState';

const { Text, Title } = Typography;

// ==================== Styles ====================

const iconBadgeStyle: React.CSSProperties = {
  width: 48,
  height: 48,
  borderRadius: 12,
  background: cardBadgeGradients.purple,
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

// ==================== Columns ====================

/**
 * `GET /accessories` (ListAccessoriesQuery) ships category/location refs + companyName AND, since
 * ACCESSORY-SORT 2026-10-21, the entity-backed modelNumber/manufacturer/supplier. They are declared
 * optional here only because `AccessoryDto` (accessories.service.ts) is outside this change's file
 * scope — the API always returns them.
 */
type AccessoryRow = AccessoryDto & {
  modelNumber?: string | null;
  manufacturer?: { id: string; name: string } | null;
  supplier?: { id: string; name: string } | null;
};

/** Versioned once, outside the component: a fresh object each render would defeat ProTable persistence.
 *  v2 (ACCESSORY-SORT 2026-10-21): `model` column removed (Accessory has no Model nav — it rendered a
 *  permanent "-"); `modelNumber` added (real column). Bumped so stale v1 saved states don't linger. */
const ACCESSORY_COLUMN_STATE_KEY = 'accessory-list-columns-v2';
const DEFAULT_COLUMN_STATE = {
  name: { show: true },
  itemNo: { show: true },
  checkedOutQty: { show: true },
  remaining: { show: true },
  location: { show: true },
  category: { show: false },
  modelNumber: { show: false },
  manufacturer: { show: false },
  supplier: { show: false },
  company: { show: false },
  notes: { show: false },
};

// ==================== Component ====================

const AccessoryListPage: React.FC = () => {
  const { message, modal } = App.useApp();
  const isMobile = useIsMobile();
  const navigate = useNavigate();
  const location = useLocation();
  const actionRef = useRef<ActionType>(null);
  const columnsState = useListColumnsState(ACCESSORY_COLUMN_STATE_KEY, DEFAULT_COLUMN_STATE);

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
  // (/accessories/:id/view is a real detail page route and must NOT open this modal.)
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

  const confirmDelete = (record: AccessoryDto) => {
    modal.confirm({
      title: 'Xóa phụ kiện này?',
      content: 'Hành động này không thể hoàn tác.',
      okText: 'Xóa',
      cancelText: 'Hủy',
      okButtonProps: { danger: true },
      onOk: () => handleDelete(record.id),
    });
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
      void message.error(`Không thể thu hồi quá số lượng đang cấp phát (${formatNumber(checkout.remainingOut)})`);
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

  // ──── Row actions: ONE primary action + ⋯ menu (UI-1 pattern) ────

  const primaryActionFor = (record: AccessoryDto): ListPrimaryAction | null => {
    if (record.checkedOutQty > 0) {
      if (!canCheckout) return null;
      return {
        key: 'checkin',
        label: 'Thu hồi',
        icon: <RollbackOutlined />,
        onClick: () => { void openCheckin(record); },
      };
    }
    if (!canCheckout) return null;
    return {
      key: 'checkout',
      label: 'Cấp phát',
      icon: <SendOutlined />,
      // ListRowActions has no `disabled` prop (shared, do not fork) → block the click instead of
      // opening the modal for an out-of-stock accessory.
      onClick: () => {
        if (record.remaining <= 0) {
          void message.warning('Phụ kiện đã hết số lượng khả dụng, không thể cấp phát');
          return;
        }
        openCheckout(record);
      },
    };
  };

  const menuItemsFor = (record: AccessoryDto): MenuProps['items'] => {
    const items: NonNullable<MenuProps['items']> = [];
    if (canEdit) {
      items.push({
        key: 'edit',
        label: 'Sửa',
        icon: <EditOutlined />,
        onClick: () => { setFormModalAccessoryId(record.id); setFormModalOpen(true); },
      });
    }
    // The ⋯ menu NEVER repeats the primary action. Primary is "Thu hồi" when checkedOutQty > 0,
    // else "Cấp phát" — so "Cấp phát" may only appear here in that first case, and only when it is
    // genuinely available (stock left). No disabled ghost entries: omit instead.
    if (record.checkedOutQty > 0 && canCheckout && record.remaining > 0) {
      items.push({
        key: 'checkout',
        label: 'Cấp phát',
        icon: <SendOutlined />,
        onClick: () => openCheckout(record),
      });
    }
    if (canDelete) {
      items.push({
        key: 'delete',
        label: 'Xóa',
        icon: <DeleteOutlined />,
        danger: true,
        disabled: record.checkedOutQty > 0,
        onClick: () => confirmDelete(record),
      });
    }
    return items;
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
              Đã cấp: {formatNumber(checkout.assignedQty)}
              {' · '}
              Đã thu: {formatNumber(checkout.returnedQty)}
              {' · '}
              Còn lại: <Text type="danger" strong>{formatNumber(checkout.remainingOut)}</Text>
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

  const searchColumns: ProColumns<AccessoryRow>[] = [
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

  // ──── Desktop columns: Tên, Mã, Đã cấp, Còn lại, Vị trí (optional ones hidden by default) ────
  // Only DB-backed columns are sortable (`sorter: true` → server-side whitelist: name/itemNo).
  // checkedOutQty/remaining are computed SUM() aggregates — never sortable (no client sort either).

  const desktopColumns: ProColumns<AccessoryRow>[] = ([
    {
      title: 'Tên phụ kiện', dataIndex: 'name', width: 200,
      sorter: true,
      render: (_: unknown, record: AccessoryRow) => (
        <Tooltip title={record.name} placement="topLeft">
          <Typography.Link
            onClick={() => navigate(`/accessories/${record.id}/view`)}
            style={{ display: 'block', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}
          >
            {record.name}
          </Typography.Link>
        </Tooltip>
      ),
    },
    {
      title: 'Mã', dataIndex: 'itemNo', width: 120,
      sorter: true,
      render: (_: unknown, record: AccessoryRow) => <NowrapText value={record.itemNo} code />,
    },
    {
      title: 'Đã cấp', dataIndex: 'checkedOutQty', width: 100, align: 'right',
      render: (_: unknown, record: AccessoryRow) => <Text>{formatNumber(record.checkedOutQty)}</Text>,
    },
    {
      title: 'Còn lại', dataIndex: 'remaining', width: 110, align: 'right',
      render: (_: unknown, record: AccessoryRow) => (
        <Text strong type={record.isLowStock ? 'danger' : undefined}>{formatNumber(record.remaining)}</Text>
      ),
    },
    {
      title: 'Vị trí', key: 'location', width: 150,
      render: (_: unknown, record: AccessoryRow) => <EllipsisText value={record.location?.name} />,
    },
    {
      title: 'Danh mục', key: 'category', width: 160,
      render: (_: unknown, record: AccessoryRow) => <EllipsisText value={record.category?.name} />,
    },
    {
      // Accessory has no Model entity — the DB column is the free-text Accessory.ModelNumber.
      title: 'Số model', dataIndex: 'modelNumber', width: 160,
      render: (_: unknown, record: AccessoryRow) => <EllipsisText value={record.modelNumber} />,
    },
    {
      title: 'Nhà sản xuất', key: 'manufacturer', width: 160,
      render: (_: unknown, record: AccessoryRow) => <EllipsisText value={record.manufacturer?.name} />,
    },
    {
      title: 'Nhà cung cấp', key: 'supplier', width: 160,
      render: (_: unknown, record: AccessoryRow) => <EllipsisText value={record.supplier?.name} />,
    },
    {
      title: 'Công ty', key: 'company', width: 160,
      render: (_: unknown, record: AccessoryRow) => <EllipsisText value={record.companyName} />,
    },
    {
      title: 'Ghi chú', key: 'notes', width: 220,
      render: (_: unknown, record: AccessoryRow) => <EllipsisText value={record.notes} />,
    },
    {
      title: 'Thao tác', key: 'actions', width: 150, fixed: 'right',
      render: (_: unknown, record: AccessoryRow) => (
        <ListRowActions primary={primaryActionFor(record)} menuItems={menuItemsFor(record)} />
      ),
    },
  ] as ProColumns<AccessoryRow>[]).map((column) => ({ ...column, hideInSearch: true, search: false }));

  // ──── Render ────

  return (
    <>
      {isMobile ? (
        <ProList<AccessoryRow>
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
            defaultCollapsed: true,
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
          locale={{ emptyText: <Empty description="Không tìm thấy phụ kiện phù hợp" /> }}
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
                    <Typography.Link
                      style={{ fontSize: 16, lineHeight: 1.4, fontWeight: 600 }}
                      onClick={(e) => { e.stopPropagation(); navigate(`/accessories/${record.id}/view`); }}
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

              {/* Divider + Actions */}
              <Divider style={{ margin: '12px 0' }} />

              <div style={{ display: 'flex', justifyContent: 'flex-end' }}>
                <ListRowActions
                  primary={primaryActionFor(record)}
                  menuItems={menuItemsFor(record)}
                  stopPropagation
                />
              </div>
            </Card>
          )}
        />
      ) : (
        <ProTable<AccessoryRow>
          headerTitle={
            <Title level={4} style={{ margin: 0 }}>
              Phụ kiện
            </Title>
          }
          actionRef={actionRef}
          rowKey="id"
          columns={[...searchColumns, ...desktopColumns]}
          search={{
            labelWidth: 'auto',
            defaultCollapsed: false,
          }}
          options={{ reload: true, density: true, setting: true }}
          columnsState={columnsState}
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
          request={async (params, sort) => {
            try {
              const { current, pageSize, ...rest } = params;
              // [ACCESSORY-SORT 2026-10-21] Server-side sort only — ProTable holds no client sorter,
              // it just forwards the single active column. Server whitelists name/itemNo.
              const sortEntry = Object.entries(sort ?? {}).find(([, order]) => order === 'ascend' || order === 'descend');
              const sortBy = sortEntry?.[0];
              const sortDir = sortEntry?.[1] === 'descend' ? 'desc' : 'asc';
              const res = await accessoriesApi.list({
                ...rest, page: current, pageSize,
                ...(sortBy ? { sortBy, sortDir } : {}),
              });
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
          locale={{ emptyText: <Empty description="Không tìm thấy phụ kiện phù hợp" /> }}
          scroll={{ x: 'max-content' }}
        />
      )}

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
      <ModalFormShell
        title={
          <Space>
            <RollbackOutlined />
            <span>Thu hồi phụ kiện — {checkinAccessoryName}</span>
          </Space>
        }
        open={checkinModalOpen}
        onCancel={() => { setCheckinModalOpen(false); setCheckinCheckouts([]); }}
        footer={null}
        size="md"
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
      </ModalFormShell>

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
