# 05 — ERD nhóm tài sản

> Bảng `assets` và các danh mục xung quanh. Thuộc tính liệt kê là cột chính
> (PK/FK/unique); xem entity đầy đủ trong `Domain/Entities/`.

## 1. Mục đích / đối tượng đọc

Dev cần thêm field/tra cứu quan hệ xóa (delete-guard chặn xóa khi còn tham
chiếu — xem `docs/ERROR_CODES.md` các mã `*_IN_USE`).

## 2. Sơ đồ

```mermaid
erDiagram
    COMPANIES ||--o{ ASSETS : owns
    ASSET_MODELS ||--o{ ASSETS : instantiates
    CATEGORIES ||--o{ ASSET_MODELS : classifies
    MANUFACTURERS ||--o{ ASSET_MODELS : makes
    SUPPLIERS ||--o{ ASSETS : supplies
    DEPRECIATIONS ||--o{ ASSET_MODELS : depreciates
    LOCATIONS ||--o{ ASSETS : stores
    SYSTEM_POSITIONS ||--o{ ASSETS : installed_at
    ASSETS ||--o{ ASSIGNMENTS : history
    ASSETS ||--o| ASSIGNMENTS : current

    ASSETS {
        uuid id PK
        string asset_tag UK "duy nhất"
        string name
        string serial "index"
        int status "0 Pending, 1 Deployed, 2 Archived"
        bool is_confirmed
        uuid model_id FK "SET NULL"
        uuid company_id FK "SET NULL"
        uuid system_position_id FK "vị trí lắp đặt"
        uuid current_assignment_id FK "SET NULL, null = sẵn sàng"
    }
    ASSET_MODELS {
        uuid id PK
        string name
        uuid manufacturer_id FK
        uuid category_id FK
        uuid depreciation_id FK
    }
    CATEGORIES {
        uuid id PK
        string name
    }
    MANUFACTURERS {
        uuid id PK
        string name UK
        string code UK
    }
    SUPPLIERS {
        uuid id PK
        string name UK
        string code UK
    }
    ASSIGNMENTS {
        uuid id PK
        uuid asset_id FK "CASCADE — lịch sử giữ lại"
        string target_type "User/Department/SystemPosition"
        uuid target_id
    }
    SYSTEM_POSITIONS {
        uuid id PK
        string code UK
        string name
    }
```

## 3. Quy tắc xóa (delete-guard)

- Xóa asset đã confirm/có lịch sử cấp phát → `400` (`ASSET_CONFIRMED_CANNOT_DELETE`,
  `ASSET_HAS_ASSIGNMENTS`, ...).
- Xóa model đang có asset → `400` không error_code (ghi chú DOC-ERR-1).
- Xóa category/manufacturer/supplier đang được tham chiếu → `400 *_IN_USE`.
- FK `ON DELETE SET NULL` ở quan hệ danh mục — xóa danh mục không xóa asset.

## Nguồn tham chiếu

- `aspire-react/aspire-react.Infrastructure/Persistence/AppDbContext.cs:196-258` (assets, models, categories, manufacturers, suppliers, assignments)
- `aspire-react/aspire-react.Application/Models/Commands/DeleteModelCommand.cs:46-52` (guard không error_code)
- `docs/ERROR_CODES.md` (section Assets, AssetModels, Categories, Manufacturers, Suppliers)
