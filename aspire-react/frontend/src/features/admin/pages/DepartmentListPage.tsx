import { useEffect, useState, useRef } from 'react';
import { Button, Space, Modal, Form, Input, Select, message } from 'antd';
import { PlusOutlined, EditOutlined, DeleteOutlined, PhoneOutlined, PrinterOutlined } from '@ant-design/icons';
import { ProTable } from '@ant-design/pro-components';
import type { ActionType, ProColumns } from '@ant-design/pro-components';
import apiClient from '../../../services/api-client';
import { usePermission } from '../../../hooks/usePermission';
import CompanyTreeSelect from '../../../components/common/CompanyTreeSelect';

interface DepartmentDto {
  id: string;
  name: string;
  companyId: string | null;
  managerId: string | null;
  phone: string | null;
  fax: string | null;
  company?: { id: string; name: string } | null;
  manager?: { id: string; username: string; firstName: string; lastName: string } | null;
}

export default function DepartmentListPage() {
  const [open, setOpen] = useState(false);
  const [editingId, setEditingId] = useState<string | null>(null);
  const [form] = Form.useForm();
  const actionRef = useRef<ActionType | null>(null);
  const [users, setUsers] = useState<{ label: string; value: string }[]>([]);

  // ST6b — permission gating (mirrors backend [Authorize(Policy=...)]).
  const canCreate = usePermission('departments.create');
  const canEdit = usePermission('departments.edit');
  const canDelete = usePermission('departments.delete');

  const loadOptions = async () => {
    try {
      // Users: show "FirstName LastName (Username)". Company tree is loaded by CompanyTreeSelect.
      const uRes = await apiClient.get('/users');
      setUsers((uRes.data.data || []).map((u: any) => ({
        label: `${[u.firstName, u.lastName].filter(Boolean).join(' ') || u.username} (${u.username})`,
        value: u.id,
      })));
    } catch { /* ignore */ }
  };

  useEffect(() => { void loadOptions(); }, []);

  const handleEdit = (r: DepartmentDto) => { setEditingId(r.id); form.setFieldsValue(r); setOpen(true); };
  const handleAdd = () => { setEditingId(null); form.resetFields(); setOpen(true); };
  const handleClose = () => { setEditingId(null); form.resetFields(); setOpen(false); };

  const handleDelete = (r: DepartmentDto) => {
    Modal.confirm({
      title: 'Xóa phòng ban?', content: `Bạn có chắc muốn xóa "${r.name}"?`,
      onOk: async () => {
        try { await apiClient.delete(`/departments/${r.id}`); message.success('Đã xóa'); actionRef.current?.reload(); }
        catch (err: any) { message.error(err?.response?.data?.message || 'Không thể xóa'); }
      },
    });
  };

  const save = async () => {
    try {
      const values = await form.validateFields();
      if (editingId) { await apiClient.put(`/departments/${editingId}`, values); message.success('Đã cập nhật'); }
      else { await apiClient.post('/departments', values); message.success('Đã tạo phòng ban'); }
      handleClose(); actionRef.current?.reload();
    } catch (err: any) {
      if (err?.errorFields) return;
      message.error(err?.response?.data?.message || 'Lỗi lưu');
    }
  };

  const columns: ProColumns<DepartmentDto>[] = [
    { title: 'Tên phòng ban', dataIndex: 'name', key: 'name' },
    { title: 'Công ty', key: 'company', render: (_, r) => r.company?.name || '-' },
    { title: 'Người quản lý', key: 'manager', render: (_, r) => r.manager ? `${r.manager.firstName} ${r.manager.lastName}` : '-' },
    { title: 'Điện thoại', dataIndex: 'phone', render: (_, r) => r.phone || '-' },
    { title: 'Fax', dataIndex: 'fax', render: (_, r) => r.fax || '-' },
    {
      title: 'Hành động', key: 'actions', valueType: 'option' as const, width: 140,
      render: (_, r) => (
        <Space size="small">
          {canEdit && <Button size="small" icon={<EditOutlined />} onClick={() => handleEdit(r)}>Sửa</Button>}
          {canDelete && <Button size="small" danger icon={<DeleteOutlined />} onClick={() => handleDelete(r)}>Xóa</Button>}
        </Space>
      ),
    },
  ];

  return (
    <div>
      <ProTable<DepartmentDto>
        headerTitle="Danh sách phòng ban"
        rowKey="id"
        size="small"
        columns={columns}
        actionRef={actionRef}
        search={false}
        options={{ reload: true, density: true, setting: true }}
        toolBarRender={() => [
          canCreate && <Button type="primary" icon={<PlusOutlined />} onClick={handleAdd}>Thêm phòng ban</Button>,
        ]}
        request={async () => {
          try {
            const r = await apiClient.get('/departments');
            return { data: r.data.data || [], success: true, total: (r.data.data || []).length };
          } catch {
            message.error('Lỗi tải danh sách phòng ban');
            return { data: [], success: false, total: 0 };
          }
        }}
        pagination={{ defaultPageSize: 20, showSizeChanger: true }}
        scroll={{ x: 'max-content' }}
      />
      <Modal open={open} title={editingId ? 'Sửa phòng ban' : 'Thêm phòng ban'} onOk={form.submit} onCancel={handleClose} destroyOnHidden width={520}>
        <Form form={form} layout="vertical" onFinish={save}>
          <Form.Item label="Tên phòng ban" name="name" rules={[{ required: true, message: 'Vui lòng nhập tên' }]}>
            <Input placeholder="Tên phòng ban" />
          </Form.Item>
          <Form.Item label="Công ty" name="companyId">
            <CompanyTreeSelect />
          </Form.Item>
          <Form.Item label="Người quản lý" name="managerId">
            <Select showSearch allowClear placeholder="Chọn người quản lý" options={users} filterOption={(inp, opt) => (opt?.label as string)?.toLowerCase().includes(inp.toLowerCase())} />
          </Form.Item>
          <Space size="middle" style={{ width: '100%' }}>
            <Form.Item label="Điện thoại" name="phone" style={{ flex: 1 }}>
              <Input prefix={<PhoneOutlined />} />
            </Form.Item>
            <Form.Item label="Fax" name="fax" style={{ flex: 1 }}>
              <Input prefix={<PrinterOutlined />} />
            </Form.Item>
          </Space>
        </Form>
      </Modal>
    </div>
  );
}
