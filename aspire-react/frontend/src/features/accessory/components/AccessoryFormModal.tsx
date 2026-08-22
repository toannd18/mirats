import { useEffect, useState } from 'react';
import {
  App, Button, Col, DatePicker, Divider, Form, Grid, Input, InputNumber, Modal,
  Row, Select, Space, Spin,
} from 'antd';
import { LockOutlined, SaveOutlined } from '@ant-design/icons';
import apiClient from '../../../services/api-client';
import { accessoriesApi } from '../services/accessories.service';
import dayjs from 'dayjs';
import CompanyTreeSelect from '../../../components/common/CompanyTreeSelect';

interface AccessoryFormModalProps {
  open: boolean;
  /** null/undefined = create mode; otherwise edit the given accessory. */
  accessoryId?: string | null;
  onClose: () => void;
  onSaved: () => void;
}

interface OptionItem { label: string; value: string; }

// ==================== Component ====================

/**
 * Modal Tạo mới/Sửa phụ kiện (thay thế AccessoryFormPage trang riêng).
 * Pattern đồng bộ ComponentFormModal/ConsumableFormModal (Task A/B/2). Modal mở TẠI CHỖ
 * bằng state cục bộ — KHÔNG navigate để mở modal (bài học Task A).
 *
 * Giữ nguyên 100% logic nghiệp vụ của AccessoryFormPage:
 * - Field: name*, itemNo, categoryId* (loại Accessory — so sánh STRING, không số), modelNumber,
 *   orderNumber, qty* (min 0, default 1), minAmt (default 0), purchaseCost (VND, format nghìn),
 *   purchaseDate, companyId (CompanyTreeSelect dùng chung), locationId, manufacturerId, supplierId, notes.
 * - Task M2 — CompanyId LOCK khi phụ kiện đã từng được cấp phát (mirror backend FIELD_LOCKED:
 *   fetch /checkouts, non-empty → disable field, giữ nguyên giá trị khi submit).
 */
export default function AccessoryFormModal({ open, accessoryId, onClose, onSaved }: AccessoryFormModalProps) {
  const { message, modal } = App.useApp();
  const { useBreakpoint } = Grid;
  const screens = useBreakpoint();
  const isMobile = !screens.md;
  const [form] = Form.useForm();
  const isEdit = !!accessoryId;

  const [loading, setLoading] = useState(false);
  const [saving, setSaving] = useState(false);
  // Task M2 — company cannot change once the accessory has ever been checked out (FIELD_LOCKED).
  const [companyLocked, setCompanyLocked] = useState(false);

  const [categoryOptions, setCategoryOptions] = useState<OptionItem[]>([]);
  const [manufacturerOptions, setManufacturerOptions] = useState<OptionItem[]>([]);
  const [supplierOptions, setSupplierOptions] = useState<OptionItem[]>([]);
  const [locationOptions, setLocationOptions] = useState<OptionItem[]>([]);

  // ──── Options (only Accessory-type categories — API serializes enum as STRING) ────
  useEffect(() => {
    Promise.all([
      apiClient.get('/categories'),
      apiClient.get('/manufacturers'),
      apiClient.get('/suppliers'),
      apiClient.get('/locations'),
    ])
      .then(([catRes, mfrRes, supRes, locRes]) => {
        const accessoryCategories = (catRes.data.data || []).filter(
          (c: { categoryType: string | number }) => c.categoryType === 'Accessory',
        );
        setCategoryOptions(accessoryCategories.map((c: { id: string; name: string }) => ({ label: c.name, value: c.id })));
        setManufacturerOptions((mfrRes.data.data || []).map((m: { id: string; name: string }) => ({ label: m.name, value: m.id })));
        setSupplierOptions((supRes.data.data || []).map((s: { id: string; name: string }) => ({ label: s.name, value: s.id })));
        setLocationOptions((locRes.data.data || []).map((l: { id: string; name: string }) => ({ label: l.name, value: l.id })));
      })
      .catch(() => { /* non-critical */ });
  }, []);

  // ──── Load edit data + company-lock flag every time the modal opens ────
  useEffect(() => {
    if (!open) return;
    if (isEdit && accessoryId) {
      setLoading(true);
      Promise.all([
        apiClient.get(`/accessories/${accessoryId}`),
        apiClient.get(`/accessories/${accessoryId}/checkouts`).catch(() => ({ data: { data: [] } })),
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
        .catch(() => void message.error('Lỗi tải phụ kiện'))
        .finally(() => setLoading(false));
    } else {
      form.resetFields();
      form.setFieldsValue({ qty: 1, minAmt: 0 });
      setCompanyLocked(false);
    }
  }, [open, isEdit, accessoryId, form, message]);

  const submit = async (values: Record<string, unknown>) => {
    setSaving(true);
    try {
      // Same payload contract as the old AccessoryFormPage (backend whitelists via
      // UpdateAccessoryRequest patch semantics — Task M2 keeps fields not sent untouched).
      const payload = {
        ...values,
        purchaseDate: values.purchaseDate ? dayjs(values.purchaseDate as dayjs.Dayjs).toISOString() : undefined,
      };
      if (isEdit && accessoryId) {
        await accessoriesApi.update(accessoryId, payload);
        void message.success('Cập nhật phụ kiện thành công');
      } else {
        await accessoriesApi.create(payload);
        void message.success('Tạo phụ kiện thành công');
      }
      onSaved();
    } catch (err: unknown) {
      const e = err as { response?: { data?: { message?: string } } };
      void message.error(e?.response?.data?.message ?? 'Lỗi lưu phụ kiện');
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
      title={isEdit ? 'Sửa phụ kiện' : 'Tạo phụ kiện mới'}
      onCancel={handleClose}
      width={isMobile ? '95%' : 720}
      destroyOnHidden
      mask={{ closable: false }}
      footer={[
        <Button key="cancel" onClick={handleClose}>Hủy</Button>,
        <Button key="submit" type="primary" htmlType="submit" form="accessory-form-modal"
          loading={saving} icon={<SaveOutlined />}>
          {isEdit ? 'Cập nhật' : 'Tạo mới'}
        </Button>,
      ]}
      styles={{ body: { maxHeight: '68vh', overflowY: 'auto', padding: '8px 24px' } }}
    >
      <Spin spinning={loading}>
        <Form id="accessory-form-modal" form={form} layout="vertical" onFinish={(v) => void submit(v)}>
          {/* ── Thông tin chung ── */}
          <Divider titlePlacement="start" plain style={{ marginTop: 0 }}>Thông tin chung</Divider>
          <Row gutter={[16, 8]}>
            <Col xs={24} sm={12}>
              <Form.Item label="Tên phụ kiện" name="name" rules={[{ required: true, message: 'Vui lòng nhập tên' }]}>
                <Input placeholder="Nhập tên phụ kiện" />
              </Form.Item>
            </Col>
            <Col xs={24} sm={12}>
              <Form.Item label="Item No." name="itemNo">
                <Input placeholder="Mã phụ kiện (tự động hoặc thủ công)" />
              </Form.Item>
            </Col>
            <Col xs={24} sm={12}>
              <Form.Item label="Danh mục" name="categoryId" rules={[{ required: true, message: 'Vui lòng chọn danh mục phụ kiện' }]}>
                <Select showSearch allowClear placeholder="Chọn danh mục phụ kiện" options={categoryOptions}
                  filterOption={filterFn} notFoundContent="Không có danh mục phụ kiện nào" />
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
          </Row>

          {/* ── Tồn kho ── */}
          <Divider titlePlacement="start" plain>Tồn kho</Divider>
          <Row gutter={[16, 8]}>
            <Col xs={24} sm={12}>
              <Form.Item label="Số lượng" name="qty" rules={[{ required: true, message: 'Vui lòng nhập số lượng' }]}>
                <InputNumber min={0} style={{ width: '100%' }} placeholder="0" />
              </Form.Item>
            </Col>
            <Col xs={24} sm={12}>
              <Form.Item label="Ngưỡng cảnh báo" name="minAmt"
                tooltip="Khi số lượng còn lại ≤ giá trị này, hệ thống sẽ cảnh báo tồn kho thấp">
                <InputNumber min={0} style={{ width: '100%' }} placeholder="0" />
              </Form.Item>
            </Col>
            <Col xs={24} sm={12}>
              <Form.Item label="Đơn giá" name="purchaseCost">
                <Space.Compact block>
                  <InputNumber min={0} style={{ width: '100%' }} placeholder="0"
                    formatter={(value) => `${value}`.replace(/\B(?=(\d{3})+(?!\d))/g, ',')}
                    parser={(value) => (value?.replace(/,/g, '') ?? '') as unknown as 0} />
                  <Button style={{ width: 56 }}>VND</Button>
                </Space.Compact>
              </Form.Item>
            </Col>
            <Col xs={24} sm={12}>
              <Form.Item label="Ngày mua" name="purchaseDate">
                <DatePicker style={{ width: '100%' }} format="DD/MM/YYYY" placeholder="Chọn ngày" />
              </Form.Item>
            </Col>
          </Row>

          {/* ── Tổ chức & Vị trí ── */}
          <Divider titlePlacement="start" plain>Tổ chức & Vị trí</Divider>
          <Row gutter={[16, 8]}>
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
            <Col xs={24} sm={12}>
              <Form.Item label="Vị trí" name="locationId">
                <Select showSearch allowClear placeholder="Chọn vị trí lưu trữ" options={locationOptions} filterOption={filterFn} />
              </Form.Item>
            </Col>
            <Col xs={24} sm={12}>
              <Form.Item label="Nhà sản xuất" name="manufacturerId">
                <Select showSearch allowClear placeholder="Chọn nhà sản xuất" options={manufacturerOptions} filterOption={filterFn} />
              </Form.Item>
            </Col>
            <Col xs={24} sm={12}>
              <Form.Item label="Nhà cung cấp" name="supplierId">
                <Select showSearch allowClear placeholder="Chọn nhà cung cấp" options={supplierOptions} filterOption={filterFn} />
              </Form.Item>
            </Col>
          </Row>

          {/* ── Ghi chú ── */}
          <Divider titlePlacement="start" plain>Ghi chú</Divider>
          <Form.Item label="Ghi chú" name="notes">
            <Input.TextArea rows={3} maxLength={1000} showCount placeholder="Nhập ghi chú thêm về phụ kiện này..." />
          </Form.Item>
        </Form>
      </Spin>
    </Modal>
  );
}
