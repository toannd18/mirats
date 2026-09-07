import apiClient from '../../../services/api-client';

/**
 * [AUTH Phase 3 fix — E2E-found] Use apiClient (NOT plain axios): the pwd_change limited
 * token MUST travel in the Authorization header. Plain axios sent no token → 401, so the
 * forced change-password form silently never advanced. Caught by the Phase 3
 * virtual-authenticator E2E — Phase 2 verify only exercised this endpoint via curl.
 */

export interface ChangePasswordResponse {
  ok: boolean;
  message?: string;
}

/** Change own password (requires the current one). Server revokes other sessions. */
export async function changeOwnPassword(currentPassword: string, newPassword: string): Promise<ChangePasswordResponse> {
  try {
    await apiClient.post('/auth/password', { currentPassword, newPassword });
    return { ok: true };
  } catch (err: unknown) {
    const e = err as { response?: { data?: { message?: string } } };
    return { ok: false, message: e?.response?.data?.message };
  }
}

/** Admin resets a user's password (users.edit policy; target must change it at next login). */
export async function adminResetPassword(userId: string, newPassword: string): Promise<ChangePasswordResponse> {
  try {
    await apiClient.post(`/users/${userId}/reset-password`, { newPassword });
    return { ok: true };
  } catch (err: unknown) {
    const e = err as { response?: { data?: { message?: string } } };
    return { ok: false, message: e?.response?.data?.message };
  }
}
