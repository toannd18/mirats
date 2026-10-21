import { useRef, useState, useCallback } from 'react';
import {
  Button, Space, Tag, Tooltip, App, Card, Divider, Input, Typography, Modal, Empty, Avatar,
} from 'antd';
import type { MenuProps } from 'antd';
import {
  PlusOutlined, EditOutlined, KeyOutlined, UserOutlined, StopOutlined,
  CheckCircleOutlined, WarningOutlined,
} from '@ant-design/icons';
import { useNavigate } from 'react-router-dom';
import { ProList, ProTable } from '@ant-design/pro-components';
import type { ActionType, ProColumns } from '@ant-design/pro-components';
import type { UserDto, ReferenceOption } from '../types/users';
import apiClient from '../../../services/api-client';
import { adminResetPassword } from '../../auth/services/auth.service';
import { usePermission } from '../../../hooks/usePermission';
import { useIsMobile } from '../../../hooks/useIsMobile';
import UserFormModal from '../components/UserFormModal';
import ListRowActions from '../../../shared/components/list/ListRowActions';
import type { ListPrimaryAction } from '../../../shared/components/list/ListRowActions';
import { EllipsisText, NowrapText } from '../../../shared/components/list/ListText';
import { useListColumnsState } from '../../../shared/hooks/useListColumnsState';
import { formatDate } from '../../../utils/format';

const { Text, Title } = Typography;

// ==================== Columns ====================

/**
 * [SORT-API] The list API returns entity-backed CreatedAt/UpdatedAt (already projected by
 * ListUsersQuery); they are typed here because the shared `UserDto` type does not declare them.
 * There is no last-login column: the User entity has no such field, so no always-'-' column.
 */
type UserRow = UserDto & { createdAt?: string | null; updatedAt?: string | null };

const fullNameOf = (record: UserRow) =>
  [record.firstName, record.lastName].filter(Boolean).join(' ') || '-';

/** Versioned once, outside the component: a fresh object each render would defeat ProTable persistence. */
const USER_COLUMN_STATE_KEY = 'user-list-columns-v2';
const DEFAULT_COLUMN_STATE = {
  username: { show: true },
  fullName: { show: true },
  email: { show: true },
  role: { show: true },
  status: { show: true },
  company: { show: true },
  department: { show: false },
  location: { show: false },
  employeeNumber: { show: false },
  jobTitle: { show: false },
  createdAt: { show: false },
};

// ==================== Mobile card styles ====================

const dataGridStyle: React.CSSProperties = {
  display: 'grid',
  gridTemplateColumns: '1fr 1fr',
  gap: '6px 12px',
  padding: '10px 12px',
  background: '#fafafa',
  borderRadius: 8,
  border: '1px solid #f0f0f0',
};

// ==================== Component ====================

const UserListPage: React.FC = () => {
  const { message, modal } = App.useApp();
  const navigate = useNavigate();

  // 1 actionRef dùng chung cho ProTable (desktop) và ProList (mobile Card).
  const actionRef = useRef<ActionType>(null);
  const isMobile = useIsMobile();
  const columnsState = useListColumnsState(USER_COLUMN_STATE_KEY, DEFAULT_COLUMN_STATE);

  // Trạng thái modal
  const [modalOpen, setModalOpen] = useState(false);
  const [editingUser, setEditingUser] = useState<UserDto | null>(null);
  const [companyOptions, setCompanyOptions] = useState<ReferenceOption[]>([]);

  // [AUTH Phase 4] Admin reset-password modal state
  const [resetUser, setResetUser] = useState<UserDto | null>(null);
  const [resetOpen, setResetOpen] = useState(false);
  const [resetPassword, setResetPassword] = useState('');
  const [resetSubmitting, setResetSubmitting] = useState(false);

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
    } catch (err: unknown) {
      const e = err as { response?: { data?: { message?: string } } };
      void message.error(e?.response?.data?.message ?? 'Không thể vô hiệu hóa người dùng');
    }
  };

  // Kích hoạt lại: PUT /users/{id} (Policy="admin") — patch semantics, chỉ gửi isActive.
  const handleActivate = async (id: string) => {
    try {
      await apiClient.put(`/users/${id}`, { id, isActive: true });
      void message.success('Đã kích hoạt người dùng');
      actionRef.current?.reload();
    } catch (err: unknown) {
      const e = err as { response?: { data?: { message?: string } } };
      void message.error(e?.response?.data?.message ?? 'Không thể kích hoạt người dùng');
    }
  };

  const confirmDeactivate = (record: UserRow) => {
    modal.confirm({
      title: 'Vô hiệu hóa người dùng này?',
      content: 'Người dùng sẽ không thể đăng nhập. Lịch sử hoạt động vẫn được giữ lại.',
      okText: 'Vô hiệu hóa',
      cancelText: 'Hủy',
      okButtonProps: { danger: true },
      onOk: () => handleDeactivate(record.id),
    });
  };

  const confirmActivate = (record: UserRow) => {
    modal.confirm({
      title: 'Kích hoạt người dùng này?',
      content: 'Người dùng sẽ có thể đăng nhập trở lại.',
      okText: 'Kích hoạt',
      cancelText: 'Hủy',
      onOk: () => handleActivate(record.id),
    });
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

  // [AUTH Phase 4] Admin reset-password flow — endpoint từ Phase 1, UI B3 này.
  const openReset = (user: UserDto) => {
    setResetUser(user);
    setResetPassword('');
    setResetOpen(true);
  };

  const handleResetSubmit = async () => {
    if (!resetUser) return;
    if (resetPassword.length < 8) {
      void message.error('Mật khẩu tạm thời phải có ít nhất 8 ký tự');
      return;
    }
    setResetSubmitting(true);
    try {
      const res = await adminResetPassword(resetUser.id, resetPassword);
      if (res.ok) {
        void message.success('Đã đặt lại mật khẩu. Người dùng sẽ phải đổi ở lần đăng nhập kế tiếp.');
        setResetOpen(false);
        setResetUser(null);
        actionRef.current?.reload();
      } else {
        void message.error(res.message || 'Không thể đặt lại mật khẩu');
      }
    } catch {
      void message.error('Không thể đặt lại mật khẩu');
    } finally {
      setResetSubmitting(false);
    }
  };

  /**
   * 1 fetch dùng chung: desktop truyền nguyên params của ProTable/ProList search.
   * [SORT-API] `sort` của ProTable ({ field: 'ascend'|'descend' }) được chuyển thành
   * `sortBy`/`sortDir` cho backend (không sắp xếp phía client — server-side sort).
   */
  const fetchUsers = async (
    query: Record<string, unknown>,
    sort?: Record<string, unknown>,
  ) => {
    const sortEntry = Object.entries(sort ?? {})
      .find(([, order]) => order === 'ascend' || order === 'descend');
    const sortBy = sortEntry?.[0];
    const sortDir = sortEntry?.[1] === 'descend' ? 'desc' : 'asc';
    const res = await apiClient.get('/users', {
      params: {
        ...query,
        ...(sortBy ? { sortBy, sortDir } : {}),
      },
    });
    return {
      list: (res.data.data ?? []) as UserRow[],
      total: (res.data.pagination?.totalItems ?? 0) as number,
    };
  };

  // ──── Row actions: ONE primary action + ⋯ menu (UI-1 pattern) ────

  /** Sửa là primary action cho mọi người dùng (nếu có quyền); phần còn lại vào menu. */
  const primaryActionFor = (record: UserRow): ListPrimaryAction | null => {
    if (!canEdit) return null;
    return {
      key: 'edit',
      label: 'Sửa',
      icon: <EditOutlined />,
      onClick: () => handleEdit(record),
    };
  };

  const menuItemsFor = (record: UserRow): MenuProps['items'] => {
    const items: NonNullable<MenuProps['items']> = [];
    if (canEdit) {
      items.push({
        key: 'reset-password',
        label: 'Đặt lại mật khẩu',
        icon: <KeyOutlined />,
        onClick: () => openReset(record),
      });
    }
    if (record.isActive) {
      // Backend DELETE /users/{id} = soft delete (IsActive=false) ⇒ "Vô hiệu hóa".
      if (canDelete) {
        items.push({
          key: 'deactivate',
          label: 'Vô hiệu hóa',
          icon: <StopOutlined />,
          danger: true,
          onClick: () => confirmDeactivate(record),
        });
      }
    } else if (canEdit) {
      items.push({
        key: 'activate',
        label: 'Kích hoạt',
        icon: <CheckCircleOutlined />,
        onClick: () => confirmActivate(record),
      });
    }
    return items;
  };

  // ──── Search columns (mobile collapsed by default, desktop expanded) ────

  const searchColumns: ProColumns<UserRow>[] = [
    {
      title: 'Tìm kiếm',
      dataIndex: 'search',
      valueType: 'text',
      hideInTable: true,
      fieldProps: { placeholder: 'Tên, tài khoản, email...' },
    },
    {
      title: 'Công ty',
      dataIndex: 'companyId',
      valueType: 'select',
      hideInTable: true,
      request: async () => {
        const res = await apiClient.get('/companies', { params: { pageSize: 500 } });
        return (res.data.data as ReferenceOption[]).map((c) => ({ label: c.name, value: c.id }));
      },
    },
    {
      title: 'Trạng thái',
      dataIndex: 'isActive',
      valueType: 'select',
      hideInTable: true,
      valueEnum: {
        true: { text: 'Hoạt động' },
        false: { text: 'Đã khóa' },
      },
    },
    {
      title: 'Vai trò',
      dataIndex: 'isSuperUser',
      valueType: 'select',
      hideInTable: true,
      valueEnum: {
        true: { text: 'Quản trị' },
        false: { text: 'Người dùng' },
      },
    },
  ];

  // ──── Display columns: 6 mặc định + 5 tuỳ chọn (mặc định ẩn) ────

  const renderStatus = (record: UserRow) => (
    <Space size={4} wrap>
      <Tag color={record.isActive ? 'success' : 'error'} style={{ marginInlineEnd: 0 }}>
        {record.isActive ? 'Hoạt động' : 'Đã khóa'}
      </Tag>
      {record.hasPassword === false && (
        <Tooltip title="Tài khoản chưa có mật khẩu đăng nhập — dùng “Đặt lại mật khẩu”.">
          <WarningOutlined style={{ color: '#faad14' }} />
        </Tooltip>
      )}
    </Space>
  );

  const renderRole = (record: UserRow) => (
    <Tag color={record.isSuperUser ? 'purple' : 'blue'} style={{ marginInlineEnd: 0 }}>
      {record.isSuperUser ? 'Quản trị' : 'Người dùng'}
    </Tag>
  );

  const desktopColumns: ProColumns<UserRow>[] = ([
    {
      title: 'Tên đăng nhập', dataIndex: 'username', key: 'username', width: 140,
      // [SORT-API] server-side, natural collation (user2 < user10) — no client sort.
      sorter: true,
      render: (_: unknown, record: UserRow) => (
        <Tooltip title={record.username} placement="topLeft">
          <Typography.Link
            onClick={() => navigate(`/users/${record.id}`)}
            style={{
              display: 'block',
              overflow: 'hidden',
              textOverflow: 'ellipsis',
              whiteSpace: 'nowrap',
              fontFamily: 'monospace',
            }}
          >
            {record.username}
          </Typography.Link>
        </Tooltip>
      ),
    },
    {
      // Họ tên là cột tính toán (firstName + lastName) — backend không có khoá sort tương ứng,
      // nên KHÔNG sortable (không sắp xếp client).
      title: 'Họ tên', key: 'fullName', width: 160,
      render: (_: unknown, record: UserRow) => {
        const text = fullNameOf(record);
        return (
          <Tooltip title={text} placement="topLeft">
            <Typography.Link
              onClick={() => navigate(`/users/${record.id}`)}
              style={{ display: 'block', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}
            >
              {text}
            </Typography.Link>
          </Tooltip>
        );
      },
    },
    {
      title: 'Email', dataIndex: 'email', key: 'email', width: 180,
      sorter: true,
      render: (_: unknown, record: UserRow) => <EllipsisText value={record.email} />,
    },
    {
      title: 'Vai trò', key: 'role', width: 90,
      render: (_: unknown, record: UserRow) => renderRole(record),
    },
    {
      title: 'Trạng thái', key: 'status', width: 100,
      render: (_: unknown, record: UserRow) => renderStatus(record),
    },
    {
      title: 'Công ty', key: 'company', width: 130,
      render: (_: unknown, record: UserRow) => <EllipsisText value={record.companyName} />,
    },
    {
      title: 'Phòng ban', key: 'department', width: 170,
      render: (_: unknown, record: UserRow) => <EllipsisText value={record.departmentName} />,
    },
    {
      title: 'Vị trí', key: 'location', width: 150,
      render: (_: unknown, record: UserRow) => <EllipsisText value={record.locationName} />,
    },
    {
      title: 'Số nhân viên', key: 'employeeNumber', width: 140,
      render: (_: unknown, record: UserRow) => <NowrapText value={record.employeeNumber} />,
    },
    {
      title: 'Chức danh', key: 'jobTitle', width: 160,
      render: (_: unknown, record: UserRow) => <EllipsisText value={record.jobTitle} />,
    },
    {
      // [SORT-API] entity-backed CreatedAt (already in the list projection); replaces the old
      // "Đăng nhập gần nhất" column which had no data source (always '-').
      title: 'Ngày tạo', dataIndex: 'createdAt', key: 'createdAt', width: 160,
      sorter: true,
      render: (_: unknown, record: UserRow) => <NowrapText value={formatDate(record.createdAt)} />,
    },
    {
      title: 'Thao tác', key: 'actions', width: 150, fixed: 'right',
      render: (_: unknown, record: UserRow) => (
        <ListRowActions primary={primaryActionFor(record)} menuItems={menuItemsFor(record)} />
      ),
    },
  ] as ProColumns<UserRow>[]).map((column) => ({ ...column, hideInSearch: true, search: false }));

  // ──── Render ────

  return (
    <>
      {isMobile ? (
        <ProList<UserRow>
          headerTitle="Quản lý người dùng"
          actionRef={actionRef}
          rowKey="id"
          ghost
          cardProps={false}
          columns={searchColumns}
          grid={{ gutter: 16, xs: 1, sm: 1 }}
          search={{ labelWidth: 'auto', defaultCollapsed: true }}
          toolBarRender={() => [
            canCreate && (
              <Button key="create" type="primary" size="middle" icon={<PlusOutlined />} onClick={handleCreate}>
                Thêm
              </Button>
            ),
          ]}
          request={async (params, sort) => {
            try {
              const { current, pageSize, ...rest } = params;
              const { list, total } = await fetchUsers({ ...rest, page: current, pageSize }, sort);
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
          locale={{ emptyText: <Empty description="Không tìm thấy người dùng phù hợp" /> }}
          itemRender={(record) => (
            <Card
              hoverable
              onClick={() => navigate(`/users/${record.id}`)}
              style={{ borderRadius: 12, marginBottom: 16, cursor: 'pointer' }}
              styles={{ body: { padding: 16 } }}
            >
              {/* Header: avatar + họ tên + tên đăng nhập + email */}
              <div style={{ display: 'flex', alignItems: 'center', gap: 10, marginBottom: 10 }}>
                <Avatar
                  size={40}
                  icon={<UserOutlined />}
                  style={{
                    flexShrink: 0,
                    background: record.isActive ? '#e6f4ff' : '#fff1f0',
                    color: record.isActive ? '#1677ff' : '#ff4d4f',
                  }}
                />
                <div style={{ flex: 1, minWidth: 0 }}>
                  <div style={{ display: 'flex', alignItems: 'baseline', gap: 6, flexWrap: 'wrap' }}>
                    <Typography.Link
                      style={{ fontSize: 15, fontWeight: 600, cursor: 'pointer' }}
                      onClick={(e) => { e.stopPropagation(); navigate(`/users/${record.id}`); }}
                    >
                      {fullNameOf(record)}
                    </Typography.Link>
                    <Text code style={{ fontSize: 11, whiteSpace: 'nowrap' }}>{record.username}</Text>
                  </div>
                  <Text
                    type="secondary"
                    style={{ fontSize: 12, display: 'block', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}
                  >
                    {record.email || '-'}
                  </Text>
                </div>
              </div>

              {/* Tags: vai trò + trạng thái */}
              <Space size={[4, 4]} wrap style={{ marginBottom: 10 }}>
                {renderRole(record)}
                {renderStatus(record)}
              </Space>

              {/* Data grid: công ty + chức danh */}
              <div style={dataGridStyle}>
                <div style={{ minWidth: 0 }}>
                  <Text type="secondary" style={{ fontSize: 12, display: 'block' }}>Công ty</Text>
                  <Text style={{ fontSize: 13 }} ellipsis>{record.companyName || '-'}</Text>
                </div>
                <div style={{ minWidth: 0 }}>
                  <Text type="secondary" style={{ fontSize: 12, display: 'block' }}>Chức danh</Text>
                  <Text style={{ fontSize: 13 }} ellipsis>{record.jobTitle || '-'}</Text>
                </div>
              </div>

              <Divider style={{ margin: '10px 0' }} />

              <div style={{ display: 'flex', justifyContent: 'flex-end' }}>
                <ListRowActions
                  primary={primaryActionFor(record)}
                  menuItems={menuItemsFor(record)}
                  stopPropagation
                />
              </div>
            </Card>
          )}
        />
      ) : (
        <ProTable<UserRow>
          headerTitle={<Title level={4} style={{ margin: 0 }}>Quản lý người dùng</Title>}
          actionRef={actionRef}
          rowKey="id"
          size="small"
          cardBordered
          columns={[...searchColumns, ...desktopColumns]}
          search={{ labelWidth: 'auto', defaultCollapsed: false }}
          options={{ reload: true, density: true, setting: true }}
          columnsState={columnsState}
          toolBarRender={() => [
            canCreate && (
              <Button key="create" type="primary" size="middle" icon={<PlusOutlined />} onClick={handleCreate}>
                Thêm người dùng
              </Button>
            ),
          ]}
          request={async (params, sort) => {
            try {
              const { current, pageSize, ...rest } = params;
              const { list, total } = await fetchUsers({ ...rest, page: current, pageSize }, sort);
              return { data: list, success: true, total };
            } catch {
              void message.error('Không thể tải danh sách người dùng');
              return { data: [], success: false, total: 0 };
            }
          }}
          pagination={{
            defaultPageSize: 20,
            showSizeChanger: true,
            showTotal: (total, range) => `${range[0]}-${range[1]} / ${total} người dùng`,
          }}
          scroll={{ x: 'max-content' }}
        />
      )}

      <UserFormModal
        key={editingUser?.id ?? 'new'}
        open={modalOpen}
        user={editingUser}
        onSuccess={handleModalSuccess}
        onCancel={handleModalCancel}
      />

      {/* [AUTH Phase 4] Admin reset-password modal — render cho CẢ desktop và mobile
          (trước đây nằm trong nhánh desktop nên mobile không mở được). */}
      <Modal
        title={`Đặt lại mật khẩu — ${resetUser?.username ?? ''}`}
        open={resetOpen}
        onOk={() => void handleResetSubmit()}
        onCancel={() => { setResetOpen(false); setResetUser(null); }}
        confirmLoading={resetSubmitting}
        okText="Đặt lại mật khẩu"
        destroyOnHidden
      >
        <Text type="secondary" style={{ display: 'block', marginBottom: 12 }}>
          Nhập mật khẩu tạm thời (≥ 8 ký tự) để giao cho người dùng. Họ sẽ bị bắt buộc đổi mật
          khẩu ở lần đăng nhập kế tiếp; mọi phiên đăng nhập hiện tại bị thu hồi.
        </Text>
        <Input.Password
          placeholder="Mật khẩu tạm thời (tối thiểu 8 ký tự)"
          value={resetPassword}
          onChange={(e) => setResetPassword(e.target.value)}
          autoComplete="new-password"
        />
      </Modal>
    </>
  );
};

export default UserListPage;
