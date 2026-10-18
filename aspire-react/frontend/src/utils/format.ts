export function formatDate(value?: string | null): string {
  return value ? new Date(value).toLocaleDateString('vi-VN') : '-';
}

export function formatDateTime(value?: string | null): string {
  return value ? new Date(value).toLocaleString('vi-VN') : '-';
}

/**
 * Ngày + giờ dạng `dd/MM/yyyy HH:mm` theo múi giờ MÁY NGƯỜI DÙNG.
 *
 * Vì sao không dùng `formatDateTime`: `toLocaleString('vi-VN')` xuất theo thứ tự mặc định của locale
 * ("16:30:00 17/10/2026" — giờ trước ngày, có giây) nên không ra đúng khuôn `dd/MM/yyyy HH:mm` mà
 * màn Lý lịch thiết bị cần (LL-4). Ở đây tự đệm từng thành phần; KHÔNG tự cộng/trừ múi giờ —
 * value truyền vào là ISO-8601 UTC (`...Z`) nên `Date` tự quy đổi về giờ địa phương của máy.
 */
export function formatDateTimeShort(value?: string | null): string {
  if (!value) return '-';
  const d = new Date(value);
  if (Number.isNaN(d.getTime())) return '-';
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${pad(d.getDate())}/${pad(d.getMonth() + 1)}/${d.getFullYear()} ${pad(d.getHours())}:${pad(d.getMinutes())}`;
}

export function formatMoney(value?: unknown): string {
  return typeof value === 'number' && !Number.isNaN(value)
    ? `${value.toLocaleString('vi-VN')} VND`
    : '-';
}
