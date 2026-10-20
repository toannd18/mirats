/**
 * [UI-1 GĐ2] Single source of truth for user-facing dates/numbers.
 *
 * Rules:
 * - Date-only fields (purchase date, start date, expiration date…) → `formatDate` (dd/MM/yyyy).
 * - Date+time fields (audit timestamps, checkouts, campaign start/end with time) → `formatDateTime`
 *   (dd/MM/yyyy HH:mm, local time).
 * - Numbers → `formatNumber` (vi-VN thousands separators).
 *
 * Do NOT call `toLocaleString` / `toLocaleDateString` / `toLocaleTimeString` or hand-roll
 * `DD/MM/YYYY` formatting elsewhere in the app (Sweep 7 in scripts/audit-sweeps.ps1 enforces this).
 */
function pad(n: number): string {
  return String(n).padStart(2, '0');
}

function parseDate(value?: string | Date | null): Date | null {
  if (value === null || value === undefined || value === '') return null;
  const d = value instanceof Date ? value : new Date(value);
  return Number.isNaN(d.getTime()) ? null : d;
}

/** dd/MM/yyyy — use for date-only values. */
export function formatDate(value?: string | Date | null): string {
  const d = parseDate(value);
  if (!d) return '-';
  return `${pad(d.getDate())}/${pad(d.getMonth() + 1)}/${d.getFullYear()}`;
}

/** dd/MM/yyyy HH:mm — use for timestamps. */
export function formatDateTime(value?: string | Date | null): string {
  const d = parseDate(value);
  if (!d) return '-';
  return `${formatDate(d)} ${pad(d.getHours())}:${pad(d.getMinutes())}`;
}

/** Backward-compatible alias used by the LL-4/LL-6 timeline code. */
export const formatDateTimeShort = formatDateTime;

/** vi-VN number formatting (thousands separators); empty/invalid → "-". */
export function formatNumber(value?: number | null): string {
  return typeof value === 'number' && !Number.isNaN(value) ? value.toLocaleString('vi-VN') : '-';
}

export function formatMoney(value?: unknown): string {
  return typeof value === 'number' && !Number.isNaN(value) ? `${formatNumber(value)} VND` : '-';
}
