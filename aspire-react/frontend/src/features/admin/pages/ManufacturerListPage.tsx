import { useState, useRef, type ReactNode } from 'react';
import { Button, Space, Modal, Form, Input, Card, Divider, Tag, Typography, Popconfirm, message } from 'antd';
import { PlusOutlined, EditOutlined, DeleteOutlined } from '@ant-design/icons';
import { ProList, ProTable } from '@ant-design/pro-components';
import type { ActionType, ProColumns } from '@ant-design/pro-components';
import apiClient from '../../../services/api-client';
import { usePermission } from '../../../hooks/usePermission';
import { useIsMobile } from '../../../hooks/useIsMobile';

const { Text } = Typography;

interface ManufacturerDto {
  id: string;
  code: string;
  name: string;
  url?: string | null;
  supportUrl?: string | null;
  supportEmail?: string | null;
}

export default function ManufacturerListPage() {
  const [open, setOpen] = useState(false);
  const [editingId, setEditingId] = useState<string | null>(null);
  const [form] = Form.useForm();
  // ST7b — 1 actionRef dùng chung cho ProTable (desktop) và ProList (mobile Card).
  const actionRef = useRef<ActionType | null>(null);
  const isMobile = useIsMobile();

  // ST6b — permission gating (mirrors backend [Authorize(Policy=...)]).
  const canCreate = usePermission('manufacturers.create');
  const canEdit = usePermission('manufacturers.edit');
  const canDelete = usePermission('manufacturers.delete');

  const handleEdit = (record: ManufacturerDto) => {
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
  const handleDelete = async (record: ManufacturerDto) => {
    try {
      await apiClient.delete(`/manufacturers/${record.id}`);
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
        await apiClient.put(`/manufacturers/${editingId}`, values);
        message.success('Đã cập nhật');
      } else {
        await apiClient.post('/manufacturers', values);
        message.success('Đã tạo nhà sản xuất');
      }
      handleClose();
      actionRef.current?.reload();
    } catch (err: unknown) {
      if ((err as { errorFields?: unknown }).errorFields) return;
      const e = err as { response?: { data?: { message?: string } } };
      message.error(e?.response?.data?.message || 'Lỗi lưu');
    }
  };

  // ST7b — 1 fetch dùng chung cho ProTable (desktop) và ProList (mobile Card).
  const fetchList = async () => {
    const r = await apiClient.get('/manufacturers');
    return { list: (r.data.data || []) as ManufacturerDto[], total: (r.data.data || []).length };
  };

  // ST7b — action buttons dùng chung desktop/mobile: permission-gating + handler MỘT chỗ.
  const renderActions = (record: ManufacturerDto): ReactNode[] => [
    canEdit && <Button key="edit" size="small" icon={<EditOutlined />} onClick={() => handleEdit(record)}>Sửa</Button>,
    canDelete && (
      <Popconfirm key="del" title="Xóa nhà sản xuất này?" onConfirm={() => handleDelete(record)}>
        <Button size="small" danger icon={<DeleteOutlined />}>Xóa</Button>
      </Popconfirm>
    ),
  ].filter(Boolean) as ReactNode[];

  // Modal tạo/sửa — định nghĩa MỘT lần, render chung cho cả mobile và desktop.
  const formModal = (
    <Modal
      open={open}
      title={editingId ? 'Sửa nhà sản xuất' : 'Thêm nhà sản xuất'}
      onOk={form.submit}
      onCancel={handleClose}
      destroyOnHidden
    >
      <Form form={form} layout="vertical" onFinish={save}>
        <Form.Item
          label="Mã NSX"
          name="code"
          rules={[
            { required: true, message: 'Vui lòng nhập mã' },
            { pattern: /^[A-Z0-9]{2,5}$/, message: 'Mã bắt buộc 2-5 ký tự viết hoa (A-Z, 0-9)' },
          ]}
          getValueFromEvent={(e) => e.target.value.toUpperCase()}
        >
          <Input
            placeholder="VD: APPLE"
            maxLength={5}
            style={{ textTransform: 'uppercase' }}
          />
        </Form.Item>
        <Form.Item label="Tên NSX" name="name" rules={[{ required: true, message: 'Vui lòng nhập tên' }]}>
          <Input placeholder="Tên nhà sản xuất" />
        </Form.Item>
        <Form.Item label="Website" name="url" rules={[{ type: 'url', message: 'URL không hợp lệ' }]}>
          <Input placeholder="https://example.com" />
        </Form.Item>
        <Form.Item label="Support URL" name="supportUrl" rules={[{ type: 'url', message: 'URL không hợp lệ' }]}>
          <Input placeholder="https://support.example.com" />
        </Form.Item>
        <Form.Item label="Support Email" name="supportEmail" rules={[{ type: 'email', message: 'Email không hợp lệ' }]}>
          <Input placeholder="support@example.com" />
        </Form.Item>
      </Form>
    </Modal>
  );

  const columns: ProColumns<ManufacturerDto>[] = [
    { title: 'Mã NSX', dataIndex: 'code', key: 'code', width: 100 },
    { title: 'Tên NSX', dataIndex: 'name', key: 'name' },
    {
      title: 'Website', dataIndex: 'url', key: 'url',
      render: (_, r) => r.url ? <a href={r.url} target="_blank" rel="noreferrer">{r.url}</a> : '-',
    },
    {
      title: 'Support URL', dataIndex: 'supportUrl', key: 'supportUrl',
      render: (_, r) => r.supportUrl ? <a href={r.supportUrl} target="_blank" rel="noreferrer">{r.supportUrl}</a> : '-',
    },
    { title: 'Support Email', dataIndex: 'supportEmail', key: 'supportEmail', render: (_, r) => r.supportEmail || '-' },
    {
      title: 'Hành động', key: 'actions', valueType: 'option' as const, width: 140,
      render: (_, record) => <Space size="small">{renderActions(record)}</Space>,
    },
  ];

  // ─── Mobile (ST7b): ProList Card thay Table — cùng fetch + cùng renderActions ───
  if (isMobile) {
    return (
      <div>
        <ProList<ManufacturerDto>
          headerTitle="Danh sách nhà sản xuất"
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
              message.error('Lỗi tải danh sách nhà sản xuất');
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
                <Text type="secondary" style={{ fontSize: 12 }}>Website</Text>
                <Text style={{ fontSize: 13 }}>
                  {record.url
                    ? <a href={record.url} target="_blank" rel="noreferrer" style={{ wordBreak: 'break-all' }}>{record.url}</a>
                    : '-'}
                </Text>
                <Text type="secondary" style={{ fontSize: 12 }}>Support Email</Text>
                <Text style={{ fontSize: 13 }}>{record.supportEmail || '-'}</Text>
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
      <ProTable<ManufacturerDto>
        headerTitle="Danh sách nhà sản xuất"
        rowKey="id"
        size="small"
        columns={columns}
        actionRef={actionRef}
        search={false}
        options={{ reload: true, density: true, setting: true }}
        toolBarRender={() => [
          canCreate && (
            <Button type="primary" icon={<PlusOutlined />} onClick={handleAdd}>Thêm NSX</Button>
          ),
        ]}
        request={async () => {
          try {
            const { list, total } = await fetchList();
            return { data: list, success: true, total };
          } catch {
            message.error('Lỗi tải danh sách nhà sản xuất');
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
