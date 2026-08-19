import { useState, useRef } from 'react';
import { Button, Space, Modal, Form, Input, Select, message } from 'antd';
import { PlusOutlined, EditOutlined, DeleteOutlined, PhoneOutlined, MailOutlined, LinkOutlined, PrinterOutlined } from '@ant-design/icons';
import { ProTable } from '@ant-design/pro-components';
import type { ActionType, ProColumns } from '@ant-design/pro-components';
import apiClient from '../../../services/api-client';
import { usePermission } from '../../../hooks/usePermission';

interface SupplierDto {
  id: string;
  code: string;
  name: string;
  url?: string | null;
  address?: string | null;
  city?: string | null;
  state?: string | null;
  country?: string | null;
  zip?: string | null;
  phone?: string | null;
  fax?: string | null;
  contactName?: string | null;
  contactEmail?: string | null;
}

const COUNTRY_OPTIONS = [
  { label: 'Việt Nam', value: 'Vietnam' },
  { label: 'United States', value: 'USA' },
  { label: 'Japan', value: 'Japan' },
  { label: 'Korea', value: 'Korea' },
  { label: 'China', value: 'China' },
];

export default function SupplierListPage() {
  const [open, setOpen] = useState(false);
  const [editingId, setEditingId] = useState<string | null>(null);
  const [form] = Form.useForm();
  const actionRef = useRef<ActionType | null>(null);

  // ST6b — permission gating (mirrors backend [Authorize(Policy=...)]).
  const canCreate = usePermission('suppliers.create');
  const canEdit = usePermission('suppliers.edit');
  const canDelete = usePermission('suppliers.delete');

  const handleEdit = (record: SupplierDto) => {
    setEditingId(record.id);
    form.setFieldsValue(record);
    setOpen(true);
  };

  const handleAdd = () => {
    setEditingId(null);
    form.resetFields();
    setOpen(true);
  };

  const handleClose = () => {
    setEditingId(null);
    form.resetFields();
    setOpen(false);
  };

  const handleDelete = (record: SupplierDto) => {
    Modal.confirm({
      title: 'Xóa nhà cung cấp?',
      content: `Bạn có chắc muốn xóa "${record.name}"?`,
      onOk: async () => {
        try {
          await apiClient.delete(`/suppliers/${record.id}`);
          message.success('Đã xóa');
          actionRef.current?.reload();
        } catch (err: any) {
          message.error(err?.response?.data?.message || 'Không thể xóa');
        }
      },
    });
  };

  const save = async () => {
    try {
      const values = await form.validateFields();
      if (editingId) {
        await apiClient.put(`/suppliers/${editingId}`, values);
        message.success('Đã cập nhật');
      } else {
        await apiClient.post('/suppliers', values);
        message.success('Đã tạo nhà cung cấp');
      }
      handleClose();
      actionRef.current?.reload();
    } catch (err: any) {
      if (err?.errorFields) return;
      message.error(err?.response?.data?.message || 'Lỗi lưu');
    }
  };

  const buildAddress = (r: SupplierDto) =>
    [r.address, r.city, r.state, r.country, r.zip].filter(Boolean).join(', ') || '-';

  const columns: ProColumns<SupplierDto>[] = [
    { title: 'Mã NCC', dataIndex: 'code', key: 'code', width: 100 },
    { title: 'Tên NCC', dataIndex: 'name', key: 'name' },
    { title: 'Địa chỉ', key: 'address', render: (_, r) => buildAddress(r) },
    { title: 'Người liên hệ', dataIndex: 'contactName', key: 'contactName', render: (_, r) => r.contactName || '-' },
    { title: 'Điện thoại', dataIndex: 'phone', key: 'phone', render: (_, r) => r.phone || '-' },
    {
      title: 'Email', dataIndex: 'contactEmail', key: 'contactEmail',
      render: (_, r) => r.contactEmail ? <a href={`mailto:${r.contactEmail}`}>{r.contactEmail}</a> : '-',
    },
    {
      title: 'Hành động', key: 'actions', valueType: 'option' as const, width: 140,
      render: (_, record) => (
        <Space size="small">
          {canEdit && <Button size="small" icon={<EditOutlined />} onClick={() => handleEdit(record)}>Sửa</Button>}
          {canDelete && <Button size="small" danger icon={<DeleteOutlined />} onClick={() => handleDelete(record)}>Xóa</Button>}
        </Space>
      ),
    },
  ];

  return (
    <div>
      <ProTable<SupplierDto>
        headerTitle="Danh sách nhà cung cấp"
        rowKey="id"
        size="small"
        columns={columns}
        actionRef={actionRef}
        search={false}
        options={{ reload: true, density: true, setting: true }}
        toolBarRender={() => [
          canCreate && (
            <Button type="primary" icon={<PlusOutlined />} onClick={handleAdd}>Thêm NCC</Button>
          ),
        ]}
        request={async () => {
          try {
            const r = await apiClient.get('/suppliers');
            return { data: r.data.data || [], success: true, total: (r.data.data || []).length };
          } catch {
            message.error('Lỗi tải danh sách nhà cung cấp');
            return { data: [], success: false, total: 0 };
          }
        }}
        pagination={{ defaultPageSize: 20, showSizeChanger: true }}
        scroll={{ x: 'max-content' }}
      />
      <Modal
        open={open}
        title={editingId ? 'Sửa nhà cung cấp' : 'Thêm nhà cung cấp'}
        onOk={form.submit}
        onCancel={handleClose}
        destroyOnHidden
        width={640}
      >
        <Form form={form} layout="vertical" onFinish={save}>
          <Space size="middle" style={{ width: '100%' }}>
            <Form.Item label="Mã NCC" name="code" style={{ width: 120 }}
              rules={[
                { required: true, message: 'Vui lòng nhập mã' },
                { pattern: /^[A-Z0-9]{2,5}$/, message: 'Mã 2-5 ký tự viết hoa (A-Z, 0-9)' },
              ]}
              getValueFromEvent={(e) => e.target.value.toUpperCase()}
            >
              <Input placeholder="VD: APPL" maxLength={5} style={{ textTransform: 'uppercase' }} />
            </Form.Item>
            <Form.Item label="Tên NCC" name="name" style={{ flex: 1 }} rules={[{ required: true, message: 'Vui lòng nhập tên' }]}>
              <Input placeholder="Tên nhà cung cấp" />
            </Form.Item>
          </Space>
          <Form.Item label="Địa chỉ" name="address"><Input placeholder="Số nhà, đường..." /></Form.Item>
          <Space size="middle" style={{ width: '100%' }}>
            <Form.Item label="Thành phố" name="city" style={{ flex: 1 }}><Input /></Form.Item>
            <Form.Item label="Bang/Tỉnh" name="state" style={{ flex: 1 }}><Input /></Form.Item>
          </Space>
          <Space size="middle" style={{ width: '100%' }}>
            <Form.Item label="Quốc gia" name="country" style={{ flex: 1 }}>
              <Select showSearch options={COUNTRY_OPTIONS} placeholder="Chọn quốc gia" allowClear />
            </Form.Item>
            <Form.Item label="Mã bưu điện" name="zip" style={{ flex: 1 }}><Input /></Form.Item>
          </Space>
          <Space size="middle" style={{ width: '100%' }}>
            <Form.Item label="Số điện thoại" name="phone" style={{ flex: 1 }}>
              <Input prefix={<PhoneOutlined />} />
            </Form.Item>
            <Form.Item label="Fax" name="fax" style={{ flex: 1 }}>
              <Input prefix={<PrinterOutlined />} />
            </Form.Item>
          </Space>
          <Form.Item label="Người liên hệ" name="contactName"><Input /></Form.Item>
          <Space size="middle" style={{ width: '100%' }}>
            <Form.Item label="Email" name="contactEmail" style={{ flex: 1 }} rules={[{ type: 'email', message: 'Email không hợp lệ' }]}>
              <Input prefix={<MailOutlined />} />
            </Form.Item>
            <Form.Item label="Website" name="url" style={{ flex: 1 }} rules={[{ type: 'url', message: 'URL không hợp lệ' }]}>
              <Input prefix={<LinkOutlined />} />
            </Form.Item>
          </Space>
        </Form>
      </Modal>
    </div>
  );
}
