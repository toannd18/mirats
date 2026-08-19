import axios, { AxiosError } from 'axios';
import type { InternalAxiosRequestConfig } from 'axios';
import { logout } from './keycloak';
import keycloak from './keycloak';

// Backend API base URL — can be overridden via VITE_API_BASE_URL env variable.
// Semantics: it is the SERVER base (origin or path). The `/api/v1` prefix is appended
// here — UNLESS the base already ends with it (e.g. prod `VITE_API_BASE_URL=/api/v1`
// via compose build arg must NOT become `/api/v1/api/v1`).
const API_BASE = (import.meta as any).env?.VITE_API_BASE_URL ?? 'http://localhost:5428';
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

// Request interceptor — proactively refresh token before each request
apiClient.interceptors.request.use(
  async (config: InternalAxiosRequestConfig) => {
    try {
      // Ensure the Keycloak adapter is initialized
      if (!keycloak.authenticated) return config;

      // Refresh token if it will expire within 30 seconds.
      // updateToken(30) returns a Promise that resolves when refresh is complete
      // and rejects if the refresh fails (e.g., session expired).
      const refreshed = await keycloak.updateToken(30);

      if (refreshed) {
        console.debug('Keycloak token refreshed proactively');
      }

      const token = keycloak.token;
      if (token && config.headers) {
        config.headers.Authorization = `Bearer ${token}`;
      }
    } catch (err) {
      // Token refresh failed — session likely expired
      console.warn('Token refresh failed, redirecting to login...', err);
      logout();
      return Promise.reject(err);
    }

    return config;
  },
  (error) => Promise.reject(error)
);

// Response interceptor — handle 401 with auto-retry after re-authentication
apiClient.interceptors.response.use(
  (response) => response,
  async (error: AxiosError) => {
    const originalRequest = error.config as InternalAxiosRequestConfig & { _retry?: boolean };

    if (error.response?.status === 401) {
      // If we already retried once, don't loop
      if (originalRequest._retry) {
        console.warn('API returned 401 after token refresh. Redirecting to login.');
        logout();
        return Promise.reject(error);
      }

      // Attempt to refresh the token and retry once
      if (!isRefreshing) {
        isRefreshing = true;
        originalRequest._retry = true;

        try {
          const refreshed = await keycloak.updateToken(30);
          if (refreshed) {
            const newToken = keycloak.token;
            processQueue(null, newToken!);
          } else {
            processQueue(null, keycloak.token!);
          }
        } catch (refreshError) {
          processQueue(refreshError, null);
          logout();
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

export default apiClient;