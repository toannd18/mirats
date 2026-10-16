# 12 — Sequence đợt bảo dưỡng: tạo → ghi kết quả → hoàn thành

> Từ template version tới campaign, snapshot thiết bị, upsert kết quả, complete.

## 1. Mục đích / đối tượng đọc

Kỹ thuật bảo trì + dev: hiểu pin version, snapshot, quy tắc ghi kết quả.

## 2. Sơ đồ

```mermaid
sequenceDiagram
    participant U as Người dùng
    participant FE as Frontend
    participant API as API
    participant DB as Postgres

    U->>FE: Chọn hệ thống (+ template nếu nhiều)
    FE->>API: POST /maintenance/campaigns
    API->>DB: Resolve version hiện hành đã publish
    alt Chưa có template/version
        API-->>U: 400 NO_TEMPLATE / NO_CURRENT_VERSION
    else Đã có đợt InProgress
        API-->>U: 400 CAMPAIGN_ALREADY_IN_PROGRESS
    else OK
        API->>DB: Tạo campaign + snapshot thiết bị + executors
        API-->>U: 201 campaign
    end
    loop Từng thiết bị × hạng mục
        FE->>API: POST /campaigns/{id}/results
        API->>DB: Check snapshot/item/param + upsert (409 RESULT_CONCURRENT_WRITE → thử lại)
    end
    FE->>API: POST /campaigns/{id}/complete
    alt Còn thiếu kết quả
        API-->>U: 400 CAMPAIGN_RESULTS_INCOMPLETE (kèm "x/y bản ghi")
    else Đủ
        API-->>U: 200 hoàn thành (bất biến từ đây)
    end
```

## 3. Quy tắc nhớ

- Campaign pin **version hiện hành đã publish** — version đó bất biến.
- Snapshot denormalized: đổi asset/vị trí sau này không ảnh hưởng kết quả cũ.
- Ghi đè kết quả cho phép tới khi complete; campaign hoàn thành thì khóa
  (`CAMPAIGN_COMPLETED`).
- Race ghi cùng ô → `409 RESULT_CONCURRENT_WRITE` (duy nhất toàn API) — client
  thử lại.

## Nguồn tham chiếu

- `aspire-react/aspire-react.Server/Web/Controllers/MaintenanceCampaignsController.cs:81-141,141-210` (create, results, complete)
- `aspire-react/aspire-react.Application/MaintenanceCampaigns/CampaignAccess.cs:61-87` (resolve version)
- `docs/ERROR_CODES.md` (section MaintenanceCampaigns, duy nhất 409)
