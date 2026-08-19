-- ============================================================================
-- One-time backfill: action_logs."TargetSystemInfoId"
-- ============================================================================
-- Populates the new snapshot column for EXISTING rows where it is still NULL.
--
-- DISCLAIMER (same as the LocationName backfill):
--   This reflects the CURRENT parent-system assignment (SystemPosition -> SystemInfo),
--   NOT necessarily the assignment at the time the action was logged — if a position
--   was ever re-parented to a different system, old rows will reflect today's mapping.
--   Going forward, ActionLogService writes TargetSystemInfoId + TargetSystemInfoName
--   together at log time, so every new write is accurate.
--
--   "TargetType" = 3  ⇔  AssignmentTargetType.SystemPosition
-- ============================================================================

UPDATE action_logs AS al
SET "TargetSystemInfoId" = sp."SystemInfoId"
FROM system_positions AS sp
WHERE al."TargetType" = 3
  AND al."TargetId" = sp."Id"
  AND al."TargetSystemInfoId" IS NULL;

-- Verify (should be 0 rows when complete):
-- SELECT COUNT(*) FROM action_logs
-- WHERE "TargetType" = 3 AND "TargetId" IS NOT NULL AND "TargetSystemInfoId" IS NULL;
