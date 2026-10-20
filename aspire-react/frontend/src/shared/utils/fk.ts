/**
 * [FIX-GUID-EMPTY phần 2 — 2026-10-20] Quy ước trường KHÓA NGOẠI (FK) TÙY CHỌN trong payload **Update**:
 *
 * - **Vắng field** (`undefined` / không có key) → backend **giữ nguyên** liên kết hiện tại.
 * - **`EMPTY_GUID`** (`00000000-0000-0000-0000-000000000000`) → backend **XÓA liên kết** (gán `null`).
 * - **id thật** → gán liên kết; id phải tồn tại, nếu không backend trả `400 RESOURCE_NOT_FOUND`.
 *
 * Vì sao cần helper: AntD `Select` + `allowClear` trả `undefined` khi người dùng bấm X. Nếu gửi nguyên
 * `undefined`, axios bỏ field khỏi JSON ⇒ backend hiểu là "không gửi" ⇒ **không xóa được liên kết**.
 * Form Update phải đổi giá trị rỗng thành `EMPTY_GUID`.
 *
 * Lưu ý: chỉ áp dụng cho **Update** (Create không dùng sentinel này); và chỉ cho field **có mặt** trong
 * form (field bị ẩn/không đăng ký thì không gửi, tránh xóa nhầm).
 */

export const EMPTY_GUID = '00000000-0000-0000-0000-000000000000';

/** True khi giá trị rỗng (undefined/null/'') — tức người dùng đã bỏ chọn. */
export function isEmptyRef(value: unknown): boolean {
  return value === undefined || value === null || value === '';
}

/**
 * Chuẩn hoá MỘT field FK cho payload Update: rỗng ⇒ `EMPTY_GUID` (xóa liên kết), còn lại giữ id.
 * Dùng khi field **luôn được render** trong form sửa.
 */
export function fkForUpdate(value: unknown): string {
  return isEmptyRef(value) ? EMPTY_GUID : String(value);
}

/**
 * Trả về object chỉ chứa các key FK **có mặt** trong `values` (form đã đăng ký field), đã chuẩn hoá
 * rỗng ⇒ `EMPTY_GUID`. Dùng để spread vào payload Update của form dạng `{ ...values }`.
 *
 * `validateFields()` của AntD trả key với giá trị `undefined` cho field đã đăng ký nhưng bị xóa lựa chọn,
 * nên `key in values` phân biệt được "xóa" với "field không render".
 */
export function withOptionalFkCleared(
  values: Record<string, unknown>,
  keys: string[],
): Record<string, string> {
  const cleared: Record<string, string> = {};
  for (const key of keys) {
    if (key in values) cleared[key] = fkForUpdate(values[key]);
  }
  return cleared;
}
