import { useCallback, useEffect, useMemo, useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import {
  Alert, App, Button, Card, Descriptions, Drawer, Empty, Input, InputNumber, Modal,
  Popconfirm, Select, Space, Spin, Table, Tabs, Tag, Tooltip, Typography,
} from 'antd';
import { ArrowLeftOutlined, EditOutlined, HistoryOutlined, InboxOutlined, RollbackOutlined, SendOutlined } from '@ant-design/icons';
import apiClient from '../../../services/api-client';
import {
  componentsApi, type ComponentDto, type ComponentUnitDto, type ComponentUnitStatus,
} from '../services/components.service';
import { usePermission } from '../../../hooks/usePermission';
import ActionLogTable from '../../../shared/components/ActionLogTable';
import type { ActionLogRow } from '../../../shared/components/ActionLogTable';
import ComponentFormModal from '../components/ComponentFormModal';
import { statusColors } from '../../../theme/designTokens';

const { Title, Text } = Typography;

const UNIT_STATUS_TAGS: Record<ComponentUnitStatus, { color: string; label: string }> = {
  InStock: { color: statusColors.ready, label: 'Trong kho' },
  Allocated: { color: 'orange', label: 'Đã cấp phát' },
  Damaged: { color: 'red', label: 'Hư hỏng' },
  Disposed: { color: 'default', label: 'Đã loại bỏ' },
};

const UNIT_STATUS_FILTERS: { label: string; value: ComponentUnitStatus }[] = [
  { label: 'Trong kho', value: 'InStock' },
  { label: 'Đã cấp phát', value: 'Allocated' },
  { label: 'Hư hỏng', value: 'Damaged' },
  { label: 'Đã loại bỏ', value: 'Disposed' },
];

interface AssetOption {
  label: string;
  value: string;
  companyId?: string | null;
}

export default function ComponentDetailPage() {
  const navigate = useNavigate();
  const { id } = useParams<{ id: string }>();
  const { message } = App.useApp();

  const [component, setComponent] = useState<ComponentDto | null>(null);
  const [loading, setLoading] = useState(true);

  // Units table (serial components)
  const [units, setUnits] = useState<ComponentUnitDto[]>([]);
  const [unitsLoading, setUnitsLoading] = useState(false);
  const [unitsPagination, setUnitsPagination] = useState({ current: 1, pageSize: 20, total: 0 });
  const [statusFilter, setStatusFilter] = useState<ComponentUnitStatus | undefined>(undefined);

  // Assets catalog (for checkout/checkin)
  const [assets, setAssets] = useState<AssetOption[]>([]);

  // Modals
  const [stockInOpen, setStockInOpen] = useState(false);
  const [checkoutOpen, setCheckoutOpen] = useState(false);
  const [checkinOpen, setCheckinOpen] = useState(false);
  const [historyUnit, setHistoryUnit] = useState<ComponentUnitDto | null>(null);
  const [editOpen, setEditOpen] = useState(false);

  // ST6b — permission gating (mirrors backend [Authorize(Policy=...)]).
  const canEdit = usePermission('components.edit');
  const canDelete = usePermission('components.delete');
  const canCheckout = usePermission('components.checkout'); // covers assign/remove/checkout/checkin

  const load = useCallback(async () => {
    if (!id) return;
    setLoading(true);
    try {
      const res = await componentsApi.get(id);
      setComponent(res.data.data as ComponentDto);
    } catch {
      message.error('Không thể tải linh kiện');
    } finally {
      setLoading(false);
    }
  }, [id, message]);

  const loadUnits = useCallback(async (page = 1, pageSize = 20, status?: ComponentUnitStatus) => {
    if (!id) return;
    setUnitsLoading(true);
    try {
      const res = await componentsApi.listUnits(id, { status, page, pageSize });
      setUnits(res.data.data as ComponentUnitDto[]);
      setUnitsPagination(p => ({ ...p, current: page, pageSize, total: res.data.pagination?.totalItems ?? 0 }));
    } catch {
      message.error('Không thể tải danh sách serial');
    } finally {
      setUnitsLoading(false);
    }
  }, [id, message]);

  const loadAssets = useCallback(async () => {
    try {
      const res = await apiClient.get('/assets', { params: { pageSize: 500 } });
      const list = (res.data?.data ?? []) as { id: string; assetTag: string; name: string; company?: { id: string } | null }[];
      setAssets(list.map(a => ({
        label: `${a.assetTag} - ${a.name}`,
        value: a.id,
        companyId: a.company?.id ?? null,
      })));
    } catch {
      message.error('Không thể tải danh sách tài sản');
    }
  }, [message]);

  useEffect(() => { void load(); }, [load]);

  useEffect(() => {
    if (component?.trackingType === 'Serial') void loadUnits(1, 20, statusFilter);
  }, [component?.trackingType, statusFilter, loadUnits]);

  useEffect(() => { void loadAssets(); }, [loadAssets]);

  const refreshAll = () => { void load(); void loadUnits(unitsPagination.current, unitsPagination.pageSize, statusFilter); };

  // ===== Action handlers =====
  const handleStockIn = async (serials: string[], note?: string) => {
    if (!id) return;
    await componentsApi.stockInUnits(id, { serialNumbers: serials, note });
    message.success('Đã nhập kho serial');
    setStockInOpen(false);
    refreshAll();
  };

  const handleCheckout = async (assetId: string, quantity: number, serialNo?: string, note?: string) => {
    if (!id) return;
    await componentsApi.checkout(id, { assetId, quantity, serialNo, note });
    message.success('Đã cấp phát');
    setCheckoutOpen(false);
    refreshAll();
  };

  const handleCheckin = async (data: { assetId?: string; quantity?: number; serialNo?: string; note?: string }) => {
    if (!id) return;
    await componentsApi.checkin(id, data);
    message.success('Đã thu hồi');
    setCheckinOpen(false);
    refreshAll();
  };

  const handleUnitStatus = async (unit: ComponentUnitDto, status: ComponentUnitStatus) => {
    await componentsApi.updateUnitStatus(unit.id, { status });
    message.success(`Đã cập nhật trạng thái ${UNIT_STATUS_TAGS[status].label.toLowerCase()}`);
    refreshAll();
  };

  const handleUnitDelete = async (unit: ComponentUnitDto) => {
    await componentsApi.deleteUnit(unit.id);
    message.success('Đã xóa serial');
    refreshAll();
  };

  // Reload the history tab whenever the component detail is refreshed (new object identity).
  const historyParams = useMemo(
    () => ({ refreshKey: component?.updatedAt ?? 0 }),
    [component],
  );

  if (loading || !component) return <Spin style={{ display: 'block', margin: '80px auto' }} />;

  const unitsColumns = [
    { title: 'Serial', dataIndex: 'serialNo', key: 'serialNo', render: (v: string | null) => v || '-' },
    {
      title: 'Trạng thái', dataIndex: 'status', key: 'status', width: 140,
      render: (v: ComponentUnitStatus) => {
        const t = UNIT_STATUS_TAGS[v] ?? { color: 'default', label: v };
        return <Tag color={t.color}>{t.label}</Tag>;
      },
    },
    {
      title: 'Tài sản đang gắn', key: 'currentAsset', width: 240,
      render: (_: unknown, u: ComponentUnitDto) =>
        u.currentAsset ? `${u.currentAsset.assetTag} - ${u.currentAsset.name}` : '-',
    },
    {
      title: 'Cập nhật', dataIndex: 'updatedAt', key: 'updatedAt', width: 170,
      render: (v: string) => new Date(v).toLocaleString('vi-VN'),
    },
    {
      title: 'Thao tác', key: 'actions', width: 280,
      render: (_: unknown, u: ComponentUnitDto) => (
        <Space size={4}>
          {u.status === 'Allocated' && canCheckout && (
            <Popconfirm title="Thu hồi serial này?" onConfirm={() => void handleCheckin({ serialNo: u.serialNo ?? undefined })}>
              <Button size="small" icon={<RollbackOutlined />}>Thu hồi</Button>
            </Popconfirm>
          )}
          {u.status === 'InStock' && canEdit && (
            <Popconfirm title="Đánh dấu hư hỏng?" onConfirm={() => void handleUnitStatus(u, 'Damaged')}>
              <Button size="small">Hỏng</Button>
            </Popconfirm>
          )}
          {(u.status === 'InStock' || u.status === 'Damaged') && canEdit && (
            <Popconfirm title="Loại bỏ (dispose)?" onConfirm={() => void handleUnitStatus(u, 'Disposed')}>
              <Button size="small" danger>Loại bỏ</Button>
            </Popconfirm>
          )}
          <Button size="small" icon={<HistoryOutlined />} onClick={() => setHistoryUnit(u)}>Lịch sử</Button>
          {canDelete && (
            <Tooltip title={(u as ComponentUnitDto & { canDelete?: boolean }).canDelete === false ? 'Đã từng cấp phát — hãy dùng Loại bỏ thay vì xóa' : 'Xóa serial'}>
              <Popconfirm
                title="Xóa serial này?"
                disabled={(u as ComponentUnitDto & { canDelete?: boolean }).canDelete === false}
                onConfirm={() => void handleUnitDelete(u)}
              >
                <Button size="small" danger disabled={(u as ComponentUnitDto & { canDelete?: boolean }).canDelete === false}>Xóa</Button>
              </Popconfirm>
            </Tooltip>
          )}
        </Space>
      ),
    },
  ];

  const assignmentsColumns = [
    {
      title: 'Tài sản', key: 'asset',
      render: (_: unknown, r: { asset: { assetTag: string; name: string } }) => `${r.asset.assetTag} - ${r.asset.name}`,
    },
    { title: 'Số lượng', dataIndex: 'assignedQty', key: 'assignedQty', width: 100 },
    { title: 'Ghi chú', dataIndex: 'note', key: 'note', render: (v: string | null) => v || '-' },
  ];

  return (
    <div>
      <Space style={{ marginBottom: 16 }}>
        <Button icon={<ArrowLeftOutlined />} onClick={() => navigate('/components')}>Danh sách</Button>
        {canEdit && <Button icon={<EditOutlined />} onClick={() => setEditOpen(true)}>Sửa</Button>}
        {canDelete && (
          <Tooltip title={component.canDelete === false ? 'Đã từng được cấp phát — không thể xóa' : 'Xóa linh kiện'}>
            <Popconfirm
              title="Xóa linh kiện này?"
              disabled={component.canDelete === false}
              onConfirm={async () => {
                try {
                  await componentsApi.delete(component.id);
                  message.success('Đã xóa linh kiện');
                  navigate('/components');
                } catch (err: unknown) {
                  const e = err as { response?: { data?: { message?: string } } };
                  message.error(e?.response?.data?.message || 'Không thể xóa linh kiện');
                }
              }}
            >
              <Button danger disabled={component.canDelete === false}>Xóa</Button>
            </Popconfirm>
          </Tooltip>
        )}
      </Space>

      <Card style={{ marginBottom: 16 }}>
        <Space style={{ marginBottom: 12 }} align="center">
          <Title level={4} style={{ margin: 0 }}>{component.name}</Title>
          {component.trackingType === 'Serial' ? <Tag color="blue">Serial</Tag> : <Tag>Bulk</Tag>}
          {component.isLowStock && <Tag color="red">Sắp hết</Tag>}
        </Space>
        <Descriptions size="small" column={4} bordered>
          <Descriptions.Item label="Tổng (Qty)">{component.qty}</Descriptions.Item>
          <Descriptions.Item label="Còn lại">{component.remaining}</Descriptions.Item>
          {component.trackingType === 'Serial' && (
            <>
              <Descriptions.Item label="Đã cấp phát">{component.unitsSummary.allocated}</Descriptions.Item>
              <Descriptions.Item label="Hỏng / Loại bỏ">{component.unitsSummary.damaged + component.unitsSummary.disposed}</Descriptions.Item>
            </>
          )}
          <Descriptions.Item label="Ngưỡng">{component.minAmt}</Descriptions.Item>
          <Descriptions.Item label="Danh mục">
            {component.category ? <Tag>{component.category.name}</Tag> : <Tag color="warning">Chưa phân loại</Tag>}
          </Descriptions.Item>
          <Descriptions.Item label="Công ty">
            {component.company ? <Tag color="blue">{component.company.name}</Tag> : <Tag color="warning">Chưa xác định</Tag>}
          </Descriptions.Item>
          <Descriptions.Item label="Vị trí">{component.location?.name || '-'}</Descriptions.Item>
          <Descriptions.Item label="Nhà sản xuất">{component.manufacturer?.name || '-'}</Descriptions.Item>
          <Descriptions.Item label="Nhà cung cấp">{component.supplier?.name || '-'}</Descriptions.Item>
          <Descriptions.Item label="Model Number">{component.modelNumber || '-'}</Descriptions.Item>
          <Descriptions.Item label="Số đơn hàng">{component.orderNumber || '-'}</Descriptions.Item>
          <Descriptions.Item label="Ngày mua">{component.purchaseDate ? new Date(component.purchaseDate).toLocaleDateString('vi-VN') : '-'}</Descriptions.Item>
          <Descriptions.Item label="Giá mua">{component.purchaseCost != null ? component.purchaseCost.toLocaleString('vi-VN') : '-'}</Descriptions.Item>
          <Descriptions.Item label="Ghi chú" span={{ xs: 1, sm: 2 }}>{component.notes || '-'}</Descriptions.Item>
        </Descriptions>

        <Space style={{ marginTop: 16 }}>
          {component.trackingType === 'Serial' && canEdit && (
            <Button type="primary" icon={<InboxOutlined />} onClick={() => setStockInOpen(true)}>Nhập kho serial</Button>
          )}
          {canCheckout && <Button icon={<SendOutlined />} onClick={() => setCheckoutOpen(true)}>Cấp phát</Button>}
          {canCheckout && <Button icon={<RollbackOutlined />} onClick={() => setCheckinOpen(true)}>Thu hồi</Button>}
        </Space>
      </Card>

      <Tabs
        items={[
          ...(component.trackingType === 'Serial'
            ? [{
                key: 'units',
                label: `Đơn vị (${component.qty})`,
                children: (
                  <Card size="small">
                    <Space style={{ marginBottom: 12 }}>
                      <Select
                        allowClear
                        placeholder="Lọc theo trạng thái"
                        style={{ minWidth: 180 }}
                        value={statusFilter}
                        onChange={v => setStatusFilter(v)}
                        options={UNIT_STATUS_FILTERS}
                      />
                    </Space>
                    <Table
                      rowKey="id"
                      size="small"
                      loading={unitsLoading}
                      scroll={{ x: 'max-content' }}
                      columns={unitsColumns}
                      dataSource={units}
                      pagination={{
                        current: unitsPagination.current,
                        pageSize: unitsPagination.pageSize,
                        total: unitsPagination.total,
                        showSizeChanger: true,
                        onChange: (page, pageSize) => void loadUnits(page, pageSize, statusFilter),
                      }}
                      locale={{ emptyText: <Empty description="Chưa có serial nào" /> }}
                    />
                  </Card>
                ),
              }]
            : [{
                key: 'assignments',
                label: 'Phân bổ',
                children: (
                  <Card size="small">
                    <Table
                      rowKey="id"
                      size="small"
                      columns={assignmentsColumns}
                      scroll={{ x: 'max-content' }}
                      dataSource={component.assignments ?? []}
                      locale={{ emptyText: <Empty description="Chưa có phân bổ nào" /> }}
                    />
                  </Card>
                ),
              }]),
          {
            key: 'history',
            label: 'Lịch sử',
            children: (
              <Card size="small">
                <ActionLogTable
                  targetColumnTitle="Tài sản"
                  params={historyParams}
                  request={async (params) => {
                    try {
                      const res = await componentsApi.getActionLogs(component.id, {
                        page: params.current ?? 1,
                        pageSize: params.pageSize ?? 10,
                      });
                      return { data: (res.data?.data ?? []) as ActionLogRow[], success: true, total: res.data?.total ?? 0 };
                    } catch {
                      return { data: [], success: false, total: 0 };
                    }
                  }}
                />
              </Card>
            ),
          },
        ]}
      />

      <StockInModal open={stockInOpen} onClose={() => setStockInOpen(false)}
        onOk={(serials, note) => handleStockIn(serials, note)} />
      <CheckoutModal open={checkoutOpen} onClose={() => setCheckoutOpen(false)}
        trackingType={component.trackingType}
        inStockUnits={units.filter(u => u.status === 'InStock')}
        assets={assets}
        componentCompanyId={component.company?.id ?? null}
        onOk={(assetId, quantity, serialNo, note) => handleCheckout(assetId, quantity, serialNo, note)} />
      <ComponentFormModal
        open={editOpen}
        componentId={component.id}
        onClose={() => setEditOpen(false)}
        onSaved={() => {
          setEditOpen(false);
          refreshAll();
        }}
      />
      <CheckinModal open={checkinOpen} onClose={() => setCheckinOpen(false)}
        trackingType={component.trackingType}
        allocatedUnits={units.filter(u => u.status === 'Allocated')}
        assets={assets}
        onOk={data => handleCheckin(data)} />

      <Drawer
        title={historyUnit ? `Lịch sử serial ${historyUnit.serialNo ?? historyUnit.id}` : 'Lịch sử'}
        open={!!historyUnit}
        onClose={() => setHistoryUnit(null)}
        width={720}
      >
        {historyUnit && (
          <ActionLogTable
            targetColumnTitle="Tài sản"
            request={async (params) => {
              try {
                const res = await componentsApi.getUnitActionLogs(historyUnit.id, {
                  page: params.current ?? 1,
                  pageSize: params.pageSize ?? 10,
                });
                return { data: (res.data?.data ?? []) as ActionLogRow[], success: true, total: res.data?.total ?? 0 };
              } catch {
                return { data: [], success: false, total: 0 };
              }
            }}
          />
        )}
      </Drawer>
    </div>
  );
}

// ==================== Sub-modals ====================

interface StockInModalProps {
  open: boolean;
  onClose: () => void;
  onOk: (serials: string[], note?: string) => Promise<void>;
}

function StockInModal({ open, onClose, onOk }: StockInModalProps) {
  const [text, setText] = useState('');
  const [note, setNote] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const { message } = App.useApp();

  const serials = text.split('\n').map(s => s.trim()).filter(Boolean);
  const hasDuplicate = new Set(serials.map(s => s.toLowerCase())).size !== serials.length;

  const submit = async () => {
    if (serials.length === 0) { message.warning('Nhập ít nhất một serial'); return; }
    if (hasDuplicate) { message.warning('Có serial trùng nhau trong danh sách'); return; }
    setSubmitting(true);
    try {
      await onOk(serials, note.trim() || undefined);
      setText(''); setNote('');
    } catch (err: unknown) {
      const e = err as { response?: { data?: { message?: string } } };
      message.error(e?.response?.data?.message || 'Lỗi nhập kho');
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <Modal title="Nhập kho serial mới" open={open} onCancel={onClose} onOk={submit}
      okText="Nhập kho" cancelText="Hủy" confirmLoading={submitting} destroyOnClose width={560}>
      <Text type="secondary" style={{ display: 'block', marginBottom: 8 }}>
        Mỗi dòng một serial — có thể paste nhiều dòng cùng lúc.
      </Text>
      <Input.TextArea rows={8} value={text} onChange={e => setText(e.target.value)}
        placeholder={'SN-0001\nSN-0002\nSN-0003'} />
      <Space style={{ marginTop: 8 }} size="small">
        <Text type="secondary">Đã nhập: {serials.length} serial</Text>
        {hasDuplicate && <Tag color="red">Có serial trùng</Tag>}
      </Space>
      <Input style={{ marginTop: 12 }} placeholder="Ghi chú (không bắt buộc)" value={note} onChange={e => setNote(e.target.value)} />
    </Modal>
  );
}

interface CheckoutModalProps {
  open: boolean;
  onClose: () => void;
  trackingType: 'Bulk' | 'Serial';
  inStockUnits: ComponentUnitDto[];
  assets: AssetOption[];
  componentCompanyId?: string | null;
  onOk: (assetId: string, quantity: number, serialNo?: string, note?: string) => Promise<void>;
}

function CheckoutModal({ open, onClose, trackingType, inStockUnits, assets, componentCompanyId, onOk }: CheckoutModalProps) {
  const [assetId, setAssetId] = useState<string | undefined>(undefined);
  const [quantity, setQuantity] = useState<number>(1);
  const [serialNo, setSerialNo] = useState<string | undefined>(undefined);
  const [note, setNote] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const { message } = App.useApp();

  const serialOptions = inStockUnits.map(u => ({ label: u.serialNo ?? u.id, value: u.serialNo ?? u.id }));

  // Company scoping: only show assets of the SAME company as the component (Bulk + Serial).
  const visibleAssets = componentCompanyId
    ? assets.filter(a => a.companyId === componentCompanyId)
    : assets;
  const selectedAsset = assets.find(a => a.value === assetId);
  const crossCompany = !!(componentCompanyId && selectedAsset && selectedAsset.companyId !== componentCompanyId);

  const submit = async () => {
    if (!assetId) { message.warning('Chọn tài sản'); return; }
    if (crossCompany) { message.error('Tài sản thuộc công ty khác — không thể cấp phát'); return; }
    setSubmitting(true);
    try {
      await onOk(assetId, trackingType === 'Bulk' ? quantity : 1, serialNo, note.trim() || undefined);
      setAssetId(undefined); setSerialNo(undefined); setNote('');
    } catch (err: unknown) {
      const e = err as { response?: { data?: { message?: string } } };
      message.error(e?.response?.data?.message || 'Lỗi cấp phát');
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <Modal title="Cấp phát linh kiện" open={open} onCancel={onClose} onOk={submit}
      okText="Cấp phát" cancelText="Hủy" confirmLoading={submitting} destroyOnClose width={520}>
      {!componentCompanyId && (
        <Alert type="warning" style={{ marginBottom: 12 }}
          message="Linh kiện chưa xác định công ty — không thể cấp phát. Hãy bổ sung công ty trước (chỉ hiển thị trong form Sửa)." />
      )}
      {crossCompany && (
        <Alert type="error" style={{ marginBottom: 12 }}
          message="Tài sản thuộc công ty khác với linh kiện — không thể cấp phát." />
      )}
      <Text strong style={{ display: 'block', marginBottom: 8 }}>Tài sản nhận *</Text>
      <Select showSearch style={{ width: '100%' }} placeholder="Chọn tài sản..."
        options={visibleAssets} value={assetId} onChange={setAssetId}
        filterOption={(input, option) => (option?.label as string)?.toLowerCase().includes(input.toLowerCase())} />
      {trackingType === 'Bulk' ? (
        <>
          <Text strong style={{ display: 'block', marginBottom: 8, marginTop: 12 }}>Số lượng *</Text>
          <InputNumber min={1} value={quantity} onChange={v => setQuantity(v ?? 1)} style={{ width: '100%' }} />
        </>
      ) : (
        <>
          <Text strong style={{ display: 'block', marginBottom: 8, marginTop: 12 }}>Serial (để trống = tự chọn theo FIFO)</Text>
          <Select showSearch allowClear style={{ width: '100%' }} placeholder="Chọn serial..."
            options={serialOptions} value={serialNo} onChange={setSerialNo}
            filterOption={(input, option) => (option?.label as string)?.toLowerCase().includes(input.toLowerCase())} />
          {inStockUnits.length === 0 && <Tag color="red" style={{ marginTop: 8 }}>Không còn serial trong kho</Tag>}
        </>
      )}
      <Input style={{ marginTop: 12 }} placeholder="Ghi chú (không bắt buộc)" value={note} onChange={e => setNote(e.target.value)} />
    </Modal>
  );
}

interface CheckinModalProps {
  open: boolean;
  onClose: () => void;
  trackingType: 'Bulk' | 'Serial';
  allocatedUnits: ComponentUnitDto[];
  assets: AssetOption[];
  onOk: (data: { assetId?: string; quantity?: number; serialNo?: string; note?: string }) => Promise<void>;
}

function CheckinModal({ open, onClose, trackingType, allocatedUnits, assets, onOk }: CheckinModalProps) {
  const [assetId, setAssetId] = useState<string | undefined>(undefined);
  const [quantity, setQuantity] = useState<number>(1);
  const [serialNo, setSerialNo] = useState<string | undefined>(undefined);
  const [note, setNote] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const { message } = App.useApp();

  const allocatedOptions = allocatedUnits.map(u => ({
    label: u.currentAsset ? `${u.serialNo ?? u.id} (${u.currentAsset.assetTag})` : (u.serialNo ?? u.id),
    value: u.serialNo ?? u.id,
  }));

  const submit = async () => {
    if (trackingType === 'Bulk' && !assetId) { message.warning('Chọn tài sản'); return; }
    if (trackingType === 'Serial' && !serialNo && allocatedUnits.length === 0) { message.warning('Không có serial đang cấp phát'); return; }
    setSubmitting(true);
    try {
      await onOk({
        assetId: trackingType === 'Bulk' ? assetId : undefined,
        quantity: trackingType === 'Bulk' ? quantity : undefined,
        serialNo: trackingType === 'Serial' ? serialNo : undefined,
        note: note.trim() || undefined,
      });
      setAssetId(undefined); setSerialNo(undefined); setNote('');
    } catch (err: unknown) {
      const e = err as { response?: { data?: { message?: string } } };
      message.error(e?.response?.data?.message || 'Lỗi thu hồi');
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <Modal title="Thu hồi linh kiện" open={open} onCancel={onClose} onOk={submit}
      okText="Thu hồi" cancelText="Hủy" confirmLoading={submitting} destroyOnClose width={520}>
      {trackingType === 'Bulk' ? (
        <>
          <Text strong style={{ display: 'block', marginBottom: 8 }}>Tài sản trả về *</Text>
          <Select showSearch style={{ width: '100%' }} placeholder="Chọn tài sản..."
            options={assets} value={assetId} onChange={setAssetId}
            filterOption={(input, option) => (option?.label as string)?.toLowerCase().includes(input.toLowerCase())} />
          <Text strong style={{ display: 'block', marginBottom: 8, marginTop: 12 }}>Số lượng *</Text>
          <InputNumber min={1} value={quantity} onChange={v => setQuantity(v ?? 1)} style={{ width: '100%' }} />
        </>
      ) : (
        <>
          <Text strong style={{ display: 'block', marginBottom: 8 }}>Serial (để trống = tự chọn theo asset gần nhất)</Text>
          <Select showSearch allowClear style={{ width: '100%' }} placeholder="Chọn serial đang cấp phát..."
            options={allocatedOptions} value={serialNo} onChange={setSerialNo}
            filterOption={(input, option) => (option?.label as string)?.toLowerCase().includes(input.toLowerCase())} />
          {allocatedUnits.length === 0 && <Tag color="red" style={{ marginTop: 8 }}>Không có serial đang cấp phát</Tag>}
        </>
      )}
      <Input style={{ marginTop: 12 }} placeholder="Ghi chú (không bắt buộc)" value={note} onChange={e => setNote(e.target.value)} />
    </Modal>
  );
}



