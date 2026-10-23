import { useRef, useState, useEffect } from 'react';
import {
  Tag, App, Button, Space, Card, Typography, Empty,
  Alert, Descriptions, Form, Input, InputNumber, Select, DatePicker, Row, Col, Dropdown, Tooltip,
} from 'antd';
import type { MenuProps } from 'antd';
import {
  PlusOutlined, EditOutlined, SendOutlined,
  LaptopOutlined,
  RollbackOutlined, DeleteOutlined, CheckSquareOutlined, UndoOutlined, InboxOutlined, MoreOutlined,
  PrinterOutlined,
} from '@ant-design/icons';
import { ProList, ProTable } from '@ant-design/pro-components';
import type { ActionType, ProColumns } from '@ant-design/pro-components';
import { useNavigate, useLocation } from 'react-router-dom';
import dayjs from 'dayjs';
import apiClient from '../../../services/api-client';
import { assetService, type CreateAssetPayload } from '../services/asset.service';
import { usePermission } from '../../../hooks/usePermission';
import { useIsMobile } from '../../../hooks/useIsMobile';
import { isSuperUser } from '../../../features/auth/services/auth';
import AssetArchiveModal from '../components/AssetArchiveModal';
import AssetAllocationModal from '../components/AssetAllocationModal';
import AssetRecallModal from '../components/AssetRecallModal';
import AssetEditModal from '../components/AssetEditModal';
import CompanyTreeSelect from '../../../components/common/CompanyTreeSelect';
import {
  getAssetActions, ASSET_STATUS_LABELS, ASSET_STATUS_COLORS, normalizeAssetStatus,
  ALLOCATION_TARGET_LABELS,
  type AssetDto, type AllocationTargetType,
} from '../types/asset';
import { statusColors } from '../../../theme/designTokens';
import { formatDate, formatMoney } from '../../../utils/format';
import ModalFormShell from '../../../shared/components/ModalFormShell';
import { serverErrorMessage } from '../../../shared/utils/formErrors';

const { Text, Title } = Typography;

// ==================== AssetListPage ====================

const AssetListPage: React.FC = () => {
  const { message, modal } = App.useApp();
  const isMobile = useIsMobile();
  const [optionalColumnsVisible, setOptionalColumnsVisible] = useState(() => {
    try {
      const raw = localStorage.getItem('asset-list-columns-v5');
      if (!raw) return false;
      const state = JSON.parse(raw) as Record<string, { show?: boolean }>;
      return ['model', 'category', 'purchaseDate', 'company'].some((key) => state[key]?.show === true);
    } catch {
      return false;
    }
  });
  const navigate = useNavigate();
  const location = useLocation();
  const actionRef = useRef<ActionType | undefined>(undefined);
  const [createModalOpen, setCreateModalOpen] = useState(false);
  const [archiveOpen, setArchiveOpen] = useState(false);
  const [archiveAsset, setArchiveAsset] = useState<AssetDto | null>(null);
  const [allocOpen, setAllocOpen] = useState(false);
  const [allocAsset, setAllocAsset] = useState<AssetDto | null>(null);
  const [recallOpen, setRecallOpen] = useState(false);
  const [recallAsset, setRecallAsset] = useState<AssetDto | null>(null);
  const [selectedRowKeys, setSelectedRowKeys] = useState<React.Key[]>([]);

  // Edit modal — opened IN PLACE via local state (Task A lesson: never navigate to open a modal).
  // Deep-link /assets/:id/edit only sets this state on the current page.
  const [editModalOpen, setEditModalOpen] = useState(false);
  const [editModalAssetId, setEditModalAssetId] = useState<string | null>(null);

  // Deep-link support: /assets/:id/edit opens the edit modal on THIS page.
  useEffect(() => {
    if (location.pathname === '/assets/new') {
      setCreateModalOpen(true);
    } else {
      const editMatch = location.pathname.match(/^\/assets\/([^/]+)\/edit$/);
      if (editMatch) {
        setEditModalAssetId(editMatch[1]);
        setEditModalOpen(true);
      }
    }
  }, [location.pathname]);

  // ST6b — permission gating (mirrors backend [Authorize(Policy=...)]).
  const canCreate = usePermission('assets.create');
  const canEdit = usePermission('assets.edit');
  const canDelete = usePermission('assets.delete');
  const canCheckout = usePermission('assets.checkout');
  const canCheckin = usePermission('assets.checkin');
  // ST6 (F40) — company column is only shown to Superusers (could see multiple companies).
  const superUser = isSuperUser();

  const reload = () => actionRef.current?.reload();

  const printSelectedLabels = () => {
    if (selectedRowKeys.length === 0) return;
    navigate(`/assets/labels/print?ids=${encodeURIComponent(selectedRowKeys.join(','))}`);
  };

  const hasAction = (record: AssetDto, action: string) =>
    (getAssetActions({
      status: record.status,
      isConfirmed: record.isConfirmed,
      assignedTo: record.assignedTo,
    }) as string[]).includes(action);

  const assignedLabel = (record: AssetDto) => (!record.assignedTo ? null
    : (record.assignedTo.name
      ?? ALLOCATION_TARGET_LABELS[record.assignedTo.type as AllocationTargetType]
      ?? record.assignedTo.type ?? null));

  const handleConfirm = (record: AssetDto) => {
    modal.confirm({
      title: 'Xác nhận tài sản',
      content: 'Sau khi xác nhận, chỉ có thể sửa Tên và Ghi chú. Không thể xóa tài sản này.',
      okText: 'Xác nhận',
      cancelText: 'Hủy',
      onOk: async () => {
        try {
          await assetService.confirm(record.id);
          void message.success('Đã xác nhận');
          reload();
        } catch {
          void message.error('Lỗi xác nhận');
          throw new Error('confirm failed');
        }
      },
    });
  };

  const handleUnarchive = (record: AssetDto) => {
    modal.confirm({
      title: 'Mở lại tài sản?',
      content: 'Tài sản sẽ trở về trạng thái Chờ cấp phát.',
      okText: 'Mở lại',
      cancelText: 'Hủy',
      onOk: async () => {
        try {
          await assetService.unarchive(record.id);
          void message.success('Đã mở lại');
          reload();
        } catch {
          void message.error('Lỗi mở lại');
          throw new Error('unarchive failed');
        }
      },
    });
  };

  const handleDelete = (record: AssetDto) => {
    modal.confirm({
      title: 'Xóa tài sản?',
      content: 'Tài sản chưa xác nhận sẽ bị xóa vĩnh viễn.',
      okText: 'Xóa',
      cancelText: 'Hủy',
      okButtonProps: { danger: true },
      onOk: async () => {
        try {
          await apiClient.delete(`/assets/${record.id}`);
          void message.success('Đã xóa');
          reload();
        } catch {
          void message.error('Không thể xóa');
          throw new Error('delete failed');
        }
      },
    });
  };

  const handleEdit = (record: AssetDto) => {
    setEditModalAssetId(record.id);
    setEditModalOpen(true);
  };

  const handleArchive = (record: AssetDto) => {
    setArchiveAsset(record);
    setArchiveOpen(true);
  };

  const handleAllocate = (record: AssetDto) => {
    setAllocAsset(record);
    setAllocOpen(true);
  };

  const handleRecall = (record: AssetDto) => {
    setRecallAsset(record);
    setRecallOpen(true);
  };

  /** One primary action per lifecycle state; everything else goes into the ⋯ menu. */
  const primaryActionFor = (record: AssetDto) => {
    const status = normalizeAssetStatus(record.status);
    if (!record.isConfirmed && hasAction(record, 'confirm') && canEdit) {
      return { key: 'confirm', label: 'Xác nhận', icon: <CheckSquareOutlined />, run: () => handleConfirm(record) };
    }
    if (status === 'Deployed' && hasAction(record, 'recall') && canCheckin) {
      return { key: 'recall', label: 'Thu hồi', icon: <RollbackOutlined />, run: () => handleRecall(record) };
    }
    if (status === 'Pending' && hasAction(record, 'allocate') && canCheckout) {
      return { key: 'allocate', label: 'Cấp phát', icon: <SendOutlined />, run: () => handleAllocate(record) };
    }
    if (status === 'Archived' && hasAction(record, 'unarchive') && canEdit) {
      return { key: 'unarchive', label: 'Mở lại', icon: <UndoOutlined />, run: () => handleUnarchive(record) };
    }
    return null;
  };

  const secondaryMenuItems = (record: AssetDto): MenuProps['items'] => {
    const items: NonNullable<MenuProps['items']> = [];
    if (hasAction(record, 'edit') && canEdit) {
      items.push({ key: 'edit', label: 'Sửa', icon: <EditOutlined />, onClick: () => handleEdit(record) });
    }
    if (hasAction(record, 'archive') && canEdit) {
      items.push({ key: 'archive', label: 'Lưu trữ', icon: <InboxOutlined />, onClick: () => handleArchive(record) });
    }
    if (hasAction(record, 'delete') && canDelete) {
      items.push({ key: 'delete', label: 'Xóa', icon: <DeleteOutlined />, danger: true, onClick: () => handleDelete(record) });
    }
    return items;
  };

  const renderRowActions = (record: AssetDto, stopPropagation = false) => {
    const primary = primaryActionFor(record);
    const menuItems = secondaryMenuItems(record);
    const stop = (e: { stopPropagation: () => void }) => { if (stopPropagation) e.stopPropagation(); };
    return (
      <Space size={4} wrap>
        {primary && (
          <Button size="small" style={{ width: 104 }}
            icon={primary.icon}
            onClick={(e) => { stop(e); primary.run(); }}>{primary.label}</Button>
        )}
        {menuItems && menuItems.length > 0 && (
          <Dropdown menu={{ items: menuItems }} trigger={['click']}>
            <Button size="small" icon={<MoreOutlined />} aria-label="Thao tác khác"
              onClick={stop} />
          </Dropdown>
        )}
      </Space>
    );
  };

  const renderEllipsis = (value?: string | null) => {
    const text = value || '-';
    return (
      <Tooltip title={text} placement="topLeft">
        <span style={{ display: 'block', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
          {text}
        </span>
      </Tooltip>
    );
  };

  const desktopColumns: ProColumns<AssetDto>[] = ([
    {
      title: 'Mã tài sản', dataIndex: 'assetTag', width: 110,
      sorter: true,
      render: (_, record) => (
        <Text code style={{ whiteSpace: 'nowrap' }}>{record.assetTag}</Text>
      ),
    },
    {
      title: 'Tên tài sản', dataIndex: 'name', width: 140,
      sorter: true,
      render: (_, record) => {
        const text = record.name || '-';
        return (
          <Tooltip title={text} placement="topLeft">
            <Typography.Link
              onClick={() => navigate(`/assets/${record.id}`)}
              style={{ display: 'block', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}
            >
              {text}
            </Typography.Link>
          </Tooltip>
        );
      },
    },
    {
      title: 'Trạng thái', dataIndex: 'status', key: 'status', width: 120,
      sorter: true,
      render: (_, record) => {
        const status = normalizeAssetStatus(record.status);
        return (
          <Space size={[0, 4]} wrap>
            <Tag color={record.isConfirmed ? (ASSET_STATUS_COLORS[status] ?? 'default') : 'default'}>
              {ASSET_STATUS_LABELS[status]}
            </Tag>
            {!record.isConfirmed && <Tag color="warning">Chưa xác nhận</Tag>}
          </Space>
        );
      },
    },
    {
      title: 'Đang giữ', key: 'assignedTo', width: 130,
      render: (_, record) => renderEllipsis(assignedLabel(record)),
    },
    {
      title: 'Vị trí', key: 'location', width: 100,
      render: (_, record) => renderEllipsis(record.location?.name),
    },
    {
      title: 'Số serial', dataIndex: 'serial', width: 160,
      sorter: true,
      render: (_, record) => (
        <Text style={{ fontFamily: 'monospace', whiteSpace: 'nowrap' }}>{record.serial || '-'}</Text>
      ),
    },
    {
      title: 'Model', key: 'model', width: 160,
      render: (_, record) => renderEllipsis(record.model?.name),
    },
    {
      title: 'Danh mục', key: 'category', width: 150,
      render: (_, record) => renderEllipsis(record.category?.name),
    },
    {
      title: 'Ngày mua', dataIndex: 'purchaseDate', width: 120,
      sorter: true,
      render: (_: unknown, record: AssetDto) => formatDate(record.purchaseDate),
    },
    ...(superUser ? [{
      title: 'Công ty', key: 'company', width: 160,
      render: (_: unknown, record: AssetDto) => renderEllipsis(record.company?.name),
    } as ProColumns<AssetDto>] : []),
    {
      title: 'Thao tác', key: 'actions', width: 150, fixed: optionalColumnsVisible ? undefined : 'right',
      render: (_: unknown, record: AssetDto) => renderRowActions(record),
    },
  ] as ProColumns<AssetDto>[]).map((column) => ({ ...column, hideInSearch: true, search: false }));

  const searchColumns: ProColumns<AssetDto>[] = [
    { title: 'Tìm kiếm', dataIndex: 'search', valueType: 'text', hideInTable: true,
      fieldProps: { placeholder: 'Tên, mã tài sản, số serial...' } },
    { key: 'search-status', title: 'Trạng thái', dataIndex: 'status', valueType: 'select', hideInTable: true,
      valueEnum: { Pending: 'Chờ cấp phát', Deployed: 'Đã cấp phát', Archived: 'Đã lưu trữ' } },
    { title: 'Vị trí', dataIndex: 'locationId', valueType: 'select', hideInTable: true,
      request: async () => {
        const res = await apiClient.get('/locations', { params: { pageSize: 500 } });
        return (res.data.data ?? []).map((l: { id: string; name: string }) => ({ label: l.name, value: l.id }));
      } },
    { title: 'Danh mục', dataIndex: 'categoryId', valueType: 'select', hideInTable: true,
      request: async () => {
        const res = await apiClient.get('/categories');
        return (res.data.data ?? []).map((c: { id: string; name: string }) => ({ label: c.name, value: c.id }));
      } },
  ];

  return (
    <>
      {isMobile ? (
        <ProList<AssetDto>
          headerTitle={<Title level={4} style={{ margin: 0 }}>Danh sách tài sản</Title>}
          actionRef={actionRef} rowKey="id" ghost cardProps={false}
          rowSelection={{ selectedRowKeys, onChange: setSelectedRowKeys, preserveSelectedRowKeys: true }}
          tableAlertRender={({ selectedRowKeys: keys }) => (
            <Space>
              <span>Đã chọn {keys.length} tài sản</span>
              <Button size="small" type="link" onClick={() => setSelectedRowKeys([])}>Bỏ chọn</Button>
            </Space>
          )}
          columns={searchColumns}
          grid={{ gutter: 16, xs: 1, sm: 1, md: 2, lg: 2, xl: 3, xxl: 3 }}
          search={{ labelWidth: 'auto', defaultCollapsed: true }}
          toolBarRender={() => [
            selectedRowKeys.length > 0 && <Button key="print-labels" icon={<PrinterOutlined />}
              onClick={printSelectedLabels}>In tem ({selectedRowKeys.length})</Button>,
            canCreate && <Button key="create" type="primary" size="middle" icon={<PlusOutlined />}
              onClick={() => setCreateModalOpen(true)}>Tạo tài sản</Button>,
          ]}
          request={async (params) => {
            try {
              const { current, pageSize, ...rest } = params;
              const res = await apiClient.get('/assets', { params: { ...rest, page: current, pageSize } });
              return { data: res.data.data ?? [], success: true, total: res.data.pagination?.totalItems ?? 0 };
            } catch { void message.error('Không thể tải danh sách tài sản'); return { data: [], success: false, total: 0 }; }
          }}
          pagination={{ defaultPageSize: 12, showSizeChanger: true,
            showTotal: (total, range) => `${range[0]}-${range[1]} / ${total} tài sản` }}
          locale={{ emptyText: <Empty description={<span>Không tìm thấy tài sản phù hợp — thử bỏ bớt điều kiện lọc hoặc <Typography.Link onClick={() => setCreateModalOpen(true)}>Tạo tài sản mới</Typography.Link></span>} /> }}
          itemRender={(record) => {
            const st = normalizeAssetStatus(record.status);
            const statusColor = record.isConfirmed ? (ASSET_STATUS_COLORS[st] ?? 'default') : 'default';
            const isDraft = !record.isConfirmed;
            const holder = assignedLabel(record);
            const isDeployed = record.assignedTo != null;
            return (
              <Card size="small" style={{ borderRadius: 12, marginBottom: 12, borderLeft: `4px solid ${statusColor}`, cursor: 'pointer', transition: 'box-shadow 0.2s' }}
                onClick={() => navigate(`/assets/${record.id}`)}
                styles={{ body: { padding: '16px' } }}>
                <div style={{ display: 'flex', alignItems: 'flex-start', gap: 10 }}>
                  <div style={{ width: 40, height: 40, borderRadius: 10, background: statusColor === statusColors.ready ? '#e6f4ff' : statusColor === statusColors.active ? '#f6ffed' : '#f5f5f5', display: 'flex', alignItems: 'center', justifyContent: 'center', flexShrink: 0 }}>
                    <LaptopOutlined style={{ fontSize: 18, color: statusColor }} />
                  </div>
                  <div style={{ flex: 1, minWidth: 0 }}>
                    <div style={{ display: 'flex', alignItems: 'baseline', gap: 6, flexWrap: 'wrap', marginBottom: 4 }}>
                      <Typography.Link style={{ fontSize: 15, fontWeight: 600 }}
                        onClick={(e) => { e.stopPropagation(); navigate(`/assets/${record.id}`); }}>
                        {record.name}
                      </Typography.Link>
                      <Text code style={{ fontSize: 11 }}>{record.assetTag}</Text>
                      {superUser && record.company?.name && <Tag style={{ fontSize: 10, lineHeight: '16px' }} color="geekblue">{record.company.name}</Tag>}
                    </div>
                    <Space size={[2, 4]} wrap style={{ marginBottom: 6 }}>
                      <Tag color={isDraft ? 'default' : statusColor}>{ASSET_STATUS_LABELS[st]}</Tag>
                      {isDraft && <Tag color="warning">Chưa xác nhận</Tag>}
                      {st === 'Archived' && <Tag icon={<InboxOutlined />} color="default">Đã lưu trữ</Tag>}
                    </Space>
                  </div>
                </div>
                <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '4px 12px', margin: '8px 0', fontSize: 12 }}>
                  <span><Text type="secondary">Model:</Text> <Text>{record.model?.name || '-'}</Text></span>
                  <span><Text type="secondary">Số serial:</Text> <Text style={{ fontFamily: 'monospace' }}>{record.serial || '-'}</Text></span>
                  <span><Text type="secondary">Vị trí:</Text> <Text>{record.location?.name || '-'}</Text></span>
                  {isDeployed && holder && <span><Text type="secondary">Đang giữ:</Text> <Text strong>{holder}</Text></span>}
                </div>
                <div style={{ display: 'flex', justifyContent: 'flex-end', marginTop: 8 }}>
                  {renderRowActions(record, true)}
                </div>
              </Card>
            );
          }}
        />
      ) : (
        <ProTable<AssetDto>
          headerTitle={<Title level={4} style={{ margin: 0 }}>Danh sách tài sản</Title>}
          actionRef={actionRef}
          rowKey="id"
          rowSelection={{ selectedRowKeys, onChange: setSelectedRowKeys, preserveSelectedRowKeys: true }}
          tableAlertRender={({ selectedRowKeys: keys }) => (
            <Space>
              <span>Đã chọn {keys.length} tài sản</span>
              <Button size="small" type="link" onClick={() => setSelectedRowKeys([])}>Bỏ chọn</Button>
            </Space>
          )}
          columns={[...searchColumns, ...desktopColumns]}
          search={{ labelWidth: 'auto', defaultCollapsed: false }}
          options={{ reload: true, density: true, setting: true }}
          columnsState={{
            persistenceKey: 'asset-list-columns-v5',
            persistenceType: 'localStorage',
            defaultValue: {
              assetTag: { show: true },
              name: { show: true },
              status: { show: true },
              assignedTo: { show: true },
              location: { show: true },
              serial: { show: true },
              model: { show: false },
              category: { show: false },
              purchaseDate: { show: false },
              company: { show: false },
            },
            onChange: (map) => {
              const wide = ['model', 'category', 'purchaseDate', 'company']
                .some((key) => map[key]?.show === true);
              setTimeout(() => setOptionalColumnsVisible(wide), 0);
            },
          }}
          toolBarRender={() => [
            selectedRowKeys.length > 0 && <Button key="print-labels" icon={<PrinterOutlined />}
              onClick={printSelectedLabels}>In tem ({selectedRowKeys.length})</Button>,
            canCreate && <Button key="create" type="primary" size="middle" icon={<PlusOutlined />}
              onClick={() => setCreateModalOpen(true)}>Tạo tài sản</Button>,
          ]}
          request={async (params, sort) => {
            try {
              const { current, pageSize, ...rest } = params;
              const sortEntry = Object.entries(sort ?? {}).find(([, order]) => order === 'ascend' || order === 'descend');
              const sortBy = sortEntry?.[0];
              const sortDir = sortEntry?.[1] === 'descend' ? 'desc' : 'asc';
              const res = await apiClient.get('/assets', {
                params: {
                  ...rest, page: current, pageSize,
                  ...(sortBy ? { sortBy, sortDir } : {}),
                },
              });
              return { data: res.data.data ?? [], success: true, total: res.data.pagination?.totalItems ?? 0 };
            } catch { void message.error('Không thể tải danh sách tài sản'); return { data: [], success: false, total: 0 }; }
          }}
          pagination={{ defaultPageSize: 12, showSizeChanger: true,
            showTotal: (total, range) => `${range[0]}-${range[1]} / ${total} tài sản` }}
          locale={{ emptyText: <Empty description={<span>Không tìm thấy tài sản phù hợp — thử bỏ bớt điều kiện lọc hoặc <Typography.Link onClick={() => setCreateModalOpen(true)}>Tạo tài sản mới</Typography.Link></span>} /> }}
          scroll={{ x: 'max-content' }}
        />
      )}
      <CreateAssetFlowModal open={createModalOpen} onClose={() => setCreateModalOpen(false)}
        onSuccess={() => { setCreateModalOpen(false); actionRef.current?.reload(); }} />
      <AssetArchiveModal open={archiveOpen} asset={archiveAsset}
        onClose={() => setArchiveOpen(false)}
        onSuccess={() => { setArchiveOpen(false); actionRef.current?.reload(); }} />
      <AssetAllocationModal open={allocOpen} asset={allocAsset}
        onClose={() => setAllocOpen(false)}
        onSuccess={() => { setAllocOpen(false); actionRef.current?.reload(); }} />
      <AssetRecallModal open={recallOpen} asset={recallAsset}
        onClose={() => setRecallOpen(false)}
        onSuccess={() => { setRecallOpen(false); actionRef.current?.reload(); }} />
      <AssetEditModal
        open={editModalOpen}
        assetId={editModalAssetId}
        onClose={() => {
          setEditModalOpen(false);
          setEditModalAssetId(null);
          navigate('/assets', { replace: true });
        }}
        onSaved={() => {
          setEditModalOpen(false);
          setEditModalAssetId(null);
          navigate('/assets', { replace: true });
          actionRef.current?.reload();
        }}
      />
    </>
  );
};

// ==================== Two-Step Create Flow (Form → Read-only Confirmation) ====================
// Business flow: Fill form → Review screen (read-only) → click "Xác nhận tạo" → POST /assets
// → asset created with Status = Pending, immediately ready for allocation.
// The confirmation screen is THE single confirmation; there is NO second Confirm step.

interface AssetLookupOption { id: string; name: string; }

interface AssetLookups {
  models: AssetLookupOption[];
  locations: AssetLookupOption[];
  suppliers: AssetLookupOption[];
  companies: AssetLookupOption[];
}

const EMPTY_LOOKUPS: AssetLookups = { models: [], locations: [], suppliers: [], companies: [] };

function toDateIso(v: unknown): string | undefined {
  if (v == null || v === '') return undefined;
  const d = dayjs(v as string | number | Date);
  return d.isValid() ? d.toISOString() : undefined;
}

function CreateAssetFlowModal({ open, onClose, onSuccess }: { open: boolean; onClose: () => void; onSuccess: () => void }) {
  const { message } = App.useApp();
  const [step, setStep] = useState<'form' | 'review'>('form');
  const [formData, setFormData] = useState<Record<string, unknown> | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [lookups, setLookups] = useState<AssetLookups>(EMPTY_LOOKUPS);

  useEffect(() => {
    if (!open) {
      setStep('form');
      setFormData(null);
      return;
    }
    let cancelled = false;
    Promise.all([
      apiClient.get('/models').catch(() => ({ data: { data: [] } })),
      apiClient.get('/locations').catch(() => ({ data: { data: [] } })),
      apiClient.get('/suppliers').catch(() => ({ data: { data: [] } })),
      apiClient.get('/companies').catch(() => ({ data: { data: [] } })),
    ]).then(([m, l, s, c]) => {
      if (cancelled) return;
      setLookups({
        models: (m.data.data ?? []).map((x: { id: string; name: string }) => ({ id: x.id, name: x.name })),
        locations: (l.data.data ?? []).map((x: { id: string; name: string }) => ({ id: x.id, name: x.name })),
        suppliers: (s.data.data ?? []).map((x: { id: string; name: string }) => ({ id: x.id, name: x.name })),
        companies: (c.data.data ?? []).flatMap((co: { id: string; name: string; children?: unknown[] }) =>
          co.children?.length ? [co, ...(co.children as unknown[])] : [co])
          .map((co: { id: string; name: string }) => ({ id: co.id, name: co.name })),
      });
    });
    return () => { cancelled = true; };
  }, [open]);

  if (step === 'form') {
    return (
      <AssetCreateFormModal
        open={open}
        onClose={onClose}
        lookups={lookups}
        onSubmit={(v) => { setFormData(v); setStep('review'); }}
      />
    );
  }

  const resolveName = (list: AssetLookupOption[], id?: unknown) =>
    list.find(x => x.id === id)?.name ?? '-';

  const handleCreate = async () => {

    if (!formData) return;
    setSubmitting(true);
    try {
      const payload: CreateAssetPayload = {
        assetTag: String(formData.assetTag ?? '').trim(),
        name: String(formData.name ?? '').trim(),
        serial: formData.serial ? String(formData.serial) : undefined,
        modelId: formData.modelId ? String(formData.modelId) : undefined,
        locationId: formData.locationId ? String(formData.locationId) : undefined,
        supplierId: formData.supplierId ? String(formData.supplierId) : undefined,
        companyId: formData.companyId ? String(formData.companyId) : undefined,
        purchaseCost: typeof formData.purchaseCost === 'number' ? formData.purchaseCost : undefined,
        purchaseDate: toDateIso(formData.purchaseDate),
        warrantyMonths: typeof formData.warrantyMonths === 'number' ? formData.warrantyMonths : undefined,
        orderNumber: formData.orderNumber ? String(formData.orderNumber) : undefined,
        notes: formData.notes ? String(formData.notes) : undefined,
      };
      await assetService.create(payload);
      message.success('Tạo tài sản thành công. Tài sản đang ở trạng thái Chờ cấp phát.');
      setStep('form');
      setFormData(null);
      onSuccess();
    } catch (err: unknown) {
      void message.error(serverErrorMessage(err, 'Không thể tạo tài sản. Vui lòng thử lại.'));
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <ModalFormShell
      title="Xác nhận tạo tài sản"
      open={open}
      onCancel={() => setStep('form')}
      onOk={() => void handleCreate()}
      cancelText="Quay lại"
      okText="Xác nhận tạo"
      confirmLoading={submitting}
      size="md"
      maskClosable={false}
    >
      <Card size="small" title="Thông tin tài sản" style={{ borderRadius: 10, marginBottom: 16 }} styles={{ body: { padding: '16px' } }}>
        <Descriptions bordered size="small" column={2}>
          <Descriptions.Item label="Mã tài sản">{formData?.assetTag ? String(formData.assetTag) : '-'}</Descriptions.Item>
          <Descriptions.Item label="Tên tài sản">{formData?.name ? String(formData.name) : '-'}</Descriptions.Item>
          <Descriptions.Item label="Serial">{formData?.serial ? String(formData.serial) : '-'}</Descriptions.Item>
          <Descriptions.Item label="Model">{resolveName(lookups.models, formData?.modelId)}</Descriptions.Item>
          <Descriptions.Item label="Địa điểm">{resolveName(lookups.locations, formData?.locationId)}</Descriptions.Item>
          <Descriptions.Item label="Nhà cung cấp">{resolveName(lookups.suppliers, formData?.supplierId)}</Descriptions.Item>
          <Descriptions.Item label="Công ty" span={2}>{resolveName(lookups.companies, formData?.companyId)}</Descriptions.Item>
        </Descriptions>
      </Card>

      <Card size="small" title="Thông tin mua sắm" style={{ borderRadius: 10, marginBottom: 16 }} styles={{ body: { padding: '16px' } }}>
        <Descriptions bordered size="small" column={2}>
          <Descriptions.Item label="Đơn giá">{formatMoney(formData?.purchaseCost)}</Descriptions.Item>
          <Descriptions.Item label="Ngày mua">{formatDate(formData?.purchaseDate as string | null | undefined)}</Descriptions.Item>
          <Descriptions.Item label="Bảo hành (tháng)">{typeof formData?.warrantyMonths === 'number' ? `${formData.warrantyMonths} tháng` : '-'}</Descriptions.Item>
          <Descriptions.Item label="Số đơn hàng">{formData?.orderNumber ? String(formData.orderNumber) : '-'}</Descriptions.Item>
        </Descriptions>
      </Card>

      <Card size="small" title="Ghi chú" style={{ borderRadius: 10, marginBottom: 16 }} styles={{ body: { padding: '16px' } }}>
        <Text>{formData?.notes ? String(formData.notes) : '-'}</Text>
      </Card>

      <Alert type="info" showIcon style={{ borderRadius: 8 }} title={
        <Space orientation="vertical" size={2}>
          <span>Sau khi xác nhận, tài sản sẽ được tạo chính thức với trạng thái “Chờ cấp phát”. Tài sản sẽ sẵn sàng để cấp phát ngay sau khi tạo.</span>
          <span>Sau khi tạo, chỉ Tên và Ghi chú có thể được chỉnh sửa. Các thông tin khác không thể thay đổi và tài sản không thể xóa.</span>
        </Space>
      } />
    </ModalFormShell>
  );
}

// ==================== Create Form (Step 1) ====================

function AssetCreateFormModal({ open, onClose, onSubmit, lookups }: {
  open: boolean;
  onClose: () => void;
  onSubmit: (v: Record<string, unknown>) => void;
  lookups: AssetLookups;
}) {
  const [form] = Form.useForm();
  useEffect(() => { if (open) form.resetFields(); }, [open, form]);
  const filterFn = (inp: string, opt?: { label: string }) => (opt?.label ?? '').toLowerCase().includes(inp.toLowerCase());
  const modelOpts = lookups.models.map(m => ({ value: m.id, label: m.name }));
  const locationOpts = lookups.locations.map(l => ({ value: l.id, label: l.name }));
  const supplierOpts = lookups.suppliers.map(s => ({ value: s.id, label: s.name }));
  return (
    <ModalFormShell
      title="Tạo tài sản"
      open={open}
      onCancel={onClose}
      onOk={() => form.submit()}
      okText="Tiếp tục"
      size="lg"
      maskClosable={false}
    >
      <Form
        form={form}
        layout="vertical"
        size="middle"
        onFinish={onSubmit}
      >
        <Card size="small" title="Thông tin chung" style={{ borderRadius: 10, marginBottom: 16 }} styles={{ body: { padding: '16px 16px 4px' } }}>
          <Row gutter={[16, 0]}>
            <Col xs={24} md={12} lg={6}><Form.Item name="assetTag" label="Mã tài sản"><Input placeholder="Để trống để tự sinh mã" /></Form.Item></Col>
            <Col xs={24} md={12} lg={12}><Form.Item name="name" label="Tên tài sản" rules={[{ required: true, message: 'Vui lòng nhập tên tài sản' }]}><Input placeholder="Nhập tên tài sản" /></Form.Item></Col>
            <Col xs={24} md={12} lg={6}><Form.Item name="serial" label="Serial"><Input placeholder="Nhập serial" /></Form.Item></Col>
          </Row>
        </Card>
        <Card size="small" title="Phân loại" style={{ borderRadius: 10, marginBottom: 16 }} styles={{ body: { padding: '16px 16px 4px' } }}>
          <Row gutter={[16, 0]}>
            <Col xs={24} md={12} lg={12}><Form.Item name="modelId" label="Model"><Select showSearch allowClear options={modelOpts} filterOption={filterFn} /></Form.Item></Col>
            <Col xs={24} md={12} lg={12}><Form.Item name="locationId" label="Địa điểm"><Select showSearch allowClear options={locationOpts} filterOption={filterFn} placeholder="Chọn địa điểm" /></Form.Item></Col>
            <Col xs={24} md={12} lg={12}><Form.Item name="supplierId" label="Nhà cung cấp"><Select showSearch allowClear options={supplierOpts} filterOption={filterFn} /></Form.Item></Col>
            <Col xs={24} md={12} lg={12}><Form.Item name="companyId" label="Công ty"><CompanyTreeSelect /></Form.Item></Col>
          </Row>
        </Card>
        <Card size="small" title="Tài chính" style={{ borderRadius: 10, marginBottom: 16 }} styles={{ body: { padding: '16px 16px 4px' } }}>
          <Row gutter={[16, 0]}>
            <Col xs={24} md={12} lg={12}><Form.Item name="purchaseCost" label="Đơn giá"><Space.Compact block><InputNumber min={0} style={{ width: '100%' }} placeholder="Nhập đơn giá" formatter={v => `${v}`.replace(/\B(?=(\d{3})+(?!\d))/g, ',')} parser={v => (v?.replace(/,/g, '') ?? '') as unknown as 0} /><Button style={{ width: 56 }}>VND</Button></Space.Compact></Form.Item></Col>
            <Col xs={24} md={12} lg={12}><Form.Item name="purchaseDate" label="Ngày mua"><DatePicker style={{ width: '100%' }} format="DD/MM/YYYY" placeholder="Chọn ngày mua" /></Form.Item></Col>
            <Col xs={24} md={12} lg={12}><Form.Item name="warrantyMonths" label="Bảo hành (tháng)"><InputNumber min={0} max={120} style={{ width: '100%' }} placeholder="Số tháng bảo hành" /></Form.Item></Col>
            <Col xs={24} md={12} lg={12}><Form.Item name="orderNumber" label="Số đơn hàng"><Input placeholder="Nhập số đơn hàng" /></Form.Item></Col>
          </Row>
        </Card>
        <Card size="small" title="Ghi chú" style={{ borderRadius: 10, marginBottom: 24 }} styles={{ body: { padding: '16px 16px 4px' } }}>
          <Form.Item name="notes" label="Ghi chú"><Input.TextArea rows={3} maxLength={1000} showCount placeholder="Ghi chú thêm" /></Form.Item>
        </Card>
      </Form>
    </ModalFormShell>
  );
}

export default AssetListPage;
