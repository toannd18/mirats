import { useEffect, useState, useRef, type ReactNode } from 'react';
import { Button, Space, Modal, Form, Input, InputNumber, Select, Switch, Card, Divider, Tag, Typography, Popconfirm, message } from 'antd';
import { PlusOutlined, EditOutlined, DeleteOutlined } from '@ant-design/icons';
import { ProList, ProTable } from '@ant-design/pro-components';
import type { ActionType, ProColumns } from '@ant-design/pro-components';
import apiClient from '../../../services/api-client';
import { usePermission } from '../../../hooks/usePermission';
import { useIsMobile } from '../../../hooks/useIsMobile';

const { Text } = Typography;

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

interface CategoryLite {
  id: string;
  name: string;
  categoryType: number | string;
  requireAcceptance?: boolean;
}

interface OptionItem {
  label: string;
  value: string;
}

export default function AssetModelListPage() {
  const [open, setOpen] = useState(false);
  const [editingId, setEditingId] = useState<string | null>(null);
  const [form] = Form.useForm();
  // ST7b — 1 actionRef dùng chung cho ProTable (desktop) và ProList (mobile Card).
  const actionRef = useRef<ActionType | null>(null);
  const isMobile = useIsMobile();

  // ST6b — permission gating (mirrors backend [Authorize(Policy=...)]).
  const canCreate = usePermission('models.create');
  const canEdit = usePermission('models.edit');
  const canDelete = usePermission('models.delete');

  // Dropdown options
  const [manufacturers, setManufacturers] = useState<OptionItem[]>([]);
  const [depreciations, setDepreciations] = useState<OptionItem[]>([]);
  const [, setFieldsets] = useState<OptionItem[]>([]);

  // Full category objects from API (needed for filter + requireAcceptance lookup)
  const [allCategories, setAllCategories] = useState<CategoryLite[]>([]);

  // Only Asset-type categories (categoryType === 'Asset'; backend serializes CategoryType as string)
  const assetCategoryOptions = allCategories
    .filter((c: CategoryLite) => c.categoryType === 'Asset')
    .map(c => ({ label: c.name, value: c.id }));

  const loadOptions = async () => {
    try {
      const [mfrRes, catRes, depRes, fsRes] = await Promise.all([
        apiClient.get('/manufacturers'),
        apiClient.get('/categories'),
        apiClient.get('/depreciations'),
        apiClient.get('/custom-fieldsets').catch(() => ({ data: { data: [] } })),
      ]);
      setManufacturers((mfrRes.data.data || []).map((m: { name: string; id: string }) => ({ label: m.name, value: m.id })));
      setAllCategories(catRes.data.data || []);
      setDepreciations((depRes.data.data || []).map((d: { name: string; id: string }) => ({ label: d.name, value: d.id })));
      setFieldsets((fsRes.data.data || []).map((f: { name: string; id: string }) => ({ label: f.name, value: f.id })));
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
    try {
      await apiClient.delete(`/models/${record.id}`);
      message.success('Đã xóa Model');
      actionRef.current?.reload();
    } catch (err: unknown) {
      const e = err as { response?: { data?: { message?: string } } };
      message.error(e?.response?.data?.message || 'Không thể xóa Model');
    }
  };

  const save = async () => {
    try {
      const values = await form.validateFields();
      if (editingId) { await apiClient.put(`/models/${editingId}`, values); message.success('Đã cập nhật Model'); }
      else { await apiClient.post('/models', values); message.success('Đã tạo Model'); }
      handleClose(); actionRef.current?.reload();
    } catch (err: unknown) {
      if ((err as { errorFields?: unknown }).errorFields) return;
      const e = err as { response?: { data?: { message?: string } } };
      message.error(e?.response?.data?.message || 'Lỗi lưu Model');
    }
  };

  // ST7b — 1 fetch dùng chung cho ProTable (desktop) và ProList (mobile Card).
  const fetchList = async () => {
    const r = await apiClient.get('/models');
    return { list: (r.data.data || []) as AssetModelDto[], total: (r.data.data || []).length };
  };

  // ST7b — action buttons dùng chung desktop/mobile.
  const renderActions = (record: AssetModelDto): ReactNode[] => [
    canEdit && <Button key="edit" size="small" icon={<EditOutlined />} onClick={() => handleEdit(record)}>Sửa</Button>,
    canDelete && (
      <Popconfirm key="del" title="Xóa Model này?" description="Hành động này không thể hoàn tác." onConfirm={() => handleDelete(record)}>
        <Button size="small" danger icon={<DeleteOutlined />}>Xóa</Button>
      </Popconfirm>
    ),
  ].filter(Boolean) as ReactNode[];

  // Modal tạo/sửa — định nghĩa MỘT lần, render chung cho cả mobile và desktop.
  const formModal = (
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
  );

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
      render: (_, record) => <Space size="small">{renderActions(record)}</Space>,
    },
  ];

  // ─── Mobile (ST7b): ProList Card thay Table — cùng fetch + cùng renderActions ───
  if (isMobile) {
    return (
      <div>
        <ProList<AssetModelDto>
          headerTitle="Danh sách Model"
          actionRef={actionRef}
          rowKey="id"
          ghost
          cardProps={false}
          search={false}
          grid={{ gutter: 16, xs: 1, sm: 1 }}
          toolBarRender={() => [
            canCreate && <Button key="add" type="primary" icon={<PlusOutlined />} onClick={handleAdd}>Thêm</Button>,
          ]}
          request={async () => {
            try {
              const { list, total } = await fetchList();
              return { data: list, success: true, total };
            } catch {
              message.error('Lỗi tải danh sách Model');
              return { data: [], success: false, total: 0 };
            }
          }}
          pagination={{ defaultPageSize: 20, showSizeChanger: false }}
          itemRender={(record) => (
            <Card hoverable style={{ borderRadius: 12, marginBottom: 16 }} styles={{ body: { padding: 16 } }}>
              <div style={{ display: 'flex', alignItems: 'center', gap: 10, marginBottom: 10, flexWrap: 'wrap' }}>
                <Text strong style={{ fontSize: 15 }}>{record.name}</Text>
                {record.requestable ? <Tag color="green" style={{ marginInlineEnd: 0 }}>Yêu cầu cấp phát</Tag> : null}
              </div>
              <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '6px 12px', padding: '10px 12px', background: '#fafafa', borderRadius: 8 }}>
                <Text type="secondary" style={{ fontSize: 12 }}>Số Model</Text>
                <Text style={{ fontSize: 13 }}>{record.modelNumber || '-'}</Text>
                <Text type="secondary" style={{ fontSize: 12 }}>Hãng SX</Text>
                <Text style={{ fontSize: 13 }}>{record.manufacturer?.name || '-'}</Text>
                <Text type="secondary" style={{ fontSize: 12 }}>Danh mục</Text>
                <Text style={{ fontSize: 13 }}>{record.category?.name || '-'}</Text>
                <Text type="secondary" style={{ fontSize: 12 }}>Khấu hao</Text>
                <Text style={{ fontSize: 13 }}>{record.depreciation ? `${record.depreciation.name} (${record.depreciation.months} tháng)` : '-'}</Text>
                <Text type="secondary" style={{ fontSize: 12 }}>EOL</Text>
                <Text style={{ fontSize: 13 }}>{record.eol ?? '-'}{record.eol != null ? ' tháng' : ''}</Text>
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
            const { list, total } = await fetchList();
            return { data: list, success: true, total };
          } catch {
            message.error('Lỗi tải danh sách Model');
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
