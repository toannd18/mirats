import { useState, useEffect } from 'react';
import { Modal, Select, Input, Typography, App, Descriptions, Alert, Space } from 'antd';
import { InboxOutlined } from '@ant-design/icons';
import apiClient from '../../../services/api-client';
import { assetService } from '../services/asset.service';
import type { AssetDto } from '../types/asset';
import { uiColors } from '../../../theme/designTokens';

const { Text } = Typography;

interface AssetArchiveModalProps {
  open: boolean;
  asset: AssetDto | null;
  onClose: () => void;
  onSuccess: () => void;
}

const AssetArchiveModal: React.FC<AssetArchiveModalProps> = ({ open, asset, onClose, onSuccess }) => {
  const { message } = App.useApp();
  const [locationId, setLocationId] = useState<string | undefined>(undefined);
  const [note, setNote] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const [locationOptions, setLocationOptions] = useState<{ label: string; value: string }[]>([]);

  useEffect(() => {
    if (open) {
      setLocationId(undefined); setNote('');
      apiClient.get('/api/v1/locations', { params: { pageSize: 500 } })
        .then(res => setLocationOptions((res.data.data ?? []).map((l: { id: string; name: string }) => ({ label: l.name, value: l.id }))))
        .catch(() => setLocationOptions([]));
    }
  }, [open]);

  const handleSubmit = async () => {
    if (!asset || !locationId) { void message.warning('Vui lòng chọn vị trí lưu trữ / kho thanh lý (bắt buộc)'); return; }
    setSubmitting(true);
    try {
      await assetService.archive(asset.id, { locationId, note: note.trim() || undefined });
      void message.success('Đã lưu trữ tài sản');
      onSuccess();
    } catch (err: unknown) {
      const e = err as { response?: { data?: { message?: string } } };
      void message.error(e?.response?.data?.message ?? 'Lỗi lưu trữ tài sản');
    } finally { setSubmitting(false); }
  };

  return (
    <Modal title={<Space><InboxOutlined style={{ color: uiColors.warningAmber }} /><span>Lưu trữ / Thanh lý tài sản</span></Space>}
      open={open} onCancel={onClose} okText="Xác nhận lưu trữ" cancelText="Hủy"
      okButtonProps={{ disabled: !locationId, danger: true }} onOk={handleSubmit} confirmLoading={submitting} destroyOnHidden width={560}>
      {asset && (
        <Descriptions bordered size="small" column={1} style={{ marginBottom: 16 }}>
          <Descriptions.Item label="Mã tài sản"><Text strong>{asset.assetTag}</Text></Descriptions.Item>
          <Descriptions.Item label="Tên tài sản"><Text>{asset.name}</Text></Descriptions.Item>
          {asset.location?.name && (
            <Descriptions.Item label="Vị trí hiện tại"><Text>{asset.location.name}</Text></Descriptions.Item>
          )}
        </Descriptions>
      )}
      <Alert type="warning" showIcon style={{ marginBottom: 16, borderRadius: 8 }}
        message="Tài sản sẽ được chuyển sang trạng thái Lưu trữ và chuyển đến vị trí đã chọn." />
      <div style={{ marginBottom: 16 }}>
        <Text strong style={{ display: 'block', marginBottom: 8 }}>Vị trí lưu trữ / Kho thanh lý *</Text>
        <Select showSearch size="large" style={{ width: '100%' }} placeholder="Chọn vị trí lưu trữ hoặc kho thanh lý (bắt buộc)"
          options={locationOptions} value={locationId} onChange={setLocationId}
          filterOption={(input, option) => (option?.label as string)?.toLowerCase().includes(input.toLowerCase())} />
      </div>
      <div style={{ marginBottom: 8 }}>
        <Text strong style={{ display: 'block', marginBottom: 8 }}>Ghi chú lưu trữ:</Text>
        <Input.TextArea rows={3} maxLength={500} showCount value={note} onChange={e => setNote(e.target.value)} placeholder="Ghi chú lưu trữ (không bắt buộc)" />
      </div>
    </Modal>
  );
};

export default AssetArchiveModal;
