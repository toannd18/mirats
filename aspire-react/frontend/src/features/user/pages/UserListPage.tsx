import { useRef, useState, useCallback } from 'react';
import {
  Button, Space, Tag, Badge, Popconfirm, Tooltip, App,
} from 'antd';
import { PlusOutlined, EditOutlined, DeleteOutlined, EyeOutlined } from '@ant-design/icons';
import { useNavigate } from 'react-router-dom';
import { ProTable } from '@ant-design/pro-components';
import type { ActionType, ProColumns } from '@ant-design/pro-components';
import type { UserDto, ReferenceOption } from '../types/users';
import apiClient from '../../../services/api-client';
import { usePermission } from '../../../hooks/usePermission';
import UserFormModal from '../components/UserFormModal';

const UserListPage: React.FC = () => {
  const { message } = App.useApp();
  const navigate = useNavigate();

  const actionRef = useRef<ActionType>(null);

  // Trạng thái modal
  const [modalOpen, setModalOpen] = useState(false);
  const [editingUser, setEditingUser] = useState<UserDto | null>(null);
  const [companyOptions, setCompanyOptions] = useState<ReferenceOption[]>([]);

  // ST6b — permission gating (mirrors backend [Authorize(Policy=...)]).
  // Task J: UpdateUser (PUT /users/{id}) toggles IsSuperUser → raised to Policy="admin"; edit gate
  // follows so the button never shows to a non-admin who would only get a 403.
  const canCreate = usePermission('users.create');
  const canEdit = usePermission('admin');
  const canDelete = usePermission('users.delete');

  // Tải danh sách công ty khi mở modal lần đầu
  const loadCompanies = useCallback(async () => {
    if (companyOptions.length > 0) return;
    try {
      const res = await apiClient.get('/companies', { params: { pageSize: 500 } });
      setCompanyOptions(res.data.data as ReferenceOption[]);
    } catch {
      void message.error('Không thể tải danh sách công ty');
    }
  }, [companyOptions.length, message]);

  // Xử lý sự kiện
  const handleCreate = () => {
    setEditingUser(null);
    setModalOpen(true);
    void loadCompanies();
  };

  const handleEdit = (user: UserDto) => {
    setEditingUser(user);
    setModalOpen(true);
    void loadCompanies();
  };

  const handleDeactivate = async (id: string) => {
    try {
      await apiClient.delete(`/users/${id}`);
      void message.success('Đã vô hiệu hóa người dùng');
      actionRef.current?.reload();
    } catch {
      void message.error('Không thể vô hiệu hóa người dùng');
    }
  };

  const handleModalSuccess = () => {
    setModalOpen(false);
    setEditingUser(null);
    actionRef.current?.reload();
  };

  const handleModalCancel = () => {
    setModalOpen(false);
    setEditingUser(null);
  };

  // Định nghĩa cột
  const columns: ProColumns<UserDto>[] = [
    {
      title: 'Họ và tên',
      dataIndex: 'fullName',
      search: false,
      width: 200,
      ellipsis: true,
      render: (_, record) => (
        <Space>
          <Badge status={record.isActive ? 'success' : 'error'} />
          {[record.firstName, record.lastName].filter(Boolean).join(' ') || '-'}
        </Space>
      ),
    },
    {
      title: 'Tìm kiếm',
      dataIndex: 'search',
      valueType: 'text',
      hideInTable: true,
      fieldProps: { placeholder: 'Tên, tài khoản, email...' },
    },
    {
      title: 'Tài khoản',
      dataIndex: 'username',
      search: false,
      ellipsis: true,
      width: 130,
    },
    {
      title: 'Email',
      dataIndex: 'email',
      search: false,
      ellipsis: true,
      responsive: ['md'],
      width: 220,
    },
    {
      title: 'Công ty',
      dataIndex: 'companyId',
      valueType: 'select',
      hideInTable: true,
      request: async () => {
        const res = await apiClient.get('/companies', { params: { pageSize: 500 } });
        return (res.data.data as ReferenceOption[]).map((c) => ({
          label: c.name,
          value: c.id,
        }));
      },
    },
    {
      title: 'Công ty',
      dataIndex: 'companyName',
      search: false,
      ellipsis: true,
      responsive: ['lg'],
      width: 160,
    },
    {
      title: 'Chức danh',
      dataIndex: 'jobTitle',
      search: false,
      ellipsis: true,
      responsive: ['lg'],
      width: 140,
    },
    {
      title: 'Trạng thái',
      dataIndex: 'isActive',
      valueType: 'select',
      width: 100,
      valueEnum: {
        true: { text: 'Hoạt động', status: 'Success' },
        false: { text: 'Đã khóa', status: 'Error' },
      },
    },
    {
      title: 'Vai trò',
      dataIndex: 'isSuperUser',
      valueType: 'select',
      width: 110,
      valueEnum: {
        true: { text: 'Quản trị' },
        false: { text: 'Người dùng' },
      },
      render: (_, record) => (
        <Tag color={record.isSuperUser ? 'purple' : 'blue'}>
          {record.isSuperUser ? 'Quản trị' : 'Người dùng'}
        </Tag>
      ),
    },
    {
      title: 'Thao tác',
      valueType: 'option',
      width: 170,
      fixed: 'right',
      render: (_, record) => (
        <Space size="small">
          <Tooltip title="Chi tiết & License đang sử dụng">
            <Button
              type="link"
              size="small"
              icon={<EyeOutlined />}
              onClick={() => navigate(`/users/${record.id}`)}
            />
          </Tooltip>
          {canEdit && (
            <Tooltip title="Chỉnh sửa">
              <Button
                type="link"
                size="small"
                icon={<EditOutlined />}
                onClick={() => handleEdit(record)}
              />
            </Tooltip>
          )}
          {canDelete && (
            <Popconfirm
              title="Vô hiệu hóa người dùng này?"
              description="Người dùng sẽ không thể đăng nhập."
              onConfirm={() => handleDeactivate(record.id)}
              okText="Vô hiệu hóa"
              okButtonProps={{ danger: true }}
              cancelText="Hủy"
              placement="left"
            >
              <Button
                type="link"
                danger
                size="small"
                icon={<DeleteOutlined />}
                disabled={!record.isActive}
              />
            </Popconfirm>
          )}
        </Space>
      ),
    },
  ];

  return (
    <>
      <ProTable<UserDto>
        headerTitle="Quản lý người dùng"
        actionRef={actionRef}
        rowKey="id"
        size="small"
        cardBordered
        columnsState={{
          persistenceKey: 'user-list-table',
          persistenceType: 'localStorage',
        }}
        search={{
          labelWidth: 120,
          defaultCollapsed: false,
          span: 6,
        }}
        toolBarRender={() => [
          canCreate && (
            <Button
              key="create"
              type="primary"
              icon={<PlusOutlined />}
              onClick={handleCreate}
            >
              Thêm người dùng
            </Button>
          ),
        ]}
        request={async (params) => {
          try {
            const { current, pageSize, ...rest } = params;
            const res = await apiClient.get('/users', {
              params: { ...rest, page: current, pageSize },
            });
            return {
              data: res.data.data,
              success: true,
              total: res.data.pagination.totalItems,
            };
          } catch {
            void message.error('Không thể tải danh sách người dùng');
            return { data: [], success: false, total: 0 };
          }
        }}
        columns={columns}
        pagination={{
          defaultPageSize: 20,
          showSizeChanger: true,
          showTotal: (total, range) => `${range[0]}-${range[1]} / ${total} người dùng`,
        }}
        scroll={{ x: 900 }}
      />

      <UserFormModal
          key={editingUser?.id ?? 'new'}
          open={modalOpen}
          user={editingUser}
          onSuccess={handleModalSuccess}
          onCancel={handleModalCancel}
      />
    </>
  );
};

export default UserListPage;