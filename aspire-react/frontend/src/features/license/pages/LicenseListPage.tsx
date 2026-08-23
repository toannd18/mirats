import { useEffect, useRef, useState } from 'react';
import {
  App, Button, Card, Divider, Empty, Input, Popconfirm, Select, Space, Tag, Typography,
} from 'antd';
import { ProList } from '@ant-design/pro-components';
import type { ActionType } from '@ant-design/pro-components';
import {
  PlusOutlined, SearchOutlined, EditOutlined, DeleteOutlined, EyeOutlined,
  SendOutlined, TeamOutlined, InboxOutlined, CalendarOutlined, EnvironmentOutlined,
  AlertOutlined, SafetyCertificateOutlined, FileTextOutlined,
} from '@ant-design/icons';
import { Link, useLocation, useNavigate } from 'react-router-dom';
import apiClient from '../../../services/api-client';
import { licensesApi, type LicenseListItem } from '../services/licenses.service';
import { usePermission } from '../../../hooks/usePermission';
import { isSuperUser } from '../../../services/keycloak';
import LicenseFormModal from '../components/LicenseFormModal';
import LicenseDetailModal from '../components/LicenseDetailModal';
import LicenseCheckoutModal from '../components/LicenseCheckoutModal';
import { statusColors, uiColors, cardBadgeGradients } from '../../../theme/designTokens';
import { formatDate } from '../../../utils/format';
import CompanyTreeSelect from '../../../components/common/CompanyTreeSelect';

const { Text, Title, Paragraph } = Typography;

// CategoryType.License = 5
const LICENSE_CATEGORY_TYPE = 5;
// Fallback icon color when a License has no category — or its category has no tagColor.
const DEFAULT_CATEGORY_COLOR = '#2f54eb';

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
  const { message } = App.useApp();
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
  const [search, setSearch] = useState('');
  const [categoryId, setCategoryId] = useState<string | undefined>(undefined);
  const [companyId, setCompanyId] = useState<string | undefined>(undefined);
  const [expiringFilter, setExpiringFilter] = useState<string | undefined>(undefined);
  const [lowSeatsFilter, setLowSeatsFilter] = useState<string | undefined>(undefined);
  const [categoryOptions, setCategoryOptions] = useState<{ label: string; value: string }[]>([]);
  // categoryId → tagColor (hex). Built from /categories (which returns TagColor); the license
  // list API only ships category {id, name}, so the color is resolved client-side via this map.
  const [categoryColorById, setCategoryColorById] = useState<Record<string, string>>({});
  // Cấp phát opens in place via local state (bài học Task A — không navigate).
  const [checkoutLicense, setCheckoutLicense] = useState<LicenseListItem | null>(null);

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
  }, [superUser]);

  // Shared fetch path for the Card list — search/category/company/expiring/lowSeats stay
  // state-driven (server-side filters); only the UI layer changed to a Card List.
  const buildParams = (current?: number, pageSize?: number): Record<string, unknown> => {
    const reqParams: Record<string, unknown> = { search, page: current ?? 1, pageSize: pageSize ?? 12 };
    if (categoryId) reqParams.categoryId = categoryId;
    if (companyId) reqParams.companyId = companyId;
    if (expiringFilter) reqParams.expiringSoon = expiringFilter === 'yes';
    if (lowSeatsFilter) reqParams.lowSeats = lowSeatsFilter === 'yes';
    return reqParams;
  };

  const fetchPage = async (current?: number, pageSize?: number) => {
    const res = await licensesApi.list(buildParams(current, pageSize));
    return { data: res.data.data as LicenseListItem[], total: res.data.pagination?.totalItems ?? 0 };
  };

  const reload = () => actionRef.current?.reload();

  const handleDelete = async (id: string) => {
    try {
      await licensesApi.delete(id);
      void message.success('Đã xóa bản quyền');
      actionRef.current?.reload();
    } catch (err: unknown) {
      const e = err as { response?: { data?: { message?: string } } };
      void message.error(e?.response?.data?.message ?? 'Lỗi xóa');
    }
  };

  return (
    <div>
      <Space style={{ marginBottom: 16 }} wrap>
        <Input
          placeholder="Tìm kiếm..."
          prefix={<SearchOutlined />}
          value={search}
          onChange={e => setSearch(e.target.value)}
          onPressEnter={() => reload()}
          style={{ width: 220, maxWidth: '100%' }}
        />
        <Select
          allowClear
          placeholder="Lọc danh mục"
          style={{ minWidth: 170, maxWidth: '100%' }}
          value={categoryId}
          onChange={v => { setCategoryId(v); actionRef.current?.reload(); }}
          options={categoryOptions}
          filterOption={(input, option) => (option?.label as string)?.toLowerCase().includes(input.toLowerCase())}
        />
        <Select
          allowClear
          placeholder="Cảnh báo hết hạn"
          style={{ minWidth: 170, maxWidth: '100%' }}
          value={expiringFilter}
          onChange={v => { setExpiringFilter(v); actionRef.current?.reload(); }}
          options={[{ label: 'Sắp hết hạn / đã hết hạn', value: 'yes' }]}
        />
        <Select
          allowClear
          placeholder="Còn ít chỗ"
          style={{ minWidth: 150, maxWidth: '100%' }}
          value={lowSeatsFilter}
          onChange={v => { setLowSeatsFilter(v); actionRef.current?.reload(); }}
          options={[{ label: 'Còn <= MinSeats', value: 'yes' }]}
        />
        {superUser && (
          <CompanyTreeSelect
            placeholder="Lọc công ty"
            value={companyId}
            onChange={v => { setCompanyId(v); actionRef.current?.reload(); }}
          />
        )}
      </Space>

      <ProList<LicenseListItem>
        headerTitle={
          <Title level={4} style={{ margin: 0 }}>
            Bản quyền
          </Title>
        }
        actionRef={actionRef}
        rowKey="id"
        ghost
        cardProps={false}
        search={false}
        locale={{ emptyText: <Empty description="Không có bản quyền" /> }}
        grid={{ gutter: 16, xs: 1, sm: 1, md: 2, lg: 2, xl: 3, xxl: 3 }}
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
        request={async (params) => {
          try {
            const { current, pageSize } = params;
            const { data, total } = await fetchPage(current, pageSize);
            return { data, total, success: true };
          } catch {
            void message.error('Lỗi tải danh sách bản quyền');
            return { data: [], total: 0, success: false };
          }
        }}
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
                    Còn ít chỗ
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
                  <Text strong style={{ fontSize: 14 }}>{record.seats.toLocaleString('vi-VN')}</Text>
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
                {canCheckout && (
                  <Button
                    size="middle"
                    type="primary"
                    ghost
                    icon={<SendOutlined />}
                    onClick={(e) => { e.stopPropagation(); setCheckoutLicense(record); }}
                    disabled={record.availableSeats <= 0}
                  >
                    Cấp phát
                  </Button>
                )}
                <Button
                  size="middle"
                  icon={<EyeOutlined />}
                  onClick={(e) => { e.stopPropagation(); navigate(`/licenses/${record.id}`); }}
                >
                  Chi tiết
                </Button>
                {canEdit && (
                  <Button
                    size="middle"
                    icon={<EditOutlined />}
                    onClick={(e) => { e.stopPropagation(); navigate(`/licenses/${record.id}/edit`); }}
                  >
                    Sửa
                  </Button>
                )}
                {canDelete && (
                  <Popconfirm
                    title="Xóa bản quyền này?"
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
              </Space>
            </Card>
          );
        }}
      />

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
          actionRef.current?.reload();
        }}
      />

      <LicenseFormModal
        open={formModalOpen}
        licenseId={formModalLicenseId}
        onClose={() => navigate('/licenses')}
        onSaved={() => {
          navigate('/licenses');
          actionRef.current?.reload();
        }}
      />
      <LicenseDetailModal
        open={detailModalOpen}
        licenseId={detailLicenseId}
        onClose={() => navigate('/licenses')}
        onSaved={() => actionRef.current?.reload()}
      />
    </div>
  );
}
