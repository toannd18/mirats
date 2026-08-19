-- ST6d — Step 2: Align live schema with EF model (pre-baseline convergence)
-- Date: 2026-08-14
-- Applies: aspire-react-db (DB làm chuẩn, model đã sửa khớp DB trong AppDbContext.cs)
-- Note: Type/Default drift được sửa theo hướng MODEL→DB (AppDbContext.cs). Phần này CHỈ bổ sung
-- FK + index + dọn orphan — KHÔNG có ALTER TYPE nào tác động dữ liệu production.

-- ==================== UP ====================
BEGIN;

-- 1) Dọn orphan CreatedByUserId (trỏ user đã bị xóa) → NULL (khớp SET NULL semantics)
UPDATE accessory_checkouts ac
SET "CreatedByUserId" = NULL
WHERE ac."CreatedByUserId" IS NOT NULL
  AND NOT EXISTS (SELECT 1 FROM users u WHERE u."Id" = ac."CreatedByUserId");

UPDATE consumable_checkouts cc
SET "CreatedByUserId" = NULL
WHERE cc."CreatedByUserId" IS NOT NULL
  AND NOT EXISTS (SELECT 1 FROM users u WHERE u."Id" = cc."CreatedByUserId");

-- 2) Bổ sung 6 FK model-yêu-cầu (Postgres không có ADD CONSTRAINT IF NOT EXISTS → DO block)
DO $$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname='FK_assets_system_positions_SystemPositionId')
  THEN ALTER TABLE assets ADD CONSTRAINT "FK_assets_system_positions_SystemPositionId"
       FOREIGN KEY ("SystemPositionId") REFERENCES system_positions ("Id"); END IF;
  IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname='FK_assets_assignments_CurrentAssignmentId')
  THEN ALTER TABLE assets ADD CONSTRAINT "FK_assets_assignments_CurrentAssignmentId"
       FOREIGN KEY ("CurrentAssignmentId") REFERENCES assignments ("Id") ON DELETE SET NULL; END IF;
  IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname='FK_accessory_checkouts_users_CreatedByUserId')
  THEN ALTER TABLE accessory_checkouts ADD CONSTRAINT "FK_accessory_checkouts_users_CreatedByUserId"
       FOREIGN KEY ("CreatedByUserId") REFERENCES users ("Id") ON DELETE SET NULL; END IF;
  IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname='FK_consumable_checkouts_users_CreatedByUserId')
  THEN ALTER TABLE consumable_checkouts ADD CONSTRAINT "FK_consumable_checkouts_users_CreatedByUserId"
       FOREIGN KEY ("CreatedByUserId") REFERENCES users ("Id") ON DELETE SET NULL; END IF;
  IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname='FK_users_departments_DepartmentId')
  THEN ALTER TABLE users ADD CONSTRAINT "FK_users_departments_DepartmentId"
       FOREIGN KEY ("DepartmentId") REFERENCES departments ("Id") ON DELETE SET NULL; END IF;
  IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname='FK_users_locations_LocationId')
  THEN ALTER TABLE users ADD CONSTRAINT "FK_users_locations_LocationId"
       FOREIGN KEY ("LocationId") REFERENCES locations ("Id") ON DELETE SET NULL; END IF;
END $$;
-- 3) Bổ sung 20 index (5 unique) model-yêu-cầu
CREATE UNIQUE INDEX IF NOT EXISTS "IX_manufacturers_Code" ON manufacturers ("Code");
CREATE UNIQUE INDEX IF NOT EXISTS "IX_manufacturers_Name" ON manufacturers ("Name");
CREATE UNIQUE INDEX IF NOT EXISTS "IX_suppliers_Code" ON suppliers ("Code");
CREATE UNIQUE INDEX IF NOT EXISTS "IX_suppliers_Name" ON suppliers ("Name");
CREATE UNIQUE INDEX IF NOT EXISTS "IX_users_Email" ON users ("Email");
CREATE INDEX IF NOT EXISTS "IX_accessory_checkouts_CreatedByUserId" ON accessory_checkouts ("CreatedByUserId");
CREATE INDEX IF NOT EXISTS "IX_asset_maintenance_assignees_UserId" ON asset_maintenance_assignees ("UserId");
CREATE INDEX IF NOT EXISTS "IX_asset_maintenances_InspectedById" ON asset_maintenances ("InspectedById");
CREATE INDEX IF NOT EXISTS "IX_asset_maintenances_SupplierId" ON asset_maintenances ("SupplierId");
CREATE INDEX IF NOT EXISTS "IX_assets_SystemPositionId" ON assets ("SystemPositionId");
CREATE INDEX IF NOT EXISTS "IX_consumable_checkouts_CreatedByUserId" ON consumable_checkouts ("CreatedByUserId");
CREATE INDEX IF NOT EXISTS "IX_departments_CompanyId" ON departments ("CompanyId");
CREATE INDEX IF NOT EXISTS "IX_departments_ManagerId" ON departments ("ManagerId");
CREATE INDEX IF NOT EXISTS "IX_license_seats_SystemPositionId" ON license_seats ("SystemPositionId");
CREATE INDEX IF NOT EXISTS "IX_licenses_Name" ON licenses ("Name");
CREATE INDEX IF NOT EXISTS "IX_licenses_SupplierId" ON licenses ("SupplierId");
CREATE INDEX IF NOT EXISTS "IX_system_infos_CompanyId" ON system_infos ("CompanyId");
CREATE INDEX IF NOT EXISTS "IX_system_positions_SystemInfoId" ON system_positions ("SystemInfoId");
CREATE INDEX IF NOT EXISTS "IX_users_DepartmentId" ON users ("DepartmentId");
CREATE INDEX IF NOT EXISTS "IX_users_LocationId" ON users ("LocationId");

COMMIT;

-- ==================== DOWN ====================
-- CẢNH BÁO: orphan cleanup KHÔNG thể đảo ngược hoàn toàn — 4+1 CreatedByUserId trỏ user đã bị xóa
-- khỏi users (không còn trong DB), nên rollback sẽ mất thông tin creator trên 5 checkout đó.
-- DOWN chỉ rollback được phần schema add (FK + index). CÂN NHẮC trước khi chạy.
BEGIN;
ALTER TABLE users DROP CONSTRAINT IF EXISTS "FK_users_locations_LocationId";
ALTER TABLE users DROP CONSTRAINT IF EXISTS "FK_users_departments_DepartmentId";
ALTER TABLE consumable_checkouts DROP CONSTRAINT IF EXISTS "FK_consumable_checkouts_users_CreatedByUserId";
ALTER TABLE accessory_checkouts DROP CONSTRAINT IF EXISTS "FK_accessory_checkouts_users_CreatedByUserId";
ALTER TABLE assets DROP CONSTRAINT IF EXISTS "FK_assets_assignments_CurrentAssignmentId";
ALTER TABLE assets DROP CONSTRAINT IF EXISTS "FK_assets_system_positions_SystemPositionId";
DROP INDEX IF EXISTS "IX_users_LocationId";
DROP INDEX IF EXISTS "IX_users_DepartmentId";
DROP INDEX IF EXISTS "IX_users_Email";
DROP INDEX IF EXISTS "IX_consumable_checkouts_CreatedByUserId";
DROP INDEX IF EXISTS "IX_accessory_checkouts_CreatedByUserId";
DROP INDEX IF EXISTS "IX_asset_maintenance_assignees_UserId";
DROP INDEX IF EXISTS "IX_asset_maintenances_InspectedById";
DROP INDEX IF EXISTS "IX_asset_maintenances_SupplierId";
DROP INDEX IF EXISTS "IX_assets_SystemPositionId";
DROP INDEX IF EXISTS "IX_departments_CompanyId";
DROP INDEX IF EXISTS "IX_departments_ManagerId";
DROP INDEX IF EXISTS "IX_license_seats_SystemPositionId";
DROP INDEX IF EXISTS "IX_licenses_Name";
DROP INDEX IF EXISTS "IX_licenses_SupplierId";
DROP INDEX IF EXISTS "IX_system_infos_CompanyId";
DROP INDEX IF EXISTS "IX_system_positions_SystemInfoId";
DROP INDEX IF EXISTS "IX_manufacturers_Code";
DROP INDEX IF EXISTS "IX_manufacturers_Name";
DROP INDEX IF EXISTS "IX_suppliers_Code";
DROP INDEX IF EXISTS "IX_suppliers_Name";
COMMIT;

