# Handoff 2026 08 12 — Maintenance: Người phụ trách (đa người, tối đa 5) + Bước Kiểm tra độc lập trước khi Đóng

Tiếp nối chuỗi task Maintenance (snapshot, company scope, close/reopen). Task này bổ sung:
1. **Người phụ trách** (Assignee) — nhiều người, tối đa 5, quan hệ nhiều-nhiều.
2. **Người kiểm tra** (Inspector) — bước nghiệm thu độc lập, tách biệt khỏi Đóng → luồng **Hoàn thành → Kiểm tra → Đóng** (3 bước).

---

## 1. Domain Model

### `asset_maintenance_assignees` (bảng mới, nhiều-nhiều)
| Cột | Kiểu | Ghi chú |
|---|---|---|
| `Id` | uuid PK | `gen_random_uuid()` |
| `MaintenanceId` | uuid NOT NULL | FK → asset_maintenances, ON DELETE CASCADE |
| `UserId` | uuid NOT NULL | FK → users, ON DELETE RESTRICT |
| `AssignedAt` | timestamp | default now |
- **Unique index `(MaintenanceId, UserId)`** chống trùng; max 5 validate ở tầng API (`MAX_5_ASSIGNEES`).

### `AssetMaintenance` — thêm field kiểm tra
```csharp
public Guid? InspectedById { get; set; }
public User? InspectedBy { get; set; }     // navigation (FK SetNull)
public DateTime? InspectedAt { get; set; }
public ICollection<AssetMaintenanceAssignee> Assignees { get; set; }
```
Độc lập hoàn toàn với `ClosedById/ClosedAt` — 2 bước riêng biệt.

## 2. Business rule — luồng 3 bước
- **Hoàn thành**: `CompletionDate != null` (như cũ).
- **Kiểm tra**: yêu cầu `CompletionDate != null` (khác → `400 MAINTENANCE_NOT_COMPLETED_YET`). Set `InspectedById/InspectedAt`. **Gọi lại nhiều lần được** (ghi đè = kiểm tra lại), không khóa. Quyền: cùng company hoặc Superuser (403 nếu khác công ty — tương tự Đóng).
- **Đóng**: điều kiện cũ + **mới** `InspectedById != null` — chưa kiểm tra → `400 MAINTENANCE_NOT_INSPECTED_YET` ("Cần kiểm tra trước khi đóng bảo trì.").
- **Sau khi Đóng**: khóa tuyệt đối như cũ — gồm cả danh sách Assignee + field Kiểm tra.

## 3. API
| Endpoint | Thay đổi |
|---|---|
| `PUT /maintenances/{id}` | Whitelist thêm `AssigneeUserIds: Guid[]` (replace-all; `null` = giữ nguyên, `[]` = xóa hết). Validate: max 5 → `400 MAX_5_ASSIGNEES`; user phải tồn tại; **cùng company với bản ghi** → `400 ASSIGNEE_COMPANY_MISMATCH` (Superuser + bản ghi floater `Guid.Empty` không giới hạn). **Sửa được sau khi Kiểm tra**, chỉ khóa từ Đóng. |
| `POST /maintenances/{id}/inspect` | **MỚI** — validate CompletionDate → set Inspector → `ActionLog ActionType.Inspect`. |
| `POST /maintenances/{id}/close` | Thêm validate `InspectedById != null` (giữ nguyên điều kiện cũ; thứ tự: IsClosed → CompletionDate → Inspected). |
| Create (cả 2 endpoint) | Nhận `AssigneeUserIds` (cùng validate). |
| Projections (list/detail) | Thêm `inspectedById/At/Name` + `assignees[{userId,name,assignedAt}]`. |

- `ActionType.Inspect = 20` (enum mới).
- `Program.cs` self-heal v4 + `docs/sql/migration_asset_maintenances.sql` UP/DOWN.

## 4. Frontend
- **Form tạo/sửa** (`MaintenanceListPage` + `AssetMaintenanceSection`): field **"Người phụ trách"** `Select mode="multiple"` searchable, **`maxCount={5}`**, load users qua `/users?companyId=` theo công ty của asset đã chọn (pattern checkout modal), `notFoundContent` động.
- **`MaintenanceTable`** (dùng chung `/maintenances` + tab SystemDetail): **không thêm cột** bảng chính. Row action thêm **"Đánh dấu đã kiểm tra"** (khi đã hoàn thành, chưa kiểm tra) / Tag **"Đã kiểm tra"** (khi đã inspect); nút **"Xác nhận đóng"** chỉ enabled khi `completionDate && inspectedById`, disabled + tooltip tương ứng.
- **Modal Chi tiết**: dòng **"Người phụ trách"** (Tag tên từng người / "Chưa phân công") + dòng **"Đã kiểm tra"** ([tên] lúc [time] hoặc nút "Đánh dấu đã kiểm tra" — disable + tooltip nếu chưa hoàn thành).
- `asset.service.ts`: DTO thêm `inspectedById/At/Name` + `assignees`; payload thêm `assigneeUserIds`; API `inspectMaintenance`.

## 5. Testing — **72/72 pass** (64 cũ + 8 mới)
`AssetMaintenanceTests.cs`:
- `UpdateAssignees_MaxFiveExceeded_ReturnsBadRequest` — 6 người → 400 `MAX_5_ASSIGNEES`.
- `UpdateAssignees_FiveAccepted_AndListedInDetail` — 5 người → OK + projection `assignees` đủ 5.
- `Close_BeforeInspect_ReturnsNotInspectedYet` — đã Hoàn thành, chưa Kiểm tra → 400 `MAINTENANCE_NOT_INSPECTED_YET`.
- `Inspect_BeforeCompletion_ReturnsNotCompletedYet` — chưa Hoàn thành → 400 `MAINTENANCE_NOT_COMPLETED_YET`.
- `FullFlow_Complete_Inspect_Close_Success_WithActionLogs` — đủ 3 mốc `Update`/`Inspect`/`Close` trong ActionLog, InspectedById = current user, IsClosed = true.
- `UpdateAssigneesAfterClose_ReturnsMaintenanceClosed` — PUT đổi assignee sau Đóng → 400 `MAINTENANCE_CLOSED`.
- `Assignees_CrossCompany_RegularUser_ReturnsCompanyMismatch` / `Assignees_Superuser_MayAssignUserOfAnyCompany` — company-scoping.
- `Create_WithAssignees_StoresRows_AndDetailReturnsThem` — tạo kèm assignee + detail trả về đủ.
- Đã cập nhật 4 test Close/Reopen cũ (thêm bước Inspect trước Close) — hậu quả đúng của luồng 3 bước.

## 6. E2E trên server thật (AppHost + Playwright, login admin)
- **API**: close trước inspect → `400 {"error_code":"MAINTENANCE_NOT_INSPECTED_YET"}`; inspect → 200 set `inspectedById/At`; danh sách trả `inspectedById` + `assignees`.
- **UI `/maintenances`**: dòng hoàn thành hiện "Đánh dấu đã kiểm tra" → bấm → thành "Đã kiểm tra" + nút "Xác nhận đóng" enable (trước đó disabled); dòng đã đóng hiện "Mở lại". Ảnh: `docs/screenshots/maintenance-detail-inspected-1440.png`.
- **Modal Chi tiết**: "Người phụ trách: Chưa phân công" + "Đã kiểm tra: admin lúc ..." sau khi inspect. Ảnh: `docs/screenshots/maintenance-create-assignee-1440.png` (modal tạo có field Người phụ trách).

## ⚠️ Database schema đã thay đổi (v4)
- Bảng mới `asset_maintenance_assignees` + 2 cột `InspectedById`/`InspectedAt` trên `asset_maintenances` + FK/index.
- **Không dùng `dotnet ef`** — raw SQL self-heal trong `Program.cs` (tự áp dụng khi server khởi động) + `docs/sql/migration_asset_maintenances.sql`.
- **Đã restart .NET Aspire AppHost** để load build mới (stop server resource → `dotnet build` → start).
