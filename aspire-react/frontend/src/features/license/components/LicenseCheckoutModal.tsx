import { useEffect, useState } from 'react';
import { App, Input, Modal, Radio, Select, Spin } from 'antd';
import apiClient from '../../../services/api-client';
import { licensesApi, type LicenseSeatTargetType } from '../services/licenses.service';

interface LicenseCheckoutModalProps {
  open: boolean;
  licenseId: string;
  licenseName: string;
  /** null = auto-pick the first free seat. */
  seatId: string | null;
  seatNumber: number | null;
  /** License's company — used to filter assignable targets (User/Asset/SystemInfo). */
  companyId: string | null;
  onClose: () => void;
  onSaved: () => void;
}

export default function LicenseCheckoutModal({
  open, licenseId, licenseName, seatId, seatNumber, companyId, onClose, onSaved,
}: LicenseCheckoutModalProps) {
  const { message } = App.useApp();
  const [targetType, setTargetType] = useState<LicenseSeatTargetType>('User');
  const [targetOptions, setTargetOptions] = useState<{ label: string; value: string }[]>([]);
  const [targetId, setTargetId] = useState<string | undefined>(undefined);
  const [note, setNote] = useState('');
  const [loading, setLoading] = useState(false);
  const [submitting, setSubmitting] = useState(false);

  const loadTargets = async (type: LicenseSeatTargetType) => {
    setLoading(true);
    setTargetId(undefined);
    setTargetOptions([]);
    try {
      let options: { label: string; value: string }[] = [];
      if (type === 'User') {
        const res = await apiClient.get('/users', { params: { pageSize: 500 } });
        options = ((res.data?.data ?? []) as {
          id: string; firstName: string; lastName: string; username: string; companyId: string | null;
        }[])
          .filter(u => !companyId || u.companyId === companyId)
          .map(u => ({ label: [u.firstName, u.lastName].filter(Boolean).join(' ') || u.username, value: u.id }));
      } else if (type === 'Asset') {
        const res = await apiClient.get('/assets', { params: { pageSize: 500 } });
        options = ((res.data?.data ?? []) as {
          id: string; name: string; assetTag: string; company?: { id: string } | null;
        }[])
          .filter(a => !companyId || a.company?.id === companyId)
          .map(a => ({ label: `${a.name} (${a.assetTag})`, value: a.id }));
      } else {
        // SystemInfo — the "Hệ thống" target is the SystemInfo PARENT (a license applies to the whole system).
        const res = await apiClient.get('/system-infos', { params: { pageSize: 100 } });
        const systems = (res.data?.data ?? []) as {
          id: string; name: string; code: string; companyId: string | null;
        }[];
        options = systems
          .filter(s => !companyId || s.companyId === companyId)
          .map(s => ({ label: `${s.name}${s.code ? ` (${s.code})` : ''}`, value: s.id }));
      }
      setTargetOptions(options);
      if (options.length === 0) message.warning('Không có đối tượng phù hợp (cùng công ty)');
    } catch {
      message.error('Không thể tải danh sách đối tượng');
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    if (open) void loadTargets('User');
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open]);

  const handleTypeChange = (v: LicenseSeatTargetType) => {
    setTargetType(v);
    void loadTargets(v);
  };

  const submit = async () => {
    if (!targetId) { message.warning('Chọn đối tượng nhận'); return; }
    setSubmitting(true);
    try {
      await licensesApi.checkout(licenseId, { seatId: seatId ?? null, targetType, targetId, note: note || null });
      message.success('Đã cấp phát seat');
      onSaved();
    } catch (err: unknown) {
      const e = err as { response?: { data?: { message?: string } } };
      message.error(e?.response?.data?.message || 'Không thể cấp phát seat');
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <Modal
      open={open}
      title={`Cấp phát seat${seatNumber ? ` #${seatNumber}` : ''} — ${licenseName}`}
      onCancel={onClose}
      onOk={() => void submit()}
      confirmLoading={submitting}
      width={560}
      destroyOnHidden
    >
      <div style={{ marginBottom: 12 }}>
        <Radio.Group
          value={targetType}
          onChange={e => handleTypeChange(e.target.value as LicenseSeatTargetType)}
          optionType="button"
          buttonStyle="solid"
        >
          <Radio.Button value="User">Người dùng</Radio.Button>
          <Radio.Button value="Asset">Tài sản</Radio.Button>
          <Radio.Button value="SystemInfo">Hệ thống</Radio.Button>
        </Radio.Group>
      </div>
      <Select
        showSearch
        style={{ width: '100%' }}
        placeholder={targetType === 'User' ? 'Chọn người dùng (cùng công ty)' : targetType === 'Asset' ? 'Chọn tài sản (cùng công ty)' : 'Chọn hệ thống (cùng công ty)'}
        loading={loading}
        value={targetId}
        onChange={setTargetId}
        options={targetOptions}
        filterOption={(input, option) => (option?.label as string)?.toLowerCase().includes(input.toLowerCase())}
        notFoundContent={loading ? <Spin size="small" /> : 'Không có đối tượng phù hợp'}
      />
      <Input.TextArea
        rows={2}
        placeholder="Ghi chú (tùy chọn)"
        value={note}
        onChange={e => setNote(e.target.value)}
        style={{ marginTop: 12 }}
      />
    </Modal>
  );
}