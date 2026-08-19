-- ============================================================================
-- MIGRATION: Asset Maintenance (Snipe-IT style) + immutable context snapshot
--   * asset_maintenances table (maintenance/repair/upgrade records)
--   * snapshot of the asset context AT CREATION TIME: SystemInfo + SystemPosition
--     (BOTH levels, SystemPosition is a child of SystemInfo), Location, User, Department
--   * v2 (2026-08-11): CompanyId (access control, non-nullable, server-set = Asset.CompanyId
--     at creation, LOCKED afterwards; Guid.Empty = floater asset visible to everyone)
--   * v3 (2026-08-11): Close / Lock (audit-trail protection) — IsClosed (bool, default false),
--     ClosedAt (timestamp), ClosedById (uuid). Closing freezes the record against ALL edits
--     (PUT → 400 MAINTENANCE_CLOSED). Close requires CompletionDate (else MAINTENANCE_NOT_COMPLETED_YET);
--     Reopen is Superuser-only and keeps ClosedAt/ClosedById as most-recent-close history.
--   * v4 (2026-08-12): Independent inspection step + assignees
--     - InspectedById / InspectedAt (uuid/timestamp NULL) — workflow Hoàn thành → Kiểm tra → Đóng.
--       Close now requires BOTH CompletionDate AND InspectedById (else MAINTENANCE_NOT_INSPECTED_YET).
--     - asset_maintenance_assignees table: many-to-many maintenance workers (max 5 enforced at the
--       API layer → 400 MAX_5_ASSIGNEES), unique (MaintenanceId, UserId), replace-all via PUT,
--       immutable once the record is closed.
-- ============================================================================
-- Project convention: schema applied on startup via raw SQL in Program.cs
-- (EnsureCreated + self-heal). `dotnet ef` is NOT used.
-- NOTE: PostgreSQL has no "ADD CONSTRAINT IF NOT EXISTS" — use DROP + ADD (idempotent).

-- ==================== UP ====================

CREATE TABLE IF NOT EXISTS asset_maintenances (
    "Id"                          uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    "AssetId"                     uuid NOT NULL,
    "Type"                        integer NOT NULL,
    "Title"                       text NOT NULL,
    "Notes"                       text NULL,
    "SupplierId"                  uuid NULL,
    "CompanyId"                   uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000',
    "StartDate"                   timestamp NOT NULL,
    "CompletionDate"              timestamp NULL,
    "Cost"                        numeric NULL,
    "IsWarranty"                  boolean NOT NULL DEFAULT false,
    "SnapshotSystemInfoId"        uuid NULL,
    "SnapshotSystemInfoName"      text NULL,
    "SnapshotSystemPositionId"    uuid NULL,
    "SnapshotSystemPositionName"  text NULL,
    "SnapshotLocationId"          uuid NULL,
    "SnapshotLocationName"        text NULL,
    "SnapshotAssignedUserId"      uuid NULL,
    "SnapshotAssignedUserName"    text NULL,
    "SnapshotDepartmentId"        uuid NULL,
    "SnapshotDepartmentName"      text NULL,
    "CreatedById"                 uuid NOT NULL,
    "CreatedAt"                   timestamp NOT NULL,
    "UpdatedAt"                   timestamp NOT NULL,
    "DeletedAt"                   timestamp NULL,
    "IsClosed"                    boolean NOT NULL DEFAULT false,
    "ClosedAt"                    timestamp NULL,
    "ClosedById"                  uuid NULL
);

CREATE INDEX IF NOT EXISTS "IX_asset_maintenances_AssetId" ON asset_maintenances ("AssetId");
CREATE INDEX IF NOT EXISTS "IX_asset_maintenances_CompanyId" ON asset_maintenances ("CompanyId");
CREATE INDEX IF NOT EXISTS "IX_asset_maintenances_SnapshotSystemInfoId" ON asset_maintenances ("SnapshotSystemInfoId");
CREATE INDEX IF NOT EXISTS "IX_asset_maintenances_SnapshotSystemPositionId" ON asset_maintenances ("SnapshotSystemPositionId");

ALTER TABLE asset_maintenances DROP CONSTRAINT IF EXISTS "FK_asset_maintenances_assets_AssetId";
ALTER TABLE asset_maintenances ADD CONSTRAINT "FK_asset_maintenances_assets_AssetId"
    FOREIGN KEY ("AssetId") REFERENCES assets ("Id") ON DELETE CASCADE;

ALTER TABLE asset_maintenances DROP CONSTRAINT IF EXISTS "FK_asset_maintenances_suppliers_SupplierId";
ALTER TABLE asset_maintenances ADD CONSTRAINT "FK_asset_maintenances_suppliers_SupplierId"
    FOREIGN KEY ("SupplierId") REFERENCES suppliers ("Id") ON DELETE SET NULL;

-- v3 self-heal for pre-existing tables (idempotent — also applied by Program.cs at startup):
ALTER TABLE asset_maintenances ADD COLUMN IF NOT EXISTS "IsClosed"   boolean NOT NULL DEFAULT false;
ALTER TABLE asset_maintenances ADD COLUMN IF NOT EXISTS "ClosedAt"   timestamp NULL;
ALTER TABLE asset_maintenances ADD COLUMN IF NOT EXISTS "ClosedById" uuid NULL;

-- v4 self-heal (idempotent — also applied by Program.cs at startup):
ALTER TABLE asset_maintenances ADD COLUMN IF NOT EXISTS "InspectedById" uuid NULL;
ALTER TABLE asset_maintenances ADD COLUMN IF NOT EXISTS "InspectedAt"   timestamp NULL;

CREATE TABLE IF NOT EXISTS asset_maintenance_assignees (
    "Id"            uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    "MaintenanceId" uuid NOT NULL,
    "UserId"        uuid NOT NULL,
    "AssignedAt"    timestamp NOT NULL DEFAULT CURRENT_TIMESTAMP
);
CREATE UNIQUE INDEX IF NOT EXISTS "IX_asset_maintenance_assignees_MaintenanceId_UserId"
    ON asset_maintenance_assignees ("MaintenanceId", "UserId");
CREATE INDEX IF NOT EXISTS "IX_asset_maintenance_assignees_MaintenanceId"
    ON asset_maintenance_assignees ("MaintenanceId");

ALTER TABLE asset_maintenance_assignees DROP CONSTRAINT IF EXISTS "FK_asset_maintenance_assignees_maintenances_MaintenanceId";
ALTER TABLE asset_maintenance_assignees ADD CONSTRAINT "FK_asset_maintenance_assignees_maintenances_MaintenanceId"
    FOREIGN KEY ("MaintenanceId") REFERENCES asset_maintenances ("Id") ON DELETE CASCADE;
ALTER TABLE asset_maintenance_assignees DROP CONSTRAINT IF EXISTS "FK_asset_maintenance_assignees_users_UserId";
ALTER TABLE asset_maintenance_assignees ADD CONSTRAINT "FK_asset_maintenance_assignees_users_UserId"
    FOREIGN KEY ("UserId") REFERENCES users ("Id") ON DELETE RESTRICT;

ALTER TABLE asset_maintenances DROP CONSTRAINT IF EXISTS "FK_asset_maintenances_users_InspectedById";
ALTER TABLE asset_maintenances ADD CONSTRAINT "FK_asset_maintenances_users_InspectedById"
    FOREIGN KEY ("InspectedById") REFERENCES users ("Id") ON DELETE SET NULL;

-- ==================== DOWN (rollback) ====================
-- ALTER TABLE asset_maintenance_assignees DROP CONSTRAINT IF EXISTS "FK_asset_maintenance_assignees_users_UserId";
-- ALTER TABLE asset_maintenance_assignees DROP CONSTRAINT IF EXISTS "FK_asset_maintenance_assignees_maintenances_MaintenanceId";
-- DROP TABLE IF EXISTS asset_maintenance_assignees;
-- ALTER TABLE asset_maintenances DROP CONSTRAINT IF EXISTS "FK_asset_maintenances_users_InspectedById";
-- ALTER TABLE asset_maintenances DROP COLUMN IF EXISTS "InspectedAt";
-- ALTER TABLE asset_maintenances DROP COLUMN IF EXISTS "InspectedById";
-- ALTER TABLE asset_maintenances DROP CONSTRAINT IF EXISTS "FK_asset_maintenances_suppliers_SupplierId";
-- ALTER TABLE asset_maintenances DROP CONSTRAINT IF EXISTS "FK_asset_maintenances_assets_AssetId";
-- ALTER TABLE asset_maintenances DROP COLUMN IF EXISTS "IsClosed";
-- ALTER TABLE asset_maintenances DROP COLUMN IF EXISTS "ClosedAt";
-- ALTER TABLE asset_maintenances DROP COLUMN IF EXISTS "ClosedById";
-- DROP INDEX IF EXISTS "IX_asset_maintenances_CompanyId";
-- DROP TABLE IF EXISTS asset_maintenances;
-- (Chỉ chạy khi chắc chắn không còn dữ liệu cần giữ.)
