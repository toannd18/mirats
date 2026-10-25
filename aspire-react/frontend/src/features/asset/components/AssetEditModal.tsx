import { useEffect, useState } from 'react';
import {
  App, Col, DatePicker, Divider, Form, Input, InputNumber,
  Row, Select, Space, Spin, Switch, Tooltip, Typography,
} from 'antd';
import { LockOutlined } from '@ant-design/icons';
import dayjs from 'dayjs';
import apiClient from '../../../services/api-client';
import { assetService, type UpdateAssetPayload } from '../services/asset.service';
import { fkForUpdate } from '../../../shared/utils/fk';
import { ASSET_STATUS_LABELS, normalizeAssetStatus, type AssetDetailDto } from '../types/asset';
import { usePermission } from '../../../hooks/usePermission';
import CompanyTreeSelect from '../../../components/common/CompanyTreeSelect';
import { useCompanyField } from '../../../hooks/useCompanyField';
import { uiColors } from '../../../theme/designTokens';
import ModalFormShell from '../../../shared/components/ModalFormShell';
import { applyServerFieldErrors, serverErrorMessage } from '../../../shared/utils/formErrors';

const { Text } = Typography;

interface SelectOption { value: string; label: string; }

interface AssetEditModalProps {
  open: boolean;
  /** The asset to edit. */
  assetId?: string | null;
  onClose: () => void;
  onSaved: () => void;
}

/**
 * Modal Sửa tài sản (thay thế AssetFormPage trang riêng).
 * Pattern đồng bộ AccessoryFormModal/ComponentFormModal — modal mở TẠI CHỖ bằng state cục bộ
 * (bài học Task A), KHÔNG navigate để mở modal.
 *
 * Giữ nguyên 100% logic Task F (field-lock theo IsConfirmed):
 *  - Asset ĐÃ confirmed → CHỈ Name/Notes sửa được; các field khác hiển thị disabled NHƯNG
 *    VẪN gửi kèm giá trị hiện tại khi submit (backend gate so bằng → không chặn nhầm).
 *  - Asset CHƯA confirmed → mọi field sửa được.
 * Patch semantics (Task F): payload chỉ chứa field có giá trị — backend giữ nguyên field absent.
 */
export default function AssetEditModal({ open, assetId, onClose, onSaved }: AssetEditModalProps) {
  const { message } = App.useApp();
  const [form] = Form.useForm();
  // ST6b — edit gated by backend policy assets.edit.
  const canEdit = usePermission('assets.edit');

  const [loading, setLoading] = useState(false);
  const [saving, setSaving] = useState(false);
  const [asset, setAsset] = useState<AssetDetailDto | null>(null);
  const [models, setModels] = useState<SelectOption[]>([]);
  const [locations, setLocations] = useState<SelectOption[]>([]);
  const [suppliers, setSuppliers] = useState<SelectOption[]>([]);

  const { showCompanyField } = useCompanyField();
  // Dropdown options (models/locations/suppliers) - công ty dùng <CompanyTreeSelect> dùng chung.
  useEffect(() => {
    const map = (items: { id: string; name: string }[]): SelectOption[] =>
      items.map(i => ({ value: i.id, label: i.name }));
    apiClient.get('/models', { params: { pageSize: 500 } })
      .then(r => setModels(map(r.data?.data ?? []))).catch(() => { /* non-critical */ });
    apiClient.get('/locations', { params: { pageSize: 500 } })
      .then(r => setLocations(map(r.data?.data ?? []))).catch(() => { /* non-critical */ });
    apiClient.get('/suppliers', { params: { pageSize: 500 } })
      .then(r => setSuppliers(map(r.data?.data ?? []))).catch(() => { /* non-critical */ });
  }, []);

  // Load asset every time the modal opens.
  useEffect(() => {
    if (!open || !assetId) return;
    setLoading(true);
    assetService.get(assetId)
      .then(r => {
        const a = r.data.data as AssetDetailDto;
        setAsset(a);
        form.setFieldsValue({
          name: a.name,
          serial: a.serial ?? undefined,
          modelId: a.model?.id,
          locationId: a.location?.id,
          supplierId: a.supplier?.id,
          companyId: a.company?.id,
          purchaseCost: a.purchaseCost ?? undefined,
          purchaseDate: a.purchaseDate ? dayjs(a.purchaseDate) : undefined,
          warrantyMonths: a.warrantyMonths ?? undefined,
          orderNumber: a.orderNumber ?? undefined,
          physical: a.physical,
          requestable: a.requestable,
          notes: a.notes ?? '',
        });
      })
      .catch(() => void message.error('Không thể tải thông tin tài sản'))
      .finally(() => setLoading(false));
  }, [open, assetId, form, message]);

  const submit = async (values: {
    name: string; serial?: string; modelId?: string; locationId?: string; supplierId?: string;
    companyId?: string; purchaseCost?: number; purchaseDate?: dayjs.Dayjs; warrantyMonths?: number;
    orderNumber?: string; physical?: boolean; requestable?: boolean; notes?: string;
  }) => {
    if (!assetId) return;
    setSaving(true);
    try {
      // Chỉ gửi field có giá trị (backend patch semantics giữ nguyên field absent).
      // Locked fields (confirmed) vẫn gửi giá trị hiện tại để gate so bằng không chặn nhầm.
      const payload: UpdateAssetPayload = { name: values.name };
      if (values.serial) payload.serial = values.serial;
      // [FIX-GUID-EMPTY phần 2] FK tùy chọn: ô bị xóa (undefined) ⇒ EMPTY_GUID để backend XÓA liên kết;
      // gửi undefined/null sẽ bị hiểu là "không gửi" ⇒ không xóa được (quy ước: docs/API.md).
      payload.modelId = fkForUpdate(values.modelId);
      payload.locationId = fkForUpdate(values.locationId);
      payload.supplierId = fkForUpdate(values.supplierId);
      if (showCompanyField) payload.companyId = fkForUpdate(values.companyId);
      if (values.purchaseCost !== undefined) payload.purchaseCost = values.purchaseCost;
      if (values.purchaseDate) payload.purchaseDate = values.purchaseDate.toISOString();
      if (values.warrantyMonths !== undefined) payload.warrantyMonths = values.warrantyMonths;
      if (values.orderNumber !== undefined) payload.orderNumber = values.orderNumber;
      payload.physical = !!values.physical;
      payload.requestable = !!values.requestable;
      payload.notes = values.notes ?? null;
      await assetService.update(assetId, payload);
      void message.success('Cập nhật tài sản thành công');
      onSaved();
    } catch (err: unknown) {
      if (!applyServerFieldErrors(form, err)) {
        void message.error(serverErrorMessage(err, 'Không thể cập nhật tài sản. Vui lòng thử lại.'));
      }
    } finally {
      setSaving(false);
    }
  };

  if (!asset) return null;

  const isConfirmed = asset.isConfirmed;
  const statusLabel = ASSET_STATUS_LABELS[normalizeAssetStatus(asset.status)];
  const filterFn = (input: string, option?: { label?: string }) =>
    (option?.label as string)?.toLowerCase().includes(input.toLowerCase());
  const lock = (label: React.ReactNode) => isConfirmed
    ? (
      <Space size={4}>
        <Tooltip title="Trường bị khóa sau khi xác nhận — chỉ Tên và Ghi chú được sửa">
          <LockOutlined style={{ color: uiColors.labelGray, fontSize: 12 }} />
        </Tooltip>
        <span>{label}</span>
      </Space>
    )
    : label;

  return (
    <ModalFormShell
      open={open}
      title="Chỉnh sửa tài sản"
      onCancel={onClose}
      onOk={() => form.submit()}
      okText="Lưu thay đổi"
      okButtonProps={{ disabled: !canEdit }}
      confirmLoading={saving}
      size="lg"
      maskClosable={false}
    >
      <Spin spinning={loading}>
        <Form
          form={form}
          layout="vertical"
          size="middle"
          initialValues={{ physical: true, requestable: false }}
          onFinish={(v) => void submit(v as Parameters<typeof submit>[0])}
        >
          {/* ── Thông tin tài sản (read-only identity) ── */}
          <Divider titlePlacement="start" plain style={{ marginTop: 0 }}>Thông tin tài sản</Divider>
          <Row gutter={[16, 8]}>
            <Col xs={12} md={6}><Text type="secondary" style={{ fontSize: 12 }}>Mã tài sản</Text><div><Text strong>{asset.assetTag}</Text></div></Col>
            <Col xs={12} md={6}><Text type="secondary" style={{ fontSize: 12 }}>Danh mục</Text><div><Text>{asset.category?.name || '-'}</Text></div></Col>
            <Col xs={12} md={6}><Text type="secondary" style={{ fontSize: 12 }}>Model</Text><div><Text>{asset.model?.name || '-'}</Text></div></Col>
            <Col xs={12} md={6}><Text type="secondary" style={{ fontSize: 12 }}>Trạng thái</Text><div><Text strong>{statusLabel}</Text></div></Col>
          </Row>

          {isConfirmed && (
            <Text type="secondary" style={{ display: 'block', marginTop: 8, fontSize: 13 }}>
              Tài sản đã xác nhận — chỉ có thể chỉnh sửa <Text strong>Tên</Text> và <Text strong>Ghi chú</Text>. Các trường còn lại đã khóa.
            </Text>
          )}

          {/* ── Chỉnh sửa ── */}
          <Divider titlePlacement="start" plain style={{ marginTop: 16 }}>Chỉnh sửa</Divider>
          <Row gutter={[16, 8]}>
            <Col xs={24} sm={12}>
              <Form.Item name="name" label="Tên tài sản" rules={[{ required: true, message: 'Vui lòng nhập tên tài sản' }]}>
                <Input placeholder="Nhập tên tài sản" />
              </Form.Item>
            </Col>
            <Col xs={24} sm={12}>
              <Form.Item name="serial" label={lock('Serial')}>
                <Input placeholder="Số serial" disabled={isConfirmed} />
              </Form.Item>
            </Col>
            <Col xs={24} sm={12}>
              <Form.Item name="modelId" label={lock('Model')}>
                <Select allowClear showSearch placeholder="Chọn model" options={models} disabled={isConfirmed} filterOption={filterFn} />
              </Form.Item>
            </Col>
            <Col xs={24} sm={12}>
              <Form.Item name="locationId" label={lock('Địa điểm')}>
                <Select allowClear showSearch placeholder="Chọn địa điểm" options={locations} disabled={isConfirmed} filterOption={filterFn} />
              </Form.Item>
            </Col>
            <Col xs={24} sm={12}>
              <Form.Item name="supplierId" label={lock('Nhà cung cấp')}>
                <Select allowClear showSearch placeholder="Chọn nhà cung cấp" options={suppliers} disabled={isConfirmed} filterOption={filterFn} />
              </Form.Item>
            </Col>
            {showCompanyField && (
              <Col xs={24} sm={12}>
                <Form.Item name="companyId" label={lock('Công ty')}>
                  <CompanyTreeSelect disabled={isConfirmed} />
                </Form.Item>
              </Col>
            )}
            <Col xs={24} sm={8}>
              <Form.Item name="purchaseCost" label={lock('Đơn giá (VND)')}>
                <InputNumber min={0} style={{ width: '100%' }} placeholder="0" disabled={isConfirmed}
                  formatter={(value) => `${value}`.replace(/\B(?=(\d{3})+(?!\d))/g, ',')}
                  parser={(value) => (value?.replace(/,/g, '') ?? '') as unknown as 0} />
              </Form.Item>
            </Col>
            <Col xs={24} sm={8}>
              <Form.Item name="purchaseDate" label={lock('Ngày mua')}>
                <DatePicker style={{ width: '100%' }} format="DD/MM/YYYY" placeholder="Chọn ngày" disabled={isConfirmed} />
              </Form.Item>
            </Col>
            <Col xs={24} sm={8}>
              <Form.Item name="warrantyMonths" label={lock('Bảo hành (tháng)')}>
                <InputNumber min={0} max={120} style={{ width: '100%' }} placeholder="12" disabled={isConfirmed} />
              </Form.Item>
            </Col>
            <Col xs={24} sm={12}>
              <Form.Item name="orderNumber" label={lock('Số đơn hàng')}>
                <Input placeholder="Nhập số đơn hàng" disabled={isConfirmed} />
              </Form.Item>
            </Col>
            <Col xs={12} sm={6}>
              <Form.Item name="physical" label={lock('Tài sản vật lý')} valuePropName="checked">
                <Switch disabled={isConfirmed} />
              </Form.Item>
            </Col>
            <Col xs={12} sm={6}>
              <Form.Item name="requestable" label={lock('Cho phép yêu cầu')} valuePropName="checked">
                <Switch disabled={isConfirmed} />
              </Form.Item>
            </Col>
            <Col span={24}>
              <Form.Item name="notes" label="Ghi chú">
                <Input.TextArea rows={3} maxLength={1000} showCount placeholder="Ghi chú" />
              </Form.Item>
            </Col>
          </Row>
        </Form>
      </Spin>
    </ModalFormShell>
  );
}
