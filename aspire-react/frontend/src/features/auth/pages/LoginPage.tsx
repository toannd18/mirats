import { useEffect, useState } from 'react';
import { Alert, Button, Card, Divider, Form, Input, Typography } from 'antd';
import { LockOutlined, UserOutlined, SafetyOutlined } from '@ant-design/icons';
import { useLocation, useNavigate } from 'react-router-dom';
import { loginWithPassword } from '../services/auth';
import { isPasskeyEnabled, loginWithPasskey } from '../services/passkeys';
import { safeInternalPath } from '../../../utils/safeRedirect';

const { Title } = Typography;

/**
 * [AUTH Phase 2 + 3] Password login page — the primary sign-in method (always available).
 * When the auth.passkeys.enabled flag is on, an OPTIONAL passkey button appears (username-less
 * discoverable login). After a successful login with mustChangePassword=true, the user is
 * routed to the FORCED password change screen (limited-scope token).
 */
export default function LoginPage() {
  const [loading, setLoading] = useState(false);
  const [passkeyLoading, setPasskeyLoading] = useState(false);
  const [passkeyEnabled, setPasskeyEnabled] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const navigate = useNavigate();
  const location = useLocation();

  // returnUrl: state.from is set by ProtectedRoute; the query parameter is accepted for external
  // links/bookmarks. After login (and a forced password change) the user lands back on the QR page.
  const returnUrl = safeInternalPath(
    (location.state as { from?: string } | null)?.from
      ?? new URLSearchParams(location.search).get('returnUrl')
      ?? '/',
  );

  useEffect(() => {
    // [AUTH Phase 3] Gate the passkey button on the server flag — hidden entirely when off.
    void isPasskeyEnabled().then(setPasskeyEnabled);
  }, []);

  const onFinish = async (values: { username: string; password: string }) => {
    setLoading(true);
    setError(null);
    try {
      const { mustChangePassword } = await loginWithPassword(values.username, values.password);
      navigate(mustChangePassword ? '/change-password' : returnUrl, {
        replace: true,
        state: mustChangePassword ? { from: returnUrl } : undefined,
      });
    } catch (err: unknown) {
      const e = err as { response?: { data?: { message?: string } } };
      // Server sends a GENERIC message (never reveals whether the username exists).
      setError(e?.response?.data?.message || 'Đăng nhập thất bại. Vui lòng thử lại.');
    } finally {
      setLoading(false);
    }
  };

  // [REVERSE-PROXY] Warn on plain HTTP (refresh cookie has Secure flag → the
  // browser drops it and the session silently dies at access-token expiry).
  // Localhost loopback is excluded (dev + secure contexts treat it as trustworthy).
  const isInsecureHttp =
    typeof window !== 'undefined' &&
    window.location.protocol === 'http:' &&
    window.location.hostname !== 'localhost' &&
    window.location.hostname !== '127.0.0.1' &&
    window.location.hostname !== '[::1]';

  const onPasskeyLogin = async () => {
    setPasskeyLoading(true);
    setError(null);
    try {
      const result = await loginWithPasskey();
      if (result.ok) {
        navigate(result.mustChangePassword ? '/change-password' : returnUrl, {
          replace: true,
          state: result.mustChangePassword ? { from: returnUrl } : undefined,
        });
      } else {
        setError(result.message || 'Xác thực passkey không thành công.');
      }
    } finally {
      setPasskeyLoading(false);
    }
  };

  return (
    <div style={{ display: 'flex', justifyContent: 'center', alignItems: 'center', minHeight: '100vh', background: '#f0f2f5' }}>
      <Card style={{ width: 380, boxShadow: '0 4px 16px rgba(0,0,0,0.08)' }}>
        <div style={{ textAlign: 'center', marginBottom: 24 }}>
          <Title level={3} style={{ marginBottom: 4 }}>Mirats</Title>
          <Typography.Text type="secondary">Đăng nhập để tiếp tục</Typography.Text>
        </div>
        {error && <Alert type="error" message={error} showIcon style={{ marginBottom: 16 }} />}
        {isInsecureHttp && (
          <Alert
            type="warning"
            showIcon
            style={{ marginBottom: 16 }}
            message="Kết nối không bảo mật (HTTP)"
            description="Phiên đăng nhập sẽ không được giữ (cookie yêu cầu HTTPS). Hãy truy cập bằng địa chỉ HTTPS (domain qua Nginx Proxy Manager) thay vì địa chỉ IP."
          />
        )}
        <Form onFinish={onFinish} layout="vertical" requiredMark={false}>
          <Form.Item name="username" rules={[{ required: true, message: 'Vui lòng nhập tên đăng nhập' }]}>
            <Input prefix={<UserOutlined />} placeholder="Tên đăng nhập" size="large" autoFocus autoComplete="username" />
          </Form.Item>
          <Form.Item name="password" rules={[{ required: true, message: 'Vui lòng nhập mật khẩu' }]}>
            <Input.Password prefix={<LockOutlined />} placeholder="Mật khẩu" size="large" autoComplete="current-password" />
          </Form.Item>
          <Form.Item style={{ marginBottom: 0 }}>
            <Button type="primary" htmlType="submit" size="large" block loading={loading}>
              Đăng nhập
            </Button>
          </Form.Item>
        </Form>
        {passkeyEnabled && (
          <>
            <Divider plain style={{ fontSize: 12 }}>hoặc</Divider>
            <Button
              size="large"
              block
              icon={<SafetyOutlined />}
              loading={passkeyLoading}
              onClick={() => void onPasskeyLogin()}
            >
              Đăng nhập bằng Passkey
            </Button>
          </>
        )}
      </Card>
    </div>
  );
}
