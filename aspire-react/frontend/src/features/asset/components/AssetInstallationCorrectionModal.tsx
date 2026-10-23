import { useCallback, useEffect, useState } from 'react';
import { Alert, App, Button, DatePicker, Form, Input, Select, Skeleton, Tag, Typography, theme } from 'antd';
import { DiffOutlined } from '@ant-design/icons';
import dayjs from 'dayjs';
import apiClient from '../../../services/api-client';
import { assetService } from '../services/asset.service';
import { formatDateTimeShort } from '../../../utils/format';
import type { AssetInstallationDetail } from '../types/timeline';
import ModalFormShell from '../../../shared/components/ModalFormShell';

const { Text } = Typography;

/** Giới hạn lý do đính chính — phải khớp validator backend (`MinReasonLength`/`MaxReasonLength`). */
const REASON_MIN = 10;
const REASON_MAX = 500;

interface Props {
  open: boolean;
  assetId: string;
  /** Id dòng lý lịch (`asset_installations.Id`) — lấy từ `refId` của sự kiện INSTALL/REMOVE. */
  entryId: string | null;
  onClose: () => void;
  /** Gọi SAU khi server trả 200 — cha hiển thị thông báo + tải lại timeline (giữ nguyên bộ lọc). */
  onSuccess: (message: string) => void;
}

/**
 * [LL-5] Modal "Đính chính" một dòng lý lịch lắp đặt.
 *
 * Đặc điểm bắt buộc:
 *  - Tải dữ liệu dòng GỐC từ `GET /assets/{assetId}/installations/{entryId}` (timeline DTO không đủ field).
 *  - Dòng ĐANG MỞ: khoá "Kết thúc" và "Vị trí" (kèm chú thích dùng Thu hồi + Cấp phát lại).
 *  - Chỉ GỬI các trường đã thay đổi (patch-semantics); `reason` luôn bắt buộc 10–500 ký tự.
 *  - Dòng ĐÃ ĐÓNG: mốc "Kết thúc" bắt buộc (không có nút xoá trống).
 *  - Dòng Backfill: ô "Ghi chú lý lịch" để TRỐNG (không điền note kỹ thuật) — backend cũng không kế thừa
 *    note của dòng backfill.
 *  - Hiển thị ĐÚNG `message` + `error_code` mà server trả về (không tự bịa lỗi).
 */
export default function AssetInstallationCorrectionModal({ open, assetId, entryId, onClose, onSuccess }: Props) {
  const { message } = App.useApp();
  const { token } = theme.useToken();
  const [form] = Form.useForm();
  const [detail, setDetail] = useState<AssetInstallationDetail | null>(null);
  const [loading, setLoading] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [positionOptions, setPositionOptions] = useState<{ label: string; value: string }[]>([]);

  const isOpenEntry = detail != null && detail.endedAt === null;
  // [LL-5 Bước 7] Dòng ĐÃ ĐÓNG — áp dụng cho MỌI nguồn (Api/Import/Backfill/Correction): mốc "Kết thúc" là
  // BẮT BUỘC và không cho xoá trống. (Không phụ thuộc `source`/`isEstimated`.)
  const isClosedRow = detail != null && detail.endedAt !== null;

  // Nạp dòng gốc mỗi lần mở modal.
  useEffect(() => {
    if (!open || !entryId) return;
    let cancelled = false;
    setLoading(true);
    setDetail(null);
    void (async () => {
      try {
        const res = await assetService.getInstallation(assetId, entryId);
        if (cancelled) return;
        const data = res.data?.data as AssetInstallationDetail | undefined;
        if (!data) {
          void message.error('Không tìm thấy dòng lý lịch để đính chính.');
          return;
        }
        setDetail(data);
        form.setFieldsValue({
          startedAt: data.startedAt ? dayjs(data.startedAt) : undefined,
          endedAt: data.endedAt ? dayjs(data.endedAt) : undefined,
          systemPositionId: data.systemPositionId ?? undefined,
          // [LL-5 Bước 6] Dòng Backfill: KHÔNG điền sẵn Note KỸ THUẬT của bước chuyển dữ liệu (ô để trống);
          // backend cũng KHÔNG kế thừa note của dòng backfill ⇒ bản mới Note = null khi không gửi.
          note: data.isEstimated ? '' : (data.note ?? ''),
          reason: '',
        });
      } catch {
        if (!cancelled) void message.error('Không thể tải dòng lý lịch để đính chính.');
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();
    return () => { cancelled = true; };
  }, [open, entryId, assetId, form, message]);

  // Danh sách VỊ TRÍ (SystemPosition) cho Select — cùng nguồn với modal cấp phát tài sản.
  useEffect(() => {
    if (!open) return;
    void (async () => {
      try {
        const res = await apiClient.get('/system-infos', { params: { pageSize: 500 } });
        const systems = (res.data?.data ?? []) as {
          name: string;
          positions?: { id: string; name: string; systemInfoName?: string }[];
        }[];
        const options: { label: string; value: string }[] = [];
        for (const sys of systems) {
          for (const pos of sys.positions ?? []) {
            options.push({ label: `${pos.name} — ${pos.systemInfoName ?? sys.name}`, value: pos.id });
          }
        }
        setPositionOptions(options);
      } catch {
        setPositionOptions([]);
      }
    })();
  }, [open]);

  const handleSubmit = useCallback(async () => {
    if (!detail) return;
    const values = await form.validateFields();

    // Chỉ gửi trường ĐÃ THAY ĐỔI (patch-semantics) — nếu không đổi gì, server trả 400 CORRECTION_NO_CHANGES.
    const payload: { startedAt?: string; endedAt?: string; systemPositionId?: string; note?: string; reason: string } = {
      reason: String(values.reason).trim(),
    };

    const startedAt = values.startedAt ? (values.startedAt as dayjs.Dayjs).toISOString() : undefined;
    if (startedAt && !dayjs(startedAt).isSame(dayjs(detail.startedAt))) payload.startedAt = startedAt;

    if (!isOpenEntry) {
      const endedAt = values.endedAt ? (values.endedAt as dayjs.Dayjs).toISOString() : undefined;
      if (endedAt && (!detail.endedAt || !dayjs(endedAt).isSame(dayjs(detail.endedAt)))) payload.endedAt = endedAt;

      const positionId = values.systemPositionId as string | undefined;
      if (positionId && positionId !== detail.systemPositionId) payload.systemPositionId = positionId;
    }

    // [LL-5 Bước 6] Dòng Backfill: giá trị "gốc" để so sánh patch là chuỗi RỖNG (note kỹ thuật KHÔNG được
    // kế thừa) ⇒ người dùng để trống thì KHÔNG gửi note, và backend cũng không kế thừa (bản mới Note = null).
    const baseNote = detail.isEstimated ? '' : (detail.note ?? '');
    const note = String(values.note ?? '');
    if (note !== baseNote) payload.note = note;

    setSubmitting(true);
    try {
      const res = await assetService.correctInstallation(assetId, detail.id, payload);
      onSuccess((res.data?.message as string | undefined) ?? 'Đã đính chính lý lịch lắp đặt.');
    } catch (err) {
      const e = err as { response?: { data?: { message?: string; error_code?: string } } };
      const serverMessage = e?.response?.data?.message;
      const errorCode = e?.response?.data?.error_code;
      void message.error(
        errorCode
          ? `${serverMessage ?? 'Không thể đính chính lý lịch.'} (${errorCode})`
          : (serverMessage ?? 'Không thể đính chính lý lịch.'),
      );
    } finally {
      setSubmitting(false);
    }
  }, [detail, form, isOpenEntry, assetId, onSuccess, message]);

  return (
    <ModalFormShell
      open={open}
      title={<span><DiffOutlined /> Đính chính lý lịch lắp đặt</span>}
      onCancel={onClose}
      size="sm"
      footer={[
        <Button key="cancel" onClick={onClose} disabled={submitting}>Hủy</Button>,
        <Button key="ok" type="primary" loading={submitting} disabled={loading || detail == null} onClick={() => void handleSubmit()}>
          Đính chính
        </Button>,
      ]}
    >
      {loading && <Skeleton active paragraph={{ rows: 5 }} />}

      {!loading && detail && (
        <>
          <Alert
            type="warning"
            showIcon
            style={{ marginBottom: 12 }}
            title="Bản ghi hiện tại sẽ bị ĐÁNH DẤU là đã bị thay thế"
            description="Dữ liệu cũ KHÔNG bị xoá: hệ thống tạo một bản ghi mới và ẩn bản cũ khỏi tab Lý lịch (xem lý do ở sự kiện Đính chính trong tab Lý lịch, chế độ “Tất cả”)."
          />

          {detail.isEstimated && (
            <Alert
              type="info"
              showIcon
              style={{ marginBottom: 12 }}
              title="Mốc thời gian hiện tại là ƯỚC TÍNH (dòng chuyển dữ liệu cũ)"
              description="Sau khi đính chính, mốc sẽ được coi là do người dùng xác nhận — bản mới KHÔNG còn nhãn “Thời điểm ước tính”."
            />
          )}

          <div style={{ marginBottom: 12, fontSize: 12, color: token.colorTextSecondary }}>
            Đang đính chính dòng gắn <b>{detail.systemPositionName ?? '(vị trí đã xóa)'}</b>
            {detail.systemInfoName ? ` — ${detail.systemInfoName}` : ''}
            {' · '}
            {formatDateTimeShort(detail.startedAt)} → {detail.endedAt ? formatDateTimeShort(detail.endedAt) : 'đang mở'}
            {isOpenEntry ? <Tag style={{ marginInlineStart: 6 }}>Dòng đang mở</Tag> : null}
            {detail.isEstimated ? <Tag color="warning" style={{ marginInlineStart: 6 }}>Thời điểm ước tính</Tag> : null}
          </div>

          <Form form={form} layout="vertical" requiredMark="optional">
            <Form.Item
              name="systemPositionId"
              label="Vị trí"
              extra={isOpenEntry ? 'Dòng đang mở: dùng Thu hồi rồi Cấp phát lại để đổi vị trí.' : undefined}
            >
              <Select
                showSearch
                optionFilterProp="label"
                disabled={isOpenEntry}
                placeholder="Chọn vị trí"
                options={positionOptions}
              />
            </Form.Item>

            <Form.Item name="startedAt" label="Bắt đầu" rules={[{ required: true, message: 'Chọn mốc bắt đầu' }]}>
              <DatePicker showTime format="DD/MM/YYYY HH:mm" style={{ width: '100%' }} />
            </Form.Item>

            <Form.Item
              name="endedAt"
              label="Kết thúc"
              // [LL-5 Bước 7] MỌI dòng ĐÃ ĐÓNG (bất kể Source): mốc kết thúc BẮT BUỘC ⇒ không còn nhãn "(Tùy chọn)".
              required={isClosedRow}
              rules={isClosedRow
                ? [{ required: true, message: 'Chọn mốc kết thúc cho dòng đã đóng' }]
                : undefined}
              extra={isOpenEntry ? 'Dòng đang mở: dùng Thu hồi để đóng khoảng lắp đặt.' : undefined}
            >
              <DatePicker
                showTime
                format="DD/MM/YYYY HH:mm"
                style={{ width: '100%' }}
                disabled={isOpenEntry}
                // Dòng ĐÃ ĐÓNG: không cho xoá trống (mốc kết thúc bắt buộc).
                allowClear={false}
              />
            </Form.Item>

            <Form.Item name="note" label="Ghi chú lý lịch" extra="Để trống để xoá ghi chú hiện tại.">
              <Input.TextArea rows={2} maxLength={500} />
            </Form.Item>

            <Form.Item
              name="reason"
              label="Lý do đính chính"
              rules={[
                { required: true, message: 'Nhập lý do đính chính' },
                { min: REASON_MIN, max: REASON_MAX, message: `Lý do phải từ ${REASON_MIN} đến ${REASON_MAX} ký tự` },
              ]}
              extra={<Text type="secondary" style={{ fontSize: 12 }}>Bắt buộc — lưu vào nhật ký (Lịch sử) để kiểm toán.</Text>}
            >
              <Input.TextArea
                rows={3}
                maxLength={REASON_MAX}
                showCount
                placeholder={`Ví dụ: nhập nhầm vị trí khi lắp đặt (${REASON_MIN}–${REASON_MAX} ký tự)`}
              />
            </Form.Item>
          </Form>
        </>
      )}
    </ModalFormShell>
  );
}
