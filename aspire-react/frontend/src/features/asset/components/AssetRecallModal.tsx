import { useState, useEffect } from 'react';
import { Modal, Select, Input, Typography, App, Descriptions, Space } from 'antd';
import { RollbackOutlined } from '@ant-design/icons';
import apiClient from '../../../services/api-client';
import { assetService } from '../services/asset.service';
import type { AssetDto } from '../types/asset';

const { Text } = Typography;

interface AssetRecallModalProps {
  open: boolean;
  asset: AssetDto | null;
  onClose: () => void;
  onSuccess: () => void;
}

const AssetRecallModal: React.FC<AssetRecallModalProps> = ({ open, asset, onClose, onSuccess }) => {
  const { message } = App.useApp();
  const [locationId, setLocationId] = useState<string | undefined>(undefined);
  const [note, setNote] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const [locationOptions, setLocationOptions] = useState<{ label: string; value: string }[]>([]);

  useEffect(() => {
    if (open) {
      setLocationId(undefined); setNote('');
      apiClient.get('/locations', { params: { pageSize: 500 } })
        .then(res => setLocationOptions((res.data.data ?? []).map((l: { id: string; name: string }) => ({ label: l.name, value: l.id }))))
        .catch(() => setLocationOptions([]));
    }
  }, [open]);

  const handleSubmit = async () => {
    if (!asset || !locationId) { void message.warning('Vui lòng chọn vị trí thu hồi (bắt buộc)'); return; }
    setSubmitting(true);
    try {
      await assetService.recall(asset.id, { locationId, note: note.trim() || undefined });
      void message.success('Đã thu hồi tài sản — tài sản trở về trạng thái Chờ cấp phát');
      onSuccess();
    } catch (err: unknown) {
      const e = err as { response?: { data?: { message?: string } } };
      void message.error(e?.response?.data?.message ?? 'Lỗi thu hồi');
    } finally { setSubmitting(false); }
  };

  return (
    <Modal title={<Space><RollbackOutlined style={{ color: '#fa8c16' }} /><span>Thu hồi tài sản</span></Space>}
      open={open} onCancel={onClose} okText="Xác nhận thu hồi" cancelText="Hủy"
      okButtonProps={{ disabled: !locationId }} onOk={handleSubmit} confirmLoading={submitting} destroyOnClose width={560}>
      {asset && (
        <Descriptions bordered size="small" column={1} style={{ marginBottom: 16 }}>
          <Descriptions.Item label="Mã tài sản"><Text strong>{asset.assetTag}</Text></Descriptions.Item>
          <Descriptions.Item label="Tên tài sản"><Text>{asset.name}</Text></Descriptions.Item>
          {asset.assignedTo?.name && (
            <Descriptions.Item label="Đang cấp cho"><Text>{asset.assignedTo.name}</Text></Descriptions.Item>
          )}
        </Descriptions>
      )}
      <div style={{ marginBottom: 16 }}>
        <Text strong style={{ display: 'block', marginBottom: 8 }}>Vị trí thu hồi *</Text>
        <Select showSearch size="large" style={{ width: '100%' }} placeholder="Chọn vị trí thu hồi (bắt buộc)"
          options={locationOptions} value={locationId} onChange={setLocationId}
          filterOption={(input, option) => (option?.label as string)?.toLowerCase().includes(input.toLowerCase())} />
      </div>
      <div style={{ marginBottom: 8 }}>
        <Text strong style={{ display: 'block', marginBottom: 8 }}>Ghi chú:</Text>
        <Input.TextArea rows={3} maxLength={500} showCount value={note} onChange={e => setNote(e.target.value)} placeholder="Ghi chú thu hồi (không bắt buộc)" />
      </div>
    </Modal>
  );
};

export default AssetRecallModal;