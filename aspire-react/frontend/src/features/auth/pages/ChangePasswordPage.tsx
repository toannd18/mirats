import { useState } from 'react';
import { Alert, Button, Card, Form, Input, Typography } from 'antd';
import { LockOutlined } from '@ant-design/icons';
import { useNavigate } from 'react-router-dom';
import { changeOwnPassword } from '../services/auth.service';
import { logout } from '../services/auth';

const { Title } = Typography;

/**
 * [AUTH Phase 2] FORCED password change — shown right after login when the backend stamped the
 * session with pwd_change=1 (MustChangePassword, set by an admin reset). The limited-scope token
 * cannot call anything except /auth/password + /users/me, so this screen IS the whole system
 * until the change succeeds. On success: sign back in with the new password (clean session).
 */
export default function ChangePasswordPage() {
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const navigate = useNavigate();

  const onFinish = async (values: { newPassword: string; confirm: string }) => {
    if (values.newPassword !== values.confirm) {
      setError('Xác nhận mật khẩu không khớp.');
      return;
    }
    setLoading(true);
    setError(null);
    try {
      // NOTE: current password is the admin-set temporary one — the user may not know it is
      // required here, so the form asks for it explicitly (self-service change needs it).
      // If the backend rejects the current password the error is surfaced verbatim.
      const values2 = values as unknown as { currentPassword: string };
      const res = await changeOwnPassword(values2.currentPassword, values.newPassword);
      if (!res.ok) {
        setError(res.message || 'Không thể đổi mật khẩu.');
        return;
      }
      // Clean re-login: the old session was revoked server-side.
      await logout();
      navigate('/login', { replace: true });
    } finally {
      setLoading(false);
    }
  };

  return (
    <div style={{ display: 'flex', justifyContent: 'center', alignItems: 'center', minHeight: '100vh', background: '#f0f2f5' }}>
      <Card style={{ width: 420, boxShadow: '0 4px 16px rgba(0,0,0,0.08)' }}>
        <Alert
          type="warning"
          showIcon
          message="Bạn đang dùng mật khẩu do quản trị viên đặt"
          description="Vui lòng đặt mật khẩu mới của riêng bạn trước khi sử dụng hệ thống."
          style={{ marginBottom: 20 }}
        />
        <Title level={4} style={{ marginBottom: 16 }}>Đổi mật khẩu bắt buộc</Title>
        {error && <Alert type="error" message={error} showIcon style={{ marginBottom: 16 }} />}
        <Form onFinish={onFinish} layout="vertical" requiredMark={false}>
          <Form.Item name="currentPassword" rules={[{ required: true, message: 'Nhập mật khẩu tạm thời' }]}>
            <Input.Password prefix={<LockOutlined />} placeholder="Mật khẩu tạm thời (do admin cấp)" size="large" autoComplete="current-password" />
          </Form.Item>
          <Form.Item name="newPassword" rules={[{ required: true, message: 'Nhập mật khẩu mới' }, { min: 8, message: 'Tối thiểu 8 ký tự' }]}>
            <Input.Password prefix={<LockOutlined />} placeholder="Mật khẩu mới (tối thiểu 8 ký tự)" size="large" autoComplete="new-password" />
          </Form.Item>
          <Form.Item name="confirm" rules={[{ required: true, message: 'Nhập lại mật khẩu mới' }]}>
            <Input.Password prefix={<LockOutlined />} placeholder="Nhập lại mật khẩu mới" size="large" autoComplete="new-password" />
          </Form.Item>
          <Form.Item style={{ marginBottom: 0 }}>
            <Button type="primary" htmlType="submit" size="large" block loading={loading}>
              Đổi mật khẩu và tiếp tục
            </Button>
          </Form.Item>
        </Form>
      </Card>
    </div>
  );
}
