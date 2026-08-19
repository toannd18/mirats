# HANDOFF — Audit DateTime Kind Mismatch (Npgsql)

> **Ngày:** 2026-08-15 · **Mục đích:** ghi nhận kết quả audit các cột `timestamp without time zone`
> đang được ghi bằng `DateTime.UtcNow` (Kind=UTC).
> **TRẠNG THÁI: ĐÃ FIX (phiên 2026-08-15, Task 9)** — NHÓM A–D đã áp dụng `DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified)`
> tại mọi entity initializer + controller write site, kèm fix bổ sung `AppDbContext.SaveChangesAsync` (nhánh riêng cho `ComponentUnit`).
> Xác minh bằng API thật + DB verify; chi tiết trong `docs/HANDOFF_LATEST.md` mục 9. NHÓM E (action_logs.DeletedAt) vẫn chưa có write site → an toàn.

## 1. Bối cảnh & cách phát hiện

- Lỗi gốc: Npgsql **từ chối** ghi `DateTime` có `Kind=UTC` vào cột `timestamp without time zone`
  (exception: `Cannot write DateTime with Kind=UTC to PostgreSQL type 'timestamp without time zone'`).
- Lỗi bị "lọt" qua `dotnet test` vì **EF InMemory không enforce** Npgsql DateTime Kind — test pass
  nhưng DB thật mới lỗi (đã xảy ra với License trước Task E, và với Maintenance ở mục 3).
- Nguồn mismatch: entity **initializer** (`= DateTime.UtcNow`) hoặc controller/handler gán trực tiếp
  `DateTime.UtcNow` vào property có cột `without time zone`.
- ST6d (2026-08-14) đã đổi nhiều cột sang `without time zone` → các insert/update sau đó mới lỗi.

## 2. Các cột `timestamp without time zone` bị ghi `DateTime.UtcNow` (Kind=UTC)

### 🟥 NHÓM A — `asset_maintenances` (7 cột, bị ghi nhiều nơi) — MỨC CAO
| Cột | Entity initializer | Controller ghi `DateTime.UtcNow` |
|---|---|---|
| `CreatedAt` | `AssetMaintenance.cs:71` | `AssetMaintenancesController.cs:308` |
| `UpdatedAt` | `AssetMaintenance.cs:72` | `:309, :370, :409, :482, :525, :555` |
| `DeletedAt` | — | `:408` |
| `ClosedAt` | — | `:480` |
| `InspectedAt` | — | `:524` |
| `StartDate` / `CompletionDate` | — | từ request (frontend ISO UTC) — **ĐÃ FIX**: `DateTime.SpecifyKind(..., Unspecified)` tại `AssetMaintenancesController.cs:293-294` (Create), `:367` (Update CompletionDate) |

### 🟥 NHÓM B — `asset_maintenance_assignees` (1 cột) — MỨC CAO
- `AssignedAt`: `AssetMaintenanceAssignee.cs:16` (initializer) + `AssetMaintenancesController.cs:320, :620`.

### 🟥 NHÓM C — `component_units` (3 cột) — MỨC CAO
| Cột | Entity initializer | Controller |
|---|---|---|
| `CreatedAt` | `ComponentUnit.cs:22` | (insert qua stock-in/allocation) |
| `UpdatedAt` | `ComponentUnit.cs:23` | `ComponentAllocationService.cs:131,216,309,353,407` (insert/allocate/delete — Task CLEANUP đã extract logic khỏi controller) |
| `DeletedAt` | — | `ComponentAllocationService.cs:406` (soft delete — Task CLEANUP đã extract khỏi `ComponentUnitsController`) |

### 🟧 NHÓM D — `licenses` (2 cột) — MỨC TRUNG BÌNH
- `DeletedAt`: `LicensesController.cs:359` (`l.DeletedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified)` — xóa license).
- `TerminationDate`: từ request (frontend ISO UTC) — **ĐÃ FIX**: `DateTime.SpecifyKind(..., Unspecified)` tại `LicensesController.cs:268` (Create), `:327` (Update).

### 🟨 NHÓM E — `action_logs` (1 cột) — MỨC THẤP
- `DeletedAt`: `DateTime?` (null default) — **chưa có write site nào ghi UtcNow** → an toàn hiện tại.

## 3. Xác minh bug Maintenance CÓ THẬT (đã gọi API thật, 2026-08-15)

- `POST /api/v1/maintenances` với body tối giản `{assetId, type:1, title:"QA-TEST-DATETIME-KIND", notes}`
  (KHÔNG gửi ngày để cô lập lỗi initializer `CreatedAt/UpdatedAt`).
- Kết quả: **HTTP 500** — log server:
  `System.ArgumentException: Cannot write DateTime with Kind=UTC to PostgreSQL type 'timestamp without time zone'`.
- → **Maintenance create đang HỎNG thật trên DB Postgres** (cùng lớp lỗi License trước Task E).
- Không có record test sót lại (500 rollback transaction — verified `COUNT = 0`).

## 4. Danh sách AN TOÀN (cột `timestamp with time zone` — ghi UtcNow OK)

Asset, Consumable, Accessory, Component (`CreatedAt/UpdatedAt/PurchaseDate`), ComponentAssignment,
ConsumableCheckout, AccessoryCheckout, **Assignment.AssignedAt**, Company, CustomField,
PermissionGroup, User, ActionLog (`ActionDate/CreatedAt`), License (`CreatedAt/UpdatedAt/ExpirationDate/PurchaseDate`),
LicenseSeat (`AssignedAt`).

## 5. Đã fix trước đó (tham chiếu — Task E)

- `LicenseSeat.CreatedAt/UpdatedAt` + `LicensesController` checkout/checkin `seat.UpdatedAt` →
  `DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified)`.
- Chỉ nhánh License; các nhóm A–D **CHƯA** được fix.

## 6. Đề xuất hướng xử lý (cho phiên sau — CHƯA thực hiện)

- Đồng nhất dùng `DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified)` cho:
  - Entity initializer: `AssetMaintenance.CreatedAt/UpdatedAt`, `AssetMaintenanceAssignee.AssignedAt`,
    `ComponentUnit.CreatedAt/UpdatedAt`.
  - Controller: `AssetMaintenancesController` (308–620), `ComponentUnitsController` (58–59),
    `LicensesController` (356 `l.DeletedAt`).
- Cân nhắc `TerminationDate`/`StartDate`/`CompletionDate` (từ request ISO UTC): chuyển Kind→Unspecified
  khi nhận payload, hoặc đổi cột sang `with time zone` (cần migration + backup).
  → **✅ ĐÃ XONG (2026-08-16):** nhánh chuyển Kind→Unspecified khi nhận payload đã áp dụng — xem mục 2
  (NHÓM A `StartDate`/`CompletionDate`, NHÓM D `TerminationDate`); không cần migration.
- Nên thêm test guard: không thể phát hiện qua EF InMemory — cân nhắc Testcontainers (đã nằm trong
  tồn đọng Task 3) để enforce đúng kiểu cột Postgres.

## 7. RE-SCAN (2026-08-16, sau Task M1) — xác nhận audit KHÔNG bỏ sót cột

- **Bối cảnh:** khi verify Task M1, Component create kèm `purchaseDate` từng trả **HTTP 500**, khiến
  nghi ngờ audit Task 9 bỏ sót cột `Component.PurchaseDate`.
- **Re-scan authoritative (query `information_schema` toàn DB):** đúng **16 cột `timestamp without
  time zone`** — `action_logs.DeletedAt`, `asset_maintenance_assignees.AssignedAt`,
  `asset_maintenances.{ClosedAt,CompletionDate,CreatedAt,DeletedAt,InspectedAt,StartDate,UpdatedAt}`,
  `component_units.{CreatedAt,DeletedAt,UpdatedAt}`, `license_seats.{CreatedAt,UpdatedAt}`,
  `licenses.{DeletedAt,TerminationDate}` → **khớp 100% mục 2 (NHÓM A–E) + LicenseSeat**, KHÔNG còn cột nào khác.
- **`Component.PurchaseDate` là `with time zone`** (migration `InitialBaseline` L369 + DB xác nhận) → **KHÔNG phải bug**.
  - 500 lúc đó là do test gửi `"2024-01-01"` (date-only → Kind=Unspecified) — Npgsql TỪ CHỐI Kind=Unspecified
    cho cột `with time zone` (`Cannot write DateTime with Kind=Unspecified to ... 'timestamp with time zone'`).
  - Re-test với `"2024-01-01T00:00:00.000Z"` (Kind=Utc) → **201 OK**. Frontend gửi `dayjs(...).toISOString()` (Kind=Utc) → luôn OK.
- **`SaveChangesAsync` hook** (AppDbContext:513-537) chỉ ghi `DateTime.UtcNow` cho IAuditable có cột `with time zone`;
  cột `without time zone` của IAuditable chỉ có `ComponentUnit` (đã special-case Unspecified);
  `AssetMaintenance`/`LicenseSeat` KHÔNG phải IAuditable (set qua initializer `SpecifyKind(UtcNow, Unspecified)`) → **toàn bộ DateTime kind ĐÃ ĐÚNG, không cần sửa thêm.**

## 8. Task R — Đối chiếu lại toàn bộ danh sách với code thật (2026-08-16) — KHỚP 100%

- **Mục đích:** tài liệu cũ (mục 2/6) còn để `TerminationDate`/`StartDate`/`CompletionDate` là "chưa xử lý" —
  thực tế ĐÃ fix từ trước. Đối chiếu từng write site trong mục 2 với code thật hiện tại:
  - **NHÓM A** `asset_maintenances`: entity initializer `AssetMaintenance.cs:71-72` ✓; controller
    `AssetMaintenancesController.cs:308,309` (create), `:370` (update), `:408,409` (delete), `:480,482` (close),
    `:524,525` (inspect), `:555` (reopen) — mọi site `SpecifyKind(UtcNow, Unspecified)` ✓. `StartDate`/`CompletionDate`:
    Create `:293-294`, Update CompletionDate `:367` — **ĐÃ SpecifyKind** ✓.
  - **NHÓM B** `asset_maintenance_assignees.AssignedAt`: entity `AssetMaintenanceAssignee.cs:16` ✓; controller `:320, :620` ✓.
  - **NHÓM C** `component_units`: entity `ComponentUnit.cs:22-23` ✓. Logic soft-delete/write `UpdatedAt/DeletedAt`
    **đã extract sang `ComponentAllocationService.cs`** (Task CLEANUP) — `:131,216,309,353` (UpdatedAt), `:406,407`
    (DeletedAt/UpdatedAt trong `DeleteUnitAsync`) — mọi site SpecifyKind ✓ (cập nhật thay cho reference cũ
    `ComponentUnitsController.cs:58-59` vốn chỉ còn là map kết quả).
  - **NHÓM D** `licenses`: `DeletedAt` `LicensesController.cs:359` ✓ (lệch dòng so với :356 cũ do Task M1);
    `TerminationDate` Create `:268`, Update `:327` — **ĐÃ SpecifyKind** ✓.
  - **NHÓM E** `action_logs.DeletedAt`: vẫn **không có write site** → an toàn.
- **Kết luận:** toàn bộ NHÓM A–E + LicenseSeat đều ĐÃ xử lý đúng (`SpecifyKind(UtcNow, Unspecified)` hoặc không
  có write site). Không còn mục nào lạc hậu trong tài liệu này; khuyến nghị mục 6 về 3 field date đã hoàn thành.
