import { useEffect, useState } from 'react';
import { Card, Form, Input, Button, message, Alert, Typography, Space } from 'antd';
import { SaveOutlined, ReloadOutlined } from '@ant-design/icons';
import apiClient from '../../../services/api-client';
import { usePermission } from '../../../hooks/usePermission';

const { Text } = Typography;

/**
 * Cấu hình hệ thống (QUẢN TRỊ) — Task ASSET-TAG-AUTO.
 * Hiện quản lý format tự sinh Mã tài sản (Asset Tag). Dùng chung bảng SystemSetting,
 * sẵn sàng mở rộng thêm setting khác trong cùng trang.
 */
export default function SystemConfigPage() {
  const [form] = Form.useForm();
  const [loading, setLoading] = useState(false);
  const [saving, setSaving] = useState(false);
  const canEdit = usePermission('system.config');

  const load = async () => {
    setLoading(true);
    try {
      const res = await apiClient.get('/system/config/asset-tag-format');
      form.setFieldsValue({ format: res.data?.data?.format ?? '' });
    } catch {
      message.error('Không thể tải cấu hình');
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    void load();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const save = async () => {
    try {
      const values = await form.validateFields();
      setSaving(true);
      await apiClient.put('/system/config/asset-tag-format', { format: String(values.format).trim() });
      message.success('Đã lưu cấu hình');
    } catch (err: any) {
      if (err?.errorFields) return;
      message.error(err?.response?.data?.message || 'Lỗi lưu cấu hình');
    } finally {
      setSaving(false);
    }
  };

  return (
    <Card
      title="Cấu hình hệ thống"
      loading={loading}
      extra={!canEdit ? <Text type="secondary">Chỉ đọc — cần quyền quản trị hệ thống</Text> : undefined}
    >
      <Alert
        type="info"
        showIcon
        style={{ marginBottom: 16 }}
        title="Format tự sinh Mã tài sản (Asset Tag)"
        description="Khi để trống ô Mã tài sản khi tạo tài sản, hệ thống tự sinh theo format dưới đây. Hỗ trợ token: {COMPANY} = mã công ty (NOCO nếu không có), {YYYY} = năm 4 số, {SEQ:n} = số thứ tự đệm n chữ số. Ví dụ: AST-{COMPANY}-{YYYY}-{SEQ:3} → AST-ABC-2026-001. Số thứ tự đếm riêng theo từng công ty và reset mỗi năm."
      />
      <Form form={form} layout="vertical" style={{ maxWidth: 560 }}>
        <Form.Item
          label="Format Mã tài sản"
          name="format"
          rules={[
            { required: true, message: 'Nhập format' },
            { pattern: /\{SEQ:\d\}/, message: 'Format phải chứa token {SEQ:n} (VD {SEQ:3}).' },
          ]}
          extra={
            <Space orientation="vertical" size={0}>
              <span>Token hợp lệ: <Text code>{'{COMPANY}'}</Text> (mã công ty), <Text code>{'{YYYY}'}</Text> (năm 4 số), <Text code>{'{SEQ:n}'}</Text> (số thứ tự đệm n chữ số).</span>
              <span>Ví dụ: <Text code>AST-{'{COMPANY}'}-{'{YYYY}'}-{'{SEQ:3}'}</Text> → <Text code>AST-ABC-2026-001</Text></span>
              <span>Nên giữ <Text code>{'{COMPANY}'}</Text> để mã unique toàn hệ thống.</span>
            </Space>
          }
        >
          <Input placeholder="AST-{COMPANY}-{YYYY}-{SEQ:3}" disabled={!canEdit} />
        </Form.Item>
        {canEdit && (
          <Space>
            <Button type="primary" icon={<SaveOutlined />} loading={saving} onClick={save}>Lưu cấu hình</Button>
            <Button icon={<ReloadOutlined />} onClick={() => void load()}>Tải lại</Button>
          </Space>
        )}
      </Form>
    </Card>
  );
}
