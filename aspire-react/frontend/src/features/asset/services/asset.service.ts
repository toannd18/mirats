import apiClient from '../../../services/api-client';

export interface CreateAssetPayload {
  assetTag: string;
  name: string;
  serial?: string;
  modelId?: string;
  locationId?: string;
  supplierId?: string;
  companyId?: string;
  purchaseCost?: number;
  purchaseDate?: string;
  warrantyMonths?: number;
  orderNumber?: string;
  notes?: string;
  image?: string;
}

export interface UpdateAssetPayload {
  assetTag?: string;
  name?: string;
  serial?: string | null;
  modelId?: string;
  locationId?: string;
  supplierId?: string;
  companyId?: string;
  purchaseCost?: number | null;
  purchaseDate?: string | null;
  warrantyMonths?: number | null;
  orderNumber?: string | null;
  physical?: boolean;
  requestable?: boolean;
  notes?: string | null;
  image?: string;
}

export interface AllocateAssetPayload {
  targetType: number; // 1=User, 2=Department, 3=SystemPosition
  targetId: string;
  locationId?: string; // Required for SystemPosition
  note?: string;
  checkoutAt?: string;
}

export interface RecallAssetPayload {
  locationId: string;
  note?: string;
}

// ─── Asset Maintenance (Snipe-IT style) ───

export interface AssetMaintenanceDto {
  id: string;
  type: string; // AssetMaintenanceType enum name
  title: string;
  notes: string | null;
  startDate: string;
  completionDate: string | null;
  cost: number | null;
  isWarranty: boolean;
  companyId: string; // access-control company (server-set, locked)
  supplier: { id: string; name: string } | null;
  asset: { id: string; assetTag: string; name: string; companyName: string | null } | null;
  // Immutable context snapshot captured at creation time
  snapshotSystemInfoId: string | null;
  snapshotSystemInfoName: string | null;
  snapshotSystemPositionId: string | null;
  snapshotSystemPositionName: string | null;
  snapshotLocationId: string | null;
  snapshotLocationName: string | null;
  snapshotAssignedUserId: string | null;
  snapshotAssignedUserName: string | null;
  snapshotDepartmentId: string | null;
  snapshotDepartmentName: string | null;
  // Audit-trail lock (close/reopen)
  isClosed: boolean;
  closedAt: string | null;
  closedById: string | null;
  // Independent inspection step (Hoàn thành → Kiểm tra → Đóng)
  inspectedById: string | null;
  inspectedAt: string | null;
  inspectedByName: string | null;
  // Assigned maintenance workers (max 5)
  assignees: { userId: string; name: string; assignedAt: string }[];
  // LIVE context of the asset at the moment of the request (only returned by the detail endpoint)
  currentContext?: MaintenanceCurrentContext;
  createdAt: string;
  updatedAt: string;
}

// Live context computed on the fly from the asset's current state — NOT a stored snapshot.
// Compare IDs with the Snapshot* fields to see what changed since the maintenance was created.
export interface MaintenanceCurrentContext {
  systemInfoId: string | null;
  systemInfoName: string | null;
  systemPositionId: string | null;
  systemPositionName: string | null;
  locationId: string | null;
  locationName: string | null;
  assignedUserId: string | null;
  assignedUserName: string | null;
  departmentId: string | null;
  departmentName: string | null;
}

export interface CreateMaintenancePayload {
  type: number; // AssetMaintenanceType enum value
  title: string;
  notes?: string;
  supplierId?: string;
  startDate: string;
  completionDate?: string | null;
  cost?: number | null;
  isWarranty: boolean;
  assigneeUserIds?: string[];
}

export type CreateMaintenanceForAssetPayload = CreateMaintenancePayload & { assetId: string };

export interface UpdateMaintenancePayload {
  title?: string;
  notes?: string | null;
  type?: number;
  supplierId?: string | null;
  completionDate?: string | null;
  cost?: number | null;
  isWarranty?: boolean;
  assigneeUserIds?: string[];
}

export const assetService = {
  list: (params?: Record<string, unknown>) => apiClient.get('/assets', { params }),
  get: (id: string) => apiClient.get(`/assets/${id}`),
  create: (data: CreateAssetPayload) => apiClient.post('/assets', data),
  update: (id: string, data: UpdateAssetPayload) => apiClient.put(`/assets/${id}`, data),
  confirm: (id: string) => apiClient.post(`/assets/${id}/confirm`),
  allocate: (id: string, data: AllocateAssetPayload) => apiClient.post(`/assets/${id}/checkout`, data),
  recall: (id: string, data: RecallAssetPayload) => apiClient.post(`/assets/${id}/checkin`, data),
  archive: (id: string, data: { locationId: string; note?: string }) => apiClient.post(`/assets/${id}/archive`, data),
  unarchive: (id: string) => apiClient.post(`/assets/${id}/unarchive`, {}),
  // Maintenance
  listAllMaintenances: (params?: Record<string, unknown>) => apiClient.get('/maintenances', { params }),
  getMaintenance: (id: string) => apiClient.get(`/maintenances/${id}`),
  createMaintenance: (assetId: string, data: CreateMaintenancePayload) => apiClient.post(`/assets/${assetId}/maintenances`, data),
  createMaintenanceForAsset: (data: CreateMaintenanceForAssetPayload) => apiClient.post('/maintenances', data),
  updateMaintenance: (id: string, data: UpdateMaintenancePayload) => apiClient.put(`/maintenances/${id}`, data),
  deleteMaintenance: (id: string) => apiClient.delete(`/maintenances/${id}`),
  inspectMaintenance: (id: string) => apiClient.post(`/maintenances/${id}/inspect`),
  closeMaintenance: (id: string) => apiClient.post(`/maintenances/${id}/close`),
  reopenMaintenance: (id: string) => apiClient.post(`/maintenances/${id}/reopen`),
};