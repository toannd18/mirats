import { useState, useRef } from 'react';
import { Button, Space, Input, Tag, Modal, Form, Select, Switch, ColorPicker, message } from 'antd';
import { PlusOutlined } from '@ant-design/icons';
import { ProTable } from '@ant-design/pro-components';
import type { ActionType, ProColumns } from '@ant-design/pro-components';
import apiClient from '../../../services/api-client';
import { usePermission } from '../../../hooks/usePermission';

// ST6b — permission gating (mirrors backend [Authorize(Policy=...)]).
const CATEGORY_TYPE_LABELS: Record<string, string> = {
  Asset: 'Asset',
  Consumable: 'Consumable',
  Accessory: 'Accessory',
  Component: 'Component',
  License: 'License',
  1: 'Asset',
  2: 'Consumable',
  3: 'Accessory',
  4: 'Component',
  5: 'License',
};

interface CategoryRow {
  id: string;
  name: string;
  tagColor: string;
  categoryType: string | number;
  requireAcceptance: boolean;
  checkinEmail: boolean;
  useDefaultEula: boolean;
  notes?: string;
}

export default function CategoryListPage() {
  const [open, setOpen] = useState(false);
  const [editingId, setEditingId] = useState<string | null>(null);
  const [form] = Form.useForm();
  const actionRef = useRef<ActionType | null>(null);
  const [typeFilter, setTypeFilter] = useState<string | undefined>(undefined);

  // ST6b — permission gating (mirrors backend [Authorize(Policy=...)]).
  const canCreate = usePermission('categories.create');
  const canEdit = usePermission('categories.edit');
  const canDelete = usePermission('categories.delete');

  const handleEdit = (record: CategoryRow) => {
    setEditingId(record.id);
    form.setFieldsValue({
      name: record.name,
      tagColor: record.tagColor || '#1890ff',
      categoryType: record.categoryType,
      requireAcceptance: record.requireAcceptance ?? false,
      checkinEmail: record.checkinEmail ?? false,
      useDefaultEula: record.useDefaultEula ?? true,
      notes: record.notes || '',
    });
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

  const handleDelete = (record: CategoryRow) => {
    Modal.confirm({
      title: 'Xóa danh mục?',
      content: `Bạn có chắc muốn xóa "${record.name}"?`,
      onOk: async () => {
        try {
          await apiClient.delete(`/categories/${record.id}`);
          message.success('Đã xóa danh mục');
          actionRef.current?.reload();
        } catch (err: any) {
          message.error(err?.response?.data?.message || 'Không thể xóa danh mục');
        }
      },
    });
  };

  const save = async () => {
    try {
      const values = await form.validateFields();
      if (editingId) {
        await apiClient.put(`/categories/${editingId}`, values);
        message.success('Đã cập nhật danh mục');
      } else {
        await apiClient.post('/categories', values);
        message.success('Đã tạo danh mục');
      }
      handleClose();
      actionRef.current?.reload();
    } catch (err: any) {
      if (err?.errorFields) return; // Form validation error, don't show message
      const serverMsg = err?.response?.data?.message;
      message.error(serverMsg || 'Lỗi lưu danh mục');
    }
  };

  const columns: ProColumns<CategoryRow>[] = [
    { title: 'Tên', dataIndex: 'name', key: 'name' },
    {
      title: 'Màu',
      dataIndex: 'tagColor',
      key: 'tagColor',
      render: (_, r) => r.tagColor ? <Tag color={r.tagColor}>{r.tagColor}</Tag> : '-',
    },
    {
      title: 'Loại',
      dataIndex: 'categoryType',
      key: 'categoryType',
      render: (_, r) => <Tag>{CATEGORY_TYPE_LABELS[r.categoryType] || `#${r.categoryType}`}</Tag>,
    },
    {
      title: 'Chính sách',
      key: 'policies',
      render: (_, r) => (
        <Space size={4} wrap>
          {r.requireAcceptance && <Tag color="orange">Cần xác nhận</Tag>}
          {r.checkinEmail && <Tag color="blue">Gửi Email</Tag>}
          {r.useDefaultEula && <Tag color="green">EULA Mặc định</Tag>}
        </Space>
      ),
    },
    {
      title: 'Thao tác',
      key: 'actions',
      valueType: 'option' as const,
      width: 120,
      render: (_, r) => (
        <Space size="small">
          {canEdit && <Button size="small" onClick={() => handleEdit(r)}>Sửa</Button>}
          {canDelete && <Button size="small" danger onClick={() => handleDelete(r)}>Xóa</Button>}
        </Space>
      ),
    },
  ];

  return (
    <div>
      <Space style={{ marginBottom: 16 }}>
        <Select
          allowClear
          placeholder="Lọc theo loại"
          style={{ minWidth: 180 }}
          value={typeFilter}
          onChange={v => { setTypeFilter(v); actionRef.current?.reload(); }}
          options={Object.entries(CATEGORY_TYPE_LABELS).filter(([k]) => !/^\d+$/.test(k)).map(([key, label]) => ({ label, value: key }))}
        />
        {canCreate && (
          <Button type="primary" icon={<PlusOutlined />} onClick={handleAdd}>
            Thêm danh mục
          </Button>
        )}
      </Space>

      <ProTable<CategoryRow>
        headerTitle="Danh sách danh mục"
        rowKey="id"
        size="small"
        columns={columns}
        actionRef={actionRef}
        search={false}
        options={{ reload: true, density: true, setting: true }}
        request={async () => {
          try {
            const params: Record<string, unknown> = {};
            if (typeFilter !== undefined) params.type = typeFilter;
            const r = await apiClient.get('/categories', { params });
            return { data: r.data.data ?? [], success: true, total: (r.data.data ?? []).length };
          } catch {
            message.error('Lỗi tải danh mục');
            return { data: [], success: false, total: 0 };
          }
        }}
        pagination={{ defaultPageSize: 20, showSizeChanger: true }}
        scroll={{ x: 'max-content' }}
      />

      <Modal
        open={open}
        title={editingId ? 'Sửa danh mục' : 'Tạo danh mục mới'}
        onOk={form.submit}
        onCancel={handleClose}
        destroyOnHidden
      >
        <Form
          form={form}
          layout="vertical"
          onFinish={save}
          initialValues={{ tagColor: '#1890ff', categoryType: 'Asset', useDefaultEula: true }}
        >
          <Form.Item label="Tên" name="name" rules={[{ required: true, message: 'Vui lòng nhập tên' }]}>
            <Input placeholder="Tên danh mục" />
          </Form.Item>

          <Form.Item
            label="Màu"
            name="tagColor"
            getValueFromEvent={(color) => (typeof color === 'string' ? color : color.toHexString())}
          >
            <ColorPicker format="hex" showText />
          </Form.Item>

          <Form.Item label="Loại" name="categoryType" rules={[{ required: true }]}>
            <Select disabled={!!editingId}>
              {Object.entries(CATEGORY_TYPE_LABELS).filter(([k]) => !/^\d+$/.test(k)).map(([key, label]) => (
                <Select.Option key={key} value={key}>{label}</Select.Option>
              ))}
            </Select>
          </Form.Item>

          <Form.Item label="Bắt buộc xác nhận" name="requireAcceptance" valuePropName="checked">
            <Switch />
          </Form.Item>

          <Form.Item label="Gửi Email khi Checkin" name="checkinEmail" valuePropName="checked">
            <Switch />
          </Form.Item>

          <Form.Item label="EULA Mặc định" name="useDefaultEula" valuePropName="checked">
            <Switch />
          </Form.Item>

          <Form.Item label="Ghi chú" name="notes">
            <Input.TextArea rows={2} placeholder="Ghi chú thêm về danh mục" />
          </Form.Item>
        </Form>
      </Modal>
    </div>
  );
}
