import apiClient from '../../../services/api-client';
import type { GroupPermissionEntry } from '../types/groups';

export interface GroupNamePayload {
  name: string;
  description?: string | null;
}

export const groupsApi = {
  list: () => apiClient.get('/groups'),
  get: (id: string) => apiClient.get(`/groups/${id}`),
  /** Full permission catalog grouped by resource (single source of truth = backend). */
  getCatalog: () => apiClient.get('/permissions'),
  create: (data: GroupNamePayload) => apiClient.post('/groups', data),
  update: (id: string, data: GroupNamePayload) => apiClient.put(`/groups/${id}`, data),
  delete: (id: string) => apiClient.delete(`/groups/${id}`),
  updatePermissions: (id: string, permissions: GroupPermissionEntry[]) =>
    apiClient.put(`/groups/${id}/permissions`, permissions),
};
