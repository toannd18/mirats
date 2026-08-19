import apiClient from '../../../services/api-client';

export type LicenseSeatTargetType = 'User' | 'Asset' | 'SystemInfo';

export interface LicenseListItem {
  id: string;
  name: string;
  serial: string | null;
  seats: number;
  reassignable: boolean;
  expirationDate: string | null;
  terminationDate: string | null;
  minSeats: number | null;
  assignedSeats: number;
  availableSeats: number;
  expiringSoon: boolean;
  isExpired: boolean;
  isLowSeats: boolean;
  category: { id: string; name: string } | null;
  company: { id: string; name: string } | null;
  supplier: { id: string; name: string } | null;
  manufacturer: { id: string; name: string } | null;
}

export interface LicenseSeatDto {
  id: string;
  seatNumber: number;
  assigned: boolean;
  targetType: LicenseSeatTargetType | null;
  user: { id: string; name: string } | null;
  asset: { id: string; assetTag: string; name: string } | null;
  systemInfo: { id: string; code: string; name: string } | null;
  note: string | null;
  assignedAt: string | null;
}

export interface LicenseDetailDto extends LicenseListItem {
  purchaseCost: number | null;
  purchaseDate: string | null;
  orderNumber: string | null;
  notes: string | null;
  supplierId: string | null;
  manufacturerId: string | null;
  categoryId: string | null;
  companyId: string | null;
  seatDetails: LicenseSeatDto[];
}

export interface CreateLicensePayload {
  name: string;
  serial?: string | null;
  seats: number;
  reassignable?: boolean;
  expirationDate?: string | null;
  terminationDate?: string | null;
  purchaseCost?: number | null;
  purchaseDate?: string | null;
  orderNumber?: string | null;
  minSeats?: number | null;
  notes?: string | null;
  supplierId?: string | null;
  manufacturerId?: string | null;
  categoryId?: string | null;
  companyId?: string | null;
}

export type UpdateLicensePayload = Partial<CreateLicensePayload>;

export interface LicenseUsageRow {
  licenseId: string;
  licenseName: string;
  serial: string | null;
  seatNumber: number;
  assignedAt: string | null;
  note: string | null;
  expirationDate: string | null;
  expiringSoon: boolean;
  isExpired: boolean;
  company: { id: string; name: string } | null;
}

export interface CheckoutLicensePayload {
  seatId?: string | null;
  targetType: LicenseSeatTargetType;
  targetId: string;
  note?: string | null;
}

export const licensesApi = {
  list: (params?: Record<string, unknown>) => apiClient.get('/licenses', { params }),
  get: (id: string) => apiClient.get(`/licenses/${id}`),
  forAsset: (assetId: string) => apiClient.get(`/licenses/for-asset/${assetId}`),
  forSystem: (systemInfoId: string) => apiClient.get(`/licenses/for-system/${systemInfoId}`),
  forUser: (userId: string) => apiClient.get(`/licenses/for-user/${userId}`),
  create: (data: CreateLicensePayload) => apiClient.post('/licenses', data),
  update: (id: string, data: UpdateLicensePayload) => apiClient.put(`/licenses/${id}`, data),
  delete: (id: string) => apiClient.delete(`/licenses/${id}`),
  checkout: (id: string, data: CheckoutLicensePayload) => apiClient.post(`/licenses/${id}/checkout`, data),
  checkin: (id: string, seatId: string) => apiClient.post(`/licenses/${id}/checkin`, { seatId }),
};