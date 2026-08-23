import { useRef, useState } from 'react';
import {
  App, Button, Col, DatePicker, Form, Input, InputNumber, Modal, Row, Select, Spin, Switch,
} from 'antd';
import { PlusOutlined } from '@ant-design/icons';
import type { ActionType } from '@ant-design/pro-components';
import dayjs from 'dayjs';
import apiClient from '../../../services/api-client';
import { assetService, type CreateMaintenanceForAssetPayload } from '../../asset/services/asset.service';
import { usePermission } from '../../../hooks/usePermission';
import { useIsMobile } from '../../../hooks/useIsMobile';
import MaintenanceTable, { MAINTENANCE_TYPE_OPTIONS } from '../components/MaintenanceTable';
import { isSuperUser } from '../../../services/keycloak';
import CompanyTreeSelect from '../../../components/common/CompanyTreeSelect';

interface AssetOption {
  label: string;
  value: string;
  companyId: string | null;
}

export default function MaintenanceListPage() {
  const { message } = App.useApp();
  const isMobile = useIsMobile();
  const actionRef = useRef<ActionType | undefined>(undefined);
  const superUser = isSuperUser();
  // ST6b — create maintenance requires assets.edit (backend POST maintenances).
  const canCreateMaintenance = usePermission('assets.edit');

  const [form] = Form.useForm();
  const [createOpen, setCreateOpen] = useState(false);
  const [submitting, setSubmitting] = useState(false);

  // Asset picker state
  const [assetOptions, setAssetOptions] = useState<AssetOption[]>([]);
  const [assetLoading, setAssetLoading] = useState(false);
  const [companyFilter, setCompanyFilter] = useState<string | undefined>(undefined);
  const [suppliers, setSuppliers] = useState<{ label: string; value: string }[]>([]);
  const [userOptions, setUserOptions] = useState<{ label: string; value: string }[]>([]);
  const [userLoading, setUserLoading] = useState(false);

  // Loads users for the assignee picker — filtered by the SELECTED ASSET's company (same principle
  // as the accessory/consumable checkout modals: "chỉ hiển thị người dùng thuộc công ty...").
  const loadAssigneeUsers = async (companyId: string | null | undefined) => {
    setUserLoading(true);
    try {
      const params: Record<string, unknown> = { pageSize: 500 };
      if (companyId) params.companyId = companyId;
      const res = await apiClient.get('/users', { params });
      const users = (res.data?.data ?? []) as {
        id: string; firstName: string; lastName: string; username: string; companyId: string | null;
      }[];
      const options = users
        .filter(u => !companyId || u.companyId === companyId)
        .map(u => ({
          label: `${[u.firstName, u.lastName].filter(Boolean).join(' ') || u.username}`,
          value: u.id,
        }));
      setUserOptions(options);
    } catch {
      setUserOptions([]);
    } finally {
      setUserLoading(false);
    }
  };

  const loadPickerData = async () => {
    setAssetLoading(true);
    try {
      const [assetsRes, suppliersRes] = await Promise.all([
        assetService.list({ page: 1, pageSize: 500 }),
        apiClient.get('/suppliers', { params: { pageSize: 500 } }),
      ]);
      const assets = (assetsRes.data?.data ?? []) as {
        id: string; assetTag: string; name: string;
        company?: { id: string; name: string } | null;
      }[];
      setAssetOptions(assets.map(a => ({
        label: `${a.name} (${a.assetTag})`,
        value: a.id,
        companyId: a.company?.id ?? null,
      })));
      const supList = (suppliersRes.data?.data ?? []) as { id: string; name: string }[];
      setSuppliers(supList.map(s => ({ label: s.name, value: s.id })));
    } catch {
      message.error('Không thể tải danh sách tài sản/nhà cung cấp');
    } finally {
      setAssetLoading(false);
    }
  };

  const openCreate = async () => {
    form.resetFields();
    form.setFieldsValue({ type: 1, isWarranty: false });
    setCompanyFilter(undefined);
    await loadPickerData();
    setCreateOpen(true);
  };

  const filteredAssetOptions = companyFilter
    ? assetOptions.filter(o => o.companyId === companyFilter)
    : assetOptions;

  const submitCreate = async (vals: Record<string, unknown>) => {
    if (!vals.assetId) {
      message.warning('Vui lòng chọn tài sản cần bảo trì');
      return;
    }
    const payload: CreateMaintenanceForAssetPayload = {
      assetId: vals.assetId as string,
      type: vals.type as number,
      title: (vals.title as string)?.trim(),
      notes: (vals.notes as string) || undefined,
      supplierId: vals.supplierId as string | undefined,
      startDate: (vals.startDate as dayjs.Dayjs).toISOString(),
      completionDate: vals.completionDate ? (vals.completionDate as dayjs.Dayjs).toISOString() : null,
      cost: vals.cost != null ? Number(vals.cost) : null,
      isWarranty: Boolean(vals.isWarranty),
      assigneeUserIds: (vals.assigneeUserIds as string[] | undefined) ?? undefined,
    };
    setSubmitting(true);
    try {
      await assetService.createMaintenanceForAsset(payload);
      message.success('Đã tạo bản ghi bảo trì');
      setCreateOpen(false);
      actionRef.current?.reload();
    } catch (err: unknown) {
      const e = err as { response?: { data?: { message?: string } } };
      message.error(e?.response?.data?.message || 'Không thể tạo bản ghi bảo trì');
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <>
      <MaintenanceTable
        actionRef={actionRef}
        createButton={
          canCreateMaintenance ? (
            <Button key="add" type="primary" icon={<PlusOutlined />} onClick={() => void openCreate()}>
              Thêm bảo trì
            </Button>
          ) : undefined
        }
      />

      {/* ─── Create modal ─── */}
      <Modal
        title="Thêm bảo trì"
        open={createOpen}
        onCancel={() => setCreateOpen(false)}
        onOk={() => form.submit()}
        confirmLoading={submitting}
        width={isMobile ? '95%' : 720}
      >
        <Form form={form} layout="vertical" onFinish={(v) => void submitCreate(v as Record<string, unknown>)}>
          {superUser && (
            <Form.Item label="Lọc theo công ty">
              <CompanyTreeSelect
                placeholder="Chọn công ty để thu hẹp danh sách tài sản"
                value={companyFilter}
                onChange={(v) => setCompanyFilter(v)}
              />
            </Form.Item>
          )}
          <Form.Item name="assetId" label="Tài sản" rules={[{ required: true, message: 'Chọn tài sản' }]}>
            <Select
              showSearch
              loading={assetLoading}
              placeholder="Tìm tên hoặc mã tài sản"
              options={filteredAssetOptions}
              onChange={(v) => {
                const asset = assetOptions.find(o => o.value === v);
                form.setFieldValue('assigneeUserIds', undefined);
                void loadAssigneeUsers(asset?.companyId);
              }}
              filterOption={(input, option) =>
                (option?.label ?? '').toLowerCase().includes(input.toLowerCase())
              }
            />
          </Form.Item>
          <Form.Item name="assigneeUserIds" label="Người phụ trách" extra="Tối đa 5 người — người thực hiện sửa chữa">
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
          <Row gutter={[16, 8]}>
            <Col xs={24} sm={12}>
              <Form.Item name="type" label="Loại" rules={[{ required: true, message: 'Chọn loại' }]}>
                <Select options={MAINTENANCE_TYPE_OPTIONS} />
              </Form.Item>
            </Col>
            <Col xs={24} sm={12}>
              <Form.Item name="title" label="Tiêu đề" rules={[{ required: true, message: 'Nhập tiêu đề' }]}>
                <Input placeholder="VD: Bảo trì định kỳ quý 3" />
              </Form.Item>
            </Col>
          </Row>
          <Form.Item name="notes" label="Ghi chú">
            <Input.TextArea rows={2} placeholder="Mô tả công việc, lỗi gặp phải..." />
          </Form.Item>
          <Row gutter={[16, 8]}>
            <Col xs={24} sm={12} md={8}>
              <Form.Item name="supplierId" label="Nhà cung cấp">
                <Select allowClear placeholder="Chọn NCC" options={suppliers} />
              </Form.Item>
            </Col>
            <Col xs={24} sm={12} md={8}>
              <Form.Item name="startDate" label="Ngày bắt đầu" rules={[{ required: true, message: 'Chọn ngày' }]}>
                <DatePicker style={{ width: '100%' }} />
              </Form.Item>
            </Col>
            <Col xs={24} sm={12} md={8}>
              <Form.Item name="completionDate" label="Ngày hoàn thành">
                <DatePicker style={{ width: '100%' }} />
              </Form.Item>
            </Col>
            <Col xs={24} sm={12} md={8}>
              <Form.Item name="cost" label="Chi phí">
                <InputNumber style={{ width: '100%' }} min={0} placeholder="VND" />
              </Form.Item>
            </Col>
            <Col xs={24} sm={12} md={8}>
              <Form.Item name="isWarranty" label="Bảo hành" valuePropName="checked">
                <Switch />
              </Form.Item>
            </Col>
          </Row>
        </Form>
      </Modal>
    </>
  );
}
