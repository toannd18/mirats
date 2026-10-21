import { useEffect, useRef, useState } from 'react';
import {
  App, Button, Card, Divider, Empty, Space, Tag, Tooltip, Typography,
} from 'antd';
import type { MenuProps } from 'antd';
import { ProList, ProTable } from '@ant-design/pro-components';
import type { ActionType, ProColumns } from '@ant-design/pro-components';
import {
  PlusOutlined, EditOutlined, DeleteOutlined, SendOutlined,
  AlertOutlined, EnvironmentOutlined, InboxOutlined, AppstoreOutlined,
} from '@ant-design/icons';
import { useLocation, useNavigate } from 'react-router-dom';
import apiClient from '../../../services/api-client';
import { componentsApi } from '../services/components.service';
import { usePermission } from '../../../hooks/usePermission';
import { useIsMobile } from '../../../hooks/useIsMobile';
import { uiColors, cardBadgeGradients } from '../../../theme/designTokens';
import { formatDateTime, formatNumber } from '../../../utils/format';
import { isSuperUser } from '../../../features/auth/services/auth';
import ComponentFormModal from '../components/ComponentFormModal';
import CompanyTreeSelect from '../../../components/common/CompanyTreeSelect';
import ListRowActions, { type ListPrimaryAction } from '../../../shared/components/list/ListRowActions';
import { EllipsisText, NowrapText } from '../../../shared/components/list/ListText';
import { useListColumnsState } from '../../../shared/hooks/useListColumnsState';

const { Text, Title } = Typography;

// CategoryType.Component = 4
const COMPONENT_CATEGORY_TYPE = 4;
const UNCATEGORIZED = '__uncategorized__';
const UNCOMPANIED = '__uncompanied__';

interface ListItem {
  id: string;
  name: string;
  serial: string | null;
  itemNo?: string | null;
  qty: number;
  minAmt: number;
  remaining: number;
  isLowStock: boolean;
  trackingType: 'Bulk' | 'Serial';
  canDelete?: boolean;
  notes: string | null;
  modelNumber?: string | null;
  createdAt?: string;
  category: { id: string; name: string } | null;
  company: { id: string; name: string } | null;
  location: { id: string; name: string } | null;
  supplier: { id: string; name: string } | null;
  manufacturer: { id: string; name: string } | null;
}

/**
 * Desktop column defaults (UI-1 pattern, shared with AssetListPage/AccessoryListPage).
 * Bump the storage key when the default set changes so saved layouts don't win.
 */
const COMPONENT_COLUMN_DEFAULTS: Record<string, { show: boolean }> = {
  name: { show: true },
  serial: { show: true },
  itemNo: { show: false },
  trackingType: { show: true },
  qty: { show: true },
  remaining: { show: true },
  createdAt: { show: false },
  location: { show: true },
  category: { show: false },
  model: { show: false },
  manufacturer: { show: false },
  supplier: { show: false },
  company: { show: false },
  notes: { show: false },
  actions: { show: true },
};

// ==================== Styles (đồng bộ AccessoryListPage/ConsumableListPage) ====================

const iconBadgeStyle: React.CSSProperties = {
  width: 48,
  height: 48,
  borderRadius: 12,
  background: cardBadgeGradients.blue,
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

export default function ComponentListPage() {
  const { message, modal } = App.useApp();
  const navigate = useNavigate();
  const location = useLocation();
  const isMobile = useIsMobile();
  // Deep-linkable create/edit modal: /components/new and /components/:id/edit render this list
  // page with the ComponentFormModal open over it (AntD Pro deep-link pattern).
  const isCreateRoute = location.pathname === '/components/new';
  const editMatch = location.pathname.match(/^\/components\/([^/]+)\/edit$/);
  const formModalOpen = isCreateRoute || !!editMatch;
  const formModalComponentId = editMatch ? editMatch[1] : null;
  const actionRef = useRef<ActionType>(null);
  const [categoryOptions, setCategoryOptions] = useState<{ label: string; value: string }[]>([]);
  const [locationOptions, setLocationOptions] = useState<{ label: string; value: string }[]>([]);
  // ST6b — permission gating (mirrors backend [Authorize(Policy=...)]).
  const canCreate = usePermission('components.create');
  const canEdit = usePermission('components.edit');
  const canDelete = usePermission('components.delete');
  const canCheckout = usePermission('components.checkout');
  // ST6 (F40) — company filter/column is only rendered for Superusers (regular users must never see it).
  const superUser = isSuperUser();
  const columnsState = useListColumnsState('component-list-columns-v2', COMPONENT_COLUMN_DEFAULTS);

  useEffect(() => {
    apiClient.get('/categories', { params: { type: COMPONENT_CATEGORY_TYPE } })
      .then(r => {
        const list = (r.data?.data ?? []) as { id: string; name: string }[];
        setCategoryOptions(list.map(c => ({ label: c.name, value: c.id })));
      })
      .catch(() => { /* non-critical */ });
    apiClient.get('/locations')
      .then(r => {
        const list = (r.data?.data ?? []) as { id: string; name: string }[];
        setLocationOptions(list.map(l => ({ label: l.name, value: l.id })));
      })
      .catch(() => { /* non-critical */ });
  }, []);

  // Shared fetch path for both responsive layouts — search/category/company/location come from
  // the ProTable/ProList search form and stay server-side filters; sorting is server-side too
  // (the page holds no client-side comparators — ProTable only reports the sort map).
  const buildParams = (
    params: Record<string, unknown>,
    sort?: Record<string, unknown>,
  ): Record<string, unknown> => {
    const reqParams: Record<string, unknown> = {
      page: (params.current as number | undefined) ?? 1,
      pageSize: (params.pageSize as number | undefined) ?? 12,
    };
    const search = typeof params.search === 'string' ? params.search.trim() : '';
    if (search) reqParams.search = search;
    const categoryId = params.categoryId as string | undefined;
    if (categoryId === UNCATEGORIZED) reqParams.uncategorized = true;
    else if (categoryId) reqParams.categoryId = categoryId;
    const companyId = params.companyId as string | undefined;
    if (companyId === UNCOMPANIED) reqParams.uncompanied = true;
    else if (companyId) reqParams.companyId = companyId;
    const locationId = params.locationId as string | undefined;
    if (locationId) reqParams.locationId = locationId;
    // API sort whitelist: name | serial | qty | trackingType | createdAt (default createdAt desc).
    // Columns without a DB-backed source (remaining, category, model, ...) never expose a sorter.
    const sortEntry = Object.entries(sort ?? {})
      .find(([, order]) => order === 'ascend' || order === 'descend');
    if (sortEntry) {
      reqParams.sortBy = sortEntry[0];
      reqParams.sortDir = sortEntry[1] === 'descend' ? 'desc' : 'asc';
    }
    return reqParams;
  };

  const fetchPage = async (
    params: Record<string, unknown>,
    sort?: Record<string, unknown>,
  ) => {
    const res = await componentsApi.list(buildParams(params, sort));
    return { data: (res.data.data ?? []) as ListItem[], total: res.data.pagination?.totalItems ?? 0 };
  };

  const reload = () => actionRef.current?.reload();

  const handleDelete = async (record: ListItem) => {
    try {
      await componentsApi.delete(record.id);
      void message.success('Đã xóa linh kiện');
      reload();
    } catch (err: unknown) {
      const e = err as { response?: { data?: { message?: string } } };
      void message.error(e?.response?.data?.message ?? 'Lỗi xóa');
    }
  };

  const confirmDelete = (record: ListItem) => {
    if (record.canDelete === false) return;
    modal.confirm({
      title: 'Xóa linh kiện này?',
      content: 'Hành động này không thể hoàn tác.',
      okText: 'Xóa',
      cancelText: 'Hủy',
      okButtonProps: { danger: true },
      onOk: () => handleDelete(record),
    });
  };

  /** One primary action per row; everything else lives in the ⋯ menu. */
  const primaryActionFor = (record: ListItem): ListPrimaryAction | null => {
    if (!canCheckout) return null;
    return {
      key: 'checkout',
      label: 'Cấp phát',
      icon: <SendOutlined />,
      // The checkout form lives on the detail page (company-scoped asset picker + serial FIFO).
      onClick: () => navigate(`/components/${record.id}`),
    };
  };

  const secondaryMenuItems = (record: ListItem): MenuProps['items'] => {
    const items: NonNullable<MenuProps['items']> = [];
    if (canEdit) {
      items.push({
        key: 'edit',
        label: 'Sửa',
        icon: <EditOutlined />,
        onClick: () => navigate(`/components/${record.id}/edit`),
      });
    }
    if (canDelete) {
      items.push({
        key: 'delete',
        label: 'Xóa',
        icon: <DeleteOutlined />,
        danger: true,
        disabled: record.canDelete === false,
        onClick: () => confirmDelete(record),
      });
    }
    return items;
  };

  const renderRowActions = (record: ListItem, stopPropagation = false) => {
    const primary = primaryActionFor(record);
    const menuItems = secondaryMenuItems(record);
    // Hết hàng: vẫn giữ đúng vị trí/nhãn của nút chính nhưng vô hiệu hóa (disabled).
    if (primary && record.remaining <= 0) {
      return (
        <Space size={4} wrap>
          <Tooltip title="Đã hết linh kiện — không thể cấp phát">
            <span>
              <Button
                size="small"
                style={{ width: 104 }}
                icon={<SendOutlined />}
                disabled
                onClick={(e) => { if (stopPropagation) e.stopPropagation(); }}
              >
                Cấp phát
              </Button>
            </span>
          </Tooltip>
          <ListRowActions primary={null} menuItems={menuItems} stopPropagation={stopPropagation} />
        </Space>
      );
    }
    return <ListRowActions primary={primary} menuItems={menuItems} stopPropagation={stopPropagation} />;
  };

  // ── Search form (shared by ProTable desktop + ProList mobile; collapsed by default on mobile) ──
  const searchColumns: ProColumns<ListItem>[] = [
    {
      title: 'Tìm kiếm',
      dataIndex: 'search',
      valueType: 'text',
      hideInTable: true,
      fieldProps: { placeholder: 'Tên, serial, model...' },
    },
    {
      title: 'Danh mục',
      dataIndex: 'categoryId',
      valueType: 'select',
      hideInTable: true,
      fieldProps: {
        options: [...categoryOptions, { label: 'Chưa phân loại', value: UNCATEGORIZED }],
        showSearch: true,
        filterOption: (input: string, option?: { label?: string }) =>
          (option?.label ?? '').toLowerCase().includes(input.toLowerCase()),
      },
    },
    ...(superUser ? [{
      title: 'Công ty',
      dataIndex: 'companyId',
      key: 'search-company',
      hideInTable: true,
      renderFormItem: () => (
        <CompanyTreeSelect
          placeholder="Lọc công ty"
          extraRootOption={{ label: 'Chưa xác định công ty', value: UNCOMPANIED }}
        />
      ),
    } as ProColumns<ListItem>] : []),
    {
      title: 'Vị trí',
      dataIndex: 'locationId',
      valueType: 'select',
      hideInTable: true,
      fieldProps: {
        options: locationOptions,
        showSearch: true,
        filterOption: (input: string, option?: { label?: string }) =>
          (option?.label ?? '').toLowerCase().includes(input.toLowerCase()),
      },
    },
  ];

  // ── Desktop columns ──
  const desktopColumns: ProColumns<ListItem>[] = ([
    {
      title: 'Tên',
      dataIndex: 'name',
      key: 'name',
      width: 200,
      sorter: true,
      render: (_: unknown, record: ListItem) => (
        <Tooltip title={record.name} placement="topLeft">
          <Typography.Link
            onClick={() => navigate(`/components/${record.id}`)}
            style={{ display: 'block', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}
          >
            {record.name || '-'}
          </Typography.Link>
        </Tooltip>
      ),
    },
    {
      title: 'Serial',
      dataIndex: 'serial',
      key: 'serial',
      width: 170,
      sorter: true,
      render: (_: unknown, record: ListItem) => <NowrapText value={record.serial} />,
    },
    {
      title: 'Mã vật tư',
      dataIndex: 'itemNo',
      key: 'itemNo',
      width: 140,
      render: (_: unknown, record: ListItem) => <EllipsisText value={record.itemNo} />,
    },
    {
      title: 'Hình thức',
      dataIndex: 'trackingType',
      key: 'trackingType',
      width: 130,
      sorter: true,
      render: (_: unknown, record: ListItem) => (
        record.trackingType === 'Serial' ? 'Theo serial' : 'Theo số lượng'
      ),
    },
    {
      title: 'Tổng SL',
      dataIndex: 'qty',
      key: 'qty',
      width: 100,
      align: 'right',
      sorter: true,
      render: (_: unknown, record: ListItem) => formatNumber(record.qty),
    },
    {
      title: 'Còn lại',
      dataIndex: 'remaining',
      key: 'remaining',
      width: 100,
      align: 'right',
      // No sorter: `remaining` is computed from assignments/units, never a DB column.
      render: (_: unknown, record: ListItem) => (
        <Text strong type={record.remaining <= record.minAmt ? 'danger' : undefined}>
          {formatNumber(record.remaining)}
        </Text>
      ),
    },
    {
      title: 'Ngày tạo',
      dataIndex: 'createdAt',
      key: 'createdAt',
      width: 150,
      sorter: true,
      render: (_: unknown, record: ListItem) => formatDateTime(record.createdAt),
    },
    {
      title: 'Vị trí',
      key: 'location',
      width: 160,
      render: (_: unknown, record: ListItem) => <EllipsisText value={record.location?.name} />,
    },
    {
      title: 'Danh mục',
      key: 'category',
      width: 160,
      render: (_: unknown, record: ListItem) => <EllipsisText value={record.category?.name} />,
    },
    {
      title: 'Model',
      key: 'model',
      width: 150,
      render: (_: unknown, record: ListItem) => <EllipsisText value={record.modelNumber} />,
    },
    {
      title: 'Nhà SX',
      key: 'manufacturer',
      width: 160,
      render: (_: unknown, record: ListItem) => <EllipsisText value={record.manufacturer?.name} />,
    },
    {
      title: 'Nhà cung cấp',
      key: 'supplier',
      width: 170,
      render: (_: unknown, record: ListItem) => <EllipsisText value={record.supplier?.name} />,
    },
    ...(superUser ? [{
      title: 'Công ty',
      key: 'company',
      width: 170,
      render: (_: unknown, record: ListItem) => <EllipsisText value={record.company?.name} />,
    } as ProColumns<ListItem>] : []),
    {
      title: 'Ghi chú',
      key: 'notes',
      width: 220,
      render: (_: unknown, record: ListItem) => <EllipsisText value={record.notes} />,
    },
    {
      title: 'Thao tác',
      key: 'actions',
      width: 150,
      fixed: 'right',
      render: (_: unknown, record: ListItem) => renderRowActions(record),
    },
  ] as ProColumns<ListItem>[]).map((column) => ({ ...column, hideInSearch: true, search: false }));

  const requestList = async (
    params: Record<string, unknown>,
    sort?: Record<string, unknown>,
  ) => {
    try {
      const { data, total } = await fetchPage(params, sort);
      return { data, total, success: true };
    } catch {
      void message.error('Lỗi tải danh sách linh kiện');
      return { data: [] as ListItem[], total: 0, success: false };
    }
  };

  return (
    <div>
      {isMobile ? (
        <ProList<ListItem>
          headerTitle={<Title level={4} style={{ margin: 0 }}>Linh kiện</Title>}
          actionRef={actionRef}
          rowKey="id"
          ghost
          cardProps={false}
          columns={searchColumns}
          search={{ labelWidth: 'auto', defaultCollapsed: true }}
          locale={{ emptyText: <Empty description="Không có linh kiện" /> }}
          grid={{ gutter: 16, xs: 1, sm: 1, md: 2, lg: 2, xl: 3, xxl: 3 }}
          toolBarRender={() => [
            canCreate && (
              <Button
                key="create"
                type="primary"
                size="middle"
                icon={<PlusOutlined />}
                onClick={() => navigate('/components/new')}
              >
                Thêm Linh kiện
              </Button>
            ),
          ]}
          request={async (params) => {
            const { current, pageSize, ...rest } = params as Record<string, unknown>;
            return requestList({ ...rest, current, pageSize });
          }}
          pagination={{
            defaultPageSize: 12,
            showSizeChanger: true,
            showTotal: (total, range) => `${range[0]}-${range[1]} / ${total} mục`,
          }}
          itemRender={(record) => (
            <Card
              hoverable
              onClick={() => navigate(`/components/${record.id}`)}
              style={{
                borderRadius: 12,
                marginBottom: 16,
                cursor: 'pointer',
                transition: 'all 0.25s cubic-bezier(0.4, 0, 0.2, 1)',
              }}
              styles={{ body: { padding: '20px 20px 16px' } }}
            >
              {/* ── Header: Icon + Name link + Serial chip ── */}
              <div style={{ display: 'flex', alignItems: 'center', gap: 12, marginBottom: 12 }}>
                <div style={iconBadgeStyle}>
                  <AppstoreOutlined style={{ fontSize: 22, color: '#2f54eb' }} />
                </div>
                <div style={{ flex: 1, minWidth: 0 }}>
                  <div style={{ display: 'flex', alignItems: 'baseline', gap: 8, flexWrap: 'wrap' }}>
                    <Typography.Link
                      style={{ fontSize: 16, fontWeight: 600, lineHeight: 1.4 }}
                      onClick={(e) => { e.stopPropagation(); navigate(`/components/${record.id}`); }}
                    >
                      {record.name}
                    </Typography.Link>
                    {record.serial && (
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
                        {record.serial}
                      </Text>
                    )}
                  </div>
                </div>
              </div>

              {/* ── Tags ── */}
              <Space size={[4, 4]} wrap style={{ marginBottom: 12, paddingLeft: 60 }}>
                {record.category?.name && (
                  <Tag color="purple" style={{ borderRadius: 4, margin: 0 }}>
                    {record.category.name}
                  </Tag>
                )}
                {record.trackingType === 'Serial' ? (
                  <Tag color="blue" style={{ borderRadius: 4, margin: 0 }}>Theo serial</Tag>
                ) : (
                  <Tag style={{ borderRadius: 4, margin: 0 }}>Theo số lượng</Tag>
                )}
                {record.remaining <= record.minAmt ? (
                  <Tag
                    color="error"
                    icon={<AlertOutlined />}
                    style={{ borderRadius: 4, margin: 0, fontWeight: 500 }}
                  >
                    Sắp hết
                  </Tag>
                ) : (
                  <Tag color="success" style={{ borderRadius: 4, margin: 0 }}>OK</Tag>
                )}
              </Space>

              {/* ── Data Grid (no Ghi chú block on mobile — it lives in the detail page) ── */}
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
                  {record.remaining <= record.minAmt ? (
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

                {superUser && (
                  <>
                    <div style={dataRowStyle}>
                      <EnvironmentOutlined style={labelIconStyle} />
                      <Text type="secondary" style={{ fontSize: 12 }}>Công ty</Text>
                    </div>
                    <div style={dataRowStyle}>
                      {record.company?.name ? (
                        <Text style={{ fontSize: 13, fontWeight: 500 }}>{record.company.name}</Text>
                      ) : (
                        <Text type="secondary" italic style={{ fontSize: 13 }}>Chưa xác định</Text>
                      )}
                    </div>
                  </>
                )}
              </div>

              {/* ── Divider + Actions (ẩn hoàn toàn khi user không có quyền thao tác nào) ── */}
              {(canCheckout || canEdit || canDelete) && (
                <>
                  <Divider style={{ margin: '12px 0' }} />
                  <Space size="small" wrap style={{ justifyContent: 'flex-end', width: '100%' }}>
                    {renderRowActions(record, true)}
                  </Space>
                </>
              )}
            </Card>
          )}
        />
      ) : (
        <ProTable<ListItem>
          headerTitle={<Title level={4} style={{ margin: 0 }}>Linh kiện</Title>}
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
                onClick={() => navigate('/components/new')}
              >
                Thêm Linh kiện
              </Button>
            ),
          ]}
          request={async (params, sort) => {
            const { current, pageSize, ...rest } = params as Record<string, unknown>;
            return requestList(
              { ...rest, current, pageSize },
              sort as Record<string, unknown> | undefined,
            );
          }}
          pagination={{
            defaultPageSize: 12,
            showSizeChanger: true,
            showTotal: (total, range) => `${range[0]}-${range[1]} / ${total} mục`,
          }}
          locale={{ emptyText: <Empty description="Không có linh kiện" /> }}
          scroll={{ x: 'max-content' }}
        />
      )}

      <ComponentFormModal
        open={formModalOpen}
        componentId={formModalComponentId}
        onClose={() => navigate('/components')}
        onSaved={() => {
          navigate('/components');
          reload();
        }}
      />
    </div>
  );
}
