import axios, { AxiosError } from 'axios';
import type { InternalAxiosRequestConfig } from 'axios';
import { getToken, logout, refreshAccessToken, mustChangePassword } from '../features/auth/services/auth';

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
apiClient.interceptors.response.use(
  (response) => response,
  async (error: AxiosError) => {
    const originalRequest = error.config as InternalAxiosRequestConfig & { _retry?: boolean };

    if (error.response?.status === 401) {
      if (originalRequest._retry) {
        console.warn('API returned 401 after token refresh. Redirecting to login.');
        void logout();
        return Promise.reject(error);
      }

      if (!isRefreshing) {
        isRefreshing = true;
        originalRequest._retry = true;

        try {
          const refreshed = await refreshAccessToken();
          if (refreshed) {
            const newToken = getToken();
            processQueue(null, newToken);
          } else {
            processQueue(null, null);
          }
        } catch (refreshError) {
          processQueue(refreshError, null);
          void logout();
          return Promise.reject(refreshError);
        } finally {
          isRefreshing = false;
        }
      }

      // Queue this request while refresh is in progress, then retry
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
