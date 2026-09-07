import axios from 'axios';

/**
 * [AUTH Phase 2] Self-hosted password authentication service (Keycloak replacement).
 * Replaces `services/keycloak.ts` with the SAME surface consumed by the app
 * (initKeycloak→initAuth, login→n/a, logout, isAuthenticated, isSuperUser, getUserInfo),
 * so the ~10 importing files only change their import path once.
 *
 * Token model (AUTH_MIGRATION_PLAYBOOK §4.2 Option A — same-origin):
 *  - access JWT: kept in module memory ONLY (never localStorage — XSS-hard).
 *  - refresh token: httpOnly cookie scoped to /api/v1/auth (server sets/clears it).
 *  - refresh: POST /api/v1/auth/refresh with credentials:'include' — the cookie travels
 *    automatically (same-origin, SameSite=Lax).
 */

const TOKEN_EXPIRY_MARGIN_MS = 30_000;

let accessToken: string | null = null;
let tokenExpiresAtMs = 0;
let authState: 'unauthenticated' | 'authenticated' | 'unknown' = 'unknown';

/** Decoded JWT payload (kept minimal — only claims the app actually reads). */
export interface AuthTokenClaims {
  sub: string;
  preferred_username: string;
  email: string;
  given_name: string;
  family_name: string;
  local_user_id: string;
  permission?: string | string[];
  realm_access?: { roles?: string[] };
  pwd_change?: string;
  exp: number;
}

function decodeClaims(token: string): AuthTokenClaims | null {
  try {
    const payloadPart = token.split('.')[1];
    const json = atob(payloadPart.replace(/-/g, '+').replace(/_/g, '/'));
    return JSON.parse(json) as AuthTokenClaims;
  } catch {
    return null;
  }
}

/** Access token for the Authorization header (null when not logged in / expired). */
export function getToken(): string | null {
  if (accessToken && Date.now() < tokenExpiresAtMs - TOKEN_EXPIRY_MARGIN_MS) return accessToken;
  return null;
}

export function isAuthenticated(): boolean {
  return authState === 'authenticated' && getToken() !== null;
}

/** True when the signed-in user must change their admin-reset password before system use. */
export function mustChangePassword(): boolean {
  if (!accessToken) return false;
  return decodeClaims(accessToken)?.pwd_change === '1';
}

/**
 * Mirrors the backend ICompanyScopeService.IsSuperUser() 1-1 (kept in sync — the self-signed
 * token carries the same permission/realm_access claims the old Keycloak tokens had).
 */
export function isSuperUser(): boolean {
  const t = accessToken ? decodeClaims(accessToken) : null;
  if (!t) return false;
  const permission = t.permission;
  if (permission === 'superuser' || (Array.isArray(permission) && permission.includes('superuser'))) return true;
  const roles = t.realm_access?.roles;
  return Array.isArray(roles) && roles.some(r => r === 'admin' || r === 'superuser');
}

/** Identity claims of the current user (App.tsx header/profile). */
export function getUserInfo(): { id: string; username: string; email: string; firstName: string; lastName: string } {
  const t = accessToken ? decodeClaims(accessToken) : null;
  return {
    id: t?.local_user_id || '',
    username: t?.preferred_username || '',
    email: t?.email || '',
    firstName: t?.given_name || '',
    lastName: t?.family_name || '',
  };
}

/** Identity used to key the useCurrentUser cache (was the Keycloak `sub`). */
export function getCurrentSub(): string {
  return accessToken ? decodeClaims(accessToken)?.local_user_id || '' : '';
}

/** POST /auth/login — password login. Server sets the refresh cookie; we keep only the access token. */
export async function loginWithPassword(username: string, password: string): Promise<{ mustChangePassword: boolean }> {
  const res = await axios.post('/api/v1/auth/login', { username, password }, { withCredentials: true });
  accessToken = res.data.accessToken;
  const claims = decodeClaims(accessToken!);
  tokenExpiresAtMs = (claims?.exp ?? 0) * 1000;
  authState = 'authenticated';
  return { mustChangePassword: res.data.mustChangePassword === true };
}

/** POST /auth/refresh — cookie-carried rotation; returns true when a new access token arrived. */
export async function refreshAccessToken(): Promise<boolean> {
  try {
    const res = await axios.post('/api/v1/auth/refresh', {}, { withCredentials: true });
    accessToken = res.data.accessToken;
    const claims = decodeClaims(accessToken!);
    tokenExpiresAtMs = (claims?.exp ?? 0) * 1000;
    authState = 'authenticated';
    return true;
  } catch {
    clearTokens();
    return false;
  }
}

/** POST /auth/logout — revoke the refresh session server-side + clear local state. */
export async function logout(): Promise<void> {
  try {
    await axios.post('/api/v1/auth/logout', {}, { withCredentials: true });
  } catch {
    // Idempotent — clear local state even if the server call fails.
  }
  clearTokens();
}

function clearTokens(): void {
  accessToken = null;
  tokenExpiresAtMs = 0;
  authState = 'unauthenticated';
}

/**
 * App boot: silently restore the session from the refresh cookie (replaces initKeycloak's
 * login-required flow). Returns true when a valid session was restored.
 */
export async function initAuth(): Promise<boolean> {
  if (accessToken && Date.now() < tokenExpiresAtMs - TOKEN_EXPIRY_MARGIN_MS) {
    authState = 'authenticated';
    return true;
  }
  return await refreshAccessToken();
}
