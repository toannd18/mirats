import { useEffect, useState } from 'react';
import {
  Alert, App, Button, Card, Col, DatePicker, Divider, Form, Input, InputNumber, Modal,
  Radio, Row, Select, Space, Spin, Tag, Tooltip, Typography, Grid,
} from 'antd';
import { LockOutlined, PlusOutlined, SaveOutlined } from '@ant-design/icons';
import apiClient from '../../../services/api-client';
import { componentsApi, type CreateComponentPayload, type ComponentDto, type TrackingType } from '../services/components.service';
import dayjs from 'dayjs';
import CompanyTreeSelect from '../../../components/common/CompanyTreeSelect';

const { Text } = Typography;

// CategoryType.Component = 4 (must match Domain/Enums/CategoryType.cs)
const COMPONENT_CATEGORY_TYPE = 4;

interface ComponentFormModalProps {
  open: boolean;
  /** null/undefined = create mode; otherwise edit the given component. */
  componentId?: string | null;
  onClose: () => void;
  onSaved: () => void;
}

function parseSerials(text: string): string[] {
  return text.split('\n').map(s => s.trim()).filter(Boolean);
}


/** Read-only locked field: Tag + lock icon + tooltip. */
function LockedFieldTag({ value, color }: { value: string; color?: string }) {
  return (
    <Tooltip title="Không thể thay đổi sau khi tạo">
      <Tag icon={<LockOutlined />} color={color} style={{ marginInlineEnd: 0 }}>{value || '—'}</Tag>
    </Tooltip>
  );
}

export default function ComponentFormModal({ open, componentId, onClose, onSaved }: ComponentFormModalProps) {
  const { message, modal } = App.useApp();
  const { useBreakpoint } = Grid;
  const screens = useBreakpoint();
  const isMobile = !screens.md;
  const [form] = Form.useForm();
  const isEdit = !!componentId;

  const [loading, setLoading] = useState(false);
  const [saving, setSaving] = useState(false);
  // Loaded component (edit mode) — source of truth for the read-only locked fields.
  const [loadedComponent, setLoadedComponent] = useState<ComponentDto | null>(null);

  const trackingType = Form.useWatch('trackingType', form) as TrackingType | undefined;
  // In edit mode TrackingType is locked → take it from the loaded component.
  const effectiveTrackingType: TrackingType = isEdit ? (loadedComponent?.trackingType ?? 'Bulk') : (trackingType ?? 'Bulk');
  const [serialText, setSerialText] = useState('');
  const serials = parseSerials(serialText);
  const hasDuplicateSerials = new Set(serials.map(s => s.toLowerCase())).size !== serials.length;

  // Options (quick-add friendly)
  const [categoryOptions, setCategoryOptions] = useState<{ label: string; value: string }[]>([]);
  const [newCategoryName, setNewCategoryName] = useState('');
  const [locationOptions, setLocationOptions] = useState<{ label: string; value: string }[]>([]);
  const [supplierOptions, setSupplierOptions] = useState<{ label: string; value: string }[]>([]);
  const [newSupplierName, setNewSupplierName] = useState('');
  const [manufacturerOptions, setManufacturerOptions] = useState<{ label: string; value: string }[]>([]);
  const [newManufacturerName, setNewManufacturerName] = useState('');

  const loadCategories = async () => {
    try {
      const res = await apiClient.get('/categories', { params: { type: COMPONENT_CATEGORY_TYPE } });
      const list = (res.data?.data ?? []) as { id: string; name: string }[];
      setCategoryOptions(list.map(c => ({ label: c.name, value: c.id })));
    } catch { /* non-critical */ }
  };

  const loadCommon = async () => {
    try {
      const [locRes, supRes, mfrRes] = await Promise.all([
        apiClient.get('/locations'), apiClient.get('/suppliers'), apiClient.get('/manufacturers'),
      ]);
      setLocationOptions(((locRes.data?.data ?? []) as { id: string; name: string }[]).map(l => ({ label: l.name, value: l.id })));
      setSupplierOptions(((supRes.data?.data ?? []) as { id: string; name: string }[]).map(s => ({ label: s.name, value: s.id })));
      setManufacturerOptions(((mfrRes.data?.data ?? []) as { id: string; name: string }[]).map(m => ({ label: m.name, value: m.id })));
    } catch { /* non-critical */ }
  };

  const addCategory = async () => {
    const name = newCategoryName.trim();
    if (!name) { message.warning('Nhập tên danh mục mới'); return; }
    try {
      const res = await apiClient.post('/categories', { name, categoryType: COMPONENT_CATEGORY_TYPE });
      const created = res.data?.data as { id: string; name: string };
      setCategoryOptions(o => [...o, { label: created.name, value: created.id }]);
      form.setFieldValue('categoryId', created.id);
      setNewCategoryName('');
    } catch (err: unknown) {
      const e = err as { response?: { data?: { message?: string } } };
      message.error(e?.response?.data?.message || 'Không thể tạo danh mục');
    }
  };

  const addSupplier = async () => {
    const name = newSupplierName.trim();
    if (!name) { message.warning('Nhập tên nhà cung cấp mới'); return; }
    try {
      const res = await apiClient.post('/suppliers', { name, code: name.slice(0, 5).toUpperCase() });
      const created = res.data?.data as { id: string; name: string };
      setSupplierOptions(o => [...o, { label: created.name, value: created.id }]);
      form.setFieldValue('supplierId', created.id);
      setNewSupplierName('');
    } catch (err: unknown) {
      const e = err as { response?: { data?: { message?: string } } };
      message.error(e?.response?.data?.message || 'Không thể tạo nhà cung cấp');
    }
  };

  const addManufacturer = async () => {
    const name = newManufacturerName.trim();
    if (!name) { message.warning('Nhập tên nhà sản xuất mới'); return; }
    try {
      const res = await apiClient.post('/manufacturers', { name, code: name.slice(0, 5).toUpperCase() });
      const created = res.data?.data as { id: string; name: string };
      setManufacturerOptions(o => [...o, { label: created.name, value: created.id }]);
      form.setFieldValue('manufacturerId', created.id);
      setNewManufacturerName('');
    } catch (err: unknown) {
      const e = err as { response?: { data?: { message?: string } } };
      message.error(e?.response?.data?.message || 'Không thể tạo nhà sản xuất');
    }
  };

  // Load options + component data every time the modal opens (destroyOnHidden keeps this component mounted).
  useEffect(() => {
    if (!open) return;
    void loadCategories();
    void loadCommon();
    setSerialText('');
    setNewCategoryName('');
    setNewSupplierName('');
    setNewManufacturerName('');

    if (isEdit && componentId) {
      setLoading(true);
      componentsApi.get(componentId)
        .then(r => {
          const d = r.data.data as ComponentDto;
          setLoadedComponent(d);
          form.setFieldsValue({
            name: d.name,
            serial: d.serial,
            qty: d.qty,
            minAmt: d.minAmt,
            trackingType: d.trackingType,
            locationId: d.location?.id,
            supplierId: d.supplier?.id,
            manufacturerId: d.manufacturer?.id,
            modelNumber: d.modelNumber,
            orderNumber: d.orderNumber,
            purchaseCost: d.purchaseCost,
            purchaseDate: d.purchaseDate ? dayjs(d.purchaseDate) : undefined,
            notes: d.notes,
          });
        })
        .catch(() => message.error('Không thể tải linh kiện'))
        .finally(() => setLoading(false));
    } else {
      setLoadedComponent(null);
      form.resetFields();
      form.setFieldsValue({ trackingType: 'Bulk' });
    }
  }, [open, componentId]);

  const submit = async (vals: Record<string, unknown>) => {
    setSaving(true);
    try {
      if (isEdit && componentId) {
        // Update whitelist — only these fields are editable. Locked fields (trackingType,
        // categoryId, companyId) and read-only Qty/Serial are intentionally NOT sent.
        const payload: Record<string, unknown> = {};
        if (vals.name !== undefined) payload.name = vals.name;
        if (vals.notes !== undefined) payload.notes = vals.notes;
        if (vals.minAmt !== undefined) payload.minAmt = vals.minAmt;
        if (vals.locationId !== undefined) payload.locationId = vals.locationId;
        if (vals.supplierId !== undefined) payload.supplierId = vals.supplierId;
        if (vals.manufacturerId !== undefined) payload.manufacturerId = vals.manufacturerId;
        if (vals.modelNumber !== undefined) payload.modelNumber = vals.modelNumber;
        if (vals.orderNumber !== undefined) payload.orderNumber = vals.orderNumber;
        if (vals.purchaseCost !== undefined) payload.purchaseCost = vals.purchaseCost;
        if (vals.purchaseDate !== undefined) payload.purchaseDate = dayjs(vals.purchaseDate as dayjs.Dayjs).toISOString();
        await componentsApi.update(componentId, payload);
        message.success('Cập nhật thành công');
      } else {
        const payload: CreateComponentPayload = {
          name: String(vals.name),
          serial: vals.serial ? String(vals.serial) : undefined,
          minAmt: typeof vals.minAmt === 'number' ? vals.minAmt : 0,
          trackingType: effectiveTrackingType,
          categoryId: String(vals.categoryId),
          companyId: String(vals.companyId),
          locationId: vals.locationId ? String(vals.locationId) : undefined,
          supplierId: vals.supplierId ? String(vals.supplierId) : undefined,
          manufacturerId: vals.manufacturerId ? String(vals.manufacturerId) : undefined,
          modelNumber: vals.modelNumber ? String(vals.modelNumber) : undefined,
          orderNumber: vals.orderNumber ? String(vals.orderNumber) : undefined,
          purchaseCost: typeof vals.purchaseCost === 'number' ? vals.purchaseCost : undefined,
          purchaseDate: vals.purchaseDate ? dayjs(vals.purchaseDate as dayjs.Dayjs).toISOString() : undefined,
          notes: vals.notes ? String(vals.notes) : undefined,
          serialNumbers: effectiveTrackingType === 'Serial' ? serials : undefined,
        };
        // [Fix] NEVER send qty for Serial components — backend derives quantity from the serial list and
        // rejects an explicit qty (even 0) with "Không gửi qty khi tạo linh kiện Serial". A leftover qty from
        // a prior Bulk toggle (or the form default) must not leak into the payload. Bulk keeps sending qty.
        if (effectiveTrackingType === 'Bulk') {
          payload.qty = typeof vals.qty === 'number' ? vals.qty : 0;
        }
        await componentsApi.create(payload);
        message.success('Tạo mới thành công');
      }
      onSaved();
    } catch (err: unknown) {
      const e = err as { response?: { data?: { message?: string; error_code?: string } } };
      message.error(e?.response?.data?.message || 'Lỗi lưu dữ liệu');
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

  return (
    <Modal
      open={open}
      title={isEdit ? 'Sửa linh kiện' : 'Tạo linh kiện mới'}
      onCancel={handleClose}
      width={isMobile ? '95%' : 720}
      destroyOnHidden
      mask={{ closable: false }}
      footer={[
        <Button key="cancel" onClick={handleClose}>Hủy</Button>,
        <Button key="submit" type="primary" htmlType="submit" form="component-form-modal"
          loading={saving} icon={<SaveOutlined />}>
          {isEdit ? 'Cập nhật' : 'Tạo mới'}
        </Button>,
      ]}
      styles={{ body: { maxHeight: '68vh', overflowY: 'auto', padding: '8px 24px' } }}
    >
      <Spin spinning={loading}>
        <Form id="component-form-modal" form={form} layout="vertical" onFinish={submit}>
          {/* ── Nhóm 1: Thông tin cơ bản ── */}
          <Divider titlePlacement="start" plain style={{ marginTop: 0 }}>Thông tin cơ bản</Divider>
          <Row gutter={[16, 8]}>
            <Col xs={24} sm={12}>
              <Form.Item label="Tên" name="name" rules={[{ required: true, message: 'Nhập tên' }]}>
                <Input placeholder="Tên linh kiện" />
              </Form.Item>
            </Col>
            <Col xs={24} sm={12}>
              {isEdit ? (
                <Form.Item label="Hình thức quản lý">
                  <LockedFieldTag value={loadedComponent?.trackingType ?? ''}
                    color={loadedComponent?.trackingType === 'Serial' ? 'blue' : undefined} />
                </Form.Item>
              ) : (
                <Form.Item label="Hình thức quản lý" name="trackingType" rules={[{ required: true, message: 'Chọn hình thức' }]}>
                  <Radio.Group optionType="button" buttonStyle="solid">
                    <Radio.Button value="Bulk">Bulk (số lượng)</Radio.Button>
                    <Radio.Button value="Serial">Serial (từng cái)</Radio.Button>
                  </Radio.Group>
                </Form.Item>
              )}
            </Col>
          </Row>
          <Row gutter={[16, 8]}>
            <Col xs={24} sm={12}>
              {isEdit ? (
                <Form.Item label="Danh mục">
                  <LockedFieldTag value={loadedComponent?.category?.name || 'Chưa phân loại'}
                    color={loadedComponent?.category ? undefined : 'warning'} />
                </Form.Item>
              ) : (
                <Form.Item label="Danh mục" name="categoryId" rules={[{ required: true, message: 'Chọn danh mục' }]}>
                  <Select showSearch placeholder="Chọn danh mục..." options={categoryOptions}
                    filterOption={(input, option) => (option?.label as string)?.toLowerCase().includes(input.toLowerCase())}
                    popupRender={(menu) => (
                      <>
                        {menu}
                        <Divider style={{ margin: '8px 0' }} />
                        <Space style={{ padding: '0 8px 4px' }}>
                          <Input placeholder="Tên danh mục mới" value={newCategoryName}
                            onChange={e => setNewCategoryName(e.target.value)}
                            onPressEnter={() => void addCategory()} style={{ width: 200 }} />
                          <Button type="text" icon={<PlusOutlined />} onClick={() => void addCategory()}>Thêm</Button>
                        </Space>
                      </>
                    )} />
                </Form.Item>
              )}
            </Col>
            <Col xs={24} sm={12}>
              <Form.Item label="Serial (danh mục)" name="serial">
                <Input disabled={isEdit} placeholder="Serial mẫu (không bắt buộc)" />
              </Form.Item>
            </Col>
          </Row>

          {/* ── Nhóm 2: Số lượng / Tồn kho ── */}
          <Divider titlePlacement="start" plain>Số lượng / Tồn kho</Divider>
          {effectiveTrackingType === 'Bulk' ? (
            <Row gutter={[16, 8]}>
              <Col xs={24} sm={12}>
                {!isEdit && (
                  <Form.Item label="Tổng số lượng" name="qty" rules={[{ required: true, message: 'Nhập số lượng' }]}>
                    <InputNumber min={1} style={{ width: '100%' }} />
                  </Form.Item>
                )}
              </Col>
              <Col xs={24} sm={12}>
                <Form.Item label="Ngưỡng cảnh báo (MinAmt)" name="minAmt">
                  <InputNumber min={0} style={{ width: '100%' }} />
                </Form.Item>
              </Col>
            </Row>
          ) : isEdit ? (
            <Row gutter={[16, 8]}>
              <Col xs={24} sm={12}>
                <Form.Item label="Ngưỡng cảnh báo (MinAmt)" name="minAmt">
                  <InputNumber min={0} style={{ width: '100%' }} />
                </Form.Item>
              </Col>
              <Col xs={24} sm={12}>
                <Alert type="info" style={{ marginTop: 30 }}
                  title="Qty = tổng số serial — được quản lý qua màn hình Nhập kho ở trang chi tiết." />
              </Col>
            </Row>
          ) : (
            <Row gutter={[16, 8]}>
              <Col xs={24} sm={12}>
                <Form.Item label="Ngưỡng cảnh báo (MinAmt)" name="minAmt">
                  <InputNumber min={0} style={{ width: '100%' }} />
                </Form.Item>
              </Col>
              <Col xs={24} sm={24}>
                <Card size="small" title="Nhập kho ban đầu (không bắt buộc)"
                  extra={<Text type="secondary">Đã nhập: {serials.length} serial</Text>}
                  style={{ marginBottom: 16 }}>
                  <Input.TextArea rows={4} value={serialText} onChange={e => setSerialText(e.target.value)}
                    placeholder={'SN-0001\nSN-0002\nSN-0003'} />
                  <Text type="secondary" style={{ display: 'block', marginTop: 4 }}>
                    Mỗi dòng một serial. Để trống nếu chưa có hàng — nhập kho sau ở trang chi tiết.
                  </Text>
                  {hasDuplicateSerials && (
                    <Alert type="warning" style={{ marginTop: 8 }}
                      title="Có serial trùng nhau trong danh sách — hãy kiểm tra lại trước khi lưu." />
                  )}
                </Card>
              </Col>
            </Row>
          )}

          {/* ── Nhóm 3: Vị trí & Công ty ── */}
          <Divider titlePlacement="start" plain>Vị trí & Công ty</Divider>
          <Row gutter={[16, 8]}>
            <Col xs={24} sm={12}>
              {isEdit ? (
                <Form.Item label="Công ty">
                  <LockedFieldTag value={loadedComponent?.company?.name || 'Chưa xác định công ty'}
                    color={loadedComponent?.company ? 'blue' : 'warning'} />
                </Form.Item>
              ) : (
                <Form.Item label="Công ty" name="companyId" rules={[{ required: true, message: 'Chọn công ty' }]}>
                  <CompanyTreeSelect allowQuickAdd />
                </Form.Item>
              )}
            </Col>
            <Col xs={24} sm={12}>
              <Form.Item label="Vị trí lưu kho" name="locationId">
                <Select showSearch allowClear placeholder="Chọn vị trí (không bắt buộc)" options={locationOptions}
                  filterOption={(input, option) => (option?.label as string)?.toLowerCase().includes(input.toLowerCase())} />
              </Form.Item>
            </Col>
          </Row>

          {/* ── Nhóm 4: Nhà cung cấp & mua hàng ── */}
          <Divider titlePlacement="start" plain>Nhà cung cấp & mua hàng</Divider>
          <Row gutter={[16, 8]}>
            <Col xs={24} sm={12}>
              <Form.Item label="Nhà sản xuất" name="manufacturerId">
                <Select showSearch allowClear placeholder="Chọn nhà sản xuất..." options={manufacturerOptions}
                  filterOption={(input, option) => (option?.label as string)?.toLowerCase().includes(input.toLowerCase())}
                  popupRender={(menu) => (
                    <>
                      {menu}
                      <Divider style={{ margin: '8px 0' }} />
                      <Space style={{ padding: '0 8px 4px' }}>
                        <Input placeholder="Tên NSX mới" value={newManufacturerName}
                          onChange={e => setNewManufacturerName(e.target.value)}
                          onPressEnter={() => void addManufacturer()} style={{ width: 200 }} />
                        <Button type="text" icon={<PlusOutlined />} onClick={() => void addManufacturer()}>Thêm</Button>
                      </Space>
                    </>
                  )} />
              </Form.Item>
            </Col>
            <Col xs={24} sm={12}>
              <Form.Item label="Nhà cung cấp" name="supplierId">
                <Select showSearch allowClear placeholder="Chọn nhà cung cấp..." options={supplierOptions}
                  filterOption={(input, option) => (option?.label as string)?.toLowerCase().includes(input.toLowerCase())}
                  popupRender={(menu) => (
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
            </Col>
            <Col xs={24} sm={12}>
              <Form.Item label="Model Number" name="modelNumber">
                <Input placeholder="VD: VLP-16" />
              </Form.Item>
            </Col>
            <Col xs={24} sm={12}>
              <Form.Item label="Số đơn hàng" name="orderNumber">
                <Input placeholder="VD: PO-2026-001" />
              </Form.Item>
            </Col>
            <Col xs={24} sm={12}>
              <Form.Item label="Ngày mua" name="purchaseDate">
                <DatePicker style={{ width: '100%' }} format="DD/MM/YYYY" placeholder="Chọn ngày" />
              </Form.Item>
            </Col>
            <Col xs={24} sm={12}>
              <Form.Item label="Giá mua (đơn vị)" name="purchaseCost">
                <InputNumber min={0} precision={2} style={{ width: '100%' }} placeholder="0" />
              </Form.Item>
            </Col>
          </Row>
          {/* ── Nhóm 5: Ghi chú ── */}
          <Divider titlePlacement="start" plain>Ghi chú</Divider>
          <Form.Item label="Ghi chú" name="notes">
            <Input.TextArea rows={2} maxLength={1000} showCount placeholder="Ghi chú thêm về linh kiện..." />
          </Form.Item>
        </Form>
      </Spin>
    </Modal>
  );
}
