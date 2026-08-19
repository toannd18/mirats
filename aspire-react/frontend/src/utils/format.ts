export function formatDate(value?: string | null): string {
  return value ? new Date(value).toLocaleDateString('vi-VN') : '-';
}

export function formatDateTime(value?: string | null): string {
  return value ? new Date(value).toLocaleString('vi-VN') : '-';
}

export function formatMoney(value?: unknown): string {
  return typeof value === 'number' && !Number.isNaN(value)
    ? `${value.toLocaleString('vi-VN')} VND`
    : '-';
}
