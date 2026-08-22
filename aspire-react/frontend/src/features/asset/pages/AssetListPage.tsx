import { useRef, useState, useEffect } from 'react';
import {
  Tag, App, Button, Space, Card, Typography, Divider, Popconfirm, Empty,
  Modal, Alert, Descriptions, Form, Input, InputNumber, Select, DatePicker, Row, Col,
} from 'antd';
import {
  PlusOutlined, EditOutlined, EyeOutlined, SendOutlined,
  LaptopOutlined,
  RollbackOutlined, DeleteOutlined, CheckSquareOutlined, UndoOutlined, InboxOutlined,
} from '@ant-design/icons';
import { ProList } from '@ant-design/pro-components';
import type { ActionType, ProColumns } from '@ant-design/pro-components';
import { useNavigate, useLocation } from 'react-router-dom';
import dayjs from 'dayjs';
import apiClient from '../../../services/api-client';
import { assetService, type CreateAssetPayload } from '../services/asset.service';
import { usePermission } from '../../../hooks/usePermission';
import { isSuperUser } from '../../../services/keycloak';
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
import { formatMoney } from '../../../utils/format';

const { Text, Title, Paragraph } = Typography;

// ==================== AssetListPage ====================

const AssetListPage: React.FC = () => {
  const { message } = App.useApp();
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

  const searchColumns: ProColumns<AssetDto>[] = [
    { title: 'Tìm kiếm', dataIndex: 'search', valueType: 'text', hideInTable: true,
      fieldProps: { placeholder: 'Tên, mã tài sản, serial...' } },
    { title: 'Trạng thái', dataIndex: 'status', valueType: 'select', hideInTable: true,
      valueEnum: { Pending: 'Chờ cấp phát', Deployed: 'Đã cấp phát', Archived: 'Đã thu hồi' } },
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
      <ProList<AssetDto>
        headerTitle={<Title level={4} style={{ margin: 0 }}>Danh sách tài sản</Title>}
        actionRef={actionRef} rowKey="id" ghost cardProps={false}
        columns={searchColumns}
        grid={{ gutter: 16, xs: 1, sm: 1, md: 2, lg: 2, xl: 3, xxl: 3 }}
        search={{ labelWidth: 'auto', defaultCollapsed: false }}
        toolBarRender={() => [
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
          const actions = getAssetActions({ status: st, isConfirmed: record.isConfirmed });
          const has = (a: string) => actions.includes(a as ReturnType<typeof getAssetActions>[number]);
          const statusColor = record.isConfirmed ? (ASSET_STATUS_COLORS[st] ?? 'default') : 'default';
          const isDraft = !record.isConfirmed;
          const assignedLabel = !record.assignedTo ? null
            : (record.assignedTo.name ?? ALLOCATION_TARGET_LABELS[record.assignedTo.type as AllocationTargetType] ?? record.assignedTo.type);
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
                    <Text strong style={{ fontSize: 15 }}>{record.name}</Text>
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
                <span><Text type="secondary">Serial:</Text> <Text style={{ fontFamily: 'monospace' }}>{record.serial || '-'}</Text></span>
                <span><Text type="secondary">Vị trí:</Text> <Text>{record.location?.name || '-'}</Text></span>
                {isDeployed && assignedLabel && <span><Text type="secondary">Đang giữ:</Text> <Text strong>{assignedLabel}</Text></span>}
                {record.notes && (
                  <div style={{ gridColumn: '1 / -1', minWidth: 0, marginTop: 2 }}>
                    <div><Text type="secondary">Ghi chú:</Text></div>
                    <Paragraph ellipsis={{ rows: 2, tooltip: record.notes }} style={{ fontSize: 12, margin: 0 }}>{record.notes}</Paragraph>
                  </div>
                )}
              </div>
              <Divider style={{ margin: '8px 0' }} />
              <Space size="small" wrap style={{ justifyContent: 'flex-end', width: '100%' }}>
                {has('view') && <Button size="small" icon={<EyeOutlined />} onClick={(e) => { e.stopPropagation(); navigate(`/assets/${record.id}`); }}>Xem</Button>}
                {has('edit') && canEdit && <Button size="small" icon={<EditOutlined />} onClick={(e) => { e.stopPropagation(); setEditModalAssetId(record.id); setEditModalOpen(true); }}>Sửa</Button>}
                {has('confirm') && canEdit && (
                  <Popconfirm title="Xác nhận tài sản" description="Sau khi xác nhận, chỉ có thể sửa Tên và Ghi chú. Không thể xóa tài sản này." okText="Xác nhận" cancelText="Hủy" onConfirm={async () => { try { await assetService.confirm(record.id); void message.success('Đã xác nhận'); actionRef.current?.reload(); } catch { void message.error('Lỗi xác nhận'); } }}>
                    <Button size="small" type="primary" ghost icon={<CheckSquareOutlined />} onClick={(e) => e.stopPropagation()}>Xác nhận</Button>
                  </Popconfirm>
                )}
                {has('allocate') && canCheckout && <Button size="small" type="primary" ghost icon={<SendOutlined />} onClick={(e) => { e.stopPropagation(); setAllocAsset(record); setAllocOpen(true); }}>Cấp phát</Button>}
                {has('archive') && canEdit && <Button size="small" danger icon={<InboxOutlined />} onClick={(e) => { e.stopPropagation(); setArchiveAsset(record); setArchiveOpen(true); }}>Lưu trữ</Button>}
                {has('recall') && canCheckin && <Button size="small" icon={<RollbackOutlined />} onClick={(e) => { e.stopPropagation(); setRecallAsset(record); setRecallOpen(true); }}>Thu hồi</Button>}
                {has('unarchive') && canEdit && (
                  <Popconfirm title="Mở lại tài sản?" description="Tài sản sẽ trở về trạng thái Chờ cấp phát." okText="Mở lại" cancelText="Hủy" onConfirm={async () => { try { await assetService.unarchive(record.id); void message.success('Đã mở lại'); actionRef.current?.reload(); } catch { void message.error('Lỗi mở lại'); } }}>
                    <Button size="small" icon={<UndoOutlined />} onClick={(e) => e.stopPropagation()}>Mở lại</Button>
                  </Popconfirm>
                )}
                {has('delete') && canDelete && (
                  <Popconfirm title="Xóa tài sản?" description="Tài sản chưa xác nhận sẽ bị xóa vĩnh viễn." okText="Xóa" cancelText="Hủy" okButtonProps={{ danger: true }} onConfirm={async () => { try { await apiClient.delete(`/assets/${record.id}`); void message.success('Đã xóa'); actionRef.current?.reload(); } catch { void message.error('Không thể xóa'); } }}>
                    <Button size="small" danger icon={<DeleteOutlined />} onClick={(e) => e.stopPropagation()}>Xóa</Button>
                  </Popconfirm>
                )}
              </Space>
            </Card>
          );
        }}
      />
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

function formatDateValue(v: unknown): string {
  if (v == null || v === '') return '-';
  const d = dayjs(v as string | number | Date);
  return d.isValid() ? d.format('DD/MM/YYYY') : '-';
}

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
      void message.error((err as { response?: { data?: { message?: string } } }).response?.data?.message ?? 'Lỗi tạo tài sản');
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <Modal
      title="Xác nhận tạo tài sản"
      open={open}
      onCancel={onClose}
      width={680}
      footer={[
        <Button key="back" onClick={() => setStep('form')}>Quay lại</Button>,
        <Button key="submit" type="primary" loading={submitting} onClick={handleCreate}>Xác nhận tạo</Button>,
      ]}
    >
      <Card size="small" title="Thông tin tài sản" style={{ borderRadius: 10, marginBottom: 16 }} styles={{ body: { padding: '16px' } }}>
        <Descriptions bordered size="small" column={2}>
          <Descriptions.Item label="Mã tài sản">{formData?.assetTag ? String(formData.assetTag) : '-'}</Descriptions.Item>
          <Descriptions.Item label="Tên tài sản">{formData?.name ? String(formData.name) : '-'}</Descriptions.Item>
          <Descriptions.Item label="Serial">{formData?.serial ? String(formData.serial) : '-'}</Descriptions.Item>
          <Descriptions.Item label="Model">{resolveName(lookups.models, formData?.modelId)}</Descriptions.Item>
          <Descriptions.Item label="Vị trí">{resolveName(lookups.locations, formData?.locationId)}</Descriptions.Item>
          <Descriptions.Item label="Nhà cung cấp">{resolveName(lookups.suppliers, formData?.supplierId)}</Descriptions.Item>
          <Descriptions.Item label="Công ty" span={2}>{resolveName(lookups.companies, formData?.companyId)}</Descriptions.Item>
        </Descriptions>
      </Card>

      <Card size="small" title="Thông tin mua sắm" style={{ borderRadius: 10, marginBottom: 16 }} styles={{ body: { padding: '16px' } }}>
        <Descriptions bordered size="small" column={2}>
          <Descriptions.Item label="Giá mua">{formatMoney(formData?.purchaseCost)}</Descriptions.Item>
          <Descriptions.Item label="Ngày mua">{formatDateValue(formData?.purchaseDate)}</Descriptions.Item>
          <Descriptions.Item label="Thời hạn bảo hành">{typeof formData?.warrantyMonths === 'number' ? `${formData.warrantyMonths} tháng` : '-'}</Descriptions.Item>
          <Descriptions.Item label="Số đơn hàng">{formData?.orderNumber ? String(formData.orderNumber) : '-'}</Descriptions.Item>
        </Descriptions>
      </Card>

      <Card size="small" title="Ghi chú" style={{ borderRadius: 10, marginBottom: 16 }} styles={{ body: { padding: '16px' } }}>
        <Text>{formData?.notes ? String(formData.notes) : '-'}</Text>
      </Card>

      <Alert type="info" showIcon style={{ borderRadius: 8 }} title={
        <Space direction="vertical" size={2}>
          <span>Sau khi xác nhận, tài sản sẽ được tạo chính thức với trạng thái “Chờ cấp phát”. Tài sản sẽ sẵn sàng để cấp phát ngay sau khi tạo.</span>
          <span>Sau khi tạo, chỉ Tên và Ghi chú có thể được chỉnh sửa. Các thông tin khác không thể thay đổi và tài sản không thể xóa.</span>
        </Space>
      } />
    </Modal>
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
    <Modal title="Tạo tài sản mới" open={open} onCancel={onClose} width={760} footer={null} destroyOnHidden>
      <Form form={form} layout="vertical" size="middle" onFinish={onSubmit}>
        <Card size="small" title="Thông tin chung" style={{ borderRadius: 10, marginBottom: 16 }} styles={{ body: { padding: '16px 16px 4px' } }}>
          <Row gutter={[16, 0]}>
            <Col xs={24} md={12} lg={6}><Form.Item name="assetTag" label="Mã tài sản"><Input placeholder="Để trống để tự sinh mã" /></Form.Item></Col>
            <Col xs={24} md={12} lg={12}><Form.Item name="name" label="Tên tài sản" rules={[{ required: true }]}><Input placeholder="Tên tài sản" /></Form.Item></Col>
            <Col xs={24} md={12} lg={6}><Form.Item name="serial" label="Serial"><Input placeholder="Số serial" /></Form.Item></Col>
          </Row>
        </Card>
        <Card size="small" title="Phân loại" style={{ borderRadius: 10, marginBottom: 16 }} styles={{ body: { padding: '16px 16px 4px' } }}>
          <Row gutter={[16, 0]}>
            <Col xs={24} md={12} lg={12}><Form.Item name="modelId" label="Model"><Select showSearch allowClear options={modelOpts} filterOption={filterFn} /></Form.Item></Col>
            <Col xs={24} md={12} lg={12}><Form.Item name="locationId" label="Vị trí"><Select showSearch allowClear options={locationOpts} filterOption={filterFn} /></Form.Item></Col>
            <Col xs={24} md={12} lg={12}><Form.Item name="supplierId" label="Nhà cung cấp"><Select showSearch allowClear options={supplierOpts} filterOption={filterFn} /></Form.Item></Col>
            <Col xs={24} md={12} lg={12}><Form.Item name="companyId" label="Công ty"><CompanyTreeSelect /></Form.Item></Col>
          </Row>
        </Card>
        <Card size="small" title="Tài chính" style={{ borderRadius: 10, marginBottom: 16 }} styles={{ body: { padding: '16px 16px 4px' } }}>
          <Row gutter={[16, 0]}>
            <Col xs={24} md={12} lg={12}><Form.Item name="purchaseCost" label="Giá mua"><Space.Compact block><InputNumber min={0} style={{ width: '100%' }} formatter={v => `${v}`.replace(/\B(?=(\d{3})+(?!\d))/g, ',')} parser={v => (v?.replace(/,/g, '') ?? '') as unknown as 0} /><Button style={{ width: 56 }}>VND</Button></Space.Compact></Form.Item></Col>
            <Col xs={24} md={12} lg={12}><Form.Item name="purchaseDate" label="Ngày mua"><DatePicker style={{ width: '100%' }} format="DD/MM/YYYY" /></Form.Item></Col>
            <Col xs={24} md={12} lg={12}><Form.Item name="warrantyMonths" label="Thời hạn bảo hành"><InputNumber min={0} max={120} style={{ width: '100%' }} /></Form.Item></Col>
            <Col xs={24} md={12} lg={12}><Form.Item name="orderNumber" label="Số đơn hàng"><Input /></Form.Item></Col>
          </Row>
        </Card>
        <Card size="small" title="Ghi chú" style={{ borderRadius: 10, marginBottom: 24 }} styles={{ body: { padding: '16px 16px 4px' } }}>
          <Form.Item name="notes" label="Ghi chú"><Input.TextArea rows={3} maxLength={1000} showCount /></Form.Item>
        </Card>
        <div style={{ display: 'flex', gap: 12, justifyContent: 'flex-end' }}><Button onClick={onClose}>Hủy</Button><Button type="primary" htmlType="submit">Tiếp tục</Button></div>
      </Form>
    </Modal>
  );
}

export default AssetListPage;
