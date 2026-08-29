import { useState, useCallback, useEffect } from 'react';
import {
  Modal, Segmented, Select, Input, Typography,
  App, Descriptions, Alert,
} from 'antd';
import { UserOutlined, TeamOutlined, ClusterOutlined } from '@ant-design/icons';
import type { SegmentedValue } from 'antd/es/segmented';
import apiClient from '../../../services/api-client';
import { assetService } from '../services/asset.service';
import type { AssetDto } from '../types/asset';

const { Text } = Typography;

type AllocationTargetType = 'User' | 'Department' | 'SystemPosition';

interface AllocationModalProps {
  open: boolean;
  asset: AssetDto | null;
  onClose: () => void;
  onSuccess: () => void;
}

const LABELS: Record<AllocationTargetType, string> = {
  User: 'Người dùng',
  Department: 'Phòng ban',
  SystemPosition: 'Hệ thống (Vị trí lắp đặt)',
};

const Icons: Record<AllocationTargetType, React.ReactNode> = {
  User: <UserOutlined />,
  Department: <TeamOutlined />,
  SystemPosition: <ClusterOutlined />,
};

const TARGET_TYPE_VALUE: Record<AllocationTargetType, number> = {
  User: 1,
  Department: 2,
  SystemPosition: 3,
};

const AssetAllocationModal: React.FC<AllocationModalProps> = ({ open, asset, onClose, onSuccess }) => {
  const { message } = App.useApp();
  const [targetType, setTargetType] = useState<AllocationTargetType>('User');
  const [targetId, setTargetId] = useState<string | undefined>(undefined);
  const [locationId, setLocationId] = useState<string | undefined>(undefined);
  const [note, setNote] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const [targetOptions, setTargetOptions] = useState<{ label: string; value: string }[]>([]);
  const [locationOptions, setLocationOptions] = useState<{ label: string; value: string }[]>([]);
  const [loadingTargets, setLoadingTargets] = useState(false);

  const fetchTargets = useCallback(async (type: AllocationTargetType) => {
    setLoadingTargets(true);
    setTargetId(undefined);
    setTargetOptions([]);
    try {
      const params: Record<string, unknown> = { pageSize: 500 };
      if (asset?.company?.id) params.companyId = asset.company.id;
      let options: { label: string; value: string }[] = [];
      if (type === 'User') {
        const res = await apiClient.get('/users', { params });
        const users = res.data.data as { id: string; firstName: string; lastName: string; username: string }[];
        options = users.map(u => ({ label: [u.firstName, u.lastName].filter(Boolean).join(' ') || u.username, value: u.id }));
      } else if (type === 'Department') {
        const res = await apiClient.get('/departments', { params });
        const depts = res.data.data as { id: string; name: string }[];
        options = depts.map(d => ({ label: d.name, value: d.id }));
      } else {
        const res = await apiClient.get('/system-infos', { params });
        const systems = res.data.data as { name: string; positions?: { id: string; name: string; systemInfoName?: string }[] }[];
        const positions: { label: string; value: string }[] = [];
        for (const sys of systems) for (const pos of sys.positions ?? []) positions.push({ label: `${pos.name} — ${pos.systemInfoName ?? sys.name}`, value: pos.id });
        options = positions;
      }
      setTargetOptions(options);
    } catch { void message.error('Không thể tải danh sách đối tượng'); }
    finally { setLoadingTargets(false); }
  }, [asset, message]);

  const fetchLocations = useCallback(async () => {
    try {
      const res = await apiClient.get('/locations', { params: { pageSize: 500 } });
      setLocationOptions((res.data.data ?? []).map((l: { id: string; name: string }) => ({ label: l.name, value: l.id })));
    } catch { setLocationOptions([]); }
  }, []);

  useEffect(() => { if (open) { setTargetType('User'); setNote(''); setLocationId(undefined); fetchTargets('User'); fetchLocations(); } }, [open, fetchTargets, fetchLocations]);

  const handleTypeChange = (val: SegmentedValue) => {
    const type = String(val) as AllocationTargetType;
    setTargetType(type); setLocationId(undefined);
    void fetchTargets(type);
  };

  const handleSubmit = async () => {
    if (!asset || !targetId) { void message.warning('Vui lòng chọn đối tượng cấp phát'); return; }
    if (targetType === 'SystemPosition' && !locationId) { void message.warning('Vui lòng chọn vị trí (bắt buộc cho Hệ thống)'); return; }
    setSubmitting(true);
    try {
      await assetService.allocate(asset.id, {
        targetType: TARGET_TYPE_VALUE[targetType],
        targetId,
        locationId: targetType === 'SystemPosition' ? locationId : undefined,
        note: note.trim() || undefined,
      });
      void message.success('Đã cấp phát tài sản (Pending → Deployed)');
      onSuccess();
    } catch (err: unknown) {
      const e = err as { response?: { data?: { message?: string } } };
      void message.error(e?.response?.data?.message ?? 'Lỗi cấp phát');
    } finally { setSubmitting(false); }
  };

  return (
    <Modal title="Cấp phát tài sản" open={open} onCancel={onClose} okText="Cấp phát" cancelText="Hủy"
      okButtonProps={{ disabled: !targetId || (targetType === 'SystemPosition' && !locationId) }}
      onOk={handleSubmit} confirmLoading={submitting} destroyOnHidden width={560}>
      {asset && (
        <Descriptions bordered size="small" column={1} style={{ marginBottom: 16 }}>
          <Descriptions.Item label="Mã tài sản"><Text strong>{asset.assetTag}</Text></Descriptions.Item>
          <Descriptions.Item label="Tên tài sản"><Text>{asset.name}</Text></Descriptions.Item>
        </Descriptions>
      )}
      {asset?.company?.name && (
        <Alert type="info" showIcon style={{ marginBottom: 16, borderRadius: 8 }}
          title={`Tài sản thuộc công ty ${asset.company.name}. Chỉ cấp phát cho đối tượng cùng công ty.`} />
      )}
      <div style={{ marginBottom: 16 }}>
        <Text strong style={{ display: 'block', marginBottom: 8 }}>Đối tượng cấp phát:</Text>
        <Segmented block size="large" value={targetType} onChange={handleTypeChange}
          options={['User', 'Department', 'SystemPosition'].map(t => ({ label: LABELS[t as AllocationTargetType], value: t, icon: Icons[t as AllocationTargetType] }))} />
      </div>
      <div style={{ marginBottom: 16 }}>
        <Text strong style={{ display: 'block', marginBottom: 8 }}>{LABELS[targetType]} *</Text>
        <Select showSearch size="large" style={{ width: '100%' }} placeholder={`Chọn ${LABELS[targetType].toLowerCase()}`}
          loading={loadingTargets} options={targetOptions} value={targetId} onChange={setTargetId}
          filterOption={(input, option) => (option?.label as string)?.toLowerCase().includes(input.toLowerCase())} />
      </div>
      {targetType === 'SystemPosition' && (
        <div style={{ marginBottom: 16 }}>
          <Text strong style={{ display: 'block', marginBottom: 8 }}>Vị trí lắp đặt *</Text>
          <Select showSearch size="large" style={{ width: '100%' }} placeholder="Chọn vị trí (bắt buộc)"
            options={locationOptions} value={locationId} onChange={setLocationId}
            filterOption={(input, option) => (option?.label as string)?.toLowerCase().includes(input.toLowerCase())} />
        </div>
      )}
      <div style={{ marginBottom: 8 }}>
        <Text strong style={{ display: 'block', marginBottom: 8 }}>Ghi chú:</Text>
        <Input.TextArea rows={3} maxLength={500} showCount value={note} onChange={e => setNote(e.target.value)} placeholder="Ghi chú cấp phát (không bắt buộc)" />
      </div>
    </Modal>
  );
};

export default AssetAllocationModal;