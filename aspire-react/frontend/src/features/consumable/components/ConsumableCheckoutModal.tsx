import { useEffect, useState } from 'react';
import {
  Modal, Select, InputNumber, Input, Tag, Typography, App,
} from 'antd';
import apiClient from '../../../services/api-client';

const { Text, Title } = Typography;

// ==================== Types ====================

export interface CheckoutConsumable {
  id: string;
  name: string;
  companyId: string | null;
  companyName: string | null;
  remaining: number;
}

interface ConsumableCheckoutModalProps {
  open: boolean;
  consumable: CheckoutConsumable | null;
  onClose: () => void;
  onSuccess: () => void;
}

// ==================== Component ====================

/**
 * Modal "Cấp phát vật tư" dùng chung cho ConsumableListPage (nút trên card + deep-link
 * ?checkout=<id>) và ConsumableDetailPage (mở tại chỗ). Pattern đồng bộ AccessoryCheckoutModal:
 * tự tải user cùng công ty với vật tư, validate số lượng, POST /consumables/{id}/checkout.
 */
const ConsumableCheckoutModal: React.FC<ConsumableCheckoutModalProps> = ({
  open,
  consumable,
  onClose,
  onSuccess,
}) => {
  const { message } = App.useApp();

  const [qty, setQty] = useState(1);
  const [userId, setUserId] = useState<string | undefined>(undefined);
  const [note, setNote] = useState('');
  const [userOptions, setUserOptions] = useState<{ label: string; value: string }[]>([]);
  const [loadingUsers, setLoadingUsers] = useState(false);

  // ──── Reset form + load company-scoped users every time the modal opens ────

  useEffect(() => {
    if (!open || !consumable) return;
    setQty(1);
    setUserId(undefined);
    setNote('');
    setUserOptions([]);
    void loadUsers(consumable);
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open, consumable]);

  const loadUsers = async (target: CheckoutConsumable) => {
    setLoadingUsers(true);
    try {
      const params: Record<string, unknown> = { pageSize: 500 };
      if (target.companyId) params.companyId = target.companyId;
      const res = await apiClient.get('/users', { params });
      const users: { id: string; firstName: string; lastName: string; username: string; companyId: string | null }[] = res.data.data ?? [];
      const filteredUsers = target.companyId
        ? users.filter((u) => u.companyId === target.companyId)
        : users;
      const options = filteredUsers.map((u) => ({
        label: `${[u.firstName, u.lastName].filter(Boolean).join(' ') || u.username}${u.companyId ? '' : ' (Chưa gán công ty)'}`,
        value: u.id,
      }));
      setUserOptions(options);

      if (target.companyId && options.length === 0) {
        void message.warning('Không có người dùng nào thuộc công ty này');
      }
    } catch {
      void message.error('Không thể tải danh sách người dùng');
    } finally {
      setLoadingUsers(false);
    }
  };

  const handleOk = async () => {
    if (!consumable) return;
    if (!userId) {
      void message.warning('Vui lòng chọn người nhận');
      return;
    }
    if (qty > consumable.remaining) {
      void message.error(`Không thể cấp phát quá số lượng còn lại (${consumable.remaining.toLocaleString('vi-VN')})`);
      return;
    }
    if (qty < 1) {
      void message.warning('Số lượng cấp phát phải lớn hơn 0');
      return;
    }
    try {
      await apiClient.post(`/consumables/${consumable.id}/checkout`, {
        quantity: qty,
        userId,
        note: note.trim() || null,
      });
      void message.success('Đã cấp phát vật tư');
      onSuccess();
    } catch (err: unknown) {
      const e = err as { response?: { data?: { message?: string } } };
      void message.error(e?.response?.data?.message ?? 'Lỗi cấp phát');
    }
  };

  return (
    <Modal
      title="Cấp phát vật tư"
      open={open}
      onOk={() => void handleOk()}
      onCancel={onClose}
      okText="Cấp phát"
      cancelText="Hủy"
      okButtonProps={{ disabled: !userId }}
      destroyOnHidden
    >
      <div style={{ marginBottom: 12 }}>
        <Title level={5} style={{ margin: 0 }}>
          {consumable?.name}
        </Title>
        {consumable && (
          <div style={{ marginTop: 4 }}>
            {consumable.companyName && (
              <Tag color="geekblue" style={{ marginRight: 8 }}>
                {consumable.companyName}
              </Tag>
            )}
            <Text type="secondary">
              Còn lại: {consumable.remaining.toLocaleString('vi-VN')}
            </Text>
          </div>
        )}
      </div>

      <div style={{ marginBottom: 16 }}>
        <Text strong style={{ display: 'block', marginBottom: 8 }}>
          Người nhận:
        </Text>
        <Select
          showSearch
          size="large"
          style={{ width: '100%' }}
          placeholder={consumable?.companyId ? 'Chọn người nhận (cùng công ty)' : 'Chọn người nhận'}
          loading={loadingUsers}
          options={userOptions}
          value={userId}
          onChange={(v) => setUserId(v)}
          filterOption={(input, option) =>
            (option?.label as string)?.toLowerCase().includes(input.toLowerCase())
          }
          notFoundContent={
            loadingUsers ? 'Đang tải...' : (
              consumable?.companyId
                ? 'Không có người dùng nào trong công ty này'
                : 'Không có dữ liệu'
            )
          }
        />
        {consumable?.companyId && (
          <Text type="secondary" style={{ display: 'block', marginTop: 4, fontSize: 12 }}>
            Chỉ hiển thị người dùng cùng công ty với vật tư
          </Text>
        )}
      </div>

      <div style={{ marginBottom: 16 }}>
        <Text strong style={{ display: 'block', marginBottom: 8 }}>
          Số lượng cấp phát:
        </Text>
        <InputNumber
          min={1}
          max={consumable?.remaining ?? 1}
          value={qty}
          onChange={(v) => setQty(v ?? 1)}
          style={{ width: '100%' }}
          size="large"
        />
      </div>

      <div style={{ marginBottom: 30 }}>
        <Text strong style={{ display: 'block', marginBottom: 8 }}>
          Ghi chú:
        </Text>
        <Input.TextArea
          rows={3}
          maxLength={500}
          showCount
          value={note}
          onChange={(e) => setNote(e.target.value)}
          placeholder="Nhập ghi chú cho lần cấp phát này (không bắt buộc)"
        />
      </div>
    </Modal>
  );
};

export default ConsumableCheckoutModal;

