import { useEffect, useRef, useState } from 'react';
import {
  App, Button, Card, Empty, Space, Tag, Tooltip, Typography,
} from 'antd';
import type { MenuProps } from 'antd';
import {
  PlusOutlined, EditOutlined, DeleteOutlined, SendOutlined,
  TeamOutlined, InboxOutlined, CalendarOutlined, EnvironmentOutlined,
  AlertOutlined, SafetyCertificateOutlined,
} from '@ant-design/icons';
import { ProList, ProTable } from '@ant-design/pro-components';
import type { ActionType, ProColumns } from '@ant-design/pro-components';
import { Link, useLocation, useNavigate } from 'react-router-dom';
import apiClient from '../../../services/api-client';
import { licensesApi, type LicenseListItem } from '../services/licenses.service';
import { usePermission } from '../../../hooks/usePermission';
import { useIsMobile } from '../../../hooks/useIsMobile';
import { isSuperUser } from '../../../features/auth/services/auth';
import { formatDate, formatNumber } from '../../../utils/format';
import LicenseFormModal from '../components/LicenseFormModal';
import LicenseDetailModal from '../components/LicenseDetailModal';
import LicenseCheckoutModal from '../components/LicenseCheckoutModal';
import ListRowActions from '../../../shared/components/list/ListRowActions';
import { EllipsisText, NowrapText } from '../../../shared/components/list/ListText';
import { useListColumnsState } from '../../../shared/hooks/useListColumnsState';
import { statusColors, uiColors, cardBadgeGradients } from '../../../theme/designTokens';

const { Text, Title } = Typography;

// CategoryType.License = 5
const LICENSE_CATEGORY_TYPE = 5;
// Fallback icon color when a License has no category — or its category has no tagColor.
const DEFAULT_CATEGORY_COLOR = '#2f54eb';

// Versioned ProTable column state — bump the key whenever the DEFAULT column set changes.
const COLUMN_STATE_KEY = 'license-list-columns-v2';
// Columns hidden by default; when any of them is enabled the actions column stops being
// `fixed: 'right'` (otherwise it overlaps the extra content on narrower screens).
const OPTIONAL_COLUMN_KEYS = [
  'category', 'manufacturer', 'supplier', 'company', 'purchaseDate', 'reassignable', 'notes',
] as const;

const DEFAULT_COLUMN_STATE: Record<string, { show: boolean }> = {
  name: { show: true },
  serial: { show: true },
  seats: { show: true },
  assignedSeats: { show: true },
  availableSeats: { show: true },
  expirationDate: { show: true },
  category: { show: false },
  manufacturer: { show: false },
  supplier: { show: false },
  company: { show: false },
  purchaseDate: { show: false },
  reassignable: { show: false },
  notes: { show: false },
  actions: { show: true },
};

function readOptionalColumnsVisible(): boolean {
  try {
    const raw = localStorage.getItem(COLUMN_STATE_KEY);
    if (!raw) return false;
    const state = JSON.parse(raw) as Record<string, { show?: boolean }>;
    return OPTIONAL_COLUMN_KEYS.some((key) => state[key]?.show === true);
  } catch {
    return false;
  }
}

/**
 * `LicenseListItemDto` ships PurchaseDate; the intersection keeps the row type tolerant if an
 * older API build omits it (the column then renders "-" instead of breaking the build).
 */
type LicenseRow = LicenseListItem & { purchaseDate?: string | null };

interface CompanyNodeLite { id: string; name: string; children?: CompanyNodeLite[] }

function ExpiryCell({ row }: { row: LicenseListItem }) {
  if (row.isExpired) {
    return <Tag color="red">Hết hạn {formatDate(row.expirationDate)}</Tag>;
  }
  if (row.expiringSoon) {
    return <Tag color="orange">Sắp hết hạn {formatDate(row.expirationDate)}</Tag>;
  }
  return <span>{formatDate(row.expirationDate)}</span>;
}

// ==================== Styles (đồng bộ AccessoryListPage/ComponentListPage) ====================

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

// ─── Category-color helpers: icon/badge color comes DYNAMICALLY from the License's
// category tagColor (hex from the backend). Fallback = default gradient + default icon color. ───

function hexToRgba(hex: string, alpha: number): string {
  const clean = hex.trim().replace(/^#/, '');
  const full = clean.length === 3
    ? clean.split('').map((c) => c + c).join('')
    : clean;
  // Defensive: non-hex/empty input must not produce NaN — fall back to the default blue.
  if (full.length !== 6 || !/^[0-9a-fA-F]{6}$/.test(full)) {
    return `rgba(47, 84, 235, ${alpha})`;
  }
  const num = parseInt(full, 16);
  const r = (num >> 16) & 255;
  const g = (num >> 8) & 255;
  const b = num & 255;
  return `rgba(${r}, ${g}, ${b}, ${alpha})`;
}

function badgeBackground(color?: string): string {
  if (!color) return iconBadgeStyle.background as string;
  return `linear-gradient(135deg, ${hexToRgba(color, 0.16)} 0%, ${hexToRgba(color, 0.32)} 100%)`;
}

// ==================== Component ====================

export default function LicenseListPage() {
  const { message, modal } = App.useApp();
  const isMobile = useIsMobile();
  const navigate = useNavigate();
  const location = useLocation();
  // Deep-linkable create/edit/detail: /licenses/new, /licenses/:id/edit, /licenses/:id render this
  // list page with the corresponding modal open over it (same pattern as ComponentListPage).
  const isCreateRoute = location.pathname === '/licenses/new';
  const editMatch = location.pathname.match(/^\/licenses\/([^/]+)\/edit$/);
  const formModalOpen = isCreateRoute || !!editMatch;
  const formModalLicenseId = editMatch ? editMatch[1] : null;
  const detailMatch = location.pathname.match(/^\/licenses\/([^/]+)$/);
  const detailLicenseId = detailMatch && detailMatch[1] !== 'new' ? detailMatch[1] : null;
  const detailModalOpen = !!detailLicenseId && detailLicenseId !== 'new' && !formModalOpen;

  const actionRef = useRef<ActionType>(null);
  const [categoryOptions, setCategoryOptions] = useState<{ label: string; value: string }[]>([]);
  // categoryId → tagColor (hex). Built from /categories (which returns TagColor); the license
  // list API only ships category {id, name}, so the color is resolved client-side via this map.
  const [categoryColorById, setCategoryColorById] = useState<Record<string, string>>({});
  // Cấp phát opens in place via local state (bài học Task A — không navigate).
  const [checkoutLicense, setCheckoutLicense] = useState<LicenseListItem | null>(null);

  // Approved UI-1 pattern: the primary action stays `fixed: 'right'` only while every
  // optional (wide) column is hidden.
  const [optionalColumnsVisible, setOptionalColumnsVisible] = useState(readOptionalColumnsVisible);

  // ST6b — permission gating (mirrors backend [Authorize(Policy=...)]).
  const canCreate = usePermission('licenses.create');
  const canEdit = usePermission('licenses.edit');
  const canDelete = usePermission('licenses.delete');
  const canCheckout = usePermission('licenses.checkout');
  // ST6 (F40) — company column + filter only for Superusers (regular users must never see them).
  const superUser = isSuperUser();

  useEffect(() => {
    apiClient.get('/categories', { params: { type: LICENSE_CATEGORY_TYPE } })
      .then(r => {
        const list = (r.data?.data ?? []) as { id: string; name: string; tagColor?: string | null }[];
        setCategoryOptions(list.map(c => ({ label: c.name, value: c.id })));
        const map: Record<string, string> = {};
        list.forEach(c => { if (c.tagColor) map[c.id] = c.tagColor; });
        setCategoryColorById(map);
      })
      .catch(() => { /* non-critical */ });
  }, []);

  // ──── Data loading: search/category/company/expiring/lowSeats/sort are ALL server-side.
  // Sorting is never done in the client (no sorter functions) — the whitelist lives in the API
  // (`licenseListSort`: name, serial, seats, expirationdate, createdat). ────

  const fetchPage = async (params: Record<string, unknown>, sort?: Record<string, unknown>) => {
    const { current, pageSize, expiringSoon, lowSeats, ...rest } = params;
    const reqParams: Record<string, unknown> = {
      ...rest,
      page: current ?? 1,
      pageSize: pageSize ?? 12,
    };
    // ProTable's sort object → API `sortBy`/`sortDir` (only for whitelisted, DB-backed columns).
    const sortEntry = Object.entries(sort ?? {}).find(([, order]) => order === 'ascend' || order === 'descend');
    const sortBy = sortEntry?.[0];
    if (sortBy) {
      reqParams.sortBy = sortBy;
      reqParams.sortDir = sortEntry?.[1] === 'descend' ? 'desc' : 'asc';
    }
    // The search form uses 'yes' sentinel values — the API expects booleans.
    if (expiringSoon === 'yes') reqParams.expiringSoon = true;
    if (lowSeats === 'yes') reqParams.lowSeats = true;
    // Drop empty filter values so a cleared field never becomes `search=`/`categoryId=`.
    Object.keys(reqParams).forEach((key) => {
      const value = reqParams[key];
      if (value === undefined || value === null || value === '') delete reqParams[key];
    });

    const res = await licensesApi.list(reqParams);
    return {
      data: (res.data.data ?? []) as LicenseRow[],
      total: res.data.pagination?.totalItems ?? 0,
    };
  };

  const handleRequest = async (params: Record<string, unknown>, sort?: Record<string, unknown>) => {
    try {
      const { data, total } = await fetchPage(params, sort);
      return { data, total, success: true };
    } catch {
      void message.error('Lỗi tải danh sách bản quyền');
      return { data: [] as LicenseRow[], total: 0, success: false };
    }
  };

  const reload = () => actionRef.current?.reload();

  // ──── Row actions: one outlined/default fixed-width primary + ⋯ menu ────

  const confirmDelete = (record: LicenseRow) => {
    modal.confirm({
      title: 'Xóa bản quyền này?',
      content: 'Hành động này không thể hoàn tác.',
      okText: 'Xóa',
      cancelText: 'Hủy',
      okButtonProps: { danger: true },
      onOk: async () => {
        try {
          await licensesApi.delete(record.id);
          void message.success('Đã xóa bản quyền');
          reload();
        } catch (err: unknown) {
          const e = err as { response?: { data?: { message?: string } } };
          void message.error(e?.response?.data?.message ?? 'Lỗi xóa');
          throw new Error('delete failed');
        }
      },
    });
  };

  const rowMenuItems = (record: LicenseRow): MenuProps['items'] => {
    const items: NonNullable<MenuProps['items']> = [];
    if (canEdit) {
      items.push({
        key: 'edit',
        label: 'Sửa',
        icon: <EditOutlined />,
        onClick: () => navigate(`/licenses/${record.id}/edit`),
      });
    }
    if (canDelete) {
      items.push({
        key: 'delete',
        label: 'Xóa',
        icon: <DeleteOutlined />,
        danger: true,
        onClick: () => confirmDelete(record),
      });
    }
    return items;
  };

  const renderRowActions = (record: LicenseRow, stopPropagation = false) => {
    const menuItems = rowMenuItems(record);
    const primary = canCheckout ? {
      key: 'checkout',
      label: 'Cấp phát',
      icon: <SendOutlined />,
      onClick: () => setCheckoutLicense(record),
    } : null;

    // No free seat ⇒ keep the primary visible but inert (a license row has no meaningful
    // "Thu hồi" — recall happens per seat in the detail screen). ListRowActions has no
    // `disabled` flag yet, so the disabled button is rendered locally with the same
    // fixed-width outlined style, while the ⋯ menu still comes from the shared component.
    if (primary && record.availableSeats <= 0) {
      return (
        <div style={{ display: 'flex', gap: 4, flexWrap: 'wrap', alignItems: 'center' }}>
          <Button size="small" style={{ width: 104 }} icon={<SendOutlined />} disabled>
            Cấp phát
          </Button>
          {menuItems && menuItems.length > 0 && (
            <ListRowActions menuItems={menuItems} stopPropagation={stopPropagation} />
          )}
        </div>
      );
    }

    return <ListRowActions primary={primary} menuItems={menuItems} stopPropagation={stopPropagation} />;
  };

  // ──── Search form (desktop expanded, mobile collapsed) ────

  const searchColumns: ProColumns<LicenseRow>[] = [
    {
      title: 'Tìm kiếm',
      dataIndex: 'search',
      valueType: 'text',
      hideInTable: true,
      fieldProps: { placeholder: 'Tên, số serial...' },
    },
    {
      title: 'Danh mục',
      dataIndex: 'categoryId',
      valueType: 'select',
      hideInTable: true,
      fieldProps: {
        options: categoryOptions,
        showSearch: true,
        allowClear: true,
        filterOption: (input: string, option?: { label?: string }) =>
          (option?.label ?? '').toLowerCase().includes(input.toLowerCase()),
      },
    },
    {
      title: 'Cảnh báo hết hạn',
      dataIndex: 'expiringSoon',
      valueType: 'select',
      hideInTable: true,
      fieldProps: { options: [{ label: 'Sắp hết hạn / đã hết hạn', value: 'yes' }], allowClear: true },
    },
    {
      title: 'Còn ít ghế',
      dataIndex: 'lowSeats',
      valueType: 'select',
      hideInTable: true,
      fieldProps: { options: [{ label: 'Còn ≤ ngưỡng', value: 'yes' }], allowClear: true },
    },
    ...(superUser ? [{
      title: 'Công ty',
      dataIndex: 'companyId',
      valueType: 'select',
      hideInTable: true,
      fieldProps: { showSearch: true, allowClear: true },
      request: async () => {
        const res = await apiClient.get('/companies');
        const flat: { label: string; value: string }[] = [];
        const walk = (nodes: CompanyNodeLite[], depth: number) => {
          nodes.forEach((node) => {
            flat.push({ label: `${'— '.repeat(depth)}${node.name}`, value: node.id });
            if (node.children?.length) walk(node.children, depth + 1);
          });
        };
        walk((res.data?.data ?? []) as CompanyNodeLite[], 0);
        return flat;
      },
    } as ProColumns<LicenseRow>] : []),
  ];

  // ──── Desktop columns — defaults: Tên, Số serial, Tổng ghế, Đã cấp, Còn lại, Hết hạn ────

  const columns: ProColumns<LicenseRow>[] = ([
    {
      title: 'Tên',
      dataIndex: 'name',
      key: 'name',
      width: 200,
      sorter: true,
      render: (_, record) => {
        const text = record.name || '-';
        return (
          <Tooltip title={text} placement="topLeft">
            <Typography.Link
              onClick={() => navigate(`/licenses/${record.id}`)}
              style={{ display: 'block', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}
            >
              {text}
            </Typography.Link>
          </Tooltip>
        );
      },
    },
    {
      title: 'Số serial',
      dataIndex: 'serial',
      key: 'serial',
      width: 170,
      sorter: true,
      render: (_, record) => <NowrapText code value={record.serial} />,
    },
    {
      title: 'Tổng ghế',
      dataIndex: 'seats',
      key: 'seats',
      width: 90,
      align: 'right',
      sorter: true,
      render: (_, record) => <Text style={{ whiteSpace: 'nowrap' }}>{formatNumber(record.seats)}</Text>,
    },
    {
      title: 'Đã cấp',
      dataIndex: 'assignedSeats',
      key: 'assignedSeats',
      width: 90,
      align: 'right',
      render: (_, record) => <Text style={{ whiteSpace: 'nowrap' }}>{formatNumber(record.assignedSeats)}</Text>,
    },
    {
      title: 'Còn lại',
      dataIndex: 'availableSeats',
      key: 'availableSeats',
      width: 100,
      align: 'right',
      render: (_, record) => (
        <Text
          strong
          style={{
            whiteSpace: 'nowrap',
            color: record.availableSeats > 0 ? statusColors.ready : statusColors.overdue,
          }}
        >
          {formatNumber(record.availableSeats)}
        </Text>
      ),
    },
    {
      title: 'Hết hạn',
      dataIndex: 'expirationDate',
      key: 'expirationDate',
      width: 170,
      sorter: true,
      render: (_, record) => <ExpiryCell row={record} />,
    },
    {
      title: 'Danh mục',
      key: 'category',
      width: 150,
      render: (_, record) => <EllipsisText value={record.category?.name} />,
    },
    {
      title: 'Nhà SX',
      key: 'manufacturer',
      width: 150,
      render: (_, record) => <EllipsisText value={record.manufacturer?.name} />,
    },
    {
      title: 'Nhà cung cấp',
      key: 'supplier',
      width: 150,
      render: (_, record) => <EllipsisText value={record.supplier?.name} />,
    },
    ...(superUser ? [{
      title: 'Công ty',
      key: 'company',
      width: 160,
      render: (_: unknown, record: LicenseRow) => <EllipsisText value={record.company?.name} />,
    } as ProColumns<LicenseRow>] : []),
    {
      title: 'Ngày mua',
      key: 'purchaseDate',
      width: 120,
      render: (_, record) => formatDate(record.purchaseDate),
    },
    {
      title: 'Có thể cấp lại',
      dataIndex: 'reassignable',
      key: 'reassignable',
      width: 130,
      render: (_, record) => (
        <Tag color={record.reassignable ? 'green' : 'default'}>{record.reassignable ? 'Có' : 'Không'}</Tag>
      ),
    },
    {
      title: 'Ghi chú',
      dataIndex: 'notes',
      key: 'notes',
      width: 220,
      render: (_, record) => <EllipsisText value={record.notes} />,
    },
    {
      title: 'Thao tác',
      key: 'actions',
      width: 150,
      fixed: optionalColumnsVisible ? undefined : 'right',
      render: (_, record) => renderRowActions(record),
    },
  ] as ProColumns<LicenseRow>[]).map((column) => ({ ...column, search: false, hideInSearch: true }));

  const columnsState = useListColumnsState(COLUMN_STATE_KEY, DEFAULT_COLUMN_STATE);

  return (
    <>
      {isMobile ? (
        <ProList<LicenseRow>
          headerTitle={<Title level={4} style={{ margin: 0 }}>Bản quyền</Title>}
          actionRef={actionRef}
          rowKey="id"
          ghost
          cardProps={false}
          columns={searchColumns}
          grid={{ gutter: 16, xs: 1, sm: 1, md: 2, lg: 2, xl: 3, xxl: 3 }}
          search={{ labelWidth: 'auto', defaultCollapsed: true }}
          locale={{ emptyText: <Empty description="Không có bản quyền" /> }}
          toolBarRender={() => [
            canCreate && (
              <Button
                key="create"
                type="primary"
                size="middle"
                icon={<PlusOutlined />}
                onClick={() => navigate('/licenses/new')}
              >
                Tạo bản quyền
              </Button>
            ),
          ]}
          request={handleRequest}
          pagination={{
            defaultPageSize: 12,
            showSizeChanger: true,
            showTotal: (total, range) => `${range[0]}-${range[1]} / ${total} mục`,
          }}
          itemRender={(record) => {
            // Icon/badge color comes DIRECTLY from this License's category tagColor (dynamic —
            // NOT a hardcoded/fixed palette). Fallback when no category / empty color.
            const categoryColor = record.category?.id ? categoryColorById[record.category.id] : undefined;
            const hasColor = !!categoryColor;
            return (
              <Card
                hoverable
                onClick={() => navigate(`/licenses/${record.id}`)}
                style={{
                  borderRadius: 12,
                  marginBottom: 16,
                  cursor: 'pointer',
                  transition: 'all 0.25s cubic-bezier(0.4, 0, 0.2, 1)',
                }}
                styles={{ body: { padding: '20px 20px 16px' } }}
              >
                {/* ── Header: icon (category color) + name + serial chip ── */}
                <div style={{ display: 'flex', alignItems: 'center', gap: 12, marginBottom: 12 }}>
                  <div style={{ ...iconBadgeStyle, background: badgeBackground(categoryColor) }}>
                    <SafetyCertificateOutlined
                      style={{ fontSize: 22, color: categoryColor || DEFAULT_CATEGORY_COLOR }}
                    />
                  </div>
                  <div style={{ flex: 1, minWidth: 0 }}>
                    <div style={{ display: 'flex', alignItems: 'baseline', gap: 8, flexWrap: 'wrap' }}>
                      <Text strong style={{ fontSize: 16, lineHeight: 1.4 }}>
                        <Link to={`/licenses/${record.id}`}>{record.name}</Link>
                      </Text>
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

                {/* ── Tags (category tag also uses the dynamic category color) ── */}
                <Space size={[4, 4]} wrap style={{ marginBottom: 12, paddingLeft: 60 }}>
                  {record.category?.name && (
                    <Tag color={hasColor ? categoryColor : 'default'} style={{ borderRadius: 4, margin: 0 }}>
                      {record.category.name}
                    </Tag>
                  )}
                  {record.isExpired ? (
                    <Tag color="red" style={{ borderRadius: 4, margin: 0 }}>
                      Hết hạn {formatDate(record.expirationDate)}
                    </Tag>
                  ) : record.expiringSoon ? (
                    <Tag color="orange" style={{ borderRadius: 4, margin: 0 }}>
                      Sắp hết hạn {formatDate(record.expirationDate)}
                    </Tag>
                  ) : null}
                  {record.isLowSeats && (
                    <Tag
                      color="error"
                      icon={<AlertOutlined />}
                      style={{ borderRadius: 4, margin: 0, fontWeight: 500 }}
                    >
                      Còn ít ghế
                    </Tag>
                  )}
                </Space>

                {/* ── Data Grid ── */}
                <div style={dataGridStyle}>
                  <div style={dataRowStyle}>
                    <TeamOutlined style={labelIconStyle} />
                    <Text type="secondary" style={{ fontSize: 12 }}>Tổng ghế</Text>
                  </div>
                  <div style={dataRowStyle}>
                    <Text strong style={{ fontSize: 14 }}>{formatNumber(record.seats)}</Text>
                  </div>

                  <div style={dataRowStyle}>
                    <InboxOutlined style={labelIconStyle} />
                    <Text type="secondary" style={{ fontSize: 12 }}>Còn trống</Text>
                  </div>
                  <div style={dataRowStyle}>
                    {record.isLowSeats ? (
                      <Text strong type="danger" style={{ fontSize: 15, lineHeight: 1 }}>
                        {record.availableSeats}
                        <Text type="secondary" style={{ fontSize: 11, marginLeft: 4, fontWeight: 400 }}>
                          (ít)
                        </Text>
                      </Text>
                    ) : (
                      <Text strong style={{ fontSize: 14, color: record.availableSeats > 0 ? statusColors.ready : statusColors.overdue }}>
                        {record.availableSeats}
                      </Text>
                    )}
                  </div>

                  <div style={dataRowStyle}>
                    <CalendarOutlined style={labelIconStyle} />
                    <Text type="secondary" style={{ fontSize: 12 }}>Ngày hết hạn</Text>
                  </div>
                  <div style={dataRowStyle}>
                    <ExpiryCell row={record} />
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

                {/* ── Actions (approved UI-1 cell: primary + ⋯ menu) ── */}
                <div style={{ display: 'flex', justifyContent: 'flex-end', marginTop: 12 }}>
                  {renderRowActions(record, true)}
                </div>
              </Card>
            );
          }}
        />
      ) : (
        <ProTable<LicenseRow>
          headerTitle={<Title level={4} style={{ margin: 0 }}>Bản quyền</Title>}
          actionRef={actionRef}
          rowKey="id"
          columns={[...searchColumns, ...columns]}
          search={{ labelWidth: 'auto', defaultCollapsed: false }}
          options={{ reload: true, density: true, setting: true }}
          columnsState={{
            ...columnsState,
            onChange: (map) => {
              const wide = OPTIONAL_COLUMN_KEYS.some((key) => map[key]?.show === true);
              setTimeout(() => setOptionalColumnsVisible(wide), 0);
            },
          }}
          toolBarRender={() => [
            canCreate && (
              <Button
                key="create"
                type="primary"
                size="middle"
                icon={<PlusOutlined />}
                onClick={() => navigate('/licenses/new')}
              >
                Tạo bản quyền
              </Button>
            ),
          ]}
          request={handleRequest}
          pagination={{
            defaultPageSize: 12,
            showSizeChanger: true,
            showTotal: (total, range) => `${range[0]}-${range[1]} / ${total} mục`,
          }}
          locale={{ emptyText: <Empty description="Không có bản quyền" /> }}
          scroll={{ x: 'max-content' }}
        />
      )}

      {/* Cấp phát — mở tại chỗ (state cục bộ, không navigate); Task E: chọn thẳng SystemInfo. */}
      <LicenseCheckoutModal
        open={!!checkoutLicense}
        licenseId={checkoutLicense?.id ?? ''}
        licenseName={checkoutLicense?.name ?? ''}
        seatId={null}
        seatNumber={null}
        companyId={checkoutLicense?.company?.id ?? null}
        onClose={() => setCheckoutLicense(null)}
        onSaved={() => {
          setCheckoutLicense(null);
          reload();
        }}
      />

      <LicenseFormModal
        open={formModalOpen}
        licenseId={formModalLicenseId}
        onClose={() => navigate('/licenses')}
        onSaved={() => {
          navigate('/licenses');
          reload();
        }}
      />
      <LicenseDetailModal
        open={detailModalOpen}
        licenseId={detailLicenseId}
        onClose={() => navigate('/licenses')}
        onSaved={() => reload()}
      />
    </>
  );
}
