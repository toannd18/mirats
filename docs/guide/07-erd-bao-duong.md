# 07 — ERD nhóm bảo dưỡng

> Hai luồng bảo dưỡng: phiếu lẻ (`asset_maintenances`) và đợt checklist
> (`templates → versions → items → campaigns → results`).

## 1. Mục đích / đối tượng đọc

Dev bảo trì: bất biến version khi đã pin campaign, snapshot thiết bị, quy tắc
ghi kết quả duy nhất.

## 2. Sơ đồ

```mermaid
erDiagram
    ASSETS ||--o{ ASSET_MAINTENANCES : has
    ASSET_MAINTENANCES ||--o{ MAINT_ASSIGNEES : assigns
    SYSTEM_INFOS ||--o{ MC_TEMPLATES : scopes
    MC_TEMPLATES ||--|{ MC_VERSIONS : versions
    MC_VERSIONS ||--|{ MC_ITEMS : contains
    MC_ITEMS ||--o{ MC_ITEM_POSITIONS : applies_at
    MC_ITEMS ||--|{ MC_PARAMS : measures
    SYSTEM_POSITIONS ||--o{ MC_ITEM_POSITIONS : referenced
    MC_VERSIONS ||--o{ MC_CAMPAIGNS : pins
    SYSTEM_INFOS ||--o{ MC_CAMPAIGNS : runs_on
    MC_CAMPAIGNS ||--|{ MC_EXECUTORS : executes
    MC_CAMPAIGNS ||--|{ MC_SNAPSHOTS : freezes
    MC_SNAPSHOTS ||--|{ MC_RESULTS : records
    MC_ITEMS ||--o{ MC_RESULTS : answers

    MC_TEMPLATES {
        uuid id PK
        string name "unique theo system"
        uuid system_info_id FK "CASCADE"
    }
    MC_VERSIONS {
        uuid id PK
        int version_number
        bool is_current "duy nhất true/template"
    }
    MC_CAMPAIGNS {
        uuid id PK
        uuid system_info_id FK "RESTRICT"
        uuid template_version_id FK "RESTRICT bất biến"
        string batch_number
        int status
    }
    MC_SNAPSHOTS {
        uuid id PK
        uuid campaign_id FK "CASCADE"
        string asset_tag "denormalized"
        string asset_name "denormalized"
    }
    MC_RESULTS {
        uuid id PK
        uuid campaign_id FK
        uuid snapshot_id FK "không trỏ asset sống"
        uuid item_id FK "RESTRICT"
    }
    ASSET_MAINTENANCES {
        uuid id PK
        uuid asset_id FK "CASCADE"
        string title
        decimal cost
        bool is_closed
    }
```

## 3. Bất biến phải nhớ

- Version đã pin campaign: immutable (sửa/xóa → `TEMPLATE_VERSION_IN_USE`).
- Snapshot denormalized (không FK tới asset/position) — kết quả luôn trỏ
  snapshot, sống sót khi asset đổi.
- Kết quả duy nhất theo (campaign, snapshot, item, param) — 2 partial unique
  index xử lý `StandardParamId` NULL (`AppDbContext.cs:418-419`).
- Phiếu lẻ: workflow tạo → kiểm tra (inspect) → đóng (close) → mở lại
  (superuser).

## Nguồn tham chiếu

- `aspire-react/aspire-react.Infrastructure/Persistence/AppDbContext.cs:293-420,595-631` (toàn bộ MC + phiếu lẻ)
- `docs/ERROR_CODES.md` (section MaintenanceTemplates, MaintenanceCampaigns, AssetMaintenances)
