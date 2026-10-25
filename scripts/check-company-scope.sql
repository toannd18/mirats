-- READ-ONLY: this script only runs SELECT statements. It must never be used to mutate data.
-- DF-8.1: company-scope audit for a disposable/dev database.
--
-- Run (example):
--   psql "$MIRATS_TEST_POSTGRES" -f scripts/check-company-scope.sql
--
-- "Floater" is counted for both representations currently used by the schema:
--   * NULL                  (nullable CompanyId columns)
--   * 00000000-0000-0000-0000-000000000000 (AssetMaintenance.CompanyId / legacy Guid.Empty writes)
--
-- The ActionLog section counts Create/Update logs written by non-superuser actors for floater rows.

-- 1) Floater counts per entity -------------------------------------------------
SELECT 'Asset' AS entity, count(*) AS floater_count FROM assets WHERE "CompanyId" IS NULL OR "CompanyId" = '00000000-0000-0000-0000-000000000000'
UNION ALL SELECT 'Consumable', count(*) FROM consumables WHERE "CompanyId" IS NULL OR "CompanyId" = '00000000-0000-0000-0000-000000000000'
UNION ALL SELECT 'Accessory', count(*) FROM accessories WHERE "CompanyId" IS NULL OR "CompanyId" = '00000000-0000-0000-0000-000000000000'
UNION ALL SELECT 'Component', count(*) FROM components WHERE "CompanyId" IS NULL OR "CompanyId" = '00000000-0000-0000-0000-000000000000'
UNION ALL SELECT 'License', count(*) FROM licenses WHERE "CompanyId" IS NULL OR "CompanyId" = '00000000-0000-0000-0000-000000000000'
UNION ALL SELECT 'User', count(*) FROM users WHERE "CompanyId" IS NULL OR "CompanyId" = '00000000-0000-0000-0000-000000000000'
UNION ALL SELECT 'Department', count(*) FROM departments WHERE "CompanyId" IS NULL OR "CompanyId" = '00000000-0000-0000-0000-000000000000'
UNION ALL SELECT 'Location', count(*) FROM locations WHERE "CompanyId" IS NULL OR "CompanyId" = '00000000-0000-0000-0000-000000000000'
UNION ALL SELECT 'SystemInfo', count(*) FROM system_infos WHERE "CompanyId" IS NULL OR "CompanyId" = '00000000-0000-0000-0000-000000000000'
UNION ALL SELECT 'AssetMaintenance (Guid.Empty)', count(*)
    FROM asset_maintenances WHERE "CompanyId" = '00000000-0000-0000-0000-000000000000'
ORDER BY entity;

-- 2) Non-superuser accounts without a company ---------------------------------
SELECT count(*) AS companyless_non_superuser_count
FROM users
WHERE ("CompanyId" IS NULL OR "CompanyId" = '00000000-0000-0000-0000-000000000000')
  AND "IsSuperUser" = false;

-- 3) Floater rows whose Create/Update ActionLog was written by a non-superuser --
-- ItemType: Asset=1, Consumable=2, Accessory=3, Component=4, License=5, User=10,
--           Location=14, Department=16, SystemInfo=17. ActionType: Create=1, Update=2.
SELECT 'Asset' AS entity, count(*) AS floater_audit_by_non_superuser
FROM assets x
JOIN action_logs l ON l."ItemId" = x."Id" AND l."ItemType" = 1 AND l."ActionType" IN (1, 2)
JOIN users u ON u."Id" = l."CreatedBy"
WHERE (x."CompanyId" IS NULL OR x."CompanyId" = '00000000-0000-0000-0000-000000000000') AND u."IsSuperUser" = false
UNION ALL
SELECT 'Consumable', count(*) FROM consumables x
JOIN action_logs l ON l."ItemId" = x."Id" AND l."ItemType" = 2 AND l."ActionType" IN (1, 2)
JOIN users u ON u."Id" = l."CreatedBy"
WHERE (x."CompanyId" IS NULL OR x."CompanyId" = '00000000-0000-0000-0000-000000000000') AND u."IsSuperUser" = false
UNION ALL
SELECT 'Accessory', count(*) FROM accessories x
JOIN action_logs l ON l."ItemId" = x."Id" AND l."ItemType" = 3 AND l."ActionType" IN (1, 2)
JOIN users u ON u."Id" = l."CreatedBy"
WHERE (x."CompanyId" IS NULL OR x."CompanyId" = '00000000-0000-0000-0000-000000000000') AND u."IsSuperUser" = false
UNION ALL
SELECT 'Component', count(*) FROM components x
JOIN action_logs l ON l."ItemId" = x."Id" AND l."ItemType" = 4 AND l."ActionType" IN (1, 2)
JOIN users u ON u."Id" = l."CreatedBy"
WHERE (x."CompanyId" IS NULL OR x."CompanyId" = '00000000-0000-0000-0000-000000000000') AND u."IsSuperUser" = false
UNION ALL
SELECT 'License', count(*) FROM licenses x
JOIN action_logs l ON l."ItemId" = x."Id" AND l."ItemType" = 5 AND l."ActionType" IN (1, 2)
JOIN users u ON u."Id" = l."CreatedBy"
WHERE (x."CompanyId" IS NULL OR x."CompanyId" = '00000000-0000-0000-0000-000000000000') AND u."IsSuperUser" = false
UNION ALL
SELECT 'User', count(*) FROM users x
JOIN action_logs l ON l."ItemId" = x."Id" AND l."ItemType" = 10 AND l."ActionType" IN (1, 2)
JOIN users u ON u."Id" = l."CreatedBy"
WHERE (x."CompanyId" IS NULL OR x."CompanyId" = '00000000-0000-0000-0000-000000000000') AND u."IsSuperUser" = false
UNION ALL
SELECT 'Department', count(*) FROM departments x
JOIN action_logs l ON l."ItemId" = x."Id" AND l."ItemType" = 16 AND l."ActionType" IN (1, 2)
JOIN users u ON u."Id" = l."CreatedBy"
WHERE (x."CompanyId" IS NULL OR x."CompanyId" = '00000000-0000-0000-0000-000000000000') AND u."IsSuperUser" = false
UNION ALL
SELECT 'Location', count(*) FROM locations x
JOIN action_logs l ON l."ItemId" = x."Id" AND l."ItemType" = 14 AND l."ActionType" IN (1, 2)
JOIN users u ON u."Id" = l."CreatedBy"
WHERE (x."CompanyId" IS NULL OR x."CompanyId" = '00000000-0000-0000-0000-000000000000') AND u."IsSuperUser" = false
UNION ALL
SELECT 'SystemInfo', count(*) FROM system_infos x
JOIN action_logs l ON l."ItemId" = x."Id" AND l."ItemType" = 17 AND l."ActionType" IN (1, 2)
JOIN users u ON u."Id" = l."CreatedBy"
WHERE (x."CompanyId" IS NULL OR x."CompanyId" = '00000000-0000-0000-0000-000000000000') AND u."IsSuperUser" = false
ORDER BY entity;
