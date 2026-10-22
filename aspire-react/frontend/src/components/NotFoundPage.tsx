import { Button, Result } from 'antd';
import { useLocation, useNavigate } from 'react-router-dom';

/**
 * [UI-3] Real 404 page. Unknown routes render this instead of silently redirecting
 * to the dashboard, so a stale bookmark/typo is visible and recoverable.
 */
export default function NotFoundPage() {
  const location = useLocation();
  const navigate = useNavigate();

  return (
    <Result
      status="404"
      title="Không tìm thấy trang"
      subTitle={`Đường dẫn "${location.pathname}" không tồn tại hoặc đã được di chuyển.`}
      extra={[
        <Button key="back" onClick={() => navigate(-1)}>
          Quay lại
        </Button>,
        <Button key="home" type="primary" onClick={() => navigate('/dashboard')}>
          Về tổng quan
        </Button>,
      ]}
    />
  );
}
