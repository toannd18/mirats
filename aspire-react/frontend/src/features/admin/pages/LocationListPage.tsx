import { useEffect, useState, useRef } from 'react';
import { Button, Space, Modal, Form, Input, TreeSelect, Select, message, Tooltip } from 'antd';
import { PlusOutlined, EditOutlined, DeleteOutlined } from '@ant-design/icons';
import { ProTable } from '@ant-design/pro-components';
import type { ActionType, ProColumns } from '@ant-design/pro-components';
import apiClient from '../../../services/api-client';
import { usePermission } from '../../../hooks/usePermission';
import CompanyTreeSelect from '../../../components/common/CompanyTreeSelect';

interface LocationDto {
  id: string;
  name: string;
  parentId: string | null;
  companyId: string | null;
  managerId: string | null;
  address: string | null;
  city: string | null;
  state: string | null;
  country: string | null;
  zip: string | null;
  children?: LocationDto[];
}

interface SelectOption {
  label: string;
  value: string;
}

/** Build a tree from a flat list by parentId */
function buildTree(list: LocationDto[]): LocationDto[] {
  const map = new Map<string, LocationDto>();
  const roots: LocationDto[] = [];
  list.forEach(item => { map.set(item.id, { ...item, children: [] }); });
  list.forEach(item => {
    const node = map.get(item.id)!;
    if (item.parentId && map.has(item.parentId)) {
      map.get(item.parentId)!.children!.push(node);
    } else {
      roots.push(node);
    }
  });
  return roots;
}

export default function LocationListPage() {
  const [tree, setTree] = useState<LocationDto[]>([]);
  const [open, setOpen] = useState(false);
  const [editingId, setEditingId] = useState<string | null>(null);
  const [selectedParentId, setSelectedParentId] = useState<string | null>(null);
  const [form] = Form.useForm();
  const actionRef = useRef<ActionType | null>(null);
  // ST6b — permission gating (mirrors backend [Authorize(Policy=...)]).
  const canCreate = usePermission('locations.create');
  const canEdit = usePermission('locations.edit');
  const canDelete = usePermission('locations.delete');
  // Watch parentId to disable companyId field when a parent location is selected
  const watchedParentId = Form.useWatch('parentId', form);

  // Options for autocomplete dropdowns (users only; company tree is loaded by CompanyTreeSelect)
  const [userOptions, setUserOptions] = useState<SelectOption[]>([]);

  // Flat list for quick lookup (used by inheritCompanyId logic)
  const [flatList, setFlatList] = useState<LocationDto[]>([]);

  const loadOptions = async () => {
    try {
      // Users: show "FirstName LastName (Username)" for clarity
      const usersRes = await apiClient.get('/users');
      const users = usersRes.data.data || [];
      setUserOptions(users.map((u: any) => ({
        label: `${[u.firstName, u.lastName].filter(Boolean).join(' ') || u.username} (${u.username})`,
        value: u.id,
      })));
    } catch { /* ignore load errors */ }
  };

  useEffect(() => { void loadOptions(); }, []);

  const toTreeSelect = (nodes: LocationDto[]): any[] =>
    nodes.map(n => ({
      title: n.name,
      value: n.id,
      children: n.children?.length ? toTreeSelect(n.children) : undefined,
    }));

  const handleEdit = (record: LocationDto) => {
    setEditingId(record.id);
    setSelectedParentId(null);
    form.setFieldsValue({
      name: record.name,
      parentId: record.parentId,
      companyId: record.companyId,
      managerId: record.managerId,
      address: record.address,
      city: record.city,
      state: record.state,
      country: record.country,
      zip: record.zip,
    });
    setOpen(true);
  };

  /**
   * Find a location by ID in the flat list.
   * Used to inherit companyId from parent when adding child or changing parent in form.
   */
  const findLocationById = (id: string): LocationDto | undefined =>
    flatList.find(l => l.id === id);

  const handleAdd = (parentId?: string) => {
    setEditingId(null);
    setSelectedParentId(parentId || null);
    form.resetFields();
    if (parentId) {
      // Inherit parentId from the selected row
      form.setFieldValue('parentId', parentId);
      // Inherit companyId from the parent location's company
      const parent = findLocationById(parentId);
      if (parent?.companyId) {
        form.setFieldValue('companyId', parent.companyId);
      }
    }
    setOpen(true);
  };

  const handleClose = () => {
    setEditingId(null);
    setSelectedParentId(null);
    form.resetFields();
    setOpen(false);
  };

  const handleDelete = (record: LocationDto) => {
    Modal.confirm({
      title: 'Xóa địa điểm?',
      content: `Bạn có chắc muốn xóa "${record.name}"?`,
      onOk: async () => {
        try {
          await apiClient.delete(`/locations/${record.id}`);
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
        await apiClient.put(`/locations/${editingId}`, values);
        message.success('Đã cập nhật');
      } else {
        await apiClient.post('/locations', values);
        message.success('Đã tạo địa điểm');
      }
      handleClose();
      actionRef.current?.reload();
    } catch (err: any) {
      if (err?.errorFields) return;
      message.error(err?.response?.data?.message || 'Lỗi lưu');
    }
  };

  const columns: ProColumns<LocationDto>[] = [
    { title: 'Tên địa điểm', dataIndex: 'name', key: 'name' },
    { title: 'Địa chỉ', dataIndex: 'address', key: 'address', render: (_, r) => r.address || '-' },
    { title: 'Thành phố', dataIndex: 'city', key: 'city', render: (_, r) => r.city || '-' },
    { title: 'Quốc gia', dataIndex: 'country', key: 'country', render: (_, r) => r.country || '-' },
    {
      title: 'Hành động',
      key: 'actions',
      valueType: 'option' as const,
      width: 180,
      render: (_, record) => (
        <Space size="small">
          {canCreate && (
            <Tooltip title="Thêm mới">
            <Button size="small" icon={<PlusOutlined />} onClick={() => handleAdd(record.id)} >
            </Button>
            </Tooltip>
          )}
          {canEdit && <Tooltip title="Sửa"><Button size="small" icon={<EditOutlined />} onClick={() => handleEdit(record)} /></Tooltip>}
          {canDelete && <Tooltip title="Xóa"><Button size="small" danger icon={<DeleteOutlined />} onClick={() => handleDelete(record)} /></Tooltip>}
        </Space>
      ),
    },
  ];

  return (
    <div>
      <ProTable<LocationDto>
        headerTitle="Danh sách địa điểm"
        rowKey="id"
        size="small"
        columns={columns}
        actionRef={actionRef}
        search={false}
        options={{ reload: true, density: true, setting: true }}
        toolBarRender={() => [
          canCreate && (
            <Button type="primary" icon={<PlusOutlined />} onClick={() => handleAdd()}>
              Tạo địa điểm
            </Button>
          ),
        ]}
        request={async () => {
          try {
            const r = await apiClient.get('/locations');
            const list: LocationDto[] = r.data.data || [];
            setFlatList(list);
            return { data: buildTree(list), success: true, total: list.length };
          } catch {
            message.error('Lỗi tải danh sách địa điểm');
            return { data: [], success: false, total: 0 };
          }
        }}
        onLoad={(dataSource) => setTree(dataSource as LocationDto[])}
        expandable={{ defaultExpandAllRows: true }}
        pagination={false}
        scroll={{ x: 'max-content' }}
      />

      <Modal
        open={open}
        title={editingId ? 'Sửa địa điểm' : 'Tạo địa điểm' + (selectedParentId ? ' con' : '')}
        onOk={form.submit}
        onCancel={handleClose}
        destroyOnHidden
        width={520}
      >
        <Form form={form} layout="vertical" onFinish={save}>
          <Form.Item label="Tên địa điểm" name="name" rules={[{ required: true, message: 'Vui lòng nhập tên' }]}>
            <Input placeholder="Tên địa điểm" />
          </Form.Item>
          <Form.Item label="Địa điểm cha" name="parentId">
            <TreeSelect
              showSearch
              treeNodeFilterProp="title"
              treeData={toTreeSelect(tree)}
              placeholder="Chọn địa điểm cha (Để trống nếu là gốc)"
              allowClear
              treeDefaultExpandAll
              disabled={!!selectedParentId}
              onChange={(newParentId: string | undefined) => {
                // Inherit companyId from the selected parent location
                if (newParentId) {
                  const parent = findLocationById(newParentId);
                  if (parent?.companyId) {
                    form.setFieldValue('companyId', parent.companyId);
                  }
                }
              }}
            />
          </Form.Item>
          <Form.Item label="Công ty" name="companyId">
            <CompanyTreeSelect
              placeholder={watchedParentId ? 'Đã kế thừa từ địa điểm cha' : 'Chọn công ty'}
              disabled={!!watchedParentId}
            />
          </Form.Item>
          <Form.Item label="Người quản lý" name="managerId">
            <Select
              showSearch
              optionFilterProp="label"
              options={userOptions}
              placeholder="Chọn người quản lý"
              allowClear
              filterOption={(input, option) =>
                (option?.label as string)?.toLowerCase().includes(input.toLowerCase())
              }
            />
          </Form.Item>
          <Form.Item label="Địa chỉ" name="address">
            <Input placeholder="Số nhà, đường..." />
          </Form.Item>
          <Space size="middle" style={{ width: '100%' }}>
            <Form.Item label="Thành phố" name="city" style={{ flex: 1 }}>
              <Input placeholder="Thành phố" />
            </Form.Item>
            <Form.Item label="Bang/Tỉnh" name="state" style={{ flex: 1 }}>
              <Input placeholder="Bang/Tỉnh" />
            </Form.Item>
          </Space>
          <Space size="middle" style={{ width: '100%' }}>
            <Form.Item label="Quốc gia" name="country" style={{ flex: 1 }}>
              <Input placeholder="Quốc gia" />
            </Form.Item>
            <Form.Item label="Mã bưu điện" name="zip" style={{ flex: 1 }}>
              <Input placeholder="ZIP" />
            </Form.Item>
          </Space>
        </Form>
      </Modal>
    </div>
  );
}
