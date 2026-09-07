import { useCallback, useEffect, useState } from 'react';
import { Alert, Button, Card, Empty, Input, List, Popconfirm, App as AntApp, Typography } from 'antd';
import { SafetyOutlined, DeleteOutlined, PlusOutlined } from '@ant-design/icons';
import { isPasskeyEnabled, listPasskeys, registerPasskey, deletePasskey, type PasskeyItem } from '../services/passkeys';

const { Text } = Typography;

/**
 * [AUTH Phase 3] Account page — danh sách passkey của user đang đăng nhập: đăng ký passkey mới
 * (navigator.credentials.create), xóa passkey. Flag auth.passkeys.enabled = false → section
 * ẩn hoàn toàn (đúng checklist: UI ẩn khi cờ tắt).
 */
export default function AccountPage() {
  const { message } = AntApp.useApp();
  const [passkeys, setPasskeys] = useState<PasskeyItem[]>([]);
  const [enabled, setEnabled] = useState(false);
  const [loading, setLoading] = useState(true);
  const [registering, setRegistering] = useState(false);
  const [newName, setNewName] = useState('');

  const reload = useCallback(async () => {
    setLoading(true);
    try {
      const on = await isPasskeyEnabled();
      setEnabled(on);
      setPasskeys(on ? await listPasskeys() : []);
    } catch {
      message.error('Không thể tải danh sách passkey');
    } finally {
      setLoading(false);
    }
  }, [message]);

  useEffect(() => {
    void reload();
  }, [reload]);

  const onRegister = async () => {
    setRegistering(true);
    try {
      const result = await registerPasskey(newName.trim() || 'Passkey của tôi');
      if (result.ok) {
        message.success('Đã đăng ký passkey');
        setNewName('');
        await reload();
      } else {
        message.error(result.message || 'Đăng ký passkey thất bại.');
      }
    } finally {
      setRegistering(false);
    }
  };

  const onDelete = async (id: string) => {
    try {
      await deletePasskey(id);
      message.success('Đã xóa passkey');
      await reload();
    } catch {
      message.error('Không thể xóa passkey');
    }
  };

  return (
    <Card title="Tài khoản của tôi" loading={loading}>
      {enabled ? (
        <>
          <Alert
            type="info"
            showIcon
            style={{ marginBottom: 16 }}
            message="Passkey cho phép đăng nhập không cần mật khẩu (vân tay / Face ID / khóa bảo mật)."
          />
          <div style={{ display: 'flex', gap: 8, marginBottom: 16, maxWidth: 480 }}>
            <Input
              placeholder="Tên passkey (VD: Laptop công ty)"
              value={newName}
              onChange={e => setNewName(e.target.value)}
              maxLength={100}
              style={{ flex: 1 }}
            />
            <Button
              type="primary"
              icon={<PlusOutlined />}
              loading={registering}
              onClick={() => void onRegister()}
            >
              Đăng ký Passkey
            </Button>
          </div>
          {passkeys.length === 0 ? (
            <Empty description="Chưa có passkey nào được đăng ký" />
          ) : (
            <List
              dataSource={passkeys}
              style={{ maxWidth: 640 }}
              renderItem={item => (
                <List.Item
                  actions={[
                    <Popconfirm
                      key="del"
                      title="Xóa passkey này?"
                      description="Bạn sẽ không thể đăng nhập bằng passkey này nữa."
                      onConfirm={() => void onDelete(item.id)}
                    >
                      <Button size="small" danger icon={<DeleteOutlined />}>Xóa</Button>
                    </Popconfirm>
                  ]}
                >
                  <List.Item.Meta
                    avatar={<SafetyOutlined style={{ fontSize: 20, color: '#1677ff' }} />}
                    title={item.name}
                    description={
                      <Text type="secondary" style={{ fontSize: 12 }}>
                        Tạo lúc {new Date(item.createdAt).toLocaleString('vi-VN')}
                        {item.lastUsedAt ? ` · Dùng lần cuối ${new Date(item.lastUsedAt).toLocaleString('vi-VN')}` : ' · Chưa từng dùng'}
                      </Text>
                    }
                  />
                </List.Item>
              )}
            />
          )}
        </>
      ) : (
        <Alert
          type="warning"
          showIcon
          message="Tính năng Passkey hiện đang tắt"
          description="Quản trị viên chưa bật đăng nhập bằng Passkey trong Cấu hình hệ thống."
        />
      )}
    </Card>
  );
}
