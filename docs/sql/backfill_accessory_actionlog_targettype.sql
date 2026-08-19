-- ============================================================================
-- BACKFILL (data only) — fix already-wrong ActionLog.TargetType for Accessory
--   Checkout / Checkin rows caused by the MapCheckoutTypeToTargetType bug (ST4/F14).
--
-- Background:
--   The bug mapped AccessoryCheckoutType → AssignmentTargetType incorrectly:
--     Department → User, SystemPosition → User, Location → SystemPosition (before 2026-08-13).
--   The REAL target type is the source of truth:
--     - AccessoryCheckout.CheckoutType (for Checkin rows, via LogMeta.checkoutId)
--     - LogMeta.checkoutType            (for Checkout rows)
--
-- Enum int values:
--   ItemType.Accessory = 3
--   ActionType.Checkout = 4, Checkin = 5
--   AssignmentTargetType: User=1, Department=2, SystemPosition=3, Location=5
--   AccessoryCheckoutType: User=1, Department=2, Location=3, SystemPosition=4
-- ============================================================================

-- ----------------------------------  UP  ----------------------------------
-- 1) Checkout rows: real type is recoverable from LogMeta.checkoutType.
UPDATE action_logs SET "TargetType" = CASE
        WHEN ("LogMeta"::jsonb ->> 'checkoutType') = 'Department'     THEN 2  -- AssignmentTargetType.Department
        WHEN ("LogMeta"::jsonb ->> 'checkoutType') = 'Location'       THEN 5  -- AssignmentTargetType.Location (added in ST4)
        WHEN ("LogMeta"::jsonb ->> 'checkoutType') = 'SystemPosition' THEN 3  -- AssignmentTargetType.SystemPosition
        WHEN ("LogMeta"::jsonb ->> 'checkoutType') = 'User'           THEN 1  -- AssignmentTargetType.User
        ELSE "TargetType"
    END
WHERE "ItemType" = 3                       -- ItemType.Accessory
  AND "ActionType" = 4                     -- ActionType.Checkout
  AND "TargetType" IS NOT NULL
  AND "LogMeta" IS NOT NULL AND "LogMeta" <> '';

-- 2) Checkin rows: real type from AccessoryCheckout.CheckoutType via LogMeta.checkoutId.
--    ⚠️ FIXED 2026-08-14: the original script mapped 2→5 / 3→3 / 4→2 (reversed). Correct mapping
--    (matches CheckinAccessoryCommand.MapCheckoutTypeToTargetType, ST4-fixed code):
--      AccessoryCheckoutType User=1→1, Department=2→2, Location=3→5, SystemPosition=4→3.
UPDATE action_logs al SET "TargetType" = CASE co."CheckoutType"
        WHEN 1 THEN 1                       -- AccessoryCheckoutType.User          -> AssignmentTargetType.User
        WHEN 2 THEN 2                       -- AccessoryCheckoutType.Department    -> AssignmentTargetType.Department
        WHEN 3 THEN 5                       -- AccessoryCheckoutType.Location      -> AssignmentTargetType.Location
        WHEN 4 THEN 3                       -- AccessoryCheckoutType.SystemPosition -> AssignmentTargetType.SystemPosition
        ELSE al."TargetType"
    END
FROM accessory_checkouts co
WHERE al."ItemType" = 3                    -- ItemType.Accessory
  AND al."ActionType" = 5                  -- ActionType.Checkin
  AND al."TargetType" IS NOT NULL
  AND (al."LogMeta"::jsonb ->> 'checkoutId') IS NOT NULL
  AND (al."LogMeta"::jsonb ->> 'checkoutId') = co."Id"::text;

-- 3) NEW (2026-08-14): backfill TargetSystemInfoId/Name for SystemPosition-targeted rows so that
--    GET /api/v1/action-logs/by-system (filter: TargetType == SystemPosition AND TargetSystemInfoId == systemInfoId)
--    actually returns these Accessory logs. Accessory commands write ActionLog via _context.ActionLogs.Add
--    (not LogAction service), so the snapshot columns were never set.
UPDATE action_logs al SET
    "TargetSystemInfoId"   = sp."SystemInfoId",
    "TargetSystemInfoName" = si."Name"
FROM system_positions sp
LEFT JOIN system_infos si ON si."Id" = sp."SystemInfoId"
WHERE al."ItemType" = 3                    -- ItemType.Accessory
  AND al."TargetType" = 3                  -- AssignmentTargetType.SystemPosition
  AND al."TargetId" = sp."Id"
  AND al."TargetSystemInfoId" IS NULL;

-- ---------------------------------- DOWN ----------------------------------
-- NOT mechanically reversible: the original (wrong) TargetType values are not
-- preserved anywhere after this UPDATE. To roll back, restore from a snapshot /
-- pg_dump taken BEFORE running the UP script. (No-op placeholder below.)
-- SELECT 1;