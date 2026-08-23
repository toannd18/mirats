import { useRef, useState, useCallback, type ReactNode } from 'react';
import {
  Button, Space, Tag, Badge, Popconfirm, Tooltip, App, Card, Divider, Input, Select, Typography,
} from 'antd';
import { PlusOutlined, EditOutlined, DeleteOutlined, EyeOutlined } from '@ant-design/icons';
import { useNavigate } from 'react-router-dom';
import { ProList, ProTable } from '@ant-design/pro-components';
import type { ActionType, ProColumns } from '@ant-design/pro-components';
import type { UserDto, ReferenceOption } from '../types/users';
import apiClient from '../../../services/api-client';
import { usePermission } from '../../../hooks/usePermission';
import { useIsMobile } from '../../../hooks/useIsMobile';
import UserFormModal from '../components/UserFormModal';

const { Text } = Typography;

const UserListPage: React.FC = () => {
  const { message } = App.useApp();
  const navigate = useNavigate();

  // ST7b — 1 actionRef dùng chung cho ProTable (desktop) và ProList (mobile Card).
  const actionRef = useRef<ActionType>(null);
  const isMobile = useIsMobile();

  // Trạng thái modal
  const [modalOpen, setModalOpen] = useState(false);
  const [editingUser, setEditingUser] = useState<UserDto | null>(null);
  const [companyOptions, setCompanyOptions] = useState<ReferenceOption[]>([]);

  // ST7b — filter state cho mobile Card view (thay search form ProTable bị "bẹp" ở 375px).
  const [mobileSearch, setMobileSearch] = useState('');
  const [mobileStatus, setMobileStatus] = useState<string | undefined>(undefined);
  const [mobileRole, setMobileRole] = useState<string | undefined>(undefined);

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

  // ST7b — 1 fetch dùng chung: desktop truyền nguyên params của ProTable search
  // (search/companyId/isActive/isSuperUser), mobile truyền từ filter bar riêng.
  const fetchUsers = async (query: Record<string, unknown>) => {
    const res = await apiClient.get('/users', { params: query });
    return {
      list: (res.data.data ?? []) as UserDto[],
      total: (res.data.pagination?.totalItems ?? 0) as number,
    };
  };

  // ST7b — action buttons dùng chung cột Thao tác (desktop) và Card (mobile).
  const renderActions = (record: UserDto): ReactNode[] => [
    <Tooltip key="detail" title="Chi tiết & License đang sử dụng">
      <Button
        type="link"
        size="small"
        icon={<EyeOutlined />}
        onClick={() => navigate(`/users/${record.id}`)}
      />
    </Tooltip>,
    canEdit && (
      <Tooltip key="edit" title="Chỉnh sửa">
        <Button
          type="link"
          size="small"
          icon={<EditOutlined />}
          onClick={() => handleEdit(record)}
        />
      </Tooltip>
    ),
    canDelete && (
      <Popconfirm
        key="deactivate"
        title="Vô hiệu hóa người dùng này?"
        description="Người dùng sẽ không thể đăng nhập."
        onConfirm={() => handleDeactivate(record.id)}
        okText="Vô hiệu hóa"
        okButtonProps={{ danger: true }}
        cancelText="Hủy"
      >
        <Button
          type="link"
          danger
          size="small"
          icon={<DeleteOutlined />}
          disabled={!record.isActive}
        />
      </Popconfirm>
    ),
  ].filter(Boolean) as ReactNode[];

  // Định nghĩa cột (desktop)
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
      render: (_, record) => <Space size="small">{renderActions(record)}</Space>,
    },
  ];

  // ─── Mobile (ST7b): ProList Card + filter bar riêng thay search form ProTable ───
  if (isMobile) {
    return (
      <>
        {/* Filter bar thay thế search form ProTable (bị bẹp ở 375px):
            search full-width + 2 Select lọc xếp hàng riêng, không chèn nhau. */}
        <div style={{ display: 'flex', flexWrap: 'wrap', gap: 8, marginBottom: 12 }}>
          <Input.Search
            allowClear
            enterButton
            placeholder="Tên, tài khoản, email..."
            style={{ flex: '1 1 100%' }}
            onSearch={(v) => { setMobileSearch(v); actionRef.current?.reload(); }}
          />
          <Select
            allowClear
            placeholder="Trạng thái"
            style={{ flex: 1, minWidth: 140 }}
            value={mobileStatus}
            onChange={(v) => { setMobileStatus(v); actionRef.current?.reload(); }}
            options={[
              { label: 'Hoạt động', value: 'true' },
              { label: 'Đã khóa', value: 'false' },
            ]}
          />
          <Select
            allowClear
            placeholder="Vai trò"
            style={{ flex: 1, minWidth: 140 }}
            value={mobileRole}
            onChange={(v) => { setMobileRole(v); actionRef.current?.reload(); }}
            options={[
              { label: 'Quản trị', value: 'true' },
              { label: 'Người dùng', value: 'false' },
            ]}
          />
        </div>

        <ProList<UserDto>
          headerTitle="Quản lý người dùng"
          actionRef={actionRef}
          rowKey="id"
          ghost
          cardProps={false}
          search={false}
          grid={{ gutter: 16, xs: 1, sm: 1 }}
          toolBarRender={() => [
            canCreate && (
              <Button key="create" type="primary" icon={<PlusOutlined />} onClick={handleCreate}>
                Thêm
              </Button>
            ),
          ]}
          request={async () => {
            try {
              const { list, total } = await fetchUsers({
                ...(mobileSearch ? { search: mobileSearch } : {}),
                ...(mobileStatus !== undefined ? { isActive: mobileStatus } : {}),
                ...(mobileRole !== undefined ? { isSuperUser: mobileRole } : {}),
              });
              return { data: list, success: true, total };
            } catch {
              void message.error('Không thể tải danh sách người dùng');
              return { data: [], success: false, total: 0 };
            }
          }}
          pagination={{
            defaultPageSize: 20,
            showSizeChanger: false,
            showTotal: (total, range) => `${range[0]}-${range[1]} / ${total}`,
          }}
          itemRender={(record) => (
            <Card hoverable style={{ borderRadius: 12, marginBottom: 16 }} styles={{ body: { padding: 16 } }}>
              <div style={{ display: 'flex', alignItems: 'center', gap: 8, marginBottom: 10, flexWrap: 'wrap' }}>
                <Badge status={record.isActive ? 'success' : 'error'} />
                <Text strong style={{ fontSize: 15, flex: 1, minWidth: 0 }}>
                  {[record.firstName, record.lastName].filter(Boolean).join(' ') || '-'}
                </Text>
                <Tag color={record.isSuperUser ? 'purple' : 'blue'} style={{ marginInlineEnd: 0 }}>
                  {record.isSuperUser ? 'Quản trị' : 'Người dùng'}
                </Tag>
              </div>
              <div style={{ display: 'grid', gridTemplateColumns: 'auto 1fr', gap: '6px 12px', padding: '10px 12px', background: '#fafafa', borderRadius: 8 }}>
                <Text type="secondary" style={{ fontSize: 12 }}>Tài khoản</Text>
                <Text style={{ fontSize: 13 }}>{record.username}</Text>
                <Text type="secondary" style={{ fontSize: 12 }}>Email</Text>
                <Text style={{ fontSize: 13, wordBreak: 'break-all' }}>
                  {record.email
                    ? <a href={`mailto:${record.email}`}>{record.email}</a>
                    : '-'}
                </Text>
                <Text type="secondary" style={{ fontSize: 12 }}>Công ty</Text>
                <Text style={{ fontSize: 13 }}>{record.companyName || '-'}</Text>
                <Text type="secondary" style={{ fontSize: 12 }}>Chức danh</Text>
                <Text style={{ fontSize: 13 }}>{record.jobTitle || '-'}</Text>
                <Text type="secondary" style={{ fontSize: 12 }}>Trạng thái</Text>
                <Text style={{ fontSize: 13 }}>
                  {record.isActive
                    ? <Tag color="success" style={{ margin: 0 }}>Hoạt động</Tag>
                    : <Tag color="error" style={{ margin: 0 }}>Đã khóa</Tag>}
                </Text>
              </div>
              <Divider style={{ margin: '10px 0' }} />
              <Space size="small" wrap>{renderActions(record)}</Space>
            </Card>
          )}
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
  }

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
            const { list, total } = await fetchUsers({ ...rest, page: current, pageSize });
            return { data: list, success: true, total };
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
        scroll={{ x: 'max-content' }}
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
