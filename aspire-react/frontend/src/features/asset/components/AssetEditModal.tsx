import { useEffect, useState } from 'react';
import {
  App, Button, Col, DatePicker, Divider, Form, Input, InputNumber, Modal,
  Row, Select, Space, Spin, Switch, Tooltip, Typography,
} from 'antd';
import { LockOutlined, SaveOutlined } from '@ant-design/icons';
import dayjs from 'dayjs';
import apiClient from '../../../services/api-client';
import { assetService, type UpdateAssetPayload } from '../services/asset.service';
import { ASSET_STATUS_LABELS, normalizeAssetStatus, type AssetDetailDto } from '../types/asset';
import { usePermission } from '../../../hooks/usePermission';
import { useIsMobile } from '../../../hooks/useIsMobile';
import CompanyTreeSelect from '../../../components/common/CompanyTreeSelect';
import { uiColors } from '../../../theme/designTokens';

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
  const { message, modal } = App.useApp();
  const isMobile = useIsMobile();
  const [form] = Form.useForm();
  // ST6b — edit gated by backend policy assets.edit.
  const canEdit = usePermission('assets.edit');

  const [loading, setLoading] = useState(false);
  const [saving, setSaving] = useState(false);
  const [asset, setAsset] = useState<AssetDetailDto | null>(null);
  const [models, setModels] = useState<SelectOption[]>([]);
  const [locations, setLocations] = useState<SelectOption[]>([]);
  const [suppliers, setSuppliers] = useState<SelectOption[]>([]);

  // Dropdown options (models/locations/suppliers) — công ty dùng <CompanyTreeSelect> dùng chung.
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
      if (values.modelId) payload.modelId = values.modelId;
      if (values.locationId) payload.locationId = values.locationId;
      if (values.supplierId) payload.supplierId = values.supplierId;
      if (values.companyId) payload.companyId = values.companyId;
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
      const e = err as { response?: { data?: { message?: string } } };
      void message.error(e?.response?.data?.message || 'Lỗi cập nhật');
    } finally {
      setSaving(false);
    }
  };

  const handleClose = () => {
    if (form.isFieldsTouched()) {
      modal.confirm({
        title: 'Dữ liệu chưa lưu',
        content: 'Bạn có chắc muốn đóng? Các thay đổi chưa lưu sẽ bị mất.',
        okText: 'Đóng',
        cancelText: 'Tiếp tục sửa',
        onOk: onClose,
      });
    } else {
      onClose();
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
        <Tooltip title="Field khóa sau khi xác nhận — chỉ Name và Notes được sửa">
          <LockOutlined style={{ color: uiColors.labelGray, fontSize: 12 }} />
        </Tooltip>
        <span>{label}</span>
      </Space>
    )
    : label;

  return (
    <Modal
      open={open}
      title="Chỉnh sửa tài sản"
      onCancel={handleClose}
      width={isMobile ? '95%' : 760}
      destroyOnHidden
      mask={{ closable: false }}
      footer={[
        <Button key="cancel" onClick={handleClose}>Hủy</Button>,
        <Button key="submit" type="primary" htmlType="submit" form="asset-edit-modal"
          loading={saving} icon={<SaveOutlined />} disabled={!canEdit}>Lưu</Button>,
      ]}
      styles={{ body: { maxHeight: '68vh', overflowY: 'auto', padding: '8px 24px' } }}
    >
      <Spin spinning={loading}>
        <Form id="asset-edit-modal" form={form} layout="vertical" size="middle"
          onFinish={(v) => void submit(v as Parameters<typeof submit>[0])}>
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
              <Form.Item name="name" label="Tên tài sản *" rules={[{ required: true, message: 'Nhập tên tài sản' }]}>
                <Input placeholder="Tên tài sản" />
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
              <Form.Item name="locationId" label={lock('Vị trí')}>
                <Select allowClear showSearch placeholder="Chọn vị trí" options={locations} disabled={isConfirmed} filterOption={filterFn} />
              </Form.Item>
            </Col>
            <Col xs={24} sm={12}>
              <Form.Item name="supplierId" label={lock('Nhà cung cấp')}>
                <Select allowClear showSearch placeholder="Chọn nhà cung cấp" options={suppliers} disabled={isConfirmed} filterOption={filterFn} />
              </Form.Item>
            </Col>
            <Col xs={24} sm={12}>
              <Form.Item name="companyId" label={lock('Công ty')}>
                <CompanyTreeSelect disabled={isConfirmed} />
              </Form.Item>
            </Col>
            <Col xs={24} sm={8}>
              <Form.Item name="purchaseCost" label={lock('Giá mua (VND)')}>
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
              <Form.Item name="orderNumber" label={lock('Order Number')}>
                <Input placeholder="Số đơn hàng" disabled={isConfirmed} />
              </Form.Item>
            </Col>
            <Col xs={12} sm={6}>
              <Form.Item name="physical" label={lock('Physical')} valuePropName="checked">
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
    </Modal>
  );
}
