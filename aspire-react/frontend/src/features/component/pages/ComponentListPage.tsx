import { useEffect, useRef, useState } from 'react';
import {
  App, Button, Card, Divider, Empty, Input, Popconfirm, Select, Space, Tag, Tooltip, Typography,
} from 'antd';
import { ProList } from '@ant-design/pro-components';
import type { ActionType } from '@ant-design/pro-components';
import {
  PlusOutlined, SearchOutlined, EditOutlined, DeleteOutlined, EyeOutlined,
  AlertOutlined, EnvironmentOutlined, InboxOutlined, AppstoreOutlined, FileTextOutlined,
} from '@ant-design/icons';
import { useLocation, useNavigate } from 'react-router-dom';
import apiClient from '../../../services/api-client';
import { componentsApi } from '../services/components.service';
import { usePermission } from '../../../hooks/usePermission';
import { uiColors, cardBadgeGradients } from '../../../theme/designTokens';
import { isSuperUser } from '../../../services/keycloak';
import ComponentFormModal from '../components/ComponentFormModal';
import CompanyTreeSelect from '../../../components/common/CompanyTreeSelect';

const { Text, Title, Paragraph } = Typography;

// CategoryType.Component = 4
const COMPONENT_CATEGORY_TYPE = 4;
const UNCATEGORIZED = '__uncategorized__';
const UNCOMPANIED = '__uncompanied__';

interface ListItem {
  id: string;
  name: string;
  serial: string | null;
  qty: number;
  minAmt: number;
  remaining: number;
  isLowStock: boolean;
  trackingType: 'Bulk' | 'Serial';
  canDelete?: boolean;
  notes: string | null;
  category: { id: string; name: string } | null;
  company: { id: string; name: string } | null;
  location: { id: string; name: string } | null;
  supplier: { id: string; name: string } | null;
  manufacturer: { id: string; name: string } | null;
}

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
  const { message } = App.useApp();
  const navigate = useNavigate();
  const location = useLocation();
  // Deep-linkable create/edit modal: /components/new and /components/:id/edit render this list
  // page with the ComponentFormModal open over it (AntD Pro deep-link pattern).
  const isCreateRoute = location.pathname === '/components/new';
  const editMatch = location.pathname.match(/^\/components\/([^/]+)\/edit$/);
  const formModalOpen = isCreateRoute || !!editMatch;
  const formModalComponentId = editMatch ? editMatch[1] : null;
  const actionRef = useRef<ActionType>(null);
  const [search, setSearch] = useState('');
  const [categoryId, setCategoryId] = useState<string | undefined>(undefined);
  const [companyId, setCompanyId] = useState<string | undefined>(undefined);
  const [locationId, setLocationId] = useState<string | undefined>(undefined);
  const [categoryOptions, setCategoryOptions] = useState<{ label: string; value: string }[]>([]);
  const [locationOptions, setLocationOptions] = useState<{ label: string; value: string }[]>([]);
  // ST6b — permission gating (mirrors backend [Authorize(Policy=...)]).
  const canCreate = usePermission('components.create');
  const canEdit = usePermission('components.edit');
  const canDelete = usePermission('components.delete');
  // ST6 (F40) — company filter is only rendered for Superusers (regular users must never see it).
  const superUser = isSuperUser();

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

  // Shared fetch path for the Card list — search/category/company/location stay state-driven
  // (server-side filters); only the UI layer changed to a Card List.
  const buildParams = (current?: number, pageSize?: number): Record<string, unknown> => {
    const reqParams: Record<string, unknown> = { search, page: current ?? 1, pageSize: pageSize ?? 20 };
    if (categoryId === UNCATEGORIZED) reqParams.uncategorized = true;
    else if (categoryId) reqParams.categoryId = categoryId;
    if (companyId === UNCOMPANIED) reqParams.uncompanied = true;
    else if (companyId) reqParams.companyId = companyId;
    if (locationId) reqParams.locationId = locationId;
    return reqParams;
  };

  const fetchPage = async (current?: number, pageSize?: number) => {
    const res = await componentsApi.list(buildParams(current, pageSize));
    return { data: res.data.data as ListItem[], total: res.data.pagination?.totalItems ?? 0 };
  };

  const reload = () => actionRef.current?.reload();

  const handleDelete = async (id: string) => {
    try {
      await componentsApi.delete(id);
      void message.success('Đã xóa linh kiện');
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
          options={[
            ...categoryOptions,
            { label: 'Chưa phân loại', value: UNCATEGORIZED },
          ]}
          filterOption={(input, option) => (option?.label as string)?.toLowerCase().includes(input.toLowerCase())}
        />
        {superUser && (
          <CompanyTreeSelect
            placeholder="Lọc công ty"
            value={companyId}
            onChange={v => { setCompanyId(v); actionRef.current?.reload(); }}
            extraRootOption={{ label: 'Chưa xác định công ty', value: UNCOMPANIED }}
          />
        )}
        <Select
          allowClear
          placeholder="Lọc vị trí"
          style={{ minWidth: 160, maxWidth: '100%' }}
          value={locationId}
          onChange={v => { setLocationId(v); actionRef.current?.reload(); }}
          options={locationOptions}
          filterOption={(input, option) => (option?.label as string)?.toLowerCase().includes(input.toLowerCase())}
        />
      </Space>

      <ProList<ListItem>
        headerTitle={
          <Title level={4} style={{ margin: 0 }}>
            Linh kiện
          </Title>
        }
        actionRef={actionRef}
        rowKey="id"
        ghost
        cardProps={false}
        search={false}
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
          try {
            const { current, pageSize } = params;
            const { data, total } = await fetchPage(current, pageSize);
            return { data, total, success: true };
          } catch {
            void message.error('Lỗi tải danh sách linh kiện');
            return { data: [], total: 0, success: false };
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
            onClick={() => navigate(`/components/${record.id}`)}
            style={{
              borderRadius: 12,
              marginBottom: 16,
              cursor: 'pointer',
              transition: 'all 0.25s cubic-bezier(0.4, 0, 0.2, 1)',
            }}
            styles={{ body: { padding: '20px 20px 16px' } }}
          >
            {/* ── Header: Icon + Name + Serial chip ── */}
            <div style={{ display: 'flex', alignItems: 'center', gap: 12, marginBottom: 12 }}>
              <div style={iconBadgeStyle}>
                <AppstoreOutlined style={{ fontSize: 22, color: '#2f54eb' }} />
              </div>
              <div style={{ flex: 1, minWidth: 0 }}>
                <div style={{ display: 'flex', alignItems: 'baseline', gap: 8, flexWrap: 'wrap' }}>
                  <Text strong style={{ fontSize: 16, lineHeight: 1.4 }}>
                    {record.name}
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

            {/* ── Tags ── */}
            <Space size={[4, 4]} wrap style={{ marginBottom: 12, paddingLeft: 60 }}>
              {record.category?.name && (
                <Tag color="purple" style={{ borderRadius: 4, margin: 0 }}>
                  {record.category.name}
                </Tag>
              )}
              {record.trackingType === 'Serial' ? (
                <Tag color="blue" style={{ borderRadius: 4, margin: 0 }}>Serial</Tag>
              ) : (
                <Tag style={{ borderRadius: 4, margin: 0 }}>Bulk</Tag>
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
                {record.remaining <= record.minAmt ? (
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
              <Button
                size="middle"
                icon={<EyeOutlined />}
                onClick={(e) => { e.stopPropagation(); navigate(`/components/${record.id}`); }}
              >
                Chi tiết
              </Button>
              {canEdit && (
                <Button
                  size="middle"
                  icon={<EditOutlined />}
                  onClick={(e) => { e.stopPropagation(); navigate(`/components/${record.id}/edit`); }}
                >
                  Sửa
                </Button>
              )}
              {canDelete && (
                <Tooltip title={record.canDelete === false ? 'Đã từng được cấp phát — không thể xóa' : 'Xóa linh kiện'}>
                  <Popconfirm
                    title="Xóa linh kiện này?"
                    description="Hành động này không thể hoàn tác."
                    onConfirm={() => handleDelete(record.id)}
                    okText="Xóa"
                    okButtonProps={{ danger: true }}
                    cancelText="Hủy"
                  >
                    <Button
                      size="middle"
                      danger
                      icon={<DeleteOutlined />}
                      disabled={record.canDelete === false}
                      onClick={(e) => e.stopPropagation()}
                    >
                      Xóa
                    </Button>
                  </Popconfirm>
                </Tooltip>
              )}
            </Space>
          </Card>
        )}
      />

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
