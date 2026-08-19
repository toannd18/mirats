# ST6d Bước 2 — Audit Drift (Schema thật vs Model EF)

Ngày: 2026-08-14
Trạng thái: **CHỈ AUDIT, CHƯA SỬA GÌ** — chờ user quyết định hướng xử lý.
Nguồn đối chiếu:
- Schema thật: `information_schema.columns` (339 dòng), `table_constraints` (278), `pg_indexes` (103), `referential_constraints` (64 FK), `column_defaults` (48).
- Model: `dotnet ef dbcontext script` (881 dòng, không connect DB).
- Entity: Assignment, Asset, License, LicenseSeat + AppDbContext (499 dòng).
- Data check: 33 bảng; row counts khớp backup Step 1.

---

## A. TYPE DRIFT — model ≠ DB (25 cột)

### A1. text ↔ varchar(n) (11 cột)
| Bảng.Cột | Model | DB thật | Ghi chú |
|---|---|---|---|
| manufacturers.Name | varchar(100) | varchar(255) | model hẹp hơn DB |
| manufacturers.Code | varchar(5) NOT NULL | text NULL | model chặt hơn DB |
| suppliers.Name | varchar(100) | varchar(255) | model hẹp hơn DB |
| suppliers.Code | varchar(5) NOT NULL | text NULL | model chặt hơn DB |
| system_infos.Code | varchar(11) | text | model chặt hơn |
| system_infos.Name | varchar(255) | text | |
| system_positions.Code | varchar(11) | text | |
| system_positions.Name | varchar(255) | text | |
| departments.Name | varchar(255) | text | |
| asset_maintenances.Title | varchar(255) | text | |
| component_units.SerialNo | varchar(255) | text | |

### A2. timestamptz ↔ timestamp without time zone (14 cột)
| Bảng.Cột | Model | DB thật |
|---|---|---|
| action_logs.DeletedAt | timestamptz | timestamp |
| asset_maintenances.StartDate | timestamptz | timestamp |
| asset_maintenances.CompletionDate | timestamptz | timestamp |
| asset_maintenances.CreatedAt | timestamptz | timestamp |
| asset_maintenances.UpdatedAt | timestamptz | timestamp |
| asset_maintenances.DeletedAt | timestamptz | timestamp |
| asset_maintenances.ClosedAt | timestamptz | timestamp |
| asset_maintenances.InspectedAt | timestamptz | timestamp |
| asset_maintenance_assignees.AssignedAt | timestamptz | timestamp |
| component_units.CreatedAt | timestamptz | timestamp |
| component_units.UpdatedAt | timestamptz | timestamp |
| component_units.DeletedAt | timestamptz | timestamp |
| license_seats.CreatedAt | timestamptz | timestamp |
| license_seats.UpdatedAt | timestamptz | timestamp |
| licenses.TerminationDate | timestamptz | timestamp |
| licenses.DeletedAt | timestamptz | timestamp |

> Lưu ý: các cột "timestamp without time zone" chỉ nằm ở các bảng tạo bằng self-heal raw SQL
> (component_units, asset_maintenances, asset_maintenance_assignees, license_seats) + DeletedAt thêm tay.
> Các bảng EnsureCreated gốc dùng timestamptz — khớp model.

---

## B. FK DRIFT — model khai FK nhưng DB không có (7 FK) + behavior khác (1)


---

## B. FK DRIFT — model khai FK nhưng DB không có (7 FK) + behavior khác (1)

| # | FK (model) | DB thật | Data check | Rủi ro |
|---|---|---|---|---|
| B1 | FK_assignments_users_TargetId (SET NULL) | KHÔNG có — đã bị DROP có chủ đích (TargetId polymorphic: 3/12 user, 9/12 khác) | 0 orphan | **Model SAI → nên bỏ FK khỏi model** |
| B2 | FK_assets_system_positions_SystemPositionId | KHÔNG có | 4 dòng, 0 orphan | Có thể thêm vào DB |
| B3 | FK_assets_assignments_CurrentAssignmentId | KHÔNG có (chỉ có unique index) | 0 orphan | Có thể thêm vào DB |
| B4 | FK_users_departments_DepartmentId | KHÔNG có | - | Có thể thêm vào DB |
| B5 | FK_users_locations_LocationId | KHÔNG có | - | Có thể thêm vào DB |
| B6 | FK_accessory_checkouts_users_CreatedByUserId | KHÔNG có | **4 ORPHAN** (CreatedByUserId trỏ user đã xóa) | **Thêm FK sẽ FAIL → phải dọn dữ liệu trước** |
| B7 | FK_consumable_checkouts_users_CreatedByUserId | KHÔNG có | **1 ORPHAN** | **Thêm FK sẽ FAIL → phải dọn dữ liệu trước** |
| B8 | licenses.CompanyId behavior | model RESTRICT | DB SET NULL | **Behavior khác nhau → chọn 1 hướng** |

> B1 là drift nghiêm trọng nhất: model khai FK tới users nhưng DB đã bỏ vì TargetId đa hình
> (User/Location/SystemPosition). Baseline sinh từ model sẽ TẠO LẠI FK này và vỡ với dữ liệu target≠user.
> → Bắt buộc sửa model trước baseline.

---

## C. INDEX DRIFT — model có, DB không có (20 index, trong đó 5 unique)

### C1. Thiếu UNIQUE index (5) — model yêu cầu, DB không enforce
1. IX_manufacturers_Code (data sạch: 0 null, 0 dup)
2. IX_manufacturers_Name (0 dup)
3. IX_suppliers_Code (0 null, 0 dup)
4. IX_suppliers_Name (0 dup)
5. IX_users_Email (0 dup)

### C2. Thiếu non-unique index (15)
1. IX_accessory_checkouts_CreatedByUserId
2. IX_asset_maintenance_assignees_UserId
3. IX_asset_maintenances_InspectedById
4. IX_asset_maintenances_SupplierId
5. IX_assets_SystemPositionId
6. IX_consumable_checkouts_CreatedByUserId
7. IX_departments_CompanyId
8. IX_departments_ManagerId
9. IX_license_seats_SystemPositionId
10. IX_licenses_Name
11. IX_licenses_SupplierId
12. IX_system_infos_CompanyId
13. IX_system_positions_SystemInfoId
14. IX_users_DepartmentId
15. IX_users_LocationId


---

## D. CHECK CONSTRAINT DRIFT — 1

| # | Constraint | DB thật | Model | Ảnh hưởng |
|---|---|---|---|---|
| D1 | CK_license_seats_single_target (≤1 target) | CÓ | **KHÔNG khai báo** | Tạo DB mới từ baseline sẽ MẤT check này → cần thêm `HasCheckConstraint` vào model |

---

## E. COLUMN DEFAULT DRIFT — DB có default, model không khai báo (16 cột)

| Bảng.Cột | DB default |
|---|---|
| assets.Status | 0 |
| assets.IsConfirmed | false |
| consumables.Status | 1 |
| components.TrackingType | 0 |
| component_units.Status | 0 |
| accessory_checkouts.CheckoutType | 1 |
| accessory_checkouts.TargetId | gen_random_uuid() |
| accessory_checkouts.AssignedQty | 1 |
| accessory_checkouts.ReturnedQty | 0 |
| license_seats.SeatNumber | 0 |
| license_seats.CreatedAt | CURRENT_TIMESTAMP |
| license_seats.UpdatedAt | CURRENT_TIMESTAMP |
| asset_maintenance_assignees.AssignedAt | CURRENT_TIMESTAMP |
| asset_maintenances.CompanyId | '00000000-0000-0000-0000-000000000000' |
| asset_maintenances.IsWarranty | false |
| asset_maintenances.IsClosed | false |
| licenses.Reassignable | true |

> Nếu sinh baseline từ model hiện tại, DB mới sẽ MẤT các default này. Vì "mark applied without
> running" nên DB thật không vỡ, nhưng DB mới tạo từ baseline sẽ thiếu. Cần quyết định: thêm
> `HasDefaultValueSql` vào model để khớp 100%, hoặc chấp nhận bỏ.

---

## F. TÊN CONSTRAINT/INDEX KHÁC NHAU (không phải drift chức năng — gây noise diff)

| DB thật | Model | Ghi chú |
|---|---|---|
| asset_maintenances_pkey | PK_asset_maintenances | |
| asset_maintenance_assignees_pkey | PK_asset_maintenance_assignees | |
| component_units_pkey | PK_component_units | |
| FK_asset_maintenance_assignees_maintenances_MaintenanceId | FK_asset_maintenance_assignees_asset_maintenances_MaintenanceId | |
| (hàng loạt check `*_not_null`) | — | EnsureCreated cũ tạo, vô hại, sẽ không tái sinh |

---

## G. DỮ LIỆU CẦN DỌN TRƯỚC NẾU THEO HƯỚNG "THÊM FK VÀO DB"

- accessory_checkouts.CreatedByUserId: **4 dòng orphan** (user đã bị xóa khỏi users)
- consumable_checkouts.CreatedByUserId: **1 dòng orphan**
- → Cần xác định user id bị mất và xử lý (đặt NULL hoặc khôi phục user) TRƯỚC khi thêm FK.

---

## ĐỀ XUẤT HƯỚNG XỬ LÝ (chờ duyệt — KHÔNG tự ý thực thi)

**Nguyên tắc: baseline phải phản ánh schema THẬT (DB làm chuẩn), model sửa cho khớp DB — vì DB
đang chạy production data; không tạo constraint mới có thể làm vỡ dữ liệu.**

1. **A (type)**: Sửa model theo DB thật: bỏ maxLength gây hẹp hơn DB (manufacturers.Name 100→255,
   suppliers.Name; Code: bỏ NOT NULL + giữ text), các cột text thật → `HasColumnType("text")`. Với
   timestamp: thêm `HasColumnType("timestamp without time zone")` cho các cột A2.
2. **B1**: Bỏ `HasOne(AssignedUser).HasForeignKey(TargetId)` khỏi Assignment mapping (giữ index
   TargetType+TargetId). Navigation `AssignedUser` xử lý không qua EF FK.
3. **B2–B5**: Thêm FK vào DB thật (dữ liệu sạch) HOẶC bỏ FK khỏi model — quyết định theo độ tin cậy.
   Đề xuất: **thêm vào DB** vì quan hệ có thật (asset→system_position, asset→assignment,
   user→department/location).
4. **B6–B7**: Dọn 4+1 orphan (đặt CreatedByUserId = NULL — phù hợp SET NULL behavior) rồi thêm FK vào DB.
5. **B8**: Chọn behavior thống nhất licenses.CompanyId. Đề xuất giữ DB SET NULL (khớp các FK CompanyId
   khác) → sửa model từ RESTRICT → SET NULL.
6. **C**: Thêm 20 index vào DB thật (unique chỉ thêm sau khi xác nhận data sạch).
7. **D1**: Thêm `HasCheckConstraint("CK_license_seats_single_target", ...)` vào model LicenseSeat.
8. **E**: Thêm `HasDefaultValueSql` vào model cho 16 default (DB mới từ baseline giữ hành vi hiện tại).
   Hoặc chấp nhận mất default.
9. **F**: Chấp nhận khác tên (không sửa).

**Sau khi model & DB khớp 100% → mới sang Bước 3 (xóa Migrations cũ).**
---

## KẾT QUẢ SỬA (2026-08-14) — DRIFT VỀ 0

### Model sửa — AppDbContext.cs
1. **A1 (text)**: manufacturers/suppliers.Code → `text` nullable + `.IsRequired(false)`; suppliers/manufacturers.Name → varchar(255); system_infos/system_positions Code+Name → `text`; departments.Name → `text`; asset_maintenances.Title → `text`; component_units.SerialNo → `text`.
2. **A2 (timestamp without time zone)**: action_logs.DeletedAt, asset_maintenances.*, asset_maintenance_assignees.AssignedAt, component_units.*, license_seats.CreatedAt/UpdatedAt, licenses.DeletedAt/TerminationDate.
3. **B1**: Assignment — bỏ `HasOne(AssignedUser).HasForeignKey(TargetId)`, thêm `HasIndex(TargetId)` + `Ignore(AssignedUser)`.
4. **B8**: licenses.CompanyId (khối 2) RESTRICT → SET NULL.
5. **B7**: ConsumableCheckout.CreatedByUser config → SET NULL (trước đó EF emit NO ACTION).
6. **D1**: LicenseSeat `HasCheckConstraint("CK_license_seats_single_target", ...)`.
7. **E defaults**: assets.Status=0, IsConfirmed=false; consumables.Status=1; components.TrackingType=0; component_units.Status=0; accessory_checkouts.CheckoutType=1/TargetId=gen_random_uuid()/AssignedQty=1/ReturnedQty=0; license_seats.SeatNumber=0/CreatedAt/UpdatedAt=CURRENT_TIMESTAMP; asset_maintenance_assignees.AssignedAt=CURRENT_TIMESTAMP; asset_maintenances.CompanyId='0000...'::uuid/IsWarranty=false/IsClosed=false; licenses.Reassignable=true.

### DB sửa — st6d_db_sync.sql / migration_st6d_align.sql (docker psql, atomic)
1. Dọn 4+1 orphan CreatedByUserId → NULL (user đã bị xóa).
2. Thêm 6 FK: assets→system_positions (NO ACTION), assets→assignments (SET NULL), accessory/consumable_checkouts→users (SET NULL), users→departments/locations (SET NULL).
3. Thêm 20 index (5 unique: manufacturers.Code/Name, suppliers.Code/Name, users.Email + 15 non-unique).

### Verify loop (dotnet ef dbcontext script vs schema thật, name-agnostic)
So sánh tự động (compare.ps1) trên COLUMNS / FKS / INDEXES / DEFAULTS / CHECKS:
- COLUMNS: OK (no gaps) — khớp cả type + nullability.
- FKS: OK (no gaps) — mọi FK model đều có trong DB, delete-rule khớp.
- INDEXES: OK (no gaps) — 20 index đã vào đúng.
- DEFAULTS: OK (no gaps) — mọi default khớp sau chuẩn hóa.
- CHECKS: OK (no gaps) — CK_license_seats_single_target khớp. DB-only còn lại toàn `*_not_null`
  (legacy từ EnsureCreated cũ) — CHỈ khác tên, không phải drift chức năng (chấp nhận theo mục F).

### Còn lại (danh nghĩa, chấp nhận theo mục F — không phải drift chức năng)
- FK tên `FK_asset_maintenance_assignees_maintenances_MaintenanceId` (DB) vs
  `..._asset_maintenances_MaintenanceId` (model).
- PK tên `asset_maintenances_pkey` / `asset_maintenance_assignees_pkey` / `component_units_pkey`
  (DB) vs `PK_*` (model).
- Hàng loạt CHECK `*_not_null` của DB (EnsureCreated cũ).

### Trạng thái
- Server đã stop → build → start theo đúng quy trình (Aspire), health OK, DB `SELECT 1` OK.
- `Database Schema đã thay đổi` (model + DB đã hội tụ). Chờ duyệt trước Bước 3
  (xóa Migrations cũ, `dotnet ef migrations add InitialBaseline`, mark-applied, verify clone,
  bỏ self-heal `EnsureCreated()`→`Migrate()`).

