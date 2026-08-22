import apiClient from '../../../services/api-client';

export interface AccessoryDto {
  id: string;
  name: string;
  itemNo: string | null;
  notes: string | null;
  qty: number;
  minAmt: number;
  remaining: number;
  checkedOutQty: number;
  isLowStock: boolean;
  companyId: string | null;
  companyName: string | null;
  category: { id: string; name: string } | null;
  location: { id: string; name: string } | null;
}

export interface AccessoryDetail {
  id: string;
  name: string;
  itemNo: string | null;
  qty: number;
  minAmt: number;
  remaining: number;
  percentRemaining: number;
  isLowStock: boolean;
  checkedOutQty: number;
  modelNumber: string | null;
  orderNumber: string | null;
  purchaseDate: string | null;
  purchaseCost: number | null;
  notes: string | null;
  categoryId: string | null;
  manufacturerId: string | null;
  supplierId: string | null;
  locationId: string | null;
  companyId: string | null;
  category: { id: string; name: string } | null;
  manufacturer: { id: string; name: string } | null;
  supplier: { id: string; name: string } | null;
  location: { id: string; name: string } | null;
  company: { id: string; name: string } | null;
}

export interface AccessoryCheckoutDto {
  id: string;
  accessoryId: string;
  checkoutType: number | string; // 1=User, 2=Department, 3=Location, 4=SystemPosition (or enum string)
  targetId: string;
  targetName: string | null;
  assignedQty: number;
  returnedQty: number;
  remainingOut: number;
  note: string | null;
  checkedOutAt: string;
  createdByUserId: string | null;
  createdByName: string | null;
  createdByFirstName: string | null;
  createdByLastName: string | null;
}

const CHECKOUT_TYPE_BY_NAME: Record<string, number> = {
  User: 1,
  Department: 2,
  Location: 3,
  SystemPosition: 4,
};

const CHECKOUT_TYPE_LABEL_BY_VALUE: Record<number, string> = {
  1: 'Người dùng',
  2: 'Phòng ban',
  3: 'Vị trí',
  4: 'Hệ thống',
};

const CHECKOUT_TYPE_COLOR_BY_VALUE: Record<number, string> = {
  1: 'blue',
  2: 'cyan',
  3: 'green',
  4: 'purple',
};

/**
 * Backend serializes enums as strings (JsonStringEnumConverter), e.g. "SystemPosition".
 * Normalize a `number | string` checkoutType to its display label/color.
 */
export function checkoutTypeToLabel(checkoutType: number | string | null | undefined): string {
  if (checkoutType == null) return 'N/A';
  const value = typeof checkoutType === 'number' ? checkoutType : CHECKOUT_TYPE_BY_NAME[checkoutType];
  return value != null ? (CHECKOUT_TYPE_LABEL_BY_VALUE[value] ?? 'N/A') : 'N/A';
}

export function checkoutTypeToColor(checkoutType: number | string | null | undefined): string {
  if (checkoutType == null) return 'default';
  const value = typeof checkoutType === 'number' ? checkoutType : CHECKOUT_TYPE_BY_NAME[checkoutType];
  return value != null ? (CHECKOUT_TYPE_COLOR_BY_VALUE[value] ?? 'default') : 'default';
}

export interface CheckoutRequest {
  checkoutType: number;
  targetId: string;
  quantity: number;
  note?: string | null;
}

export interface CheckinRequest {
  returnQty: number;
  note?: string | null;
}

export const accessoriesApi = {
  list: (params?: Record<string, unknown>) => apiClient.get('/accessories', { params }),
  get: (id: string) => apiClient.get(`/accessories/${id}`),
  create: (data: Record<string, unknown>) => apiClient.post('/accessories', data),
  update: (id: string, data: Record<string, unknown>) => apiClient.put(`/accessories/${id}`, data),
  delete: (id: string) => apiClient.delete(`/accessories/${id}`),
  checkout: (id: string, data: CheckoutRequest) => apiClient.post(`/accessories/${id}/checkout`, data),
  checkin: (checkoutId: string, data: CheckinRequest) => apiClient.post(`/accessories/checkouts/${checkoutId}/checkin`, data),
  getCheckouts: (id: string) => apiClient.get(`/accessories/${id}/checkouts`),
  getLogs: (id: string) => apiClient.get('/action-logs', { params: { itemType: 3, itemId: id } }),
};