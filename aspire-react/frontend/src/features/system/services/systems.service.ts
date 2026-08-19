import apiClient from '../../../services/api-client';

// ─── System Detail (SystemDetailPage) ───

export interface SystemPositionDto {
  id: string;
  code: string;
  name: string;
  description?: string | null;
}

export interface SystemInfoDetailDto {
  id: string;
  code: string;
  name: string;
  description?: string | null;
  companyId?: string | null;
  company?: { id: string; name: string } | null;
  positions: SystemPositionDto[];
}

export interface SystemAssetDto {
  id: string;
  assetTag: string;
  name: string;
  serial: string | null;
  status: string;
  systemPosition: { id: string; code: string; name: string } | null;
  location: { id: string; name: string } | null;
  company: { id: string; name: string } | null;
  assignedTo: { type: string; targetId: string; name: string | null } | null;
  department: { id: string; name: string } | null;
}

export interface SystemAccessoryDto {
  id: string;
  accessoryId: string;
  accessoryName: string;
  accessoryItemNo: string | null;
  assignedQty: number;
  returnedQty: number;
  remainingCheckedOut: number;
  systemPosition: { id: string; code: string; name: string } | null;
  note: string | null;
  checkedOutAt: string;
  createdByUserId: string | null;
  createdByName: string | null;
}

export const systemsService = {
  get: (id: string) => apiClient.get(`/system-infos/${id}`),
  listAssets: (id: string, params?: Record<string, unknown>) => apiClient.get(`/systems/${id}/assets`, { params }),
  listAccessories: (id: string, params?: Record<string, unknown>) => apiClient.get(`/systems/${id}/accessories`, { params }),
};
