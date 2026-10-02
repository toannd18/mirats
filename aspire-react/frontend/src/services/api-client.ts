import axios, { AxiosError } from 'axios';
import type { InternalAxiosRequestConfig } from 'axios';
import { getToken, logout, refreshAccessTokenCrossTab, mustChangePassword } from '../features/auth/services/auth';

// Backend API base URL — can be overridden via VITE_API_BASE_URL env variable.
// Semantics: it is the SERVER base (origin or path). The `/api/v1` prefix is appended
// here — UNLESS the base already ends with it (e.g. prod `VITE_API_BASE_URL=/api/v1`
// via compose build arg must NOT become `/api/v1/api/v1`).
//
// [AUTH Phase 2 — §4.2 Option A same-origin] Default base is '' (relative) so the browser
// only ever talks to the Vite dev origin (HTTPS proxy → 7314) or the nginx prod proxy.
const API_BASE = (import.meta as unknown as { env?: Record<string, string | undefined> }).env?.VITE_API_BASE_URL ?? '';
const API_PREFIX = '/api/v1';
const baseURL = API_BASE.endsWith(API_PREFIX) ? API_BASE : `${API_BASE}${API_PREFIX}`;

const apiClient = axios.create({
  baseURL,
  timeout: 30000,
  headers: { 'Content-Type': 'application/json' },
});

// Track whether a token refresh is in progress (to deduplicate concurrent requests)
let isRefreshing = false;
let failedQueue: Array<{
  resolve: (token: string) => void;
  reject: (error: unknown) => void;
}> = [];

const processQueue = (error: unknown, token: string | null = null) => {
  failedQueue.forEach((promise) => {
    if (error || !token) {
      promise.reject(error);
    } else {
      promise.resolve(token);
    }
  });
  failedQueue = [];
};

// Request interceptor — attach the in-memory access token (refresh happens on 401; the
// refresh-token cookie travels automatically — same-origin, SameSite=Lax).
apiClient.interceptors.request.use(
  (config: InternalAxiosRequestConfig) => {
    const token = getToken();
    if (token && config.headers) {
      config.headers.Authorization = `Bearer ${token}`;
    }
    return config;
  },
  (error) => Promise.reject(error)
);

// Response interceptor — on 401: refresh via the httpOnly cookie and retry once (queueing
// concurrent failures); give up → sign out. Matches the old Keycloak updateToken(30) loop.
// [AUTH Phase 3 fix — E2E-found] The ORIGINAL 401 request must retry DIRECTLY after its own
// refresh; only requests arriving WHILE a refresh is in flight get queued. The previous code
// pushed the triggering request into the queue AFTER processQueue() had already drained it →
// the promise never resolved → every first-401 call hung forever.
apiClient.interceptors.response.use(
  (response) => response,
  async (error: AxiosError) => {
    const originalRequest = error.config as InternalAxiosRequestConfig & { _retry?: boolean };
    const url = originalRequest?.url ?? '';

    // Never refresh-loop on the auth endpoints themselves (login/refresh/passkey login are
    // anonymous; a 401 there is terminal, and passkey login carries its own error surface).
    const isAuthEndpoint = url.includes('/auth/refresh') || url.includes('/auth/login') || url.includes('/auth/passkeys/login');

    if (error.response?.status === 401 && originalRequest && !originalRequest._retry && !isAuthEndpoint) {
      if (isRefreshing) {
        // A refresh is already in flight → park this request; it is retried by processQueue.
        return new Promise((resolve, reject) => {
          failedQueue.push({
            resolve: (token: string) => {
              if (originalRequest.headers) {
                originalRequest.headers.Authorization = `Bearer ${token}`;
              }
              resolve(apiClient(originalRequest));
            },
            reject: (err: unknown) => {
              reject(err);
            },
          });
        });
      }

      isRefreshing = true;
      originalRequest._retry = true;

      try {
        // [FIX-N6] Serialized across TABS via the Web Locks API (see refreshAccessTokenCrossTab):
        // two tabs refreshing the same cookie concurrently is read by the server as token reuse and
        // revokes the whole session family. Within this tab the isRefreshing flag still dedupes.
        const refreshed = await refreshAccessTokenCrossTab();
        if (refreshed) {
          const newToken = getToken();
          processQueue(null, newToken);
          // Retry the triggering request directly with the fresh token.
          if (originalRequest.headers) {
            originalRequest.headers.Authorization = `Bearer ${newToken}`;
          }
          return apiClient(originalRequest);
        }
        processQueue(new Error('Session expired'), null);
        void logout();
        return Promise.reject(error);
      } catch (refreshError) {
        processQueue(refreshError, null);
        void logout();
        return Promise.reject(refreshError);
      } finally {
        isRefreshing = false;
      }
    }

    if (error.response?.status === 403) {
      console.warn('Access denied (403)');
      return Promise.reject(error);
    }

    return Promise.reject(error);
  }
);

// Re-export for callers that gate on the forced-password-change state.
export { mustChangePassword };

export default apiClient;
