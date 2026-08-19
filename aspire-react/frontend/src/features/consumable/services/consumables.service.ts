import apiClient from '../../../services/api-client';

export interface Consumable {
  id: string; name: string; itemNo?: string; qty: number; minAmt: number;
  remaining: number; isLowStock: boolean;
  status?: number | string; // 1/Pending or "Pending", 2/Confirmed or "Confirmed"
  companyId?: string | null;
  companyName?: string | null;
  category?: { id: string; name: string } | null;
  location?: { id: string; name: string } | null;
}

export const consumablesApi = {
  list: (params?: Record<string, unknown>) => apiClient.get('/consumables', { params }),
  get: (id: string) => apiClient.get(`/consumables/${id}`),
  create: (data: Record<string, unknown>) => apiClient.post('/consumables', data),
  update: (id: string, data: Record<string, unknown>) => apiClient.put(`/consumables/${id}`, data),
  delete: (id: string) => apiClient.delete(`/consumables/${id}`),
  checkout: (id: string, data: Record<string, unknown>) => apiClient.post(`/consumables/${id}/checkout`, data),
  lowStock: () => apiClient.get('/consumables/low-stock'),
};