import { useEffect, useState } from 'react';
import {
  Alert, App, Button, Col, DatePicker, Divider, Form, Input, InputNumber,
  Row, Select, Space, Spin,
} from 'antd';
import { LockOutlined } from '@ant-design/icons';
import apiClient from '../../../services/api-client';
import { consumablesApi } from '../services/consumables.service';
import dayjs from 'dayjs';
import { withOptionalFkCleared } from '../../../shared/utils/fk';
import CompanyTreeSelect from '../../../components/common/CompanyTreeSelect';
import { useCompanyField } from '../../../hooks/useCompanyField';
import ModalFormShell from '../../../shared/components/ModalFormShell';
import { applyServerFieldErrors, serverErrorMessage } from '../../../shared/utils/formErrors';

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
  const { showCompanyField } = useCompanyField();
  const { message } = App.useApp();
  const [form] = Form.useForm();
  const isEdit = !!consumableId;

  const [loading, setLoading] = useState(false);
  const [saving, setSaving] = useState(false);
  // ST4 — company cannot change once the consumable has ever been checked out (FIELD_LOCKED).
  const [companyLocked, setCompanyLocked] = useState(false);
  // Đã xác nhận (Confirmed) → chỉ Vị trí + Ghi chú được sửa (mirror backend CONFIRMED_CONSUMABLE_LOCKED).
  const [confirmedLocked, setConfirmedLocked] = useState(false);

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
          // Đã xác nhận → khóa mọi field trừ Vị trí + Ghi chú (mirror CONFIRMED_CONSUMABLE_LOCKED).
          setConfirmedLocked(d.status === 'Confirmed');
        })
        .catch(() => void message.error('Lỗi tải vật tư'))
        .finally(() => setLoading(false));
    } else {
      form.resetFields();
      setCompanyLocked(false);
      setConfirmedLocked(false);
    }
  }, [open, isEdit, consumableId, form, message]);

  const submit = async (values: Record<string, unknown>) => {
    setSaving(true);
    try {
      // [FIX-GUID-EMPTY phần 2] Update: FK tùy chọn bị xóa ⇒ EMPTY_GUID (backend xóa liên kết),
      // KHÔNG gửi undefined/null (backend hiểu là "không gửi" ⇒ giữ nguyên).
      const payload: Record<string, unknown> = {
        ...values,
        ...(isEdit && consumableId
          ? withOptionalFkCleared(values, ['categoryId', 'manufacturerId', 'supplierId', 'locationId', 'companyId'])
          : {}),
        purchaseDate: values.purchaseDate ? dayjs(values.purchaseDate as dayjs.Dayjs).toISOString() : undefined,
      };
      if (!showCompanyField) delete payload.companyId;
      if (isEdit && consumableId) {
        await consumablesApi.update(consumableId, payload);
        void message.success('Cập nhật vật tư thành công');
      } else {
        await consumablesApi.create(payload);
        void message.success('Tạo vật tư thành công');
      }
      onSaved();
    } catch (err: unknown) {
      if (!applyServerFieldErrors(form, err)) {
        void message.error(serverErrorMessage(err, 'Không thể lưu vật tư. Vui lòng thử lại.'));
      }
    } finally {
      setSaving(false);
    }
  };

  const filterFn = (inp: string, opt?: { label?: React.ReactNode }) =>
    typeof opt?.label === 'string' ? opt.label.toLowerCase().includes(inp.toLowerCase()) : false;

  return (
    <ModalFormShell
      open={open}
      title={isEdit ? 'Sửa vật tư tiêu hao' : 'Tạo vật tư tiêu hao'}
      onCancel={onClose}
      onOk={() => form.submit()}
      okText={isEdit ? 'Lưu thay đổi' : 'Tạo vật tư'}
      confirmLoading={saving}
      size="md"
      maskClosable={false}
    >
      <Spin spinning={loading}>
        <Form
          form={form}
          layout="vertical"
          initialValues={{ qty: 1, minAmt: 0 }}
          onFinish={(v) => void submit(v)}
        >
          {confirmedLocked && (
            <Alert
              type="warning"
              showIcon
              style={{ marginBottom: 16 }}
              title="Vật tư đã xác nhận — chỉ Vị trí và Ghi chú được sửa."
            />
          )}
          {/* ── Thông tin cơ bản ── */}
          <Divider titlePlacement="start" plain style={{ marginTop: 0 }}>Thông tin cơ bản</Divider>
          <Row gutter={[16, 8]}>
            <Col xs={24} sm={12}>
              <Form.Item label="Tên vật tư" name="name" rules={[{ required: true, message: 'Vui lòng nhập tên vật tư' }]}>
                <Input placeholder="Nhập tên vật tư" disabled={confirmedLocked} />
              </Form.Item>
            </Col>
            <Col xs={24} sm={12}>
              <Form.Item label="Mã hàng" name="itemNo">
                <Input placeholder="Nhập mã hàng" disabled={confirmedLocked} />
              </Form.Item>
            </Col>
            <Col xs={24} sm={12}>
              <Form.Item label="Danh mục" name="categoryId" rules={[{ required: true, message: 'Vui lòng chọn danh mục' }]}>
                <Select showSearch allowClear placeholder="Chọn danh mục" options={categoryOptions} filterOption={filterFn} disabled={confirmedLocked} />
              </Form.Item>
            </Col>
            {showCompanyField && (
              <Col xs={24} sm={12}>
                <Form.Item
                  label={companyLocked || confirmedLocked ? (
                    <Space size={4}>
                      <LockOutlined style={{ color: '#faad14' }} />
                      <span>Công ty</span>
                    </Space>
                  ) : 'Công ty'}
                  name="companyId"
                  extra={companyLocked ? 'Đã từng được cấp phát — không thể đổi công ty' : (confirmedLocked ? 'Vật tư đã xác nhận — không thể đổi công ty' : undefined)}
                >
                  <CompanyTreeSelect disabled={companyLocked || confirmedLocked} />
                </Form.Item>
              </Col>
            )}
          </Row>

          {/* ── Số lượng ── */}
          <Divider titlePlacement="start" plain>Số lượng</Divider>
          <Row gutter={[16, 8]}>
            <Col xs={24} sm={12}>
              <Form.Item label="Số lượng" name="qty" rules={[{ required: true, message: 'Vui lòng nhập số lượng' }]}>
                <InputNumber min={0} style={{ width: '100%' }} placeholder="Nhập số lượng" disabled={confirmedLocked} />
              </Form.Item>
            </Col>
            <Col xs={24} sm={12}>
              <Form.Item label="Số lượng tối thiểu" name="minAmt">
                <InputNumber min={0} style={{ width: '100%' }} placeholder="Số lượng cảnh báo" disabled={confirmedLocked} />
              </Form.Item>
            </Col>
          </Row>

          {/* ── Nhà cung cấp & mua hàng ── */}
          <Divider titlePlacement="start" plain>Nhà cung cấp & mua hàng</Divider>
          <Row gutter={[16, 8]}>
            <Col xs={24} sm={12}>
              <Form.Item label="Nhà cung cấp" name="supplierId">
                <Select showSearch allowClear placeholder="Chọn nhà cung cấp" options={supplierOptions} filterOption={filterFn} disabled={confirmedLocked} />
              </Form.Item>
            </Col>
            <Col xs={24} sm={12}>
              <Form.Item label="Nhà sản xuất" name="manufacturerId">
                <Select showSearch allowClear placeholder="Chọn nhà sản xuất" options={manufacturerOptions} filterOption={filterFn} disabled={confirmedLocked} />
              </Form.Item>
            </Col>
            <Col xs={24} sm={12}>
              <Form.Item label="Vị trí" name="locationId">
                <Select showSearch allowClear placeholder="Chọn vị trí" options={locationOptions} filterOption={filterFn} />
              </Form.Item>
            </Col>
            <Col xs={24} sm={12}>
              <Form.Item label="Số model" name="modelNumber">
                <Input placeholder="Số hiệu Model" disabled={confirmedLocked} />
              </Form.Item>
            </Col>
            <Col xs={24} sm={12}>
              <Form.Item label="Số đơn hàng" name="orderNumber">
                <Input placeholder="Nhập số đơn hàng" disabled={confirmedLocked} />
              </Form.Item>
            </Col>
            <Col xs={24} sm={12}>
              <Form.Item label="Ngày mua" name="purchaseDate">
                <DatePicker style={{ width: '100%' }} format="YYYY-MM-DD" placeholder="Chọn ngày mua" disabled={confirmedLocked} />
              </Form.Item>
            </Col>
            <Col xs={24} sm={12}>
              <Form.Item label="Đơn giá" name="purchaseCost">
                <Space.Compact block>
                  <InputNumber min={0} style={{ width: '100%' }} placeholder="Đơn giá" disabled={confirmedLocked} />
                  <Button style={{ width: 56 }}>VND</Button>
                </Space.Compact>
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
    </ModalFormShell>
  );
}

