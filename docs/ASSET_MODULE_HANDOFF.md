# Asset Management Module — Handoff Document (Rewritten 09/08/2026)

**Date:** 09/08/2026
**Project:** AspireReact
**Stack:** .NET 10 (CQRS + MediatR + EF Core) · React 19 + Ant Design Pro v3 (`@ant-design/pro-components`) · PostgreSQL 18.3 · Keycloak 26.6
**Trạng thái:** Backend + Frontend Asset module hoàn chỉnh; còn một số quyết định/việc nhỏ cần xử lý (xem mục 9).

---

## 1. Entity Asset (`Domain/Entities/Asset.cs`)

Assets là **vật lý duy nhất** (quantity = 1, `Physical = true`). Software license nằm module `License` riêng.

| Property | Type | Notes |
|---|---|---|
| `AssetTag` | string | **Unique, immutable** sau khi tạo, có index |
| `Name` | string | Required |
| `Serial` | string? | Optional |
| `CompanyId` | Guid? | FMCS multi-tenant |
| `CurrentAssignmentId` | Guid? | `null` = available; trỏ tới Assignment đang active |
| `SystemPositionId` | Guid? | Vị trí lắp đặt khi gán cho SystemPosition |
| `Status` | `AssetStatus` (enum int) | **Nguồn sự thật DUY NHẤT** — `Pending=0, Deployed=1, Archived=2` |
| `IsConfirmed` | bool | `true` = đã xác nhận; asset mới tạo đã `true` ngay từ `CreateAssetCommand` |
| `LocationId` | Guid? | Vị trí vật lý hiện tại |
| `ModelId` / `SupplierId` | Guid? | FK |
| `CheckoutCounter` / `CheckinCounter` | int | Audit counters |
| `Requestable` | bool | luôn `false` |

> ✅ **`bool Archived` ĐÃ BỊ XÓA** — không còn tồn tại ở entity/DB. Chỉ dùng `Status == AssetStatus.Archived`.

**Đã xóa:** `StatusId` (FK), `RtdLocationId`, `Archived` (bool), `ExpectedCheckin` (xem mục 9).

---

## 2. State Machine

```
Pending (Chờ cấp phát) ⇄ Deployed (Đã cấp phát) → Archived (Đã lưu trữ, terminal)
       └──────────────→ Archived  (Lưu trữ/Thanh lý)
```

- **Checkin (Thu hồi):** Deployed → **Pending** (KHÔNG phải Archived). `Status = Pending`, `CurrentAssignmentId = null`, `LocationId = {vị trí thu hồi}`.
- **Checkout (Cấp phát):** Pending → Deployed. 3 loại target: User/Department (không có Location) + SystemPosition (bắt buộc Location).
- **Archive (Lưu trữ):** Pending → Archived (terminal). Backend hiện **không chặn** `Deployed → Archived` trực tiếp (chỉ chặn `AlreadyArchived`) — xem mục 9.
- **Unarchive (Mở lại):** Archived → Pending.

---

## 3. Action Matrix (frontend `getAssetActions`)

File: `frontend/src/types/asset.ts` — **dùng chung cho List + Detail**. Không nhét if/else rải rác trong JSX.

```ts
export type AssetAction = 'view' | 'edit' | 'allocate' | 'recall' | 'archive' | 'confirm' | 'delete' | 'unarchive';

getAssetActions({ status, isConfirmed }):
  !isConfirmed                     → ['view','edit','confirm','delete']        // Nháp
  status == 'Archived'             → ['view','unarchive']                     // Đã lưu trữ
  status == 'Deployed'             → ['view','edit','recall']                 // Đã cấp phát
  else (Pending + confirmed)       → ['view','edit','allocate','archive']     // Chờ cấp phát
```

Màu trạng thái thống nhất: Nháp/Archived = `default` (xám), Pending = `blue`, Deployed = `green` (`ASSET_STATUS_COLORS`)
---

## 4. Backend — CQRS Commands & Endpoints

### Commands (`Application/Assets/Commands/`) — 10 file
`Create` · `Update` · `Delete` · `Confirm` · `Checkout` · `Checkin` · `Archive` · `Unarchive` · `Audit` · `BulkUpdate` · `AcceptDecline`

**Quy tắc quan trọng:**
- `CreateAssetCommand` → **`IsConfirmed = true`** + `Status = Pending` ngay (nút "Xác nhận tạo" là xác nhận cuối — không có bước confirm thứ 2).
- `UpdateAssetCommand` → gate `IsConfirmed`: chỉ `Name`/`Notes` được sửa; field khác → log `UpdateRejected`.
- `DeleteAssetCommand` → chặn `IsConfirmed == true` HOẶC `CurrentAssignmentId != null`.
- `CheckinAssetCommand` → **`Status = Pending`** (không bao giờ tự Archive). Dùng raw SQL `SELECT * FROM assets WHERE "Id" = {0} FOR UPDATE`.
- `ArchiveAssetCommand` → `LocationId` **bắt buộc**; guard `ALREADY_ARCHIVED`; clear `CurrentAssignmentId` + `SystemPositionId`.
- `CheckoutAssetCommand` → 3 target; SystemPosition bắt buộc `LocationId`; Company isolation (`COMPANY_MISMATCH`).

### Endpoints (`/api/v1/assets`)
`GET /assets` (search, status, categoryId, locationId; trả `Status` dạng **string**, `assignedTo.name` đã batch-resolve) · `GET /{id}` · `POST` · `PUT` · `DELETE` · `POST /{id}/confirm` · `POST /{id}/checkout` · `POST /{id}/checkin` · `POST /{id}/archive` · `POST /{id}/unarchive` · `POST /{id}/audit` · `GET /{id}/history` · `GET /action-logs?itemType=1&itemId={id}`

---

## 5. ActionLog & Snapshot Fields (immutable audit trail)

Entity `ActionLog` hiện có thêm 2 cột **write-time snapshot**:
- `LocationName` (string?) — resolve tên vị trí tại thời điểm log (không join live).
- `TargetSystemInfoName` (string?) — tên SystemInfo cha khi target là SystemPosition.

`ActionLogService.LogAction` tự resolve (sync `FirstOrDefault()`) — **không đổi interface/call-site**:
```csharp
LocationName = locationId.HasValue ? _context.Locations.Where(l => l.Id == locationId.Value).Select(l => l.Name).FirstOrDefault() : null,
TargetSystemInfoName = (targetType == SystemPosition && targetId.HasValue)
    ? _context.SystemPositions.Where(sp => sp.Id == targetId.Value).Select(sp => sp.SystemInfo.Name).FirstOrDefault() : null
```

`ActionLogsController` (`/api/v1/action-logs`) trả `actionTypeValue`, `creatorName`, `targetName` (batch-resolve), `locationName`, `targetSystemInfoName`.

---

## 6. DB Schema & Cơ chế đồng bộ (QUAN TRỌNG)

> ⚠️ **App KHÔNG dùng EF Core migrations** — không có bảng `__EFMigrationsHistory`.
> Cơ chế schema: `db.Database.EnsureCreated()` (chỉ tạo DB lần đầu) + **raw `ALTER TABLE ... IF NOT EXISTS` trong `Program.cs`** (chạy mỗi lần khởi động).
> => **KHÔNG dùng `dotnet ef migrations add/update`** (sẽ lỗi). Muốn đổi schema: sửa khối raw-ALTER trong `Program.cs` + chạy SQL thủ công lên DB đang chạy.

### Bảng `assets` hiện tại (32 cột)
`Id, AssetTag, Name, Serial, Image, ModelId, LocationId, SupplierId, CompanyId, CurrentAssignmentId, PurchaseCost, PurchaseDate, WarrantyMonths, AssetEolDate, EolExplicit, LastCheckout, LastCheckin, LastAuditDate, NextAuditDate, CheckinCounter, CheckoutCounter, RequestsCounter, Physical, Requestable, Accepted, OrderNumber, Notes, CreatedAt, UpdatedAt, Status, IsConfirmed, SystemPositionId`

### Các thay đổi schema đã làm (đã áp DB + thêm vào Program.cs)
- ✅ `assets.SystemPositionId` (uuid) — đã thêm (bị thiếu trước đó).
- ✅ `assets.ExpectedCheckin` — đã DROP.
- ✅ `action_logs.LocationName` + `TargetSystemInfoName` — đã thêm + backfill.
- ✅ Drop 3 FK sai trên `assignments.TargetId` (assets/locations/users) — TargetId là cột polymorphic tự do.
- ✅ `IX_assignments_AssetId` → non-unique; `IX_assets_CurrentAssignmentId` → unique (one-to-one).

### Quan hệ Asset ↔ Assignment (AppDbContext)
- One-to-one: `Asset.CurrentAssignment ↔ Assignment` FK trên `assets.CurrentAssignmentId` (SetNull).
- One-to-many history: `Asset.ChildAssignments ↔ Assignment.Asset` FK trên `assignments.AssetId` (Cascade, non-unique).
---

## 7. Frontend Architecture

### Pages
- `pages/AssetListPage.tsx` — ProList card, action matrix, filter (search/status/location/category), empty state có hướng dẫn, `AssetArchiveModal` tích hợp.
- `pages/AssetDetailPage.tsx` — Lifecycle `<Steps>`, thông tin, "Tình trạng cấp phát", **Lịch sử = `<ProTable>`** (read-only, `search={false}`, `options={false}`), nút theo state machine.
- `pages/AssetFormPage.tsx` — Edit hạn chế (Name/Notes sau confirm).
- `pages/admin/AssetModelListPage.tsx` — CRUD model.

### Components (`components/assets/`)
| Component | Vai trò |
|---|---|
| `AssetAllocationModal.tsx` | Cấp phát — 3 target; SystemPosition label `{name} — {systemInfoName}` |
| `AssetRecallModal.tsx` | Thu hồi — vị trí thu hồi + ghi chú (**đã bỏ Alert cảnh báo Archived**) |
| `AssetArchiveModal.tsx` | **Mới** — Lưu trữ: chọn vị trí kho thanh lý (bắt buộc) + ghi chú + Alert warning |
| `AssetFormModal.tsx` | ⚠️ **LEGACY orphan** — không dùng, cần dọn |
| `ActionLogTimeline.tsx` | ⚠️ **ORPHAN** — đã thay bằng ProTable trong Detail, cần dọn |

### Services / Types
- `services/asset.service.ts` — typed API: `create/update/confirm/allocate/recall/archive/unarchive/getHistory`.
- `types/asset.ts` — `AssetStatus`, `normalizeAssetStatus()` (nhận number|string), `ASSET_STATUS_LABELS/COLORS`, `getAssetActions()`.

### Status normalization
Backend trả `Status` dạng **string** (`"Pending"`/`"Deployed"`/`"Archived"`). `normalizeAssetStatus()` xử lý cả số lẫn chuỗi (an toàn).

---

## 8. Changelog gần đây (tóm tắt các phiên vừa qua)

1. **Create flow UX**: 2 bước (Form → Review read-only) → nút "Xác nhận tạo"; asset tạo ra `IsConfirmed=true`, `Status=Pending`, sẵn sàng Cấp phát ngay. Không hiển thị field name/GUID; resolve tên Model/Location/Supplier/Company.
2. **Fix schema drift**: thêm `SystemPositionId`; drop `ExpectedCheckin`; drop FK sai; sửa raw SQL `"Id"` (3 file: Checkin/Checkout/ComponentsController).
3. **Quan hệ Asset↔Assignment sửa đúng**: one-to-one qua `CurrentAssignmentId` + history one-to-many (fix assignedTo stale sau checkin).
4. **Detail page**: stepper state machine đúng, allocation card, ProTable action log, archive modal, action buttons theo matrix.
5. **Archive flow**: tách biệt với Checkin — Archive yêu cầu LocationId, terminal.
6. **Snapshot fields**: `LocationName` + `TargetSystemInfoName` write-time.
7. **List page**: card mới (border màu, fields đủ, "Đang giữ"), action matrix đầy đủ (Xác nhận/Xóa/Mở lại/Lưu trữ), filter Category, empty state.

---

## 9. ⚠️ VIỆC CÒN DANG DỞ / QUYẾT ĐỊNH CẦN LÀM (tiếp mai)

### 🔴 Critical bug
1. **Routes `/assets/:id/allocate` và `/assets/:id/recall` KHÔNG tồn tại** trong `App.tsx` — List page đang navigate tới đó (`/assets/${id}/allocate`, `/assets/${id}/recall`) → bấm Cấp phát/Thu hồi trên card sẽ vào trang trống. **Cần fix**: tạo 2 route này render modal, hoặc đổi List page dùng `AssetAllocationModal`/`AssetRecallModal` inline như Detail page.

### 🟡 Quyết định cần chốt
2. **`ExpectedCheckin`**: đã xóa khỏi DB/entity/projection (theo yêu cầu cũ). Nhưng Issue 2 của task List page yêu cầu hiển thị "Ngày hẹn trả" trên card → **mâu thuẫn**. Nếu cần: re-add property vào `Asset.cs` + cột DB + projection list/detail + `CheckoutRequestDto` + `AllocateAssetPayload`. Quyết định rõ trước khi làm.
3. **Deployed → Archived trực tiếp**: backend `ArchiveAssetCommand` đang **cho phép** (chỉ chặn `AlreadyArchived`, không check `Status == Pending`). UI không hiện nút Lưu trữ cho Deployed. Quyết định: chặn backend (`if Status != Pending → error`) hay giữ nguyên.
4. **3 handler THIẾU `IActionLogService`**: `AcceptDeclineAssetCommand`, `AuditAssetCommand`, `BulkUpdateAssetsCommand` — chưa ghi log. Cần thêm.
5. **Filter Company / Model**: backend chưa có `companyId`/`modelId` param cho `/assets`. Nếu cần filter company (multi-tenant) → thêm `[FromQuery] Guid? companyId` + Where. Không tự lọc client (không scale).

### 🟢 Việc nhỏ / dọn dẹp
6. Xóa orphan: `AssetFormModal.tsx`, `ActionLogTimeline.tsx`.
7. Detail page chưa render nút "Mở lại" (unarchive) dù `getAssetActions` trả về — List đã có, Detail chưa. Cân nhắc thêm.
8. Dashboard page: label summary card chưa khớp semantic mới (Pending=Chờ cấp phát, Deployed, Archived) — backend `DashboardController` đã sửa, frontend `DashboardPage.tsx` chưa.
9. File migrations trong `Migrations/` là **orphan** (model snapshot vẫn còn `StatusId`/`RtdLocationId` cũ) — không dùng, có thể xóa hoặc giữ.

---

## 10. Cách build & chạy

```bash
# Backend
cd aspire-react/aspire-react.Server
dotnet build

# Frontend
cd aspire-react/frontend
npx tsc --noEmit
npm run dev        # hoặc để Aspire quản lý (Vite HMR)

# AppHost (Aspire) — postgres/keycloak/redis tự spin
cd aspire-react/aspire-react.AppHost && dotnet run
```

**Restart backend resource** (sau khi đổi C#): Aspire Dashboard → resource `server` → Restart; do restart dùng `--no-build`, nếu đổi code phải `dotnet build` trước.

**Đổi DB schema:** sửa khối `ALTER TABLE ... IF NOT EXISTS` trong `Program.cs` + chạy SQL thủ công lên container Postgres (psql) — KHÔNG dùng `dotnet ef`.

**Test nhanh qua API:** token Keycloak (`admin`/`Admin123!`, client `frontend`, realm `aspire-react`), gọi `GET /api/v1/health` để tìm port http của server.
