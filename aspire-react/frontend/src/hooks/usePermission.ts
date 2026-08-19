import { useEffect, useState } from 'react';
import apiClient from '../services/api-client';

/**
 * Kết quả GET /api/v1/permissions/check — bản đồ permission hiệu dụng của user hiện tại
 * (nghĩa giá trị: 1 = Grant, -1 = Deny, 0 = NotSet), cộng cờ superuser/admin.
 */
export interface PermissionCheck {
  permissions: Record<string, number>;
  isSuperUser: boolean;
  isAdmin: boolean;
}

// Module-level cache: mỗi phiên đăng nhập chỉ fetch một lần (permission của user không đổi
// trong suốt phiên; nếu admin đổi nhóm, server yêu cầu refresh token mới để phản ánh).
let cached: PermissionCheck | null = null;
let inflight: Promise<PermissionCheck> | null = null;

const fetchPermissionCheck = async (): Promise<PermissionCheck> => {
  if (cached) return cached;
  if (inflight) return inflight;
  inflight = (async () => {
    try {
      const res = await apiClient.get('/permissions/check');
      const data = res.data?.data as Partial<PermissionCheck> | undefined;
      cached = {
        permissions: data?.permissions ?? {},
        isSuperUser: !!data?.isSuperUser,
        isAdmin: !!data?.isAdmin,
      };
    } catch {
      // Fail-closed: không biết quyền thì không cấp.
      cached = { permissions: {}, isSuperUser: false, isAdmin: false };
    } finally {
      inflight = null;
    }
    return cached!;
  })();
  return inflight;
};

/** Xóa cache (dùng sau khi đăng nhập lại / đổi quyền). */
export const clearPermissionCache = () => {
  cached = null;
};

/**
 * Kiểm tra quyền thực tế của user hiện tại (lấy từ DB qua /permissions/check).
 * Superuser luôn true. Trả về true khi permission key có giá trị Grant (1).
 */
export const usePermission = (code: string): boolean => {
  const [check, setCheck] = useState<PermissionCheck | null>(cached);

  useEffect(() => {
    let alive = true;
    void fetchPermissionCheck().then(c => {
      if (alive) setCheck(c);
    });
    return () => {
      alive = false;
    };
  }, []);

  if (!check) return false;
  if (check.isSuperUser) return true;
  return (check.permissions[code] ?? 0) === 1;
};

/** Toàn bộ bản đồ permission hiệu dụng + cờ superuser/admin (dùng cho màn hình quản trị). */
export const usePermissionMap = (): PermissionCheck | null => {
  const [check, setCheck] = useState<PermissionCheck | null>(cached);

  useEffect(() => {
    let alive = true;
    void fetchPermissionCheck().then(c => {
      if (alive) setCheck(c);
    });
    return () => {
      alive = false;
    };
  }, []);

  return check;
};
