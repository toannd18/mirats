-- [MC-7c] Dọn kết quả THỪA ở các campaign đang InProgress.
--
-- "Kết quả thừa" = dòng maintenance_checklist_results mà cặp (ChecklistItem, DeviceSnapshot)
-- KHÔNG còn thuộc phạm vi áp dụng: item đã khai báo danh sách vị trí (maintenance_checklist_item_positions)
-- nhưng snapshot của thiết bị nằm ở vị trí ngoài danh sách đó.
--
-- QUY TẮC:
--  - CHỈ xử lý campaign Status = 1 (InProgress).
--  - Campaign Completed (Status = 2) => BẤT BIẾN, TUYỆT ĐỐI KHÔNG ĐỤNG.
--  - Item không có dòng maintenance_checklist_item_positions nào (universal) => không bao giờ bị coi là thừa.
--
-- Khi chạy: psql vào DB đích rồi `\i scripts/cleanup-maintenance-stale-results.sql`
-- (hoặc docker exec để chạy file). Idempotent — chạy lặp không hại.
BEGIN;

DELETE FROM maintenance_checklist_results r
USING maintenance_campaigns c,
      maintenance_checklist_items i,
      maintenance_campaign_device_snapshots s
WHERE r."CampaignId" = c."Id"
  AND c."Status" = 1                                    -- chỉ InProgress
  AND i."Id" = r."ChecklistItemId"
  AND s."Id" = r."DeviceSnapshotId"
  AND EXISTS (SELECT 1 FROM maintenance_checklist_item_positions ip
              WHERE ip."ItemId" = i."Id")               -- item có giới hạn vị trí
  AND NOT EXISTS (SELECT 1 FROM maintenance_checklist_item_positions ip
                  WHERE ip."ItemId" = i."Id"
                    AND ip."SystemPositionId" = s."SystemPositionId");  -- snapshot ngoài phạm vi

COMMIT;