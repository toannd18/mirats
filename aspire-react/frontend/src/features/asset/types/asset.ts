// ==================== Asset Domain Types ====================

import { assetStatusColors } from '../../../theme/designTokens';

export type AssetStatus = 'Pending' | 'Deployed' | 'Archived';

/** Backend serializes AssetStatus as int (0/1/2) in some payloads — normalize to the string union used by the UI. */
export type AssetStatusInput = number | AssetStatus | null | undefined;

export function normalizeAssetStatus(status: AssetStatusInput): AssetStatus {
  if (status === 'Pending' || status === 0) return 'Pending';
  if (status === 'Deployed' || status === 1) return 'Deployed';
  if (status === 'Archived' || status === 2) return 'Archived';
  return 'Pending';
}

export type AllocationTargetType = 'User' | 'Department' | 'SystemPosition';

export const ASSET_STATUS_LABELS: Record<AssetStatus, string> = {
  Pending: 'Chờ cấp phát',
  Deployed: 'Đã cấp phát',
  Archived: 'Đã thu hồi',
};

// Nguồn màu trạng thái Asset là `assetStatusColors` trong theme/designTokens (Design System)
// — map chuỗi status API trả về → hex token. Re-export tại đây để các page Asset dùng chung
// 1 nguồn (trước đây có ASSET_STATUS_COLORS chép tay preset-name 'blue'/'green'/'default'
// song song với assetStatusColors hex → 2 nguồn lệch nhau).
export const ASSET_STATUS_COLORS: Record<string, string> = assetStatusColors;

export const ALLOCATION_TARGET_LABELS: Record<AllocationTargetType, string> = {
  User: 'Người dùng',
  Department: 'Phòng ban',
  SystemPosition: 'Hệ thống',
};

export interface AssetDto {
  id: string;
  assetTag: string;
  name: string;
  serial?: string | null;
  status: number | AssetStatus;
  isConfirmed: boolean;
  model?: { id: string; name: string } | null;
  category?: { id: string; name: string; tagColor?: string } | null;
  location?: { id: string; name: string } | null;
  company?: { id: string; name: string } | null;
  assignedTo?: {
    id?: string;
    type?: string;
    targetId?: string;
    name?: string;
    username?: string;
    firstName?: string;
    lastName?: string;
  } | null;
  purchaseCost?: number | null;
  purchaseDate?: string | null;
  warrantyMonths?: number | null;
  checkoutCounter: number;
  checkinCounter: number;
  lastCheckout?: string | null;
  lastCheckin?: string | null;
  notes?: string | null;
  orderNumber?: string | null;
  physical: boolean;
  requestable: boolean;
  createdAt: string;
  updatedAt: string;
}

export interface AssetDetailDto extends AssetDto {
  manufacturer?: { id: string; name: string } | null;
  supplier?: { id: string; name: string } | null;
  model?: { id: string; name: string; modelNumber?: string } | null;
  lastAuditDate?: string | null;
  nextAuditDate?: string | null;
  accepted?: string | null;
}

/**
 * Centralized action policy — reuse across List, Detail, Card, Dropdown.
 */
export type AssetAction = 'view' | 'edit' | 'allocate' | 'recall' | 'archive' | 'confirm' | 'delete' | 'unarchive';

/**
 * Asset lifecycle action policy (state machine):
 *  - Nháp          (IsConfirmed=false)       → view, edit, confirm, delete
 *  - Chờ cấp phát  (IsConfirmed=true,Pending) → view, edit, allocate, archive
 *  - Đã cấp phát   (IsConfirmed=true,Deployed)→ view, edit, recall
 *  - Đã lưu trữ    (Status=Archived)          → view, unarchive
 * Check-in returns an asset to Pending — it never archives the asset.
 */
export function getAssetActions(asset: { status: number | AssetStatus; isConfirmed: boolean; assignedTo?: unknown }): AssetAction[] {
  const s = normalizeAssetStatus(asset.status);

  // Nháp — chưa xác nhận
  if (!asset.isConfirmed) return ['view', 'edit', 'confirm', 'delete'];

  // Đã lưu trữ — terminal + mở lại
  if (s === 'Archived') return ['view', 'unarchive'];

  // Đã cấp phát
  if (s === 'Deployed') return ['view', 'edit', 'recall'];

  // Chờ cấp phát — đã xác nhận
  return ['view', 'edit', 'allocate', 'archive'];
}