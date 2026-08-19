import { useEffect, useState } from 'react';
import {
  App, Button, Col, DatePicker, Divider, Form, Grid, Input, InputNumber, Modal,
  Row, Select, Space, Spin,
} from 'antd';
import { LockOutlined, SaveOutlined } from '@ant-design/icons';
import apiClient from '../../../services/api-client';
import { consumablesApi } from '../services/consumables.service';
import dayjs from 'dayjs';
import CompanyTreeSelect from '../../../components/common/CompanyTreeSelect';

// ==================== Types ====================

interface ConsumableFormModalProps {
  open: boolean;
  /** null/undefined = create mode; otherwise edit the given consumable. */
  consumableId?: string | null;
  onClose: () => void;
  onSaved: () => void;
}

interface OptionItem { label: string; value: string; }

// ==================== Component ====================

/**
 * Modal Tạo mới/Sửa vật tư (thay thế ConsumableFormPage trang riêng).
 * Pattern đồng bộ ComponentFormModal (Task 2). Modal mở TẠI CHỖ bằng state cục bộ —
 * KHÔNG navigate để mở modal (bài học Task A).
 *
 * Giữ nguyên 100% logic nghiệp vụ của ConsumableFormPage:
 * - Field: name*, categoryId* (loại Consumable), qty* (min 0), minAmt (min 0), supplierId,
 *   manufacturerId, locationId, companyId, modelNumber, itemNo, orderNumber, purchaseDate,
 *   purchaseCost (min 0, VND), notes.
 * - ST4 — CompanyId LOCK khi vật tư đã từng được cấp phát (mirror backend FIELD_LOCKED:
 *   fetch /checkouts, non-empty → disable field, giữ nguyên giá trị khi submit).
 */
export default function ConsumableFormModal({ open, consumableId, onClose, onSaved }: ConsumableFormModalProps) {
  const { message, modal } = App.useApp();
  const { useBreakpoint } = Grid;
  const screens = useBreakpoint();
  const isMobile = !screens.md;
  const [form] = Form.useForm();
  const isEdit = !!consumableId;

  const [loading, setLoading] = useState(false);
  const [saving, setSaving] = useState(false);
  // ST4 — company cannot change once the consumable has ever been checked out (FIELD_LOCKED).
  const [companyLocked, setCompanyLocked] = useState(false);

  const [categoryOptions, setCategoryOptions] = useState<OptionItem[]>([]);
  const [manufacturerOptions, setManufacturerOptions] = useState<OptionItem[]>([]);
  const [supplierOptions, setSupplierOptions] = useState<OptionItem[]>([]);
  const [locationOptions, setLocationOptions] = useState<OptionItem[]>([]);

  // ──── Options (only Consumable-type categories — API serializes enum as STRING) ────
  useEffect(() => {
    Promise.all([
      apiClient.get('/categories'),
      apiClient.get('/manufacturers'),
      apiClient.get('/suppliers'),
      apiClient.get('/locations'),
    ])
      .then(([catRes, mfrRes, supRes, locRes]) => {
        const consumableCategories = (catRes.data.data || []).filter(
          (c: { categoryType: string | number }) => c.categoryType === 'Consumable',
        );
        setCategoryOptions(consumableCategories.map((c: { id: string; name: string }) => ({ label: c.name, value: c.id })));
        setManufacturerOptions((mfrRes.data.data || []).map((m: { id: string; name: string }) => ({ label: m.name, value: m.id })));
        setSupplierOptions((supRes.data.data || []).map((s: { id: string; name: string }) => ({ label: s.name, value: s.id })));
        setLocationOptions((locRes.data.data || []).map((l: { id: string; name: string }) => ({ label: l.name, value: l.id })));
      })
      .catch(() => { /* non-critical */ });
  }, []);


  // ──── Load edit data + company-lock flag every time the modal opens ────
  useEffect(() => {
    if (!open) return;
    if (isEdit && consumableId) {
      setLoading(true);
      Promise.all([
        apiClient.get(`/consumables/${consumableId}`),
        apiClient.get(`/consumables/${consumableId}/checkouts`).catch(() => ({ data: { data: [] } })),
      ])
        .then(([detailRes, checkoutRes]) => {
          const d = detailRes.data.data;
          form.setFieldsValue({
            ...d,
            purchaseDate: d.purchaseDate ? dayjs(d.purchaseDate) : undefined,
          });
          // Mirrors backend FIELD_LOCKED: has ever been checked out → company can't change.
          setCompanyLocked((checkoutRes.data.data ?? []).length > 0);
        })
        .catch(() => void message.error('Lỗi tải vật tư'))
        .finally(() => setLoading(false));
    } else {
      form.resetFields();
      setCompanyLocked(false);
    }
  }, [open, isEdit, consumableId, form, message]);

  const submit = async (values: Record<string, unknown>) => {
    setSaving(true);
    try {
      // Same payload contract as the old ConsumableFormPage (backend whitelists via CreateConsumableRequest).
      const payload = {
        ...values,
        purchaseDate: values.purchaseDate ? dayjs(values.purchaseDate as dayjs.Dayjs).toISOString() : undefined,
      };
      if (isEdit && consumableId) {
        await consumablesApi.update(consumableId, payload);
        void message.success('Cập nhật vật tư thành công');
      } else {
        await consumablesApi.create(payload);
        void message.success('Tạo vật tư thành công');
      }
      onSaved();
    } catch (err: unknown) {
      const e = err as { response?: { data?: { message?: string } } };
      void message.error(e?.response?.data?.message ?? 'Lỗi lưu vật tư');
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

  const filterFn = (inp: string, opt?: { label?: React.ReactNode }) =>
    typeof opt?.label === 'string' ? opt.label.toLowerCase().includes(inp.toLowerCase()) : false;

  return (
    <Modal
      open={open}
      title={isEdit ? 'Sửa vật tư tiêu hao' : 'Tạo vật tư tiêu hao mới'}
      onCancel={handleClose}
      width={isMobile ? '95%' : 720}
      destroyOnHidden
      mask={{ closable: false }}
      footer={[
        <Button key="cancel" onClick={handleClose}>Hủy</Button>,
        <Button key="submit" type="primary" htmlType="submit" form="consumable-form-modal"
          loading={saving} icon={<SaveOutlined />}>
          {isEdit ? 'Cập nhật' : 'Tạo mới'}
        </Button>,
      ]}
      styles={{ body: { maxHeight: '68vh', overflowY: 'auto', padding: '8px 24px' } }}
    >
      <Spin spinning={loading}>
        <Form id="consumable-form-modal" form={form} layout="vertical" onFinish={(v) => void submit(v)}>
          {/* ── Thông tin cơ bản ── */}
          <Divider titlePlacement="start" plain style={{ marginTop: 0 }}>Thông tin cơ bản</Divider>
          <Row gutter={[16, 8]}>
            <Col xs={24} sm={12}>
              <Form.Item label="Tên vật tư" name="name" rules={[{ required: true, message: 'Vui lòng nhập tên' }]}>
                <Input placeholder="Tên vật tư" />
              </Form.Item>
            </Col>
            <Col xs={24} sm={12}>
              <Form.Item label="Item No." name="itemNo">
                <Input placeholder="Mã vật tư" />
              </Form.Item>
            </Col>
            <Col xs={24} sm={12}>
              <Form.Item label="Danh mục" name="categoryId" rules={[{ required: true, message: 'Vui lòng chọn danh mục' }]}>
                <Select showSearch allowClear placeholder="Chọn danh mục" options={categoryOptions} filterOption={filterFn} />
              </Form.Item>
            </Col>
            <Col xs={24} sm={12}>
              <Form.Item
                label={companyLocked ? (
                  <Space size={4}>
                    <LockOutlined style={{ color: '#faad14' }} />
                    <span>Công ty</span>
                  </Space>
                ) : 'Công ty'}
                name="companyId"
                extra={companyLocked ? 'Đã từng được cấp phát — không thể đổi công ty' : undefined}
              >
                <CompanyTreeSelect disabled={companyLocked} />
              </Form.Item>
            </Col>
          </Row>

          {/* ── Số lượng ── */}
          <Divider titlePlacement="start" plain>Số lượng</Divider>
          <Row gutter={[16, 8]}>
            <Col xs={24} sm={12}>
              <Form.Item label="Số lượng" name="qty" rules={[{ required: true, message: 'Vui lòng nhập số lượng' }]}>
                <InputNumber min={0} style={{ width: '100%' }} placeholder="Số lượng" />
              </Form.Item>
            </Col>
            <Col xs={24} sm={12}>
              <Form.Item label="Số lượng tối thiểu" name="minAmt">
                <InputNumber min={0} style={{ width: '100%' }} placeholder="Số lượng cảnh báo" />
              </Form.Item>
            </Col>
          </Row>

          {/* ── Nhà cung cấp & mua hàng ── */}
          <Divider titlePlacement="start" plain>Nhà cung cấp & mua hàng</Divider>
          <Row gutter={[16, 8]}>
            <Col xs={24} sm={12}>
              <Form.Item label="Nhà cung cấp" name="supplierId">
                <Select showSearch allowClear placeholder="Chọn nhà cung cấp" options={supplierOptions} filterOption={filterFn} />
              </Form.Item>
            </Col>
            <Col xs={24} sm={12}>
              <Form.Item label="Nhà sản xuất" name="manufacturerId">
                <Select showSearch allowClear placeholder="Chọn nhà sản xuất" options={manufacturerOptions} filterOption={filterFn} />
              </Form.Item>
            </Col>
            <Col xs={24} sm={12}>
              <Form.Item label="Vị trí" name="locationId">
                <Select showSearch allowClear placeholder="Chọn vị trí" options={locationOptions} filterOption={filterFn} />
              </Form.Item>
            </Col>
            <Col xs={24} sm={12}>
              <Form.Item label="Model No." name="modelNumber">
                <Input placeholder="Số hiệu Model" />
              </Form.Item>
            </Col>
            <Col xs={24} sm={12}>
              <Form.Item label="Order Number" name="orderNumber">
                <Input placeholder="Số đơn hàng" />
              </Form.Item>
            </Col>
            <Col xs={24} sm={12}>
              <Form.Item label="Purchase Date" name="purchaseDate">
                <DatePicker style={{ width: '100%' }} format="YYYY-MM-DD" placeholder="Chọn ngày" />
              </Form.Item>
            </Col>
            <Col xs={24} sm={12}>
              <Form.Item label="Unit Cost" name="purchaseCost">
                <InputNumber min={0} style={{ width: '100%' }} addonAfter="VND" placeholder="Đơn giá" />
              </Form.Item>
            </Col>
          </Row>

          {/* ── Ghi chú ── */}
          <Divider titlePlacement="start" plain>Ghi chú</Divider>
          <Form.Item label="Ghi chú" name="notes">
            <Input.TextArea rows={3} placeholder="Ghi chú thêm" />
          </Form.Item>
        </Form>
      </Spin>
    </Modal>
  );
}

