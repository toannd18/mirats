import { useEffect, useState, useRef } from 'react';
import { Button, Space, Modal, Form, Input, InputNumber, Select, Switch, message, Tag, Popconfirm } from 'antd';
import { PlusOutlined, EditOutlined, DeleteOutlined } from '@ant-design/icons';
import { ProTable } from '@ant-design/pro-components';
import type { ActionType, ProColumns } from '@ant-design/pro-components';
import apiClient from '../../../services/api-client';
import { usePermission } from '../../../hooks/usePermission';

interface AssetModelDto {
  id: string;
  name: string;
  modelNumber?: string | null;
  manufacturerId: string | null;
  categoryId: string | null;
  depreciationId: string | null;
  fieldsetId: string | null;
  eol?: number | null;
  notes?: string | null;
  requestable: boolean;
  manufacturer?: { id: string; name: string } | null;
  category?: { id: string; name: string } | null;
  depreciation?: { id: string; name: string; months: number } | null;
}

export default function AssetModelListPage() {
  const [open, setOpen] = useState(false);
  const [editingId, setEditingId] = useState<string | null>(null);
  const [form] = Form.useForm();
  const actionRef = useRef<ActionType | null>(null);

  // ST6b — permission gating (mirrors backend [Authorize(Policy=...)]).
  const canCreate = usePermission('models.create');
  const canEdit = usePermission('models.edit');
  const canDelete = usePermission('models.delete');

  // Dropdown options
  const [manufacturers, setManufacturers] = useState<{ label: string; value: string }[]>([]);
  const [depreciations, setDepreciations] = useState<{ label: string; value: string }[]>([]);
  const [, setFieldsets] = useState<{ label: string; value: string }[]>([]);

  // Full category objects from API (needed for filter + requireAcceptance lookup)
  const [allCategories, setAllCategories] = useState<any[]>([]);

  // Only Asset-type categories (categoryType === 'Asset'; backend serializes CategoryType as string)
  const assetCategoryOptions = allCategories
    .filter((c: { categoryType: number | string }) => c.categoryType === 'Asset')
    .map(c => ({ label: c.name, value: c.id }));

  const loadOptions = async () => {
    try {
      const [mfrRes, catRes, depRes, fsRes] = await Promise.all([
        apiClient.get('/manufacturers'),
        apiClient.get('/categories'),
        apiClient.get('/depreciations'),
        apiClient.get('/custom-fieldsets').catch(() => ({ data: { data: [] } })),
      ]);
      setManufacturers((mfrRes.data.data || []).map((m: any) => ({ label: m.name, value: m.id })));
      setAllCategories(catRes.data.data || []);
      setDepreciations((depRes.data.data || []).map((d: any) => ({ label: d.name, value: d.id })));
      setFieldsets((fsRes.data.data || []).map((f: any) => ({ label: f.name, value: f.id })));
    } catch { /* ignore */ }
  };

  useEffect(() => { void loadOptions(); }, []);

  const handleEdit = (record: AssetModelDto) => {
    setEditingId(record.id);
    form.setFieldsValue({
      name: record.name, modelNumber: record.modelNumber,
      manufacturerId: record.manufacturerId, categoryId: record.categoryId,
      depreciationId: record.depreciationId, fieldsetId: record.fieldsetId,
      eol: record.eol, notes: record.notes, requestable: record.requestable,
    });
    setOpen(true);
  };

  const handleAdd = () => { setEditingId(null); form.resetFields(); setOpen(true); };
  const handleClose = () => { setEditingId(null); form.resetFields(); setOpen(false); };

  const handleDelete = async (record: AssetModelDto) => {
    try { await apiClient.delete(`/models/${record.id}`); message.success('Đã xóa Model'); actionRef.current?.reload(); }
    catch (err: any) { message.error(err?.response?.data?.message || 'Không thể xóa Model'); }
  };

  const save = async () => {
    try {
      const values = await form.validateFields();
      if (editingId) { await apiClient.put(`/models/${editingId}`, values); message.success('Đã cập nhật Model'); }
      else { await apiClient.post('/models', values); message.success('Đã tạo Model'); }
      handleClose(); actionRef.current?.reload();
    } catch (err: any) {
      if (err?.errorFields) return;
      message.error(err?.response?.data?.message || 'Lỗi lưu Model');
    }
  };

  const columns: ProColumns<AssetModelDto>[] = [
    { title: 'Tên Model', dataIndex: 'name', key: 'name' },
    { title: 'Số Model', dataIndex: 'modelNumber', key: 'modelNumber', render: (_, r) => r.modelNumber || '-' },
    { title: 'Hãng SX', key: 'manufacturer', render: (_, r) => r.manufacturer?.name || '-' },
    { title: 'Danh mục', key: 'category', render: (_, r) => r.category?.name || '-' },
    { title: 'Khấu hao', key: 'depreciation', render: (_, r) => r.depreciation ? `${r.depreciation.name} (${r.depreciation.months} tháng)` : '-' },
    { title: 'EOL (tháng)', dataIndex: 'eol', key: 'eol', render: (_, r) => r.eol ?? '-' },
    { title: 'Yêu cầu cấp phát', dataIndex: 'requestable', key: 'requestable', render: (_, r) => r.requestable ? <Tag color="green">Có</Tag> : <Tag>Không</Tag> },
    {
      title: 'Hành động', key: 'actions', valueType: 'option' as const, width: 140,
      render: (_, record) => (
        <Space size="small">
          {canEdit && <Button size="small" icon={<EditOutlined />} onClick={() => handleEdit(record)}>Sửa</Button>}
          {canDelete && (
            <Popconfirm title="Xóa Model này?" description="Hành động này không thể hoàn tác." onConfirm={() => handleDelete(record)}>
              <Button size="small" danger icon={<DeleteOutlined />}>Xóa</Button>
            </Popconfirm>
          )}
        </Space>
      ),
    },
  ];

  return (
    <div>
      <ProTable<AssetModelDto>
        headerTitle="Danh sách Model"
        rowKey="id"
        size="small"
        columns={columns}
        actionRef={actionRef}
        search={false}
        options={{ reload: true, density: true, setting: true }}
        toolBarRender={() => [
          canCreate && <Button type="primary" icon={<PlusOutlined />} onClick={handleAdd}>Thêm Model</Button>,
        ]}
        request={async () => {
          try {
            const r = await apiClient.get('/models');
            return { data: r.data.data || [], success: true, total: (r.data.data || []).length };
          } catch {
            message.error('Lỗi tải danh sách Model');
            return { data: [], success: false, total: 0 };
          }
        }}
        pagination={{ defaultPageSize: 20, showSizeChanger: true }}
        scroll={{ x: 'max-content' }}
      />
      <Modal
        open={open} title={editingId ? 'Sửa Model' : 'Thêm Model'}
        onOk={form.submit} onCancel={handleClose} destroyOnHidden width={560}
      >
        <Form form={form} layout="vertical" onFinish={save}>
          <Form.Item label="Tên Model" name="name" rules={[{ required: true, message: 'Vui lòng nhập tên' }]}>
            <Input placeholder="Tên Model" />
          </Form.Item>
          <Form.Item label="Số Model" name="modelNumber">
            <Input placeholder="Số hiệu Model" />
          </Form.Item>
          <Space size="middle" style={{ width: '100%' }}>
            <Form.Item label="Danh mục" name="categoryId" rules={[{ required: true, message: 'Bắt buộc' }]} style={{ flex: 1 }}>
              <Select
                showSearch allowClear placeholder="Chọn danh mục"
                options={assetCategoryOptions}
                filterOption={(inp, opt) => (opt?.label as string)?.toLowerCase().includes(inp.toLowerCase())}
                onChange={(value: string) => {
                  // Auto-set requestable from category's requireAcceptance
                  if (value) {
                    const cat = allCategories.find(c => c.id === value);
                    if (cat) form.setFieldValue('requestable', cat.requireAcceptance || false);
                  }
                }}
              />
            </Form.Item>
            <Form.Item label="Hãng SX" name="manufacturerId" rules={[{ required: true, message: 'Bắt buộc' }]} style={{ flex: 1 }}>
              <Select showSearch allowClear placeholder="Chọn hãng" options={manufacturers}
                filterOption={(inp, opt) => (opt?.label as string)?.toLowerCase().includes(inp.toLowerCase())} />
            </Form.Item>
          </Space>
          <Space size="middle" style={{ width: '100%' }}>
            <Form.Item label="Khấu hao" name="depreciationId" style={{ flex: 1 }}>
              <Select showSearch allowClear placeholder="Chọn khấu hao" options={depreciations}
                filterOption={(inp, opt) => (opt?.label as string)?.toLowerCase().includes(inp.toLowerCase())} />
            </Form.Item>
            <Form.Item label="EOL (tháng)" name="eol" style={{ flex: 1 }}>
              <InputNumber min={0} style={{ width: '100%' }} placeholder="Số tháng EOL" />
            </Form.Item>
          </Space>
          <Form.Item label="Yêu cầu cấp phát" name="requestable" valuePropName="checked">
            <Switch disabled />
          </Form.Item>
          <Form.Item label="Ghi chú" name="notes">
            <Input.TextArea rows={2} placeholder="Ghi chú về Model" />
          </Form.Item>
        </Form>
      </Modal>
    </div>
  );
}
