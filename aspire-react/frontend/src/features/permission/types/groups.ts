// ==================== Group / Permission Types ====================

export interface PermissionDefinition {
  code: string;
  resource: string;
  action: string;
  description: string;
}

/** Một resource (module) với danh sách permission — từ GET /api/v1/permissions. */
export interface PermissionResourceGroup {
  resource: string;
  permissions: { code: string; action: string; description: string }[];
}

export interface GroupPermissionEntry {
  permissionKey: string;
  value: number; // 1 = Grant, 0 = NotSet, -1 = Deny
}

export interface GroupDto {
  id: string;
  name: string;
  description: string | null;
  isSystem: boolean;
  createdAt: string;
  updatedAt: string;
  permissions: GroupPermissionEntry[];
  userCount: number;
}
