import { useEffect, useState } from 'react';
import {
  App, Button, Col, DatePicker, Divider, Form, Input, InputNumber, Modal, Row, Select, Space, Spin, Switch, Tag, Tooltip, Grid,
} from 'antd';
import { LockOutlined, PlusOutlined } from '@ant-design/icons';
import apiClient from '../../../services/api-client';
import { licensesApi, type CreateLicensePayload, type LicenseDetailDto } from '../services/licenses.service';
import { isSuperUser } from '../../../services/keycloak';
import dayjs from 'dayjs';
import CompanyTreeSelect from '../../../components/common/CompanyTreeSelect';

// CategoryType.License = 5 (must match Domain/Enums/CategoryType.cs)
const LICENSE_CATEGORY_TYPE = 5;

interface LicenseFormModalProps {
  open: boolean;
  /** null/undefined = create mode; otherwise edit the given license. */
  licenseId?: string | null;
  onClose: () => void;
  onSaved: () => void;
}

/** Read-only locked field: Tag + lock icon + tooltip. */
function LockedFieldTag({ value }: { value: string }) {
  return (
    <Tooltip title="Không thể thay đổi sau khi tạo">
      <Tag icon={<LockOutlined />} style={{ marginInlineEnd: 0 }}>{value || '—'}</Tag>
    </Tooltip>
  );
}

export default function LicenseFormModal({ open, licenseId, onClose, onSaved }: LicenseFormModalProps) {
  const { message, modal } = App.useApp();
  const { useBreakpoint } = Grid;
  const screens = useBreakpoint();
  const isMobile = !screens.md;
  const [form] = Form.useForm();
  const isEdit = !!licenseId;
  const superUser = isSuperUser();

  const [loading, setLoading] = useState(false);
  const [saving, setSaving] = useState(false);
  const [loaded, setLoaded] = useState<LicenseDetailDto | null>(null);

  const [categoryOptions, setCategoryOptions] = useState<{ label: string; value: string }[]>([]);
  const [newCategoryName, setNewCategoryName] = useState('');
  const [supplierOptions, setSupplierOptions] = useState<{ label: string; value: string }[]>([]);
  const [newSupplierName, setNewSupplierName] = useState('');
  const [manufacturerOptions, setManufacturerOptions] = useState<{ label: string; value: string }[]>([]);
  const [newManufacturerName, setNewManufacturerName] = useState('');

  const loadCategories = async () => {
    try {
      const res = await apiClient.get('/categories', { params: { type: LICENSE_CATEGORY_TYPE } });
      setCategoryOptions(((res.data?.data ?? []) as { id: string; name: string }[]).map(c => ({ label: c.name, value: c.id })));
    } catch { /* non-critical */ }
  };

  const loadCommon = async () => {
    try {
      const [supRes, mfrRes] = await Promise.all([
        apiClient.get('/suppliers'), apiClient.get('/manufacturers'),
      ]);
      setSupplierOptions(((supRes.data?.data ?? []) as { id: string; name: string }[]).map(s => ({ label: s.name, value: s.id })));
      setManufacturerOptions(((mfrRes.data?.data ?? []) as { id: string; name: string }[]).map(m => ({ label: m.name, value: m.id })));
    } catch { /* non-critical */ }
  };

  const addCategory = async () => {
    const name = newCategoryName.trim();
    if (!name) { message.warning('Nhập tên danh mục mới'); return; }
    try {
      const res = await apiClient.post('/categories', { name, categoryType: LICENSE_CATEGORY_TYPE });
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
  useEffect(() => {
    if (!open) return;
    void loadCategories();
    void loadCommon();
    if (licenseId) {
      setLoading(true);
      licensesApi.get(licenseId)
        .then(r => {
          const d = r.data.data as LicenseDetailDto;
          setLoaded(d);
          form.setFieldsValue({
            name: d.name, serial: d.serial, seats: d.seats, reassignable: d.reassignable,
            expirationDate: d.expirationDate ? dayjs(d.expirationDate) : undefined,
            terminationDate: d.terminationDate ? dayjs(d.terminationDate) : undefined,
            purchaseCost: d.purchaseCost,
            purchaseDate: d.purchaseDate ? dayjs(d.purchaseDate) : undefined,
            orderNumber: d.orderNumber, minSeats: d.minSeats, notes: d.notes,
            supplierId: d.supplierId, manufacturerId: d.manufacturerId,
          });
        })
        .catch(() => message.error('Không thể tải bản quyền'))
        .finally(() => setLoading(false));
    } else {
      setLoaded(null);
      form.resetFields();
      form.setFieldsValue({ reassignable: true, seats: 1 });
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open, licenseId]);

  const submit = async (vals: Record<string, unknown>) => {
    setSaving(true);
    try {
      const common = {
        name: String(vals.name),
        serial: vals.serial ? String(vals.serial) : null,
        seats: typeof vals.seats === 'number' ? vals.seats : 0,
        reassignable: !!vals.reassignable,
        expirationDate: vals.expirationDate ? dayjs(vals.expirationDate as dayjs.Dayjs).toISOString() : null,
        terminationDate: vals.terminationDate ? dayjs(vals.terminationDate as dayjs.Dayjs).toISOString() : null,
        purchaseCost: typeof vals.purchaseCost === 'number' ? vals.purchaseCost : null,
        purchaseDate: vals.purchaseDate ? dayjs(vals.purchaseDate as dayjs.Dayjs).toISOString() : null,
        orderNumber: vals.orderNumber ? String(vals.orderNumber) : null,
        minSeats: typeof vals.minSeats === 'number' ? vals.minSeats : null,
        notes: vals.notes ? String(vals.notes) : null,
        supplierId: vals.supplierId ? String(vals.supplierId) : null,
        manufacturerId: vals.manufacturerId ? String(vals.manufacturerId) : null,
      };
      if (isEdit && licenseId) {
        // Whitelist update — locked fields (categoryId/companyId) are intentionally NOT sent.
        await licensesApi.update(licenseId, common);
        message.success('Cập nhật thành công');
      } else {
        const payload: CreateLicensePayload = {
          ...common,
          categoryId: String(vals.categoryId),
          // Regular users are forced to their own company by the server.
          companyId: superUser ? (vals.companyId ? String(vals.companyId) : undefined) : undefined,
        };
        await licensesApi.create(payload);
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
      title={isEdit ? 'Sửa bản quyền' : 'Tạo bản quyền mới'}
      onCancel={handleClose}
      onOk={() => form.submit()}
      confirmLoading={saving}
      width={isMobile ? '95%' : 760}
      destroyOnHidden
    >
      <Spin spinning={loading}>
        <Form form={form} layout="vertical" onFinish={(v) => void submit(v as Record<string, unknown>)}>
          {/* ── Nhóm 1: Thông tin chung ── */}
          <Divider titlePlacement="start" plain>Thông tin chung</Divider>
          <Row gutter={[16, 8]}>
            <Col xs={24} sm={12}>
              <Form.Item label="Tên" name="name" rules={[{ required: true, message: 'Nhập tên' }]}>
                <Input placeholder="VD: Microsoft Office 2024 Pro" />
              </Form.Item>
            </Col>
            <Col xs={24} sm={12}>
              <Form.Item label="Product Key / Serial" name="serial">
                <Input placeholder="XXXXX-XXXXX-XXXXX" />
              </Form.Item>
            </Col>
            <Col xs={24} sm={12}>
              {isEdit
                ? (
                  <Form.Item label="Danh mục">
                    <LockedFieldTag value={loaded?.category?.name ?? '—'} />
                  </Form.Item>
                )
                : (
                  <Form.Item label="Danh mục" name="categoryId" rules={[{ required: true, message: 'Chọn danh mục' }]}>
                    <Select showSearch placeholder="Chọn danh mục (loại Bản quyền)" options={categoryOptions}
                      filterOption={(input, option) => (option?.label as string)?.toLowerCase().includes(input.toLowerCase())}
                      notFoundContent={
                        <Space style={{ padding: '4px 8px' }}>
                          <Input size="small" placeholder="Tên danh mục mới" value={newCategoryName}
                            onChange={e => setNewCategoryName(e.target.value)}
                            onPressEnter={() => void addCategory()} style={{ width: 180 }} />
                          <Button size="small" type="primary" icon={<PlusOutlined />} onClick={() => void addCategory()}>Thêm</Button>
                        </Space>
                      }
                    />
                  </Form.Item>
                )}
            </Col>
            <Col xs={24} sm={12}>
              {isEdit
                ? (
                  <Form.Item label="Công ty">
                    <LockedFieldTag value={loaded?.company?.name ?? '—'} />
                  </Form.Item>
                )
                : superUser && (
                  <Form.Item label="Công ty" name="companyId" rules={[{ required: true, message: 'Chọn công ty' }]}>
                    <CompanyTreeSelect allowQuickAdd />
                  </Form.Item>
                )}
            </Col>
          </Row>

          {/* ── Nhóm 2: Số chỗ ── */}
          <Divider titlePlacement="start" plain>Số chỗ</Divider>
          <Row gutter={[16, 8]}>
            <Col xs={24} sm={8}>
              <Form.Item label="Tổng số chỗ (Seats)" name="seats" rules={[{ required: true, message: 'Nhập số chỗ' }]}>
                <InputNumber min={1} precision={0} style={{ width: '100%' }} placeholder="VD: 5" />
              </Form.Item>
            </Col>
            <Col xs={24} sm={8}>
              <Form.Item label="Cảnh báo còn ít chỗ (MinSeats)" name="minSeats" tooltip="Cảnh báo khi số chỗ còn trống <= giá trị này">
                <InputNumber min={0} precision={0} style={{ width: '100%' }} placeholder="VD: 1" />
              </Form.Item>
            </Col>
            <Col xs={24} sm={8}>
              <Form.Item label="Cho phép thu hồi & cấp lại" name="reassignable" valuePropName="checked"
                tooltip="Tắt (false) = license OEM gắn chết vào đối tượng, không checkin được">
                <Switch />
              </Form.Item>
            </Col>
          </Row>

          {/* ── Nhóm 3: Hết hạn & hợp đồng ── */}
          <Divider titlePlacement="start" plain>Hết hạn & hợp đồng</Divider>
          <Row gutter={[16, 8]}>
            <Col xs={24} sm={12}>
              <Form.Item label="Ngày hết hạn" name="expirationDate">
                <DatePicker style={{ width: '100%' }} format="DD/MM/YYYY" placeholder="Chọn ngày" />
              </Form.Item>
            </Col>
            <Col xs={24} sm={12}>
              <Form.Item label="Ngày chấm dứt hợp đồng" name="terminationDate" tooltip="Áp dụng cho license thuê bao">
                <DatePicker style={{ width: '100%' }} format="DD/MM/YYYY" placeholder="Chọn ngày" />
              </Form.Item>
            </Col>
          </Row>
          {/* ── Nhóm 4: Nhà sản xuất & mua hàng ── */}
          <Divider titlePlacement="start" plain>Nhà sản xuất & mua hàng</Divider>
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
              <Form.Item label="Giá mua" name="purchaseCost">
                <InputNumber min={0} precision={2} style={{ width: '100%' }} placeholder="0" />
              </Form.Item>
            </Col>
          </Row>

          {/* ── Nhóm 5: Ghi chú ── */}
          <Divider titlePlacement="start" plain>Ghi chú</Divider>
          <Form.Item label="Ghi chú" name="notes">
            <Input.TextArea rows={2} maxLength={1000} showCount placeholder="Ghi chú thêm về bản quyền..." />
          </Form.Item>
        </Form>
      </Spin>
    </Modal>
  );
}