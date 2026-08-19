-- ============================================================================
-- MIGRATION: Component Serial/Bulk tracking
--   * components."TrackingType" (integer, default 0 = Bulk)
--   * component_units table (serial-tracked units)
-- ============================================================================
-- Project convention: schema is applied on startup via raw ALTER in Program.cs
-- (EnsureCreated + self-heal) — `dotnet ef` is NOT used (no __EFMigrationsHistory).
-- This script documents the exact change and provides a rollback (DOWN) section.

-- ==================== UP ====================

ALTER TABLE components ADD COLUMN IF NOT EXISTS "TrackingType" integer NOT NULL DEFAULT 0;

CREATE TABLE IF NOT EXISTS component_units (
    "Id"             uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    "ComponentId"    uuid NOT NULL,
    "SerialNo"       text NULL,
    "Status"         integer NOT NULL DEFAULT 0,   -- 0 InStock, 1 Allocated, 2 Damaged, 3 Disposed
    "CurrentAssetId" uuid NULL,
    "Notes"          text NULL,
    "CreatedAt"      timestamp NOT NULL,
    "UpdatedAt"      timestamp NOT NULL,
    "DeletedAt"      timestamp NULL
);

-- Unique serial (Postgres allows multiple NULLs → NULL serial rows are fine).
CREATE UNIQUE INDEX IF NOT EXISTS "IX_component_units_SerialNo" ON component_units ("SerialNo");
CREATE INDEX IF NOT EXISTS "IX_component_units_ComponentId" ON component_units ("ComponentId");
CREATE INDEX IF NOT EXISTS "IX_component_units_Status" ON component_units ("Status");
CREATE INDEX IF NOT EXISTS "IX_component_units_CurrentAssetId" ON component_units ("CurrentAssetId");

ALTER TABLE component_units
    ADD CONSTRAINT IF NOT EXISTS "FK_component_units_components_ComponentId"
    FOREIGN KEY ("ComponentId") REFERENCES components ("Id") ON DELETE CASCADE;

ALTER TABLE component_units
    ADD CONSTRAINT IF NOT EXISTS "FK_component_units_assets_CurrentAssetId"
    FOREIGN KEY ("CurrentAssetId") REFERENCES assets ("Id") ON DELETE SET NULL;

-- ==================== DOWN (rollback) ====================
-- DROP TABLE IF EXISTS component_units;
-- ALTER TABLE components DROP COLUMN IF EXISTS "TrackingType";
-- (Chỉ chạy khi đã chắc chắn không còn dữ liệu serial cần giữ.)

-- ==================== DATA MIGRATION (Bulk → Serial) ====================
-- Chuyển 1 component Bulk hiện có sang Serial: sinh N bản ghi ComponentUnit
-- (N = Qty hiện tại) với SerialNo = NULL để admin bổ sung serial sau.
-- KHÔNG làm mất dữ liệu tồn kho hiện có.
--
--   UPDATE components SET "TrackingType" = 1 WHERE "Id" = '<component-id>';
--   INSERT INTO component_units ("ComponentId", "SerialNo", "Status", "CreatedAt", "UpdatedAt")
--   SELECT "Id", NULL, 0, now(), now()
--   FROM components, generate_series(1, "Qty") WHERE "Id" = '<component-id>';
