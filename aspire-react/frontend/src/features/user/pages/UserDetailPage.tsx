import { useCallback, useEffect, useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import {
  ArrowLeftOutlined, CheckCircleOutlined, EditOutlined, KeyOutlined, StopOutlined,
} from '@ant-design/icons';
import { App, Button, Card, Descriptions, Empty, Input, Modal, Space, Spin, Tag, Typography } from 'antd';
import apiClient from '../../../services/api-client';
import { isSuperUser } from '../../auth/services/auth';
import { adminResetPassword } from '../../auth/services/auth.service';
import { usePermission } from '../../../hooks/usePermission';
import ActionLogTable, { type ActionLogRow } from '../../../shared/components/ActionLogTable';
import LicenseUsageTable from '../../../shared/components/LicenseUsageTable';
import ListRowActions from '../../../shared/components/list/ListRowActions';
import UserFormModal from '../components/UserFormModal';
import type { UserDto } from '../types/users';

const { Title } = Typography;

export default function UserDetailPage() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const { message, modal } = App.useApp();
  const [user, setUser] = useState<UserDto | null>(null);
  const [loading, setLoading] = useState(true);
  const [editOpen, setEditOpen] = useState(false);
  const [resetOpen, setResetOpen] = useState(false);
  const [resetPassword, setResetPassword] = useState('');
  const [resetSubmitting, setResetSubmitting] = useState(false);

  // Same gates as UserListPage: edit follows admin policy, deactivate uses users.delete.
  const canEdit = usePermission('admin');
  const canDelete = usePermission('users.delete');

  const loadUser = useCallback(() => {
    if (!id) return;
    setLoading(true);
    apiClient.get(`/users/${id}`)
      .then(r => setUser(r.data.data as UserDto))
      .catch(() => setUser(null))
      .finally(() => setLoading(false));
  }, [id]);

  useEffect(() => { loadUser(); }, [loadUser]);

  if (loading) return <Spin style={{ display: 'block', margin: '80px auto' }} />;
  if (!user) return <Empty description="Không tìm thấy người dùng" style={{ marginTop: 80 }} />;

  const fullName = [user.firstName, user.lastName].filter(Boolean).join(' ') || user.username;

  const confirmDeactivate = () => {
    modal.confirm({
      title: 'Vô hiệu hóa người dùng?',
      content: 'Tài khoản sẽ bị khóa và không thể đăng nhập cho tới khi được kích hoạt lại.',
      okText: 'Vô hiệu hóa',
      cancelText: 'Hủy',
      okButtonProps: { danger: true },
      onOk: async () => {
        try {
          await apiClient.delete(`/users/${user.id}`);
          void message.success('Đã vô hiệu hóa người dùng');
          loadUser();
        } catch (err: unknown) {
          const e = err as { response?: { data?: { message?: string } } };
          void message.error(e?.response?.data?.message ?? 'Không thể vô hiệu hóa người dùng');
        }
      },
    });
  };

  const confirmActivate = async () => {
    try {
      await apiClient.put(`/users/${user.id}`, { id: user.id, isActive: true });
      void message.success('Đã kích hoạt người dùng');
      loadUser();
    } catch (err: unknown) {
      const e = err as { response?: { data?: { message?: string } } };
      void message.error(e?.response?.data?.message ?? 'Không thể kích hoạt người dùng');
    }
  };

  const submitReset = async () => {
    if (resetPassword.length < 8) {
      void message.warning('Mật khẩu mới phải có ít nhất 8 ký tự');
      return;
    }
    setResetSubmitting(true);
    try {
      const res = await adminResetPassword(user.id, resetPassword);
      if (!res.ok) {
        void message.error(res.message ?? 'Đặt lại mật khẩu thất bại');
        return;
      }
      void message.success('Đã đặt lại mật khẩu. Người dùng sẽ phải đổi ở lần đăng nhập kế tiếp.');
      setResetOpen(false);
      setResetPassword('');
    } finally {
      setResetSubmitting(false);
    }
  };

  return (
    <div>
      <div style={{ marginBottom: 16, display: 'flex', alignItems: 'center', gap: 12, flexWrap: 'wrap' }}>
        <Button icon={<ArrowLeftOutlined />} onClick={() => navigate('/users')}>Quay lại</Button>
        <div style={{ minWidth: 0 }}>
          <Title level={4} style={{ margin: 0 }}>{fullName}</Title>
          <Space size={8} wrap>
            <Tag>{user.username}</Tag>
            <Tag color={user.isSuperUser ? 'purple' : 'blue'}>{user.isSuperUser ? 'Quản trị' : 'Người dùng'}</Tag>
            <Tag color={user.isActive ? 'green' : 'red'}>{user.isActive ? 'Hoạt động' : 'Đã khóa'}</Tag>
          </Space>
        </div>
        <div style={{ flex: 1 }} />
        <ListRowActions
          primary={canEdit ? {
            key: 'edit', label: 'Sửa', icon: <EditOutlined />,
            onClick: () => setEditOpen(true),
          } : null}
          menuItems={[
            ...(canEdit ? [{
              key: 'reset-password', label: 'Đặt lại mật khẩu', icon: <KeyOutlined />,
              onClick: () => { setResetPassword(''); setResetOpen(true); },
            }] : []),
            ...(user.isActive
              ? (canDelete ? [{
                key: 'deactivate', label: 'Vô hiệu hóa', icon: <StopOutlined />, danger: true,
                onClick: confirmDeactivate,
              }] : [])
              : (canEdit ? [{
                key: 'activate', label: 'Kích hoạt', icon: <CheckCircleOutlined />,
                onClick: confirmActivate,
              }] : [])),
          ]}
        />
      </div>
      <Card title="Thông tin người dùng" style={{ marginBottom: 16 }}>
        <Descriptions bordered size="small" column={{ xs: 1, sm: 2 }}>
          <Descriptions.Item label="Tài khoản">{user.username}</Descriptions.Item>
          <Descriptions.Item label="Email">{user.email || '—'}</Descriptions.Item>
          <Descriptions.Item label="Mã nhân viên">{user.employeeNumber || '—'}</Descriptions.Item>
          <Descriptions.Item label="Chức danh">{user.jobTitle || '—'}</Descriptions.Item>
          <Descriptions.Item label="Công ty">{user.companyName || '—'}</Descriptions.Item>
          <Descriptions.Item label="Phòng ban">{user.departmentName || '—'}</Descriptions.Item>
          <Descriptions.Item label="Vị trí">{user.locationName || '—'}</Descriptions.Item>
          <Descriptions.Item label="Vai trò">
            <Tag color={user.isSuperUser ? 'purple' : 'blue'}>{user.isSuperUser ? 'Quản trị' : 'Người dùng'}</Tag>
          </Descriptions.Item>
          <Descriptions.Item label="Trạng thái">
            {user.isActive ? <Tag color="green">Hoạt động</Tag> : <Tag color="red">Đã khóa</Tag>}
          </Descriptions.Item>
        </Descriptions>
      </Card>
      <Card title="Bản quyền đang sử dụng" style={{ marginBottom: 16 }}>
        <LicenseUsageTable scope={{ type: 'user', id: user.id }} />
      </Card>
      {/* [AUDIT-LOG B6a] Nhật ký hoạt động của người dùng — dùng chung ActionLogTable để mọi định dạng
          logMeta (camelCase của log CŨ và snake_case của log MỚI) hiển thị cùng một cách.
          [B7a] Backend chỉ cho SUPERUSER đọc nhật ký User (GetActionLogsQueryHandler) ⇒ card chỉ hiện
          với superuser, tránh gọi API chắc chắn 404 cho người dùng thường. */}
      {isSuperUser() && (
        <Card title="Lịch sử hoạt động">
          <ActionLogTable
            params={{ itemType: 10, itemId: user.id }}
            request={async () => {
              try {
                const res = await apiClient.get('/action-logs', { params: { itemType: 10, itemId: user.id } });
                const data = (res.data?.data ?? []) as ActionLogRow[];
                return { data, success: true, total: data.length };
              } catch (err) {
                console.error('Failed to load user action logs:', err);
                return { data: [], success: false, total: 0 };
              }
            }}
          />
        </Card>
      )}

      <UserFormModal
        open={editOpen}
        user={user}
        onSuccess={() => { setEditOpen(false); loadUser(); }}
        onCancel={() => setEditOpen(false)}
      />

      <Modal
        title="Đặt lại mật khẩu"
        open={resetOpen}
        onCancel={() => setResetOpen(false)}
        onOk={submitReset}
        okText="Đặt lại"
        cancelText="Hủy"
        confirmLoading={resetSubmitting}
        destroyOnHidden
      >
        <Input.Password
          placeholder="Mật khẩu mới (ít nhất 8 ký tự)"
          value={resetPassword}
          onChange={(e) => setResetPassword(e.target.value)}
          onPressEnter={submitReset}
        />
      </Modal>
    </div>
  );
}