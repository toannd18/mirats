-- ============================================================================
-- MIGRATION: Component Category
--   * components."CategoryId" (uuid, nullable — bắt buộc khi TẠO MỚI qua API)
--   * FK components.CategoryId -> categories.Id (ON DELETE RESTRICT)
-- ============================================================================
-- Project convention: schema applied on startup via raw SQL in Program.cs
-- (EnsureCreated + self-heal). `dotnet ef` is NOT used.
-- The `categories` table + `CategoryType` enum (Component = 4) already exist
-- (shared by Asset/Accessory/Consumable/License via AssetModel etc.).

-- ==================== UP ====================

CREATE TABLE IF NOT EXISTS categories (
    "Id"                uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    "Name"              text NOT NULL,
    "CategoryType"      integer NOT NULL,            -- 1 Asset, 2 Consumable, 3 Accessory, 4 Component, 5 License
    "TagColor"          text NULL,
    "UseDefaultEula"    boolean NOT NULL DEFAULT false,
    "RequireAcceptance" boolean NOT NULL DEFAULT false,
    "CheckinEmail"      boolean NOT NULL DEFAULT false,
    "Notes"             text NULL
);

ALTER TABLE components ADD COLUMN IF NOT EXISTS "CategoryId" uuid;
CREATE INDEX IF NOT EXISTS "IX_components_CategoryId" ON components ("CategoryId");

-- Recreate FK as RESTRICT (defense-in-depth: a category referenced by any component —
-- including soft-deleted ones — cannot be deleted). The API also guards with CATEGORY_IN_USE.
ALTER TABLE components DROP CONSTRAINT IF EXISTS "FK_components_categories_CategoryId";
ALTER TABLE components ADD CONSTRAINT "FK_components_categories_CategoryId"
    FOREIGN KEY ("CategoryId") REFERENCES categories ("Id") ON DELETE RESTRICT;

-- ==================== DOWN (rollback) ====================
-- ALTER TABLE components DROP CONSTRAINT IF EXISTS "FK_components_categories_CategoryId";
-- DROP INDEX IF EXISTS "IX_components_CategoryId";
-- ALTER TABLE components DROP COLUMN IF EXISTS "CategoryId";
-- DROP TABLE IF EXISTS categories;
-- (Chỉ chạy khi chắc chắn không còn dữ liệu category cần giữ.)

-- ==================== SEED (tuỳ chọn) ====================
-- INSERT INTO categories ("Name", "CategoryType", "TagColor") VALUES
--   ('RAM', 4, 'blue'),
--   ('Ổ cứng', 4, 'cyan'),
--   ('Cáp', 4, 'green'),
--   ('Phụ kiện khác', 4, 'default');
