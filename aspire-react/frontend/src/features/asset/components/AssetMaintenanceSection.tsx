import { useCallback, useEffect, useState } from 'react';
import {
  App, Badge, Button, DatePicker, Descriptions, Divider, Form, Input, InputNumber,
  Modal, Popconfirm, Select, Space, Spin, Switch, Table, Tag, Tooltip,
} from 'antd';
import { PlusOutlined, CheckOutlined, CloseOutlined, EditOutlined, LockOutlined, UnlockOutlined } from '@ant-design/icons';
import apiClient from '../../../services/api-client';
import { assetService, type AssetMaintenanceDto, type CreateMaintenancePayload } from '../services/asset.service';
import { usePermission } from '../../../hooks/usePermission';
import { isSuperUser } from '../../../services/keycloak';
import dayjs from 'dayjs';
import {
  MAINTENANCE_STATUS_COLORS, MAINTENANCE_TYPE_LABELS, MAINTENANCE_TYPE_VALUE,
  MAINTENANCE_TYPE_COLORS, MAINTENANCE_TYPE_OPTIONS,
} from '../../maintenance/components/MaintenanceTable';
import { formatDate, formatDateTime, formatMoney } from '../../../utils/format';

// Compares context by ID (not display name) — the Snapshot field gets an orange "Đã thay đổi" marker
// when the LIVE context differs from what was captured at maintenance-creation time.
function contextChanged(snapshotId: string | null, currentId: string | null | undefined): boolean {
  return !!currentId && currentId !== snapshotId;
}

export default function AssetMaintenanceSection({ assetId }: { assetId: string }) {
  const { message, modal } = App.useApp();
  const canDeleteMaintenance = usePermission('assets.edit');
  // ST6b — Reopen is Superuser-only (backend Reopen checks IsSuperUser in body), NOT assets.edit.
  const superUser = isSuperUser();
  const [form] = Form.useForm();

  const [items, setItems] = useState<AssetMaintenanceDto[]>([]);
  const [loading, setLoading] = useState(false);
  const [pagination, setPagination] = useState({ current: 1, pageSize: 10, total: 0 });

  const [formOpen, setFormOpen] = useState(false);
  const [editing, setEditing] = useState<AssetMaintenanceDto | null>(null);
  const [detail, setDetail] = useState<AssetMaintenanceDto | null>(null);
  const [closedByUserName, setClosedByUserName] = useState<string | null>(null);

  const [suppliers, setSuppliers] = useState<{ label: string; value: string }[]>([]);
  const [newSupplierName, setNewSupplierName] = useState('');
  const [userOptions, setUserOptions] = useState<{ label: string; value: string }[]>([]);
  const [userLoading, setUserLoading] = useState(false);

  const fetch = useCallback(async (page = 1, pageSize = 10) => {
    setLoading(true);
    try {
      const res = await assetService.listAllMaintenances({ assetId, page, pageSize });
      setItems(res.data.data as AssetMaintenanceDto[]);
      setPagination(p => ({ ...p, current: page, pageSize, total: res.data.pagination?.totalItems ?? 0 }));
    } catch {
      message.error('Không thể tải lịch sử bảo trì');
    } finally {
      setLoading(false);
    }
  }, [assetId, message]);

  useEffect(() => { void fetch(); }, [fetch]);

  useEffect(() => {
    apiClient.get('/suppliers')
      .then(r => {
        const list = (r.data?.data ?? []) as { id: string; name: string }[];
        setSuppliers(list.map(s => ({ label: s.name, value: s.id })));
      })
      .catch(() => { /* non-critical */ });
  }, []);

  const addSupplier = async () => {
    const name = newSupplierName.trim();
    if (!name) { message.warning('Nhập tên nhà cung cấp mới'); return; }
    try {
      const res = await apiClient.post('/suppliers', { name, code: name.slice(0, 5).toUpperCase() });
      const created = res.data?.data as { id: string; name: string };
      setSuppliers(o => [...o, { label: created.name, value: created.id }]);
      form.setFieldValue('supplierId', created.id);
      setNewSupplierName('');
    } catch (err: unknown) {
      const e = err as { response?: { data?: { message?: string } } };
      message.error(e?.response?.data?.message || 'Không thể tạo nhà cung cấp');
    }
  };

  const loadAssigneeUsers = async () => {
    setUserLoading(true);
    try {
      let companyId: string | undefined;
      try {
        const a = await assetService.get(assetId);
        companyId = (a.data?.data as { companyId?: string | null })?.companyId ?? undefined;
      } catch { /* non-critical */ }
      const params: Record<string, unknown> = { pageSize: 500 };
      if (companyId) params.companyId = companyId;
      const res = await apiClient.get('/users', { params });
      const users = (res.data?.data ?? []) as {
        id: string; firstName: string; lastName: string; username: string; companyId: string | null;
      }[];
      setUserOptions(users
        .filter(u => !companyId || u.companyId === companyId)
        .map(u => ({ label: [u.firstName, u.lastName].filter(Boolean).join(' ') || u.username, value: u.id })));
    } catch {
      setUserOptions([]);
    } finally {
      setUserLoading(false);
    }
  };

  const openCreate = () => {
    setEditing(null);
    form.resetFields();
    form.setFieldsValue({ type: 1, isWarranty: false });
    void loadAssigneeUsers();
    setFormOpen(true);
  };

  const openEdit = (m: AssetMaintenanceDto) => {
    setEditing(m);
    form.setFieldsValue({
      type: MAINTENANCE_TYPE_VALUE[m.type] ?? 1,
      title: m.title,
      notes: m.notes,
      supplierId: m.supplier?.id,
      startDate: dayjs(m.startDate),
      completionDate: m.completionDate ? dayjs(m.completionDate) : undefined,
      cost: m.cost,
      isWarranty: m.isWarranty,
      assigneeUserIds: (m.assignees ?? []).map(a => a.userId),
    });
    void loadAssigneeUsers();
    setFormOpen(true);
  };

  const submit = async (vals: Record<string, unknown>) => {
    try {
      if (editing) {
        // Whitelist only — StartDate & snapshot fields are locked (not sent).
        const payload: Record<string, unknown> = {
          type: vals.type as number,
          title: vals.title as string,
          notes: (vals.notes as string) || null,
          supplierId: vals.supplierId ? (vals.supplierId as string) : null,
          completionDate: vals.completionDate ? dayjs(vals.completionDate as dayjs.Dayjs).toISOString() : null,
          cost: typeof vals.cost === 'number' ? vals.cost : null,
          isWarranty: !!vals.isWarranty,
          assigneeUserIds: (vals.assigneeUserIds as string[] | undefined) ?? undefined,
        };
        await assetService.updateMaintenance(editing.id, payload);
        message.success('Đã cập nhật bảo trì');
      } else {
        const payload: CreateMaintenancePayload = {
          type: vals.type as number,
          title: vals.title as string,
          notes: (vals.notes as string) || undefined,
          supplierId: vals.supplierId ? (vals.supplierId as string) : undefined,
          startDate: dayjs(vals.startDate as dayjs.Dayjs).toISOString(),
          completionDate: vals.completionDate ? dayjs(vals.completionDate as dayjs.Dayjs).toISOString() : null,
          cost: typeof vals.cost === 'number' ? vals.cost : null,
          isWarranty: !!vals.isWarranty,
          assigneeUserIds: (vals.assigneeUserIds as string[] | undefined) ?? undefined,
        };
        await assetService.createMaintenance(assetId, payload);
        message.success('Đã tạo bảo trì');
      }
      setFormOpen(false);
      void fetch();
    } catch (err: unknown) {
      const e = err as { response?: { data?: { message?: string } } };
      message.error(e?.response?.data?.message || 'Lỗi lưu bảo trì');
    }
  };

  const handleDelete = async (m: AssetMaintenanceDto) => {
    try {
      await assetService.deleteMaintenance(m.id);
      message.success('Đã xóa bảo trì');
      void fetch();
    } catch (err: unknown) {
      const e = err as { response?: { data?: { message?: string } } };
      message.error(e?.response?.data?.message || 'Không thể xóa bảo trì');
    }
  };

  // Detail comes from GET /maintenances/{id} (list rows do not carry currentContext).
  const openDetail = async (m: AssetMaintenanceDto) => {
    try {
      const res = await assetService.getMaintenance(m.id);
      const d = (res.data?.data ?? m) as AssetMaintenanceDto;
      setDetail(d);
      if (d.isClosed && d.closedById) {
        try {
          const u = await apiClient.get(`/users/${d.closedById}`);
          const name = u.data?.data?.username ?? u.data?.data?.firstName;
          setClosedByUserName(typeof name === 'string' ? name : null);
        } catch {
          setClosedByUserName(null);
        }
      } else {
        setClosedByUserName(null);
      }
    } catch {
      setDetail(m);
      setClosedByUserName(null);
    }
  };

  const handleClose = async (m: AssetMaintenanceDto) => {
    try {
      await assetService.closeMaintenance(m.id);
      message.success('Đã đóng bảo trì (khóa mọi chỉnh sửa)');
      void fetch();
    } catch (err: unknown) {
      const e = err as { response?: { data?: { message?: string } } };
      message.error(e?.response?.data?.message || 'Không thể đóng bảo trì');
    }
  };

  const handleInspect = async (m: AssetMaintenanceDto) => {
    try {
      await assetService.inspectMaintenance(m.id);
      message.success('Đã đánh dấu đã kiểm tra');
      void fetch();
    } catch (err: unknown) {
      const e = err as { response?: { data?: { message?: string } } };
      message.error(e?.response?.data?.message || 'Không thể đánh dấu đã kiểm tra');
    }
  };

  const handleReopen = (m: AssetMaintenanceDto) => {
    modal.confirm({
      title: 'Mở lại bản ghi bảo trì?',
      content: 'Hành động này phá bỏ khóa audit — bản ghi sẽ có thể chỉnh sửa lại. Chỉ Superuser được phép và thao tác sẽ được ghi vào nhật ký (ActionLog).',
      okText: 'Mở lại',
      okButtonProps: { danger: true },
      cancelText: 'Hủy',
      onOk: async () => {
        try {
          await assetService.reopenMaintenance(m.id);
          message.success('Đã mở lại bảo trì');
          void fetch();
        } catch {
          message.error('Không thể mở lại bảo trì');
        }
      },
    });
  };

  const inProgressCount = items.filter(i => !i.completionDate).length;

  const columns = [
    {
      title: 'Loại', key: 'type', width: 150,
      render: (_: unknown, r: AssetMaintenanceDto) => <Tag color={MAINTENANCE_TYPE_COLORS[MAINTENANCE_TYPE_VALUE[r.type] ?? 1]}>{MAINTENANCE_TYPE_LABELS[MAINTENANCE_TYPE_VALUE[r.type] ?? 0] ?? r.type}</Tag>,
    },
    { title: 'Tiêu đề', dataIndex: 'title', key: 'title' },
    {
      title: 'Ngày bắt đầu', dataIndex: 'startDate', key: 'startDate', width: 120,
      render: (v: string) => formatDate(v),
    },
    {
      title: 'Ngày hoàn thành', dataIndex: 'completionDate', key: 'completionDate', width: 160,
      render: (_: unknown, r: AssetMaintenanceDto) => (
        <Space size={4} wrap>
          {r.completionDate ? formatDate(r.completionDate) : <Tag color={MAINTENANCE_STATUS_COLORS.in_progress}>Đang thực hiện</Tag>}
          {r.isClosed && <Tag color={MAINTENANCE_STATUS_COLORS.closed} icon={<LockOutlined />}>Đã đóng</Tag>}
        </Space>
      ),
    },
    {
      title: 'Chi phí', dataIndex: 'cost', key: 'cost', width: 140,
      render: (v: number | null) => formatMoney(v),
    },
    {
      title: 'Nhà cung cấp', key: 'supplier', width: 160,
      render: (_: unknown, r: AssetMaintenanceDto) => r.supplier?.name || '-',
    },
    {
      title: 'Bảo hành', dataIndex: 'isWarranty', key: 'isWarranty', width: 90,
      render: (v: boolean) => v ? <Tag color="green" icon={<CheckOutlined />}>Có</Tag> : <Tag icon={<CloseOutlined />}>Không</Tag>,
    },
    {
      title: 'Thao tác', key: 'actions', width: 340,
      render: (_: unknown, r: AssetMaintenanceDto) => (
        <Space size="small" wrap>
          <Button size="small" onClick={() => void openDetail(r)}>Chi tiết</Button>
          {r.isClosed
            ? (
              <Tooltip title="Bản ghi đã đóng, không thể sửa">
                <Button size="small" disabled icon={<EditOutlined />}>Sửa</Button>
              </Tooltip>
            )
            : canDeleteMaintenance && <Button size="small" icon={<EditOutlined />} onClick={() => openEdit(r)}>Sửa</Button>}
          {!r.isClosed && r.completionDate && (
            r.inspectedById
              ? <Tag color="green" icon={<CheckOutlined />}>Đã kiểm tra</Tag>
              : canDeleteMaintenance && <Button size="small" icon={<CheckOutlined />} onClick={() => void handleInspect(r)}>Đánh dấu đã kiểm tra</Button>
          )}
          {!r.isClosed && canDeleteMaintenance && (r.completionDate && r.inspectedById
            ? (
              <Popconfirm
                title="Đóng bản ghi bảo trì này?"
                description="Sau khi đóng, bản ghi sẽ bị khóa và không thể chỉnh sửa (khóa audit)."
                onConfirm={() => void handleClose(r)}
              >
                <Button size="small" icon={<LockOutlined />}>Đóng</Button>
              </Popconfirm>
            )
            : (
              <Tooltip title={r.completionDate ? 'Cần kiểm tra trước khi đóng bảo trì' : 'Cần nhập Ngày hoàn thành trước khi đóng bảo trì'}>
                <Button size="small" disabled icon={<LockOutlined />}>Đóng</Button>
              </Tooltip>
            ))}
          {superUser && r.isClosed && (
            <Button size="small" icon={<UnlockOutlined />} onClick={() => void handleReopen(r)}>Mở lại</Button>
          )}
          {canDeleteMaintenance && (
            <Popconfirm title="Xóa bản ghi bảo trì này?" onConfirm={() => void handleDelete(r)}>
              <Button size="small" danger>Xóa</Button>
            </Popconfirm>
          )}
        </Space>
      ),
    },
  ];

  return (
    <div>
      <Space style={{ marginBottom: 12 }}>
        <Badge count={inProgressCount} color={MAINTENANCE_STATUS_COLORS.in_progress} overflowCount={99}>
          <Tag color={MAINTENANCE_STATUS_COLORS.in_progress}>Đang thực hiện</Tag>
        </Badge>
        {canDeleteMaintenance && <Button type="primary" icon={<PlusOutlined />} onClick={openCreate}>Thêm bảo trì</Button>}
      </Space>
      <Table
        rowKey="id"
        size="small"
        columns={columns}
        dataSource={items}
        loading={loading}
        scroll={{ x: 'max-content' }}
        pagination={{
          current: pagination.current,
          pageSize: pagination.pageSize,
          total: pagination.total,
          showSizeChanger: true,
          onChange: (page, pageSize) => void fetch(page, pageSize),
        }}
      />

      {/* ── Form Modal (create/edit) ── */}
      <Modal
        open={formOpen}
        title={editing ? 'Sửa bảo trì' : 'Thêm bảo trì'}
        onCancel={() => setFormOpen(false)}
        onOk={form.submit}
        confirmLoading={false}
        destroyOnClose
        width={600}
        okText={editing ? 'Cập nhật' : 'Tạo mới'}
        cancelText="Hủy"
      >
        <Form form={form} layout="vertical" onFinish={(vals) => void submit(vals)}>
          <Form.Item label="Loại" name="type" rules={[{ required: true }]}>
            <Select options={MAINTENANCE_TYPE_OPTIONS} />
          </Form.Item>
          <Form.Item label="Tiêu đề" name="title" rules={[{ required: true, message: 'Nhập tiêu đề' }]}>
            <Input placeholder="VD: Bảo trì định kỳ tháng 6" />
          </Form.Item>
          {!editing && (
            <Form.Item label="Ngày bắt đầu" name="startDate" rules={[{ required: true, message: 'Chọn ngày bắt đầu' }]}>
              <DatePicker style={{ width: '100%' }} format="DD/MM/YYYY" placeholder="Chọn ngày" />
            </Form.Item>
          )}
          <Form.Item label="Ngày hoàn thành" name="completionDate"
            extra="Để trống nếu đang thực hiện (sẽ hiển thị tag 'Đang thực hiện')">
            <DatePicker style={{ width: '100%' }} format="DD/MM/YYYY" placeholder="Chọn ngày" />
          </Form.Item>
          <Form.Item label="Chi phí (VND)" name="cost">
            <InputNumber min={0} precision={2} style={{ width: '100%' }} placeholder="0" />
          </Form.Item>
          <Form.Item label="Nhà cung cấp thực hiện" name="supplierId">
            <Select showSearch allowClear placeholder="Chọn nhà cung cấp..." options={suppliers}
              filterOption={(input, option) => (option?.label as string)?.toLowerCase().includes(input.toLowerCase())}
              dropdownRender={(menu) => (
                <>
                  {menu}
                  <Divider style={{ margin: '8px 0' }} />
                  <Space style={{ padding: '0 8px 4px' }}>
                    <Input placeholder="Tên NCC mới" value={newSupplierName}
                      onChange={e => setNewSupplierName(e.target.value)}
                      onPressEnter={() => void addSupplier()} style={{ width: 200 }} />
                    <Button type="text" icon={<PlusOutlined />} onClick={() => void addSupplier()}>Thêm</Button>
                  </Space>
                </>
              )} />
          </Form.Item>
          <Form.Item label="Thuộc bảo hành" name="isWarranty" valuePropName="checked">
            <Switch />
          </Form.Item>
          <Form.Item label="Ghi chú" name="notes">
            <Input.TextArea rows={3} placeholder="Mô tả công việc bảo trì..." />
          </Form.Item>
          <Form.Item label="Người phụ trách" name="assigneeUserIds" extra="Tối đa 5 người — người thực hiện sửa chữa">
            <Select
              mode="multiple"
              showSearch
              loading={userLoading}
              maxCount={5}
              optionFilterProp="label"
              placeholder="Chọn người phụ trách (tối đa 5)"
              options={userOptions}
              notFoundContent={userLoading ? <Spin size="small" /> : 'Không có người dùng phù hợp'}
            />
          </Form.Item>
        </Form>
      </Modal>

      {/* ── Detail Modal (with context snapshot) ── */}
      <Modal open={!!detail} title={detail?.title} onCancel={() => setDetail(null)} footer={null} width={640}>
        {detail && (
          <>
            <Descriptions bordered size="small" column={2}>
              <Descriptions.Item label="Loại"><Tag color={MAINTENANCE_TYPE_COLORS[MAINTENANCE_TYPE_VALUE[detail.type] ?? 1]}>{MAINTENANCE_TYPE_LABELS[MAINTENANCE_TYPE_VALUE[detail.type] ?? 0] ?? detail.type}</Tag></Descriptions.Item>
              <Descriptions.Item label="Trạng thái">
                <Space size={4}>
                  {detail.completionDate ? <Tag color={MAINTENANCE_STATUS_COLORS.completed}>Đã hoàn thành</Tag> : <Tag color={MAINTENANCE_STATUS_COLORS.in_progress}>Đang thực hiện</Tag>}
                  {detail.isClosed && <Tag color={MAINTENANCE_STATUS_COLORS.closed} icon={<LockOutlined />}>Đã đóng</Tag>}
                </Space>
              </Descriptions.Item>
              <Descriptions.Item label="Ngày bắt đầu">{formatDate(detail.startDate)}</Descriptions.Item>
              <Descriptions.Item label="Ngày hoàn thành">{formatDate(detail.completionDate)}</Descriptions.Item>
              <Descriptions.Item label="Chi phí">{formatMoney(detail.cost)}</Descriptions.Item>
              <Descriptions.Item label="Thuộc bảo hành">{detail.isWarranty ? 'Có' : 'Không'}</Descriptions.Item>
              <Descriptions.Item label="Nhà cung cấp" span={2}>{detail.supplier?.name || '-'}</Descriptions.Item>
              <Descriptions.Item label="Ghi chú" span={2}>{detail.notes || '-'}</Descriptions.Item>
              <Descriptions.Item label="Người phụ trách" span={2}>
                {(detail.assignees?.length ?? 0) > 0
                  ? (
                    <Space size={[4, 4]} wrap>
                      {detail.assignees!.map(a => <Tag key={a.userId} color="blue" style={{ marginInlineEnd: 0 }}>{a.name}</Tag>)}
                    </Space>
                  )
                  : <Tag color="default">Chưa phân công</Tag>}
              </Descriptions.Item>
              <Descriptions.Item label="Đã kiểm tra" span={2}>
                {detail.inspectedById
                  ? <span><CheckOutlined style={{ color: MAINTENANCE_STATUS_COLORS.completed }} /> {detail.inspectedByName || 'Đã kiểm tra'} lúc {formatDateTime(detail.inspectedAt)}</span>
                  : detail.completionDate
                    ? <Button size="small" icon={<CheckOutlined />} onClick={() => void handleInspect(detail)}>Đánh dấu đã kiểm tra</Button>
                    : (
                      <Tooltip title="Cần nhập Ngày hoàn thành trước khi kiểm tra bảo trì">
                        <Button size="small" disabled icon={<CheckOutlined />}>Đánh dấu đã kiểm tra</Button>
                      </Tooltip>
                    )}
              </Descriptions.Item>
              {detail.isClosed && (
                <Descriptions.Item label="Đã đóng" span={2}>
                  <Tag color="default" icon={<LockOutlined />}>Đã đóng</Tag>{' '}
                  lúc {formatDateTime(detail.closedAt)}
                  {closedByUserName
                    ? <> bởi <b>{closedByUserName}</b></>
                    : detail.closedById
                      ? <> (id: {detail.closedById.slice(0, 8)}…)</>
                      : null}
                </Descriptions.Item>
              )}
            </Descriptions>

            <Divider titlePlacement="start" plain style={{ marginTop: 16 }}>Ngữ cảnh tại thời điểm bảo trì (ảnh chụp nhanh)</Divider>
            <Descriptions bordered size="small" column={1}>
              <Descriptions.Item label="Hệ thống (System)">
                {detail.snapshotSystemInfoName || <Tag color="default">Chưa xác định</Tag>}
                {contextChanged(detail.snapshotSystemInfoId, detail.currentContext?.systemInfoId)
                  && <Tag color="orange" style={{ marginLeft: 8 }}>Đã thay đổi</Tag>}
              </Descriptions.Item>
              <Descriptions.Item label="Vị trí trong hệ thống (SystemPosition)">
                {detail.snapshotSystemPositionName || <Tag color="default">Chưa xác định</Tag>}
                {contextChanged(detail.snapshotSystemPositionId, detail.currentContext?.systemPositionId)
                  && <Tag color="orange" style={{ marginLeft: 8 }}>Đã thay đổi</Tag>}
              </Descriptions.Item>
              <Descriptions.Item label="Vị trí lưu kho (Location)">
                {detail.snapshotLocationName || <Tag color="default">Chưa xác định</Tag>}
                {contextChanged(detail.snapshotLocationId, detail.currentContext?.locationId)
                  && <Tag color="orange" style={{ marginLeft: 8 }}>Đã thay đổi</Tag>}
              </Descriptions.Item>
              <Descriptions.Item label="Người dùng">
                {detail.snapshotAssignedUserName || <Tag color="default">Chưa xác định</Tag>}
                {contextChanged(detail.snapshotAssignedUserId, detail.currentContext?.assignedUserId)
                  && <Tag color="orange" style={{ marginLeft: 8 }}>Đã thay đổi</Tag>}
              </Descriptions.Item>
              <Descriptions.Item label="Phòng ban">
                {detail.snapshotDepartmentName || <Tag color="default">Chưa xác định</Tag>}
                {contextChanged(detail.snapshotDepartmentId, detail.currentContext?.departmentId)
                  && <Tag color="orange" style={{ marginLeft: 8 }}>Đã thay đổi</Tag>}
              </Descriptions.Item>
            </Descriptions>

            <Divider titlePlacement="start" plain style={{ marginTop: 16 }}>Ngữ cảnh hiện tại (dữ liệu sống)</Divider>
            <Descriptions bordered size="small" column={1}>
              <Descriptions.Item label="Hệ thống (System)">{detail.currentContext?.systemInfoName || <Tag color="default">Chưa xác định</Tag>}</Descriptions.Item>
              <Descriptions.Item label="Vị trí trong hệ thống (SystemPosition)">{detail.currentContext?.systemPositionName || <Tag color="default">Chưa xác định</Tag>}</Descriptions.Item>
              <Descriptions.Item label="Vị trí lưu kho (Location)">{detail.currentContext?.locationName || <Tag color="default">Chưa xác định</Tag>}</Descriptions.Item>
              <Descriptions.Item label="Người dùng">{detail.currentContext?.assignedUserName || <Tag color="default">Chưa xác định</Tag>}</Descriptions.Item>
              <Descriptions.Item label="Phòng ban">{detail.currentContext?.departmentName || <Tag color="default">Chưa xác định</Tag>}</Descriptions.Item>
            </Descriptions>
          </>
        )}
      </Modal>
    </div>
  );
}
