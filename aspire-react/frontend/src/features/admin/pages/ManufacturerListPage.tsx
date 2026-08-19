import { useState, useRef } from 'react';
import { Button, Space, Modal, Form, Input, message } from 'antd';
import { PlusOutlined, EditOutlined, DeleteOutlined } from '@ant-design/icons';
import { ProTable } from '@ant-design/pro-components';
import type { ActionType, ProColumns } from '@ant-design/pro-components';
import apiClient from '../../../services/api-client';
import { usePermission } from '../../../hooks/usePermission';

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
  const actionRef = useRef<ActionType | null>(null);

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

  const handleDelete = (record: ManufacturerDto) => {
    Modal.confirm({
      title: 'Xóa nhà sản xuất?',
      content: `Bạn có chắc muốn xóa "${record.name}"?`,
      onOk: async () => {
        try {
          await apiClient.delete(`/manufacturers/${record.id}`);
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
        await apiClient.put(`/manufacturers/${editingId}`, values);
        message.success('Đã cập nhật');
      } else {
        await apiClient.post('/manufacturers', values);
        message.success('Đã tạo nhà sản xuất');
      }
      handleClose();
      actionRef.current?.reload();
    } catch (err: any) {
      if (err?.errorFields) return;
      message.error(err?.response?.data?.message || 'Lỗi lưu');
    }
  };

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
            const r = await apiClient.get('/manufacturers');
            return { data: r.data.data || [], success: true, total: (r.data.data || []).length };
          } catch {
            message.error('Lỗi tải danh sách nhà sản xuất');
            return { data: [], success: false, total: 0 };
          }
        }}
        pagination={{ defaultPageSize: 20, showSizeChanger: true }}
        scroll={{ x: 'max-content' }}
      />
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
    </div>
  );
}
