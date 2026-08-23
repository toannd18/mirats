import { useState, useRef, type ReactNode } from 'react';
import { Button, Space, Modal, Form, Input, Select, Card, Divider, Tag, Typography, Popconfirm, message } from 'antd';
import { PlusOutlined, EditOutlined, DeleteOutlined, PhoneOutlined, MailOutlined, LinkOutlined, PrinterOutlined } from '@ant-design/icons';
import { ProList, ProTable } from '@ant-design/pro-components';
import type { ActionType, ProColumns } from '@ant-design/pro-components';
import apiClient from '../../../services/api-client';
import { usePermission } from '../../../hooks/usePermission';
import { useIsMobile } from '../../../hooks/useIsMobile';

const { Text } = Typography;

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
  // ST7b — 1 actionRef dùng chung cho ProTable (desktop) và ProList (mobile Card).
  const actionRef = useRef<ActionType | null>(null);
  const isMobile = useIsMobile();

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

  // ST7b: confirm nằm ở Popconfirm trong renderActions (dùng chung 2 view) —
  // hàm này chỉ thực hiện xóa sau khi đã confirm.
  const handleDelete = async (record: SupplierDto) => {
    try {
      await apiClient.delete(`/suppliers/${record.id}`);
      message.success('Đã xóa');
      actionRef.current?.reload();
    } catch (err: unknown) {
      const e = err as { response?: { data?: { message?: string } } };
      message.error(e?.response?.data?.message || 'Không thể xóa');
    }
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
    } catch (err: unknown) {
      if ((err as { errorFields?: unknown }).errorFields) return;
      const e = err as { response?: { data?: { message?: string } } };
      message.error(e?.response?.data?.message || 'Lỗi lưu');
    }
  };

  const buildAddress = (r: SupplierDto) =>
    [r.address, r.city, r.state, r.country, r.zip].filter(Boolean).join(', ') || '-';

  // ST7b — 1 fetch dùng chung cho ProTable (desktop) và ProList (mobile Card).
  const fetchList = async () => {
    const r = await apiClient.get('/suppliers');
    return { list: (r.data.data || []) as SupplierDto[], total: (r.data.data || []).length };
  };

  // ST7b — action buttons dùng chung desktop/mobile.
  const renderActions = (record: SupplierDto): ReactNode[] => [
    canEdit && <Button key="edit" size="small" icon={<EditOutlined />} onClick={() => handleEdit(record)}>Sửa</Button>,
    canDelete && (
      <Popconfirm key="del" title="Xóa nhà cung cấp này?" onConfirm={() => handleDelete(record)}>
        <Button size="small" danger icon={<DeleteOutlined />}>Xóa</Button>
      </Popconfirm>
    ),
  ].filter(Boolean) as ReactNode[];

  // Modal tạo/sửa — định nghĩa MỘT lần, render chung cho cả mobile và desktop.
  const formModal = (
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
  );

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
      render: (_, record) => <Space size="small">{renderActions(record)}</Space>,
    },
  ];

  // ─── Mobile (ST7b): ProList Card thay Table — cùng fetch + cùng renderActions ───
  if (isMobile) {
    return (
      <div>
        <ProList<SupplierDto>
          headerTitle="Danh sách nhà cung cấp"
          actionRef={actionRef}
          rowKey="id"
          ghost
          cardProps={false}
          search={false}
          grid={{ gutter: 16, xs: 1, sm: 1 }}
          toolBarRender={() => [
            canCreate && (
              <Button key="add" type="primary" icon={<PlusOutlined />} onClick={handleAdd}>Thêm</Button>
            ),
          ]}
          request={async () => {
            try {
              const { list, total } = await fetchList();
              return { data: list, success: true, total };
            } catch {
              message.error('Lỗi tải danh sách nhà cung cấp');
              return { data: [], success: false, total: 0 };
            }
          }}
          pagination={{ defaultPageSize: 20, showSizeChanger: false }}
          itemRender={(record) => (
            <Card hoverable style={{ borderRadius: 12, marginBottom: 16 }} styles={{ body: { padding: 16 } }}>
              <div style={{ display: 'flex', alignItems: 'center', gap: 10, marginBottom: 10 }}>
                <Text strong style={{ fontSize: 15 }}>{record.name}</Text>
                {record.code && <Tag style={{ marginInlineEnd: 0 }}>{record.code}</Tag>}
              </div>
              <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '6px 12px', padding: '10px 12px', background: '#fafafa', borderRadius: 8 }}>
                <Text type="secondary" style={{ fontSize: 12 }}>Địa chỉ</Text>
                <Text style={{ fontSize: 13 }}>{buildAddress(record)}</Text>
                <Text type="secondary" style={{ fontSize: 12 }}>Người liên hệ</Text>
                <Text style={{ fontSize: 13 }}>{record.contactName || '-'}</Text>
                <Text type="secondary" style={{ fontSize: 12 }}>Điện thoại</Text>
                <Text style={{ fontSize: 13 }}>
                  {record.phone ? <a href={`tel:${record.phone}`}>{record.phone}</a> : '-'}
                </Text>
                <Text type="secondary" style={{ fontSize: 12 }}>Email</Text>
                <Text style={{ fontSize: 13 }}>
                  {record.contactEmail ? <a href={`mailto:${record.contactEmail}`} style={{ wordBreak: 'break-all' }}>{record.contactEmail}</a> : '-'}
                </Text>
              </div>
              <Divider style={{ margin: '10px 0' }} />
              <Space size="small" wrap>{renderActions(record)}</Space>
            </Card>
          )}
        />
        {formModal}
      </div>
    );
  }

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
            const { list, total } = await fetchList();
            return { data: list, success: true, total };
          } catch {
            message.error('Lỗi tải danh sách nhà cung cấp');
            return { data: [], success: false, total: 0 };
          }
        }}
        pagination={{ defaultPageSize: 20, showSizeChanger: true }}
        scroll={{ x: 'max-content' }}
      />
      {formModal}
    </div>
  );
}
