import type { FormInstance } from 'antd';

interface ApiErrorLike {
  response?: {
    status?: number;
    data?: {
      message?: string;
      error_code?: string;
      errorCode?: string;
      errors?: Record<string, string[] | string>;
    };
  };
}

const ERROR_CODE_MESSAGES: Record<string, string> = {
  FIELD_LOCKED: 'Trường này không thể thay đổi sau khi bản ghi đã được sử dụng.',
  COMPANY_MISMATCH: 'Bạn chỉ được thao tác với dữ liệu thuộc công ty của mình.',
  COMPANY_REQUIRED: 'Vui lòng chọn công ty.',
  FORBIDDEN: 'Bạn không có quyền thực hiện thao tác này.',
  SELF_APPROVAL_FORBIDDEN: 'Bạn không thể tự nghiệm thu phiếu do mình tạo.',
  INVALID_COMPANY: 'Công ty không hợp lệ.',
  CATEGORY_REQUIRED: 'Vui lòng chọn danh mục.',
  INVALID_CATEGORY: 'Danh mục không hợp lệ.',
  LOCATION_REQUIRED: 'Vui lòng chọn địa điểm.',
  TARGET_NOT_FOUND: 'Không tìm thấy đối tượng được chọn.',
  NOT_FOUND: 'Không tìm thấy bản ghi.',
  DUPLICATE_ASSET_TAG: 'Mã tài sản đã tồn tại.',
  RESOURCE_IN_USE: 'Bản ghi đang được sử dụng, không thể xóa.',
  REFERENCE_NOT_FOUND: 'Dữ liệu tham chiếu không tồn tại.',
  VALIDATION_ERROR: 'Dữ liệu không hợp lệ, vui lòng kiểm tra lại.',
  SELF_LOCKOUT: 'Không thể tự khóa quyền quản trị của chính mình.',
  SYSTEM_GROUP_LOCKED: 'Không thể sửa nhóm hệ thống.',
  CONFIRMED_CONSUMABLE_LOCKED: 'Vật tư đã được xác nhận nên không thể sửa trường này.',
  COMPONENT_HAS_ALLOCATION_HISTORY: 'Linh kiện đã có lịch sử cấp phát, không thể xóa.',
  CONSUMABLE_HAS_CHECKOUTS: 'Vật tư đã có lịch sử cấp phát, không thể xóa.',
  ACCESSORY_HAS_CHECKOUTS: 'Phụ kiện đã có lịch sử cấp phát, không thể xóa.',
  INSTALLATION_PERIOD_OVERLAP: 'Khoảng thời gian lắp đặt bị trùng với lịch sử hiện có.',
  INSTALLATION_ALREADY_SUPERSEDED: 'Bản ghi lắp đặt đã bị thay thế, không thể đánh chỉnh.',
  MAINTENANCE_CLOSED: 'Phiếu bảo trì đã đóng, không thể sửa.',
  RESULT_CONCURRENT_WRITE: 'Dữ liệu vừa được người khác cập nhật. Vui lòng thử lại.',
  VERSION_ALREADY_PUBLISHED: 'Phiên bản này đã được công bố.',
  TITLE_REQUIRED: 'Vui lòng nhập tiêu đề.',
  COMPLETION_BEFORE_START: 'Ngày hoàn thành không được trước ngày bắt đầu.',
  INVALID_COST: 'Chi phí không được âm.',
  INVALID_SUPPLIER: 'Nhà cung cấp không hợp lệ.',
};

/** Return a Vietnamese message; never leak a raw English server message. */
export function serverErrorMessage(error: unknown, fallback = 'Không thể lưu dữ liệu. Vui lòng thử lại.'): string {
  const e = error as ApiErrorLike;
  const data = e?.response?.data;
  const code = data?.error_code || data?.errorCode;
  if (code && ERROR_CODE_MESSAGES[code]) return ERROR_CODE_MESSAGES[code];

  const message = data?.message?.trim();
  // Server messages in this project are normally Vietnamese. Only accept a message that
  // contains Vietnamese diacritics; otherwise use the localized fallback.
  if (message && /[ăâđêôơưàáảãạằắẳẵặầấẩẫậèéẻẽẹềếểễệìíỉĩịòóỏõọồốổỗộờớởỡợùúủũụừứửữựỳýỷỹỵ]/i.test(message)) {
    return message;
  }
  return fallback;
}

/**
 * Apply ASP.NET validation errors to the matching AntD form fields.
 * Field keys are normalised from `FirstName`/`firstName` to the camelCase used by forms.
 */
export function applyServerFieldErrors(
  form: FormInstance,
  error: unknown,
): boolean {
  const e = error as ApiErrorLike;
  const errors = e?.response?.data?.errors;
  if (!errors) return false;

  const entries = Object.entries(errors)
    .map(([name, messages]) => {
      const text = Array.isArray(messages) ? messages[0] : messages;
      return { name: name.charAt(0).toLowerCase() + name.slice(1), errors: [String(text)] };
    })
    .filter(entry => entry.errors[0] && entry.errors[0] !== 'undefined');
  if (entries.length === 0) return false;

  form.setFields(entries);
  return true;
}
