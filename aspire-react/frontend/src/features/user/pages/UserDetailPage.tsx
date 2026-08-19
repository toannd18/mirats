import { useEffect, useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { ArrowLeftOutlined } from '@ant-design/icons';
import { Button, Card, Descriptions, Empty, Space, Spin, Tag, Typography } from 'antd';
import apiClient from '../../../services/api-client';
import LicenseUsageTable from '../../../shared/components/LicenseUsageTable';

const { Title } = Typography;

interface UserDetail {
  id: string;
  username: string;
  email: string;
  firstName: string;
  lastName: string;
  employeeNumber: string | null;
  jobTitle: string | null;
  isSuperUser: boolean;
  isActive: boolean;
  companyName: string | null;
  departmentName: string | null;
  locationName: string | null;
}

export default function UserDetailPage() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const [user, setUser] = useState<UserDetail | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    if (!id) return;
    setLoading(true);
    apiClient.get(`/users/${id}`)
      .then(r => setUser(r.data.data as UserDetail))
      .catch(() => setUser(null))
      .finally(() => setLoading(false));
  }, [id]);

  if (loading) return <Spin style={{ display: 'block', margin: '80px auto' }} />;
  if (!user) return <Empty description="Không tìm thấy người dùng" style={{ marginTop: 80 }} />;

  const fullName = [user.firstName, user.lastName].filter(Boolean).join(' ') || user.username;

  return (
    <div>
      <div style={{ marginBottom: 16 }}>
        <Space style={{ marginBottom: 12 }}>
          <Button icon={<ArrowLeftOutlined />} onClick={() => navigate('/users')}>Quay lại</Button>
        </Space>
        <Title level={4} style={{ margin: 0 }}>{fullName}</Title>
      </div>
      <Card title="Thông tin người dùng" style={{ marginBottom: 16 }}>
        <Descriptions bordered size="small" column={3}>
          <Descriptions.Item label="Tài khoản">{user.username}</Descriptions.Item>
          <Descriptions.Item label="Email">{user.email || '-'}</Descriptions.Item>
          <Descriptions.Item label="Mã nhân viên">{user.employeeNumber || '-'}</Descriptions.Item>
          <Descriptions.Item label="Chức danh">{user.jobTitle || '-'}</Descriptions.Item>
          <Descriptions.Item label="Công ty">{user.companyName || '-'}</Descriptions.Item>
          <Descriptions.Item label="Phòng ban">{user.departmentName || '-'}</Descriptions.Item>
          <Descriptions.Item label="Vị trí">{user.locationName || '-'}</Descriptions.Item>
          <Descriptions.Item label="Vai trò">
            <Tag color={user.isSuperUser ? 'purple' : 'blue'}>{user.isSuperUser ? 'Quản trị' : 'Người dùng'}</Tag>
          </Descriptions.Item>
          <Descriptions.Item label="Trạng thái">
            {user.isActive ? <Tag color="green">Hoạt động</Tag> : <Tag color="red">Đã khóa</Tag>}
          </Descriptions.Item>
        </Descriptions>
      </Card>
      <Card title="License đang sử dụng" style={{ marginBottom: 16 }}>
        <LicenseUsageTable scope={{ type: 'user', id: user.id }} />
      </Card>
    </div>
  );
}