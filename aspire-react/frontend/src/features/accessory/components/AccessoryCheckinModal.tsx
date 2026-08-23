import { useState } from 'react';
import {
  Modal, InputNumber, Input, Space, Tag, Typography, App, Descriptions,
} from 'antd';
import { RollbackOutlined, EnvironmentOutlined } from '@ant-design/icons';
import { accessoriesApi, checkoutTypeToLabel } from '../services/accessories.service';
import type { AccessoryCheckoutDto, CheckinRequest } from '../services/accessories.service';
import { uiColors } from '../../../theme/designTokens';

const { Text, Title } = Typography;

// ==================== Component Props ====================

interface AccessoryCheckinModalProps {
  open: boolean;
  checkout: AccessoryCheckoutDto | null;
  onClose: () => void;
  onSuccess: () => void;
}

// ==================== Component ====================

const AccessoryCheckinModal: React.FC<AccessoryCheckinModalProps> = ({
  open,
  checkout,
  onClose,
  onSuccess,
}) => {
  const { message } = App.useApp();

  const [returnQty, setReturnQty] = useState(1);
  const [note, setNote] = useState('');
  const [submitting, setSubmitting] = useState(false);

  const maxReturnable = checkout ? checkout.assignedQty - checkout.returnedQty : 0;

  const handleSubmit = async () => {
    if (!checkout) return;

    if (returnQty < 1) {
      void message.warning('Số lượng thu hồi phải > 0');
      return;
    }
    if (returnQty > maxReturnable) {
      void message.error(`Không thể thu hồi quá ${maxReturnable.toLocaleString('vi-VN')}`);
      return;
    }

    setSubmitting(true);
    try {
      const payload: CheckinRequest = {
        returnQty,
        note: note.trim() || undefined,
      };
      await accessoriesApi.checkin(checkout.id, payload);
      void message.success(`Đã thu hồi ${returnQty.toLocaleString('vi-VN')} phụ kiện`);
      onSuccess();
    } catch (err: unknown) {
      const e = err as { response?: { data?: { message?: string } } };
      void message.error(e?.response?.data?.message ?? 'Lỗi thu hồi');
    } finally {
      setSubmitting(false);
    }
  };

  const handleClose = () => {
    setReturnQty(1);
    setNote('');
    onClose();
  };

  return (
    <Modal
      title={
        <Space>
          <RollbackOutlined style={{ color: uiColors.warningAmber }} />
          <span>Thu hồi phụ kiện</span>
        </Space>
      }
      open={open}
      onOk={handleSubmit}
      onCancel={handleClose}
      okText="Xác nhận thu hồi"
      cancelText="Hủy"
      confirmLoading={submitting}
      okButtonProps={{ disabled: returnQty < 1 || !checkout }}
      destroyOnHidden
      width={520}
    >
      {checkout && (
        <div style={{ marginBottom: 20 }}>
          <Title level={5} style={{ margin: 0, marginBottom: 8 }}>
            Thông tin cấp phát
          </Title>
          <Descriptions bordered size="small" column={1}>
            <Descriptions.Item label="Loại đối tượng">
              <Tag color="purple">
                {checkoutTypeToLabel(checkout.checkoutType)}
              </Tag>
            </Descriptions.Item>
            <Descriptions.Item label="Đối tượng nhận">
              <Space>
                <EnvironmentOutlined style={{ color: uiColors.labelGray }} />
                <Text strong>{checkout.targetName ?? checkout.targetId}</Text>
              </Space>
            </Descriptions.Item>
            <Descriptions.Item label="Đã cấp">
              <Text strong>{checkout.assignedQty.toLocaleString('vi-VN')}</Text>
            </Descriptions.Item>
            <Descriptions.Item label="Đã thu hồi">
              <Text>{checkout.returnedQty.toLocaleString('vi-VN')}</Text>
            </Descriptions.Item>
            <Descriptions.Item label="Còn lại có thể thu">
              <Text type="warning" strong style={{ fontSize: 16 }}>
                {maxReturnable.toLocaleString('vi-VN')}
              </Text>
            </Descriptions.Item>
          </Descriptions>
        </div>
      )}

      {/* Return Quantity */}
      <div style={{ marginBottom: 16 }}>
        <Text strong style={{ display: 'block', marginBottom: 8 }}>
          Số lượng thu hồi:
        </Text>
        <InputNumber
          min={1}
          max={maxReturnable}
          value={returnQty}
          onChange={(v) => setReturnQty(v ?? 1)}
          style={{ width: '100%' }}
          size="large"
        />
        <Text type="secondary" style={{ display: 'block', marginTop: 4, fontSize: 12 }}>
          Tối đa: {maxReturnable.toLocaleString('vi-VN')}
        </Text>
      </div>

      {/* Note */}
      <div style={{ marginBottom: 8 }}>
        <Text strong style={{ display: 'block', marginBottom: 8 }}>
          Ghi chú thu hồi:
        </Text>
        <Input.TextArea
          rows={3}
          maxLength={500}
          showCount
          value={note}
          onChange={(e) => setNote(e.target.value)}
          placeholder="Ví dụ: Hỏng, mất, hoặc trả lại nguyên trạng..."
        />
      </div>
    </Modal>
  );
};

export default AccessoryCheckinModal;