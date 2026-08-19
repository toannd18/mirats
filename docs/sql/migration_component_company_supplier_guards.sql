-- ============================================================================
-- MIGRATION: Component — Company/Location scoping + Supplier/Manufacturer + FK fix
--   * components."SupplierId"      uuid nullable, FK -> suppliers   ON DELETE SET NULL
--   * components."ManufacturerId"  uuid nullable, FK -> manufacturers ON DELETE SET NULL
--   * components."ModelNumber"     text nullable
--   * components."CompanyId"  FK CHANGED: SET NULL -> RESTRICT (company in use by a component cannot be deleted)
--   * components."LocationId" FK CHANGED: SET NULL -> RESTRICT (location in use by a component cannot be deleted)
--   * component_units FKs FIX: "ADD CONSTRAINT IF NOT EXISTS" is INVALID in PostgreSQL —
--     replaced with DROP CONSTRAINT IF EXISTS + ADD CONSTRAINT (idempotent self-heal).
-- ============================================================================
-- Project convention: schema applied on startup via raw SQL in Program.cs
-- (EnsureCreated + self-heal). `dotnet ef` is NOT used.
-- Company/Location/Supplier/Manufacturer are SHARED entities (already used by Asset/Accessory/Consumable).

-- ==================== UP ====================

-- 1) Fix component_units FKs (postgres has no ADD CONSTRAINT IF NOT EXISTS)
ALTER TABLE component_units DROP CONSTRAINT IF EXISTS "FK_component_units_components_ComponentId";
ALTER TABLE component_units ADD CONSTRAINT "FK_component_units_components_ComponentId"
    FOREIGN KEY ("ComponentId") REFERENCES components ("Id") ON DELETE CASCADE;

ALTER TABLE component_units DROP CONSTRAINT IF EXISTS "FK_component_units_assets_CurrentAssetId";
ALTER TABLE component_units ADD CONSTRAINT "FK_component_units_assets_CurrentAssetId"
    FOREIGN KEY ("CurrentAssetId") REFERENCES assets ("Id") ON DELETE SET NULL;

-- 2) Company/Location FKs -> RESTRICT (defense-in-depth; API guards return COMPANY_IN_USE / LOCATION_IN_USE)
ALTER TABLE components DROP CONSTRAINT IF EXISTS "FK_components_companies_CompanyId";
ALTER TABLE components ADD CONSTRAINT "FK_components_companies_CompanyId"
    FOREIGN KEY ("CompanyId") REFERENCES companies ("Id") ON DELETE RESTRICT;

ALTER TABLE components DROP CONSTRAINT IF EXISTS "FK_components_locations_LocationId";
ALTER TABLE components ADD CONSTRAINT "FK_components_locations_LocationId"
    FOREIGN KEY ("LocationId") REFERENCES locations ("Id") ON DELETE RESTRICT;

-- 3) New columns + indexes for Supplier / Manufacturer / ModelNumber
ALTER TABLE components ADD COLUMN IF NOT EXISTS "SupplierId" uuid;
ALTER TABLE components ADD COLUMN IF NOT EXISTS "ManufacturerId" uuid;
ALTER TABLE components ADD COLUMN IF NOT EXISTS "ModelNumber" text;
CREATE INDEX IF NOT EXISTS "IX_components_CompanyId" ON components ("CompanyId");
CREATE INDEX IF NOT EXISTS "IX_components_LocationId" ON components ("LocationId");
CREATE INDEX IF NOT EXISTS "IX_components_SupplierId" ON components ("SupplierId");
CREATE INDEX IF NOT EXISTS "IX_components_ManufacturerId" ON components ("ManufacturerId");

ALTER TABLE components DROP CONSTRAINT IF EXISTS "FK_components_suppliers_SupplierId";
ALTER TABLE components ADD CONSTRAINT "FK_components_suppliers_SupplierId"
    FOREIGN KEY ("SupplierId") REFERENCES suppliers ("Id") ON DELETE SET NULL;

ALTER TABLE components DROP CONSTRAINT IF EXISTS "FK_components_manufacturers_ManufacturerId";
ALTER TABLE components ADD CONSTRAINT "FK_components_manufacturers_ManufacturerId"
    FOREIGN KEY ("ManufacturerId") REFERENCES manufacturers ("Id") ON DELETE SET NULL;

-- ==================== DOWN (rollback) ====================
-- ALTER TABLE components DROP CONSTRAINT IF EXISTS "FK_components_manufacturers_ManufacturerId";
-- ALTER TABLE components DROP CONSTRAINT IF EXISTS "FK_components_suppliers_SupplierId";
-- ALTER TABLE components DROP COLUMN IF EXISTS "ModelNumber";
-- ALTER TABLE components DROP COLUMN IF EXISTS "ManufacturerId";
-- ALTER TABLE components DROP COLUMN IF EXISTS "SupplierId";
-- (CompanyId/LocationId restore to SET NULL if you ever need to roll back the RESTRICT change)
-- ALTER TABLE components DROP CONSTRAINT IF EXISTS "FK_components_locations_LocationId";
-- ALTER TABLE components ADD CONSTRAINT "FK_components_locations_LocationId"
--     FOREIGN KEY ("LocationId") REFERENCES locations ("Id") ON DELETE SET NULL;
-- ALTER TABLE components DROP CONSTRAINT IF EXISTS "FK_components_companies_CompanyId";
-- ALTER TABLE components ADD CONSTRAINT "FK_components_companies_CompanyId"
--     FOREIGN KEY ("CompanyId") REFERENCES companies ("Id") ON DELETE SET NULL;
-- (Chỉ chạy khi chắc chắn không còn dữ liệu cần giữ.)
