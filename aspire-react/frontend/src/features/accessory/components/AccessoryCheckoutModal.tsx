import { useState, useCallback, useEffect } from 'react';
import {
  Modal, Segmented, Select, InputNumber, Input, Tag, Typography,
  App, Space, Alert,
} from 'antd';
import {
  UserOutlined, TeamOutlined, EnvironmentOutlined,
  ClusterOutlined,
} from '@ant-design/icons';
import type { SegmentedValue } from 'antd/es/segmented';
import apiClient from '../../../services/api-client';
import type { AccessoryDto } from '../services/accessories.service';
import { accessoriesApi } from '../services/accessories.service';

const { Text, Title } = Typography;

// ==================== Types ====================

type CheckoutType = 1 | 2 | 3 | 4; // User | Department | Location | SystemPosition

interface TargetOption {
  label: string;
  value: string;
}

const CHECKOUT_TYPE_LABELS: Record<CheckoutType, string> = {
  1: 'Người dùng',
  2: 'Phòng ban',
  3: 'Vị trí',
  4: 'Hệ thống',
};

const CHECKOUT_TYPE_ICONS: Record<CheckoutType, React.ReactNode> = {
  1: <UserOutlined />,
  2: <TeamOutlined />,
  3: <EnvironmentOutlined />,
  4: <ClusterOutlined />,
};

// ==================== Component Props ====================

interface AccessoryCheckoutModalProps {
  open: boolean;
  accessory: AccessoryDto | null;
  onClose: () => void;
  onSuccess: () => void;
}

// ==================== Component ====================

const AccessoryCheckoutModal: React.FC<AccessoryCheckoutModalProps> = ({
  open,
  accessory,
  onClose,
  onSuccess,
}) => {
  const { message } = App.useApp();

  const [checkoutType, setCheckoutType] = useState<CheckoutType>(1);
  const [targetId, setTargetId] = useState<string | undefined>(undefined);
  const [quantity, setQuantity] = useState(1);
  const [note, setNote] = useState('');
  const [submitting, setSubmitting] = useState(false);

  const [targetOptions, setTargetOptions] = useState<TargetOption[]>([]);
  const [loadingTargets, setLoadingTargets] = useState(false);

  const isGlobalAccessory = !accessory?.companyId;

  // ──── Fetch target options based on checkoutType ────

  const fetchTargets = useCallback(async (type: CheckoutType) => {
    setLoadingTargets(true);
    setTargetId(undefined);
    setTargetOptions([]);

    try {
      let options: TargetOption[] = [];

      // Common params: limit results + filter by company if accessory is scoped
      const params: Record<string, unknown> = { pageSize: 500 };
      if (accessory?.companyId) {
        params.companyId = accessory.companyId;
      }

      switch (type) {
        case 1: { // User
          const res = await apiClient.get('/users', { params });
          const users = res.data.data as { id: string; firstName: string; lastName: string; username: string }[];
          // Server-side filter may not exist on older code; client-side guard
          const filtered = accessory?.companyId
            ? users.filter((u) => (u as unknown as { companyId?: string }).companyId === accessory.companyId)
            : users;
          options = filtered.map((u) => ({
            label: [u.firstName, u.lastName].filter(Boolean).join(' ') || u.username,
            value: u.id,
          }));
          break;
        }
        case 2: { // Department — already supports ?companyId=
          const res = await apiClient.get('/departments', { params });
          const depts = res.data.data as { id: string; name: string }[];
          options = depts.map((d) => ({ label: d.name, value: d.id }));
          break;
        }
        case 3: { // Location — no CompanyId, show all
          const res = await apiClient.get('/locations', { params: { pageSize: 500 } });
          const locs = res.data.data as { id: string; name: string }[];
          options = locs.map((l) => ({ label: l.name, value: l.id }));
          break;
        }
        case 4: { // SystemPosition — fetch from system-infos, filter by company
          const sysParams: Record<string, unknown> = {};
          if (accessory?.companyId) {
            sysParams.companyId = accessory.companyId;
          }
          const res = await apiClient.get('/system-infos', { params: sysParams });
          const systems = res.data.data as {
            positions?: { id: string; name: string }[];
            companyId?: string | null;
            name: string;
          }[];

          const positions: { id: string; name: string; systemName: string }[] = [];
          for (const sys of systems) {
            // Client-side company filter for system-info
            if (accessory?.companyId && sys.companyId !== accessory.companyId) continue;
            if (sys.positions) {
              for (const pos of sys.positions) {
                positions.push({ ...pos, systemName: sys.name ?? '' });
              }
            }
          }
          options = positions.map((sp) => ({
            label: `${sp.name} (${sp.systemName})`,
            value: sp.id,
          }));
          break;
        }
      }

      setTargetOptions(options);
    } catch {
      void message.error('Không thể tải danh sách đối tượng');
    } finally {
      setLoadingTargets(false);
    }
  }, [message, accessory]);

  // ──── Reset & load when modal opens or type changes ────

  useEffect(() => {
    if (open) {
      setCheckoutType(1);
      setQuantity(1);
      setNote('');
      void fetchTargets(1);
    }
  }, [open, fetchTargets]);

  const handleTypeChange = (val: SegmentedValue) => {
    const type = Number(val) as CheckoutType;
    setCheckoutType(type);
    void fetchTargets(type);
  };

  // ──── Submit checkout ────

  const handleSubmit = async () => {
    if (!accessory) return;
    if (!targetId) {
      void message.warning('Vui lòng chọn đối tượng nhận');
      return;
    }
    if (quantity < 1) {
      void message.warning('Số lượng phải > 0');
      return;
    }
    if (quantity > accessory.remaining) {
      void message.error(`Không thể cấp phát quá số lượng còn lại (${accessory.remaining.toLocaleString('vi-VN')})`);
      return;
    }

    setSubmitting(true);
    try {
      await accessoriesApi.checkout(accessory.id, {
        checkoutType,
        targetId,
        quantity,
        note: note.trim() || null,
      });
      void message.success('Đã cấp phát phụ kiện');
      onSuccess();
    } catch (err: unknown) {
      const e = err as { response?: { data?: { message?: string } } };
      void message.error(e?.response?.data?.message ?? 'Lỗi cấp phát');
    } finally {
      setSubmitting(false);
    }
  };

  // ──── Render ────

  return (
    <Modal
      title="Cấp phát phụ kiện"
      open={open}
      onOk={handleSubmit}
      onCancel={onClose}
      okText="Cấp phát"
      cancelText="Hủy"
      confirmLoading={submitting}
      okButtonProps={{ disabled: !targetId || quantity < 1 }}
      destroyOnHidden
      width={520}
    >
      {/* Accessory info */}
      <div style={{ marginBottom: 16 }}>
        <Title level={5} style={{ margin: 0 }}>
          {accessory?.name}
        </Title>
        {accessory && (
          <div style={{ marginTop: 4 }}>
            {accessory.companyName && (
              <Tag color="geekblue" style={{ marginRight: 8 }}>
                {accessory.companyName}
              </Tag>
            )}
            <Space size={16}>
              <Text type="secondary">
                Còn lại: <Text strong>{accessory.remaining.toLocaleString('vi-VN')}</Text>
              </Text>
              {accessory.checkedOutQty > 0 && (
                <Text type="secondary">
                  Đã cấp: <Text strong>{accessory.checkedOutQty.toLocaleString('vi-VN')}</Text>
                </Text>
              )}
            </Space>
          </div>
        )}
      </div>

      {/* Company isolation hint */}
      {accessory?.companyId && accessory.companyName && (
        <Alert
          type="info"
          showIcon
          style={{ marginBottom: 16, borderRadius: 8 }}
          title={
            <Text style={{ fontSize: 13 }}>
              Phụ kiện thuộc công ty <Text strong>{accessory.companyName}</Text>.
              Chỉ có thể cấp phát cho đối tượng thuộc cùng công ty này.
            </Text>
          }
        />
      )}

      {/* Checkout Type — Segmented */}
      <div style={{ marginBottom: 16 }}>
        <Text strong style={{ display: 'block', marginBottom: 8 }}>
          Loại đối tượng nhận:
        </Text>
        <Segmented<CheckoutType>
          block
          size="large"
          value={checkoutType}
          onChange={handleTypeChange}
          options={[
            { label: 'Người dùng', value: 1 as CheckoutType, icon: <UserOutlined /> },
            { label: 'Phòng ban', value: 2 as CheckoutType, icon: <TeamOutlined /> },
            { label: 'Vị trí', value: 3 as CheckoutType, icon: <EnvironmentOutlined /> },
            { label: 'Hệ thống', value: 4 as CheckoutType, icon: <ClusterOutlined /> },
          ]}
        />
      </div>

      {/* Target Select (dynamic based on checkoutType) */}
      <div style={{ marginBottom: 16 }}>
        <Text strong style={{ display: 'block', marginBottom: 8 }}>
          {CHECKOUT_TYPE_LABELS[checkoutType]}:
        </Text>
        <Select
          showSearch
          size="large"
          style={{ width: '100%' }}
          placeholder={
            isGlobalAccessory
              ? `Chọn ${CHECKOUT_TYPE_LABELS[checkoutType].toLowerCase()}`
              : `Chọn ${CHECKOUT_TYPE_LABELS[checkoutType].toLowerCase()} thuộc ${accessory?.companyName}`
          }
          loading={loadingTargets}
          options={targetOptions}
          value={targetId}
          onChange={(v) => setTargetId(v)}
          filterOption={(input, option) =>
            (option?.label as string)?.toLowerCase().includes(input.toLowerCase())
          }
          notFoundContent={
            loadingTargets
              ? 'Đang tải...'
              : isGlobalAccessory
                ? `Không có ${CHECKOUT_TYPE_LABELS[checkoutType].toLowerCase()} nào`
                : `Không có ${CHECKOUT_TYPE_LABELS[checkoutType].toLowerCase()} nào thuộc ${accessory?.companyName}`
          }
          optionRender={(option) => (
            <Space>
              {CHECKOUT_TYPE_ICONS[checkoutType]}
              <span>{option.label}</span>
            </Space>
          )}
        />
        {!isGlobalAccessory && (
          <Text type="secondary" style={{ display: 'block', marginTop: 4, fontSize: 12 }}>
            Chỉ hiển thị các đối tượng thuộc <Text strong>{accessory?.companyName}</Text>
          </Text>
        )}
      </div>

      {/* Quantity */}
      <div style={{ marginBottom: 16 }}>
        <Text strong style={{ display: 'block', marginBottom: 8 }}>
          Số lượng cấp phát:
        </Text>
        <InputNumber
          min={1}
          max={accessory?.remaining ?? 1}
          value={quantity}
          onChange={(v) => setQuantity(v ?? 1)}
          style={{ width: '100%' }}
          size="large"
        />
        <Text type="secondary" style={{ display: 'block', marginTop: 4, fontSize: 12 }}>
          Tối đa: {accessory?.remaining.toLocaleString('vi-VN') ?? 0}
        </Text>
      </div>

      {/* Note */}
      <div style={{ marginBottom: 24 }}>
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

export default AccessoryCheckoutModal;