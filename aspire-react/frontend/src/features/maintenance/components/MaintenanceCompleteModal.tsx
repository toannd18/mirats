import { useEffect, useState } from 'react';
import {
  App, Col, DatePicker, Form, Grid, Input, InputNumber, Modal, Row, Select, Switch,
} from 'antd';
import dayjs, { type Dayjs } from 'dayjs';
import apiClient from '../../../services/api-client';
import { assetService, type AssetMaintenanceDto } from '../../asset/services/asset.service';

interface MaintenanceCompleteModalProps {
  /** The in-progress maintenance being completed. `null` = modal closed. */
  record: AssetMaintenanceDto | null;
  onClose: () => void;
  /** Called after a successful save — the parent reloads its list so the Card reflects the new data. */
  onSaved: () => void;
}

/**
 * Task H — "Hoàn thành bảo trì": a dedicated, compact modal for the COMPLETION step only.
 * It contains exactly the fields needed to finish a maintenance ticket (completionDate + cost +
 * supplierId + isWarranty + notes). Title/Type/Assignees/StartDate/Asset are NOT shown and NOT
 * sent — they were fixed at creation time.
 *
 * PAYLOAD NOTE (Task F lesson): the backend PUT is full-replace for SupplierId/CompletionDate/Cost
 * (absent field → wiped). Therefore this modal ALWAYS sends the complete 5-field completion group,
 * pre-filled from the record's current values — so no field outside the modal is ever touched and
 * no field inside the modal is accidentally wiped by an absent key.
 */
export default function MaintenanceCompleteModal({ record, onClose, onSaved }: MaintenanceCompleteModalProps) {
  const { message } = App.useApp();
  const { useBreakpoint } = Grid;
  const screens = useBreakpoint();
  const isMobile = !screens.md;
  const [form] = Form.useForm();
  const [submitting, setSubmitting] = useState(false);
  const [suppliers, setSuppliers] = useState<{ label: string; value: string }[]>([]);

  useEffect(() => {
    apiClient.get('/suppliers', { params: { pageSize: 500 } })
      .then(r => {
        const list = (r.data?.data ?? []) as { id: string; name: string }[];
        setSuppliers(list.map(s => ({ label: s.name, value: s.id })));
      })
      .catch(() => { /* non-critical — dropdown rỗng không crash */ });
  }, []);

  // Pre-fill from the record every time a new target opens.
  useEffect(() => {
    if (record) {
      form.setFieldsValue({
        completionDate: record.completionDate ? dayjs(record.completionDate) : undefined,
        cost: record.cost ?? undefined,
        supplierId: record.supplier?.id,
        isWarranty: record.isWarranty,
        notes: record.notes ?? undefined,
      });
    }
  }, [record, form]);

  const submit = async (vals: Record<string, unknown>) => {
    if (!record) return;
    setSubmitting(true);
    try {
      // CHỈ 5 field nhóm "hoàn thành" — không bao giờ kèm title/type/assigneeUserIds/startDate.
      const payload = {
        completionDate: (vals.completionDate as Dayjs).toISOString(),
        cost: typeof vals.cost === 'number' ? vals.cost : null,
        supplierId: vals.supplierId ? (vals.supplierId as string) : null,
        isWarranty: Boolean(vals.isWarranty),
        notes: (vals.notes as string | undefined)?.trim() || null,
      };
      await assetService.updateMaintenance(record.id, payload);
      message.success('Đã cập nhật ngày hoàn thành bảo trì');
      onSaved();
    } catch (err: unknown) {
      const e = err as { response?: { data?: { message?: string } } };
      message.error(e?.response?.data?.message || 'Không thể lưu bảo trì');
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <Modal
      title="Hoàn thành bảo trì"
      open={!!record}
      onCancel={onClose}
      onOk={() => form.submit()}
      confirmLoading={submitting}
      width={isMobile ? '95%' : 560}
      okText="Lưu"
      cancelText="Hủy"
    >
      <Form
        form={form}
        layout="vertical"
        onFinish={(v) => void submit(v as Record<string, unknown>)}
      >
        <Form.Item
          name="completionDate"
          label="Ngày hoàn thành"
          rules={[{ required: true, message: 'Chọn ngày hoàn thành' }]}
        >
          <DatePicker
            style={{ width: '100%' }}
            disabledDate={(d) => !!record && d.isBefore(dayjs(record.startDate).startOf('day'))}
          />
        </Form.Item>
        <Row gutter={[16, 8]}>
          <Col xs={24} sm={12}>
            <Form.Item name="cost" label="Chi phí">
              <InputNumber style={{ width: '100%' }} min={0} placeholder="VND" />
            </Form.Item>
          </Col>
          <Col xs={24} sm={12}>
            <Form.Item name="supplierId" label="Nhà cung cấp">
              <Select allowClear placeholder="Chọn NCC" options={suppliers} />
            </Form.Item>
          </Col>
          <Col xs={24} sm={12}>
            <Form.Item name="isWarranty" label="Bảo hành" valuePropName="checked">
              <Switch />
            </Form.Item>
          </Col>
        </Row>
        <Form.Item name="notes" label="Ghi chú kết quả">
          <Input.TextArea rows={3} placeholder="Kết quả xử lý, nội dung đã thực hiện..." />
        </Form.Item>
      </Form>
    </Modal>
  );
}
