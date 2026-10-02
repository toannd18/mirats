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
  // [FIX-N11 2026-10-02] Explicit timeout: raw axios has NO default timeout, so a hung server left
  // the caller (and the UI spinner) waiting forever.
  const res = await axios.post('/api/v1/auth/login', { username, password }, { withCredentials: true, timeout: 15000 });
  accessToken = res.data.accessToken;
  const claims = decodeClaims(accessToken!);
  tokenExpiresAtMs = (claims?.exp ?? 0) * 1000;
  authState = 'authenticated';
  return { mustChangePassword: res.data.mustChangePassword === true };
}

/** POST /auth/refresh — cookie-carried rotation; returns true when a new access token arrived. */
export async function refreshAccessToken(): Promise<boolean> {
  try {
    // [FIX-N11] 10s timeout — without it a hung /auth/refresh left the api-client `isRefreshing`
    // flag stuck true forever, and every later 401 parked in the failed-queue permanently.
    const res = await axios.post('/api/v1/auth/refresh', {}, { withCredentials: true, timeout: 10000 });
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

/**
 * [FIX-N6 2026-10-02] Cross-tab single-flight refresh.
 *
 * The api-client module flag `isRefreshing` only dedupes requests WITHIN one JS context (one tab).
 * Two tabs hitting 401 at the same moment both POST /auth/refresh with the SAME cookie: the first
 * rotates it, the second presents the already-rotated token → the server's reuse-detection revokes
 * the ENTIRE session family and BOTH tabs are signed out (availability bug, not a security hole —
 * containment works as designed). The Web Locks API serializes the refresh across all tabs of this
 * origin, so the second tab waits and then rotates the NEW cookie legitimately.
 *
 * Fallback: browsers without navigator.locks (or non-secure contexts) keep the previous behaviour.
 */
export async function refreshAccessTokenCrossTab(): Promise<boolean> {
  const locks = (navigator as Navigator & {
    locks?: { request: <T>(name: string, callback: () => Promise<T>) => Promise<T> };
  }).locks;
  if (!locks?.request) return refreshAccessToken();
  try {
    return await locks.request('aspire-react-auth-refresh', () => refreshAccessToken());
  } catch {
    // Lock API failure must never break authentication — fall back to the unserialized path.
    return refreshAccessToken();
  }
}

/** POST /auth/logout — revoke the refresh session server-side + clear local state. */
export async function logout(): Promise<void> {
  try {
    // [FIX-N11] Fire-and-forget call still needs a timeout so it can never hang the sign-out flow.
    await axios.post('/api/v1/auth/logout', {}, { withCredentials: true, timeout: 5000 });
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
  // [FIX-N6] Boot-time restore is the most common multi-tab race (several tabs opening together
  // after a restart) → go through the cross-tab lock as well.
  return await refreshAccessTokenCrossTab();
}

/**
 * [AUTH Phase 3] Store an access token issued OUTSIDE this module (passkey login in
 * passkeys.ts) — keeps the token state single-sourced.
 */
export function applyAccessToken(token: string): void {
  accessToken = token;
  const claims = decodeClaims(token);
  tokenExpiresAtMs = (claims?.exp ?? 0) * 1000;
  authState = 'authenticated';
}
