import apiClient from '../../../services/api-client';

export type TrackingType = 'Bulk' | 'Serial';
export type ComponentUnitStatus = 'InStock' | 'Allocated' | 'Damaged' | 'Disposed';

export interface ComponentUnitDto {
  id: string;
  serialNo: string | null;
  status: ComponentUnitStatus;
  currentAssetId: string | null;
  notes: string | null;
  createdAt: string;
  updatedAt: string;
  currentAsset: { id: string; assetTag: string; name: string } | null;
}

export interface ComponentDto {
  id: string;
  name: string;
  serial: string | null;
  itemNo?: string | null;
  qty: number;
  minAmt: number;
  trackingType: TrackingType;
  remaining: number;
  isLowStock: boolean;
  modelNumber: string | null;
  orderNumber: string | null;
  purchaseCost: number | null;
  purchaseDate: string | null;
  notes: string | null;
  updatedAt: string;
  canDelete?: boolean;
  unitsSummary: { inStock: number; allocated: number; damaged: number; disposed: number };
  category: { id: string; name: string } | null;
  company: { id: string; name: string } | null;
  location: { id: string; name: string } | null;
  supplier: { id: string; name: string } | null;
  manufacturer: { id: string; name: string } | null;
  units?: ComponentUnitDto[];
  assignments?: Array<{
    id: string;
    assignedQty: number;
    note: string | null;
    asset: { id: string; assetTag: string; name: string };
  }>;
}

export interface CreateComponentPayload {
  name: string;
  serial?: string;
  qty?: number;
  minAmt?: number;
  categoryId?: string;
  locationId?: string;
  companyId?: string;
  supplierId?: string;
  manufacturerId?: string;
  modelNumber?: string;
  orderNumber?: string;
  purchaseCost?: number;
  purchaseDate?: string;
  notes?: string;
  trackingType: TrackingType;
  serialNumbers?: string[];
}

export const componentsApi = {
  list: (params?: Record<string, unknown>) => apiClient.get('/components', { params }),
  get: (id: string) => apiClient.get(`/components/${id}`),
  create: (data: CreateComponentPayload) => apiClient.post('/components', data),
  update: (id: string, data: Record<string, unknown>) => apiClient.put(`/components/${id}`, data),
  delete: (id: string) => apiClient.delete(`/components/${id}`),

  // === Serial & Bulk unified endpoints ===
  listUnits: (id: string, params?: Record<string, unknown>) => apiClient.get(`/components/${id}/units`, { params }),
  stockInUnits: (id: string, data: { serialNumbers: string[]; note?: string }) => apiClient.post(`/components/${id}/units`, data),
  checkout: (id: string, data: { assetId: string; quantity?: number; serialNo?: string; note?: string }) =>
    apiClient.post(`/components/${id}/checkout`, data),
  checkin: (id: string, data: { assetId?: string; quantity?: number; serialNo?: string; note?: string }) =>
    apiClient.post(`/components/${id}/checkin`, data),
  updateUnitStatus: (unitId: string, data: { status: ComponentUnitStatus; note?: string }) =>
    apiClient.patch(`/component-units/${unitId}`, data),
  deleteUnit: (unitId: string) => apiClient.delete(`/component-units/${unitId}`),
  getActionLogs: (id: string, params?: Record<string, unknown>) => apiClient.get(`/components/${id}/action-logs`, { params }),
  getUnitActionLogs: (unitId: string, params?: Record<string, unknown>) => apiClient.get(`/component-units/${unitId}/action-logs`, { params }),
};
