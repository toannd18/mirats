import { useEffect, useState } from 'react';
import apiClient from '../services/api-client';
import { getCurrentSub } from '../services/keycloak';

/**
 * Kết quả GET /api/v1/users/me — thông tin user hiện tại (đã camelCase từ JSON).
 * Chỉ dùng cho AppBar (tên công ty, họ tên hiển thị). KHÔNG dùng làm nguồn quyền.
 */
export interface CurrentUserDto {
  id: string;
  username: string;
  email: string | null;
  firstName: string | null;
  lastName: string | null;
  employeeNumber: string | null;
  jobTitle: string | null;
  isSuperUser: boolean;
  isActive: boolean;
  companyId: string | null;
  companyName: string | null;
  departmentId: string | null;
  departmentName: string | null;
  locationId: string | null;
  locationName: string | null;
  groups: { groupId: string; name: string; isSystem: boolean }[];
}

// Module-level cache keyed by the Keycloak identity (token `sub`): each login fetches /users/me
// exactly once. Keying by `sub` guarantees that if the signed-in user changes within the same SPA
// lifetime, the NEXT user's profile is fetched fresh — a stale cached user can NEVER leak into the
// "Xem hồ sơ" route (this previously served the FIRST login's id to every later login in a session,
// sending users to the wrong profile → "Không tìm thấy người dùng").
const cache = new Map<string, CurrentUserDto | null>();
const inflight = new Map<string, Promise<CurrentUserDto | null>>();

const fetchCurrentUser = async (sub: string): Promise<CurrentUserDto | null> => {
  if (cache.has(sub)) return cache.get(sub)!;
  if (inflight.has(sub)) return inflight.get(sub)!;
  const p = (async () => {
    try {
      const res = await apiClient.get('/users/me');
      const u = (res.data?.data as CurrentUserDto) ?? null;
      cache.set(sub, u);
      return u;
    } catch {
      // Fallback im lặng — KHÔNG throw, KHÔNG crash layout. Chỉ cần ẩn phần công ty.
      cache.set(sub, null);
      return null;
    } finally {
      inflight.delete(sub);
    }
  })();
  inflight.set(sub, p);
  return p;
};

/** Xóa toàn bộ cache (dùng sau khi đăng nhập lại / logout). */
export const clearCurrentUserCache = () => {
  cache.clear();
};

/** Hook đọc user hiện tại (cache theo identity, fetch 1 lần/user/phiên). Trả null khi chưa load hoặc lỗi. */
export const useCurrentUser = (): CurrentUserDto | null => {
  const sub = getCurrentSub();
  const [user, setUser] = useState<CurrentUserDto | null>(cache.get(sub) ?? null);

  useEffect(() => {
    let alive = true;
    void fetchCurrentUser(sub).then(u => {
      if (alive) setUser(u);
    });
    return () => {
      alive = false;
    };
  }, [sub]);

  return user;
};
