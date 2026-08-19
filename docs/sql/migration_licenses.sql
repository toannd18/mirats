-- ============================================================================
-- MIGRATION: License module — v5 hardening (2026-08-12)
--   * licenses / license_seats were created by EF EnsureCreated (Phase 4/7). This script is the
--     idempotent self-heal for the v5 changes (also applied by Program.cs on startup):
--     - License: Reassignable (bool default true), SupplierId, TerminationDate, MinSeats, DeletedAt
--     - LicenseSeat: SeatNumber (int, unique per license), SystemPositionId (3rd checkout target,
--       convention: always the SystemPosition child like Accessory), CreatedAt/UpdatedAt
--     - Check constraint forbidding 2+ targets on one seat (service layer enforces exactly-one)
--   * Project convention: raw SQL self-heal (no `dotnet ef`).
--   * PostgreSQL has no "ADD CONSTRAINT IF NOT EXISTS" — use DROP + ADD (idempotent).
-- ============================================================================

-- ==================== UP ====================

-- License columns
ALTER TABLE licenses ADD COLUMN IF NOT EXISTS "Reassignable"   boolean NOT NULL DEFAULT true;
ALTER TABLE licenses ADD COLUMN IF NOT EXISTS "SupplierId"      uuid NULL;
ALTER TABLE licenses ADD COLUMN IF NOT EXISTS "TerminationDate" timestamp NULL;
ALTER TABLE licenses ADD COLUMN IF NOT EXISTS "MinSeats"        integer NULL;
ALTER TABLE licenses ADD COLUMN IF NOT EXISTS "DeletedAt"       timestamp NULL;

-- Seat columns
ALTER TABLE license_seats ADD COLUMN IF NOT EXISTS "SeatNumber"       integer NOT NULL DEFAULT 0;
ALTER TABLE license_seats ADD COLUMN IF NOT EXISTS "SystemPositionId" uuid NULL;
ALTER TABLE license_seats ADD COLUMN IF NOT EXISTS "CreatedAt"        timestamp NOT NULL DEFAULT CURRENT_TIMESTAMP;
ALTER TABLE license_seats ADD COLUMN IF NOT EXISTS "UpdatedAt"        timestamp NOT NULL DEFAULT CURRENT_TIMESTAMP;

-- Backfill SeatNumber for pre-existing rows (row_number per license) so the unique index below holds.
UPDATE license_seats ls SET "SeatNumber" = sub.rn FROM (
    SELECT "Id", row_number() OVER (PARTITION BY "LicenseId" ORDER BY "AssignedAt", "Id") AS rn
    FROM license_seats WHERE "SeatNumber" = 0
) sub WHERE ls."Id" = sub."Id";

CREATE INDEX IF NOT EXISTS "IX_license_seats_LicenseId" ON license_seats ("LicenseId");
CREATE UNIQUE INDEX IF NOT EXISTS "IX_license_seats_LicenseId_SeatNumber" ON license_seats ("LicenseId", "SeatNumber");
CREATE INDEX IF NOT EXISTS "IX_licenses_CompanyId" ON licenses ("CompanyId");
CREATE INDEX IF NOT EXISTS "IX_licenses_CategoryId" ON licenses ("CategoryId");

-- Exactly-one-of-three is enforced by the checkout service; the DB CHECK only forbids 2+ targets at once.
ALTER TABLE license_seats DROP CONSTRAINT IF EXISTS "CK_license_seats_single_target";
ALTER TABLE license_seats ADD CONSTRAINT "CK_license_seats_single_target"
    CHECK ((("UserId" IS NOT NULL)::int + ("AssetId" IS NOT NULL)::int + ("SystemPositionId" IS NOT NULL)::int) <= 1);

ALTER TABLE license_seats DROP CONSTRAINT IF EXISTS "FK_license_seats_licenses_LicenseId";
ALTER TABLE license_seats ADD CONSTRAINT "FK_license_seats_licenses_LicenseId"
    FOREIGN KEY ("LicenseId") REFERENCES licenses ("Id") ON DELETE CASCADE;
ALTER TABLE license_seats DROP CONSTRAINT IF EXISTS "FK_license_seats_assets_AssetId";
ALTER TABLE license_seats ADD CONSTRAINT "FK_license_seats_assets_AssetId"
    FOREIGN KEY ("AssetId") REFERENCES assets ("Id") ON DELETE SET NULL;
ALTER TABLE license_seats DROP CONSTRAINT IF EXISTS "FK_license_seats_users_UserId";
ALTER TABLE license_seats ADD CONSTRAINT "FK_license_seats_users_UserId"
    FOREIGN KEY ("UserId") REFERENCES users ("Id") ON DELETE SET NULL;
ALTER TABLE license_seats DROP CONSTRAINT IF EXISTS "FK_license_seats_system_positions_SystemPositionId";
ALTER TABLE license_seats ADD CONSTRAINT "FK_license_seats_system_positions_SystemPositionId"
    FOREIGN KEY ("SystemPositionId") REFERENCES system_positions ("Id") ON DELETE SET NULL;
ALTER TABLE licenses DROP CONSTRAINT IF EXISTS "FK_licenses_suppliers_SupplierId";
ALTER TABLE licenses ADD CONSTRAINT "FK_licenses_suppliers_SupplierId"
    FOREIGN KEY ("SupplierId") REFERENCES suppliers ("Id") ON DELETE SET NULL;
ALTER TABLE licenses DROP CONSTRAINT IF EXISTS "FK_licenses_manufacturers_ManufacturerId";
ALTER TABLE licenses ADD CONSTRAINT "FK_licenses_manufacturers_ManufacturerId"
    FOREIGN KEY ("ManufacturerId") REFERENCES manufacturers ("Id") ON DELETE SET NULL;

-- ==================== DOWN (rollback) ====================
-- ALTER TABLE license_seats DROP CONSTRAINT IF EXISTS "CK_license_seats_single_target";
-- ALTER TABLE license_seats DROP CONSTRAINT IF EXISTS "FK_license_seats_system_positions_SystemPositionId";
-- ALTER TABLE license_seats DROP CONSTRAINT IF EXISTS "FK_license_seats_users_UserId";
-- ALTER TABLE license_seats DROP CONSTRAINT IF EXISTS "FK_license_seats_assets_AssetId";
-- ALTER TABLE license_seats DROP CONSTRAINT IF EXISTS "FK_license_seats_licenses_LicenseId";
-- ALTER TABLE license_seats DROP COLUMN IF EXISTS "UpdatedAt";
-- ALTER TABLE license_seats DROP COLUMN IF EXISTS "CreatedAt";
-- ALTER TABLE license_seats DROP COLUMN IF EXISTS "SystemPositionId";
-- ALTER TABLE license_seats DROP COLUMN IF EXISTS "SeatNumber";
-- ALTER TABLE licenses DROP CONSTRAINT IF EXISTS "FK_licenses_manufacturers_ManufacturerId";
-- ALTER TABLE licenses DROP CONSTRAINT IF EXISTS "FK_licenses_suppliers_SupplierId";
-- ALTER TABLE licenses DROP COLUMN IF EXISTS "DeletedAt";
-- ALTER TABLE licenses DROP COLUMN IF EXISTS "MinSeats";
-- ALTER TABLE licenses DROP COLUMN IF EXISTS "TerminationDate";
-- ALTER TABLE licenses DROP COLUMN IF EXISTS "SupplierId";
-- ALTER TABLE licenses DROP COLUMN IF EXISTS "Reassignable";
