import axios from 'axios';

/**
 * [AUTH Phase 2] Thin API surface for the auth feature pages (on top of the token/cookie
 * plumbing in ./auth.ts — this module handles the FORM-driven flows only).
 */

export interface ChangePasswordResponse {
  ok: boolean;
  message?: string;
}

/** Change own password (requires the current one). Server revokes other sessions. */
export async function changeOwnPassword(currentPassword: string, newPassword: string): Promise<ChangePasswordResponse> {
  try {
    await axios.post('/api/v1/auth/password', { currentPassword, newPassword }, { withCredentials: true });
    return { ok: true };
  } catch (err: unknown) {
    const e = err as { response?: { data?: { message?: string } } };
    return { ok: false, message: e?.response?.data?.message };
  }
}

/** Admin resets a user's password (users.edit policy; target must change it at next login). */
export async function adminResetPassword(userId: string, newPassword: string): Promise<ChangePasswordResponse> {
  try {
    await axios.post(`/api/v1/users/${userId}/reset-password`, { newPassword });
    return { ok: true };
  } catch (err: unknown) {
    const e = err as { response?: { data?: { message?: string } } };
    return { ok: false, message: e?.response?.data?.message };
  }
}
