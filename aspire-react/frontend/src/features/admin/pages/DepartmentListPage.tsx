import { useEffect, useState, useRef, type ReactNode } from 'react';
import { Button, Space, Modal, Form, Input, Select, Card, Divider, Tag, Typography, Popconfirm, message } from 'antd';
import { PlusOutlined, EditOutlined, DeleteOutlined, PhoneOutlined, PrinterOutlined } from '@ant-design/icons';
import { ProList, ProTable } from '@ant-design/pro-components';
import type { ActionType, ProColumns } from '@ant-design/pro-components';
import apiClient from '../../../services/api-client';
import { usePermission } from '../../../hooks/usePermission';
import { useIsMobile } from '../../../hooks/useIsMobile';
import CompanyTreeSelect from '../../../components/common/CompanyTreeSelect';

const { Text } = Typography;

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

interface UserOption {
  id: string;
  username: string;
  firstName?: string | null;
  lastName?: string | null;
}

export default function DepartmentListPage() {
  const [open, setOpen] = useState(false);
  const [editingId, setEditingId] = useState<string | null>(null);
  const [form] = Form.useForm();
  // ST7b — 1 actionRef dùng chung cho ProTable (desktop) và ProList (mobile Card).
  const actionRef = useRef<ActionType | null>(null);
  const isMobile = useIsMobile();
  const [users, setUsers] = useState<{ label: string; value: string }[]>([]);

  // ST6b — permission gating (mirrors backend [Authorize(Policy=...)]).
  const canCreate = usePermission('departments.create');
  const canEdit = usePermission('departments.edit');
  const canDelete = usePermission('departments.delete');

  const loadOptions = async () => {
    try {
      // Users: show "FirstName LastName (Username)". Company tree is loaded by CompanyTreeSelect.
      const uRes = await apiClient.get('/users');
      setUsers((uRes.data.data || []).map((u: UserOption) => ({
        label: `${[u.firstName, u.lastName].filter(Boolean).join(' ') || u.username} (${u.username})`,
        value: u.id,
      })));
    } catch { /* ignore */ }
  };

  useEffect(() => { void loadOptions(); }, []);

  const handleEdit = (r: DepartmentDto) => { setEditingId(r.id); form.setFieldsValue(r); setOpen(true); };
  const handleAdd = () => { setEditingId(null); form.resetFields(); setOpen(true); };
  const handleClose = () => { setEditingId(null); form.resetFields(); setOpen(false); };

  // ST7b: confirm nằm ở Popconfirm trong renderActions (dùng chung 2 view) —
  // hàm này chỉ thực hiện xóa sau khi đã confirm.
  const handleDelete = async (r: DepartmentDto) => {
    try {
      await apiClient.delete(`/departments/${r.id}`);
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
      if (editingId) { await apiClient.put(`/departments/${editingId}`, values); message.success('Đã cập nhật'); }
      else { await apiClient.post('/departments', values); message.success('Đã tạo phòng ban'); }
      handleClose(); actionRef.current?.reload();
    } catch (err: unknown) {
      if ((err as { errorFields?: unknown }).errorFields) return;
      const e = err as { response?: { data?: { message?: string } } };
      message.error(e?.response?.data?.message || 'Lỗi lưu');
    }
  };

  // ST7b — 1 fetch dùng chung cho ProTable (desktop) và ProList (mobile Card).
  const fetchList = async () => {
    const r = await apiClient.get('/departments');
    return { list: (r.data.data || []) as DepartmentDto[], total: (r.data.data || []).length };
  };

  // ST7b — action buttons dùng chung desktop/mobile.
  const renderActions = (r: DepartmentDto): ReactNode[] => [
    canEdit && <Button key="edit" size="small" icon={<EditOutlined />} onClick={() => handleEdit(r)}>Sửa</Button>,
    canDelete && (
      <Popconfirm key="del" title="Xóa phòng ban này?" onConfirm={() => handleDelete(r)}>
        <Button size="small" danger icon={<DeleteOutlined />}>Xóa</Button>
      </Popconfirm>
    ),
  ].filter(Boolean) as ReactNode[];

  // Modal tạo/sửa — định nghĩa MỘT lần, render chung cho cả mobile và desktop.
  const formModal = (
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
  );

  const columns: ProColumns<DepartmentDto>[] = [
    { title: 'Tên phòng ban', dataIndex: 'name', key: 'name' },
    { title: 'Công ty', key: 'company', render: (_, r) => r.company?.name || '-' },
    { title: 'Người quản lý', key: 'manager', render: (_, r) => r.manager ? `${r.manager.firstName} ${r.manager.lastName}` : '-' },
    { title: 'Điện thoại', dataIndex: 'phone', render: (_, r) => r.phone || '-' },
    { title: 'Fax', dataIndex: 'fax', render: (_, r) => r.fax || '-' },
    {
      title: 'Hành động', key: 'actions', valueType: 'option' as const, width: 140,
      render: (_, r) => <Space size="small">{renderActions(r)}</Space>,
    },
  ];

  // ─── Mobile (ST7b): ProList Card thay Table — cùng fetch + cùng renderActions ───
  if (isMobile) {
    return (
      <div>
        <ProList<DepartmentDto>
          headerTitle="Danh sách phòng ban"
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
              message.error('Lỗi tải danh sách phòng ban');
              return { data: [], success: false, total: 0 };
            }
          }}
          pagination={{ defaultPageSize: 20, showSizeChanger: false }}
          itemRender={(record) => (
            <Card hoverable style={{ borderRadius: 12, marginBottom: 16 }} styles={{ body: { padding: 16 } }}>
              <div style={{ display: 'flex', alignItems: 'center', gap: 10, marginBottom: 10 }}>
                <Text strong style={{ fontSize: 15 }}>{record.name}</Text>
                {record.company?.name && <Tag style={{ marginInlineEnd: 0 }} color="blue">{record.company.name}</Tag>}
              </div>
              <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '6px 12px', padding: '10px 12px', background: '#fafafa', borderRadius: 8 }}>
                <Text type="secondary" style={{ fontSize: 12 }}>Người quản lý</Text>
                <Text style={{ fontSize: 13 }}>{record.manager ? `${record.manager.firstName} ${record.manager.lastName}` : '-'}</Text>
                <Text type="secondary" style={{ fontSize: 12 }}>Điện thoại</Text>
                <Text style={{ fontSize: 13 }}>
                  {record.phone ? <a href={`tel:${record.phone}`}>{record.phone}</a> : '-'}
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
            const { list, total } = await fetchList();
            return { data: list, success: true, total };
          } catch {
            message.error('Lỗi tải danh sách phòng ban');
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
