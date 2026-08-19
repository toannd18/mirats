# Handoff 2026 08 11 — Maintenance: Xác nhận đóng (khóa audit) + Ngữ cảnh cấp phát hiện tại

Tiếp nối task "Maintenance: Trang riêng `/maintenances` + Company-scoped visibility".
Task này bổ sung: cơ chế đóng/mở lại bản ghi bảo trì (khóa audit) và hiển thị ngữ cảnh cấp phát **hiện tại** của tài sản (đối chiếu với Snapshot đã có).

---

## 1. Domain Model — Close / Lock (audit-trail protection)

Thêm vào `AssetMaintenance`:
```csharp
public bool IsClosed { get; set; }        // default false
public DateTime? ClosedAt { get; set; }
public Guid? ClosedById { get; set; }
```
- **`IsClosed` ≠ trạng thái Hoàn thành** (đang suy từ `CompletionDate`). Đóng là lớp khóa riêng biệt: ghi đã có `CompletionDate` (Hoàn thành) nhưng vẫn **chưa đóng** → còn sửa được; ngược lại **không đóng được** khi chưa hoàn thành.
- Reopen **giữ nguyên** `ClosedAt`/`ClosedById` (lịch sử lần đóng gần nhất); mỗi vòng đóng/mở lại có `ActionLog` riêng (`ActionType.Close = 18`, `ActionType.Reopen = 19`).

## 2. API (`AssetMaintenancesController`)

| Endpoint | Quyền | Hành vi |
|---|---|---|
| `POST /maintenances/{id}/close` | `assets.edit` + cùng company (hoặc Superuser) | `CompletionDate == null` → `400 MAINTENANCE_NOT_COMPLETED_YET`; đã đóng → `400 MAINTENANCE_ALREADY_CLOSED`; thành công → `IsClosed=true, ClosedAt=now, ClosedById=user` + ActionLog `Close` |
| `POST /maintenances/{id}/reopen` | **Superuser only** (403 khác) | `IsClosed=false`, **giữ** ClosedAt/ClosedById + ActionLog `Reopen`; chưa đóng → `400 MAINTENANCE_NOT_CLOSED` |
| `PUT /maintenances/{id}` | — | **Guard đầu hàm**: `IsClosed == true` → `400 MAINTENANCE_CLOSED` trước mọi logic whitelist cũ — khóa tuyệt đối, không phân biệt whitelist/blacklist |
| `DELETE /maintenances/{id}` | Superuser (giữ nguyên) | **Vẫn cho phép xóa bản ghi đã đóng** (theo đề xuất mặc định của spec: đóng chặn SỬA, không chặn XÓA của Superuser) |

- Cross-company close → `Forbid()` (403), cùng chuẩn với update.
- `ActionType` mới: `Close = 18`, `Reopen = 19` (ItemType vẫn `AssetMaintenance`).

## 3. API — `currentContext` (ngữ cảnh cấp phát HIỆN TẠI)

- Chỉ trả ở `GET /maintenances/{id}` (chi tiết): object **sống** `currentContext` — join trực tiếp từ `Asset` hiện tại qua `AssetId`, **không cache, không lưu bảng**.
- Giải quyết luôn ghi chú cũ "bỏ qua so sánh vì response chi tiết asset không expose SystemInfo" — join thẳng trong query của Maintenance, không phụ thuộc Asset API.
- Chi tiết query giờ `.Include(Asset.SystemPosition).ThenInclude(SystemInfo)` + `.Include(Asset.Location)` + `.Include(Asset.CurrentAssignment)`, rồi tái sử dụng `BuildSnapshotAsync(m.Asset, ...)` để tính live context.
- JSON:
```json
"currentContext": {
  "systemInfoId": "...", "systemInfoName": "...",
  "systemPositionId": "...", "systemPositionName": "...",
  "locationId": "...", "locationName": "...",
  "assignedUserId": "...", "assignedUserName": "...",
  "departmentId": "...", "departmentName": "..."
}
```
- **Danh sách `GET /maintenances` KHÔNG trả currentContext** (tránh N+1 join/dòng — theo lựa chọn được phép của spec); chỉ thêm `isClosed` để hiển thị tag.

## 4. Frontend

### `MaintenanceListPage.tsx` (/maintenances)
- Cột **Trạng thái**: thêm `<Tag>Đã đóng</Tag>` (màu default + icon 🔒 `LockOutlined`) hiển thị **song song** với Hoàn thành/Đang thực hiện khi `isClosed`.
- Cột **Thao tác**:
  - **"Xác nhận đóng"** — hiện khi `!isClosed`; `CompletionDate == null` → **disabled + tooltip** "Cần nhập Ngày hoàn thành trước khi đóng bảo trì" (không ẩn hẳn); có CompletionDate → Popconfirm "Sau khi đóng, bản ghi sẽ bị khóa".
  - **"Mở lại"** — chỉ Superuser + `isClosed`, bấm → `Modal.confirm` cảnh báo phá khóa audit.
- **Chi tiết** giờ gọi `GET /maintenances/{id}` (list không mang currentContext) + resolve tên người đóng qua `GET /users/{id}` (fallback hiển thị id rút gọn).
- Modal Chi tiết: dòng **"Đã đóng lúc [ClosedAt] bởi [tên]"**; khối **"Ngữ cảnh hiện tại (dữ liệu sống)"**; khối Snapshot đánh dấu **`<Tag color="orange">Đã thay đổi</Tag>`** khi `currentContext.<field>Id != snapshot<Field>Id` (so sánh **theo ID** qua helper `contextChanged()`, không so tên hiển thị).

### `AssetMaintenanceSection.tsx` (Card trong Asset detail — áp dụng nhất quán)
- Cột Ngày hoàn thành: thêm tag "Đã đóng".
- Nút **Sửa** → **disabled + tooltip** "Bản ghi đã đóng, không thể sửa" khi `isClosed` (không mở form rồi mới lỗi).
- Nút **Đóng** (Popconfirm) + **Mở lại** (Modal.confirm, Superuser) + Xóa giữ nguyên.
- Modal Chi tiết: đầy đủ dòng "Đã đóng", khối "Ngữ cảnh hiện tại", marker "Đã thay đổi".

### `asset.service.ts`
- DTO `AssetMaintenanceDto` + `MaintenanceCurrentContext` interface (tên field mirror đúng JSON camelCase).
- `closeMaintenance(id)`, `reopenMaintenance(id)`.

---

## 5. Tests — `aspire-react.Tests`

**49/49 pass** (43 cũ + 6 mới) trong `AssetMaintenanceTests.cs`:
- `CloseMaintenance_NotCompleted_RejectedWithNotCompletedYet` — `CompletionDate == null` → `MAINTENANCE_NOT_COMPLETED_YET`, `IsClosed` vẫn false.
- `CloseMaintenance_Completed_SetsClosedAndLogsClose` — `IsClosed=true`, `ClosedAt`, `ClosedById` = current user, ActionLog `Close`.
- `UpdateMaintenance_ClosedRecord_RejectedWithClosed` — PUT dù whitelist hợp lệ → `MAINTENANCE_CLOSED`.
- `ReopenMaintenance_RegularUser_Forbidden` — 403, vẫn đóng.
- `ReopenMaintenance_Superuser_Succeeds_AndLogsReopen` — `IsClosed=false`, **giữ** `ClosedAt`, ActionLog `Reopen`.
- `GetMaintenance_CurrentContext_ReflectsLiveAsset_NotSnapshot` — đổi User/Department/System/Location của Asset sau khi tạo → `currentContext` theo giá trị mới, `Snapshot*` giữ giá trị cũ.

Helpers mới: `ReadCurrentContext`, `ReadSnapshotFields`, `ParseGuid` (parse theo ID để khớp assertion).

## 6. E2E trên server thật (đã chạy, server đang chạy build mới)

- Self-heal `Program.cs`: DB có `IsClosed` (NOT NULL default false), `ClosedAt`, `ClosedById` (verify qua docker psql).
- `POST /maintenances/{id}/close` (admin) → success, `isClosed:true`, `closedAt` + `closedById` trả về.
- `PUT /maintenances/{id}` sau khi đóng → **400 `MAINTENANCE_CLOSED`** (dù body chỉ đổi title hợp lệ).
- `POST /maintenances/{id}/reopen` (admin=superuser) → success, `isClosed:false`, `closedAt`/`closedById` **giữ nguyên**.
- `GET /maintenances/{id}` → `currentContext` trả `systemInfoName`, `systemPositionName`, `locationName` đúng asset thật; list trả `isClosed`.
- ActionLog: 4 dòng (Create, Close, Reopen, Delete) → dọn sạch test data.

## 7. Ràng buộc / quyết định

- **DELETE bản ghi đã đóng: vẫn cho phép Superuser** (mặc định đề xuất trong spec — đóng chặn SỬA, không chặn XÓA; không cần hỏi lại vì spec đã đưa mặc định rõ).
- **Reopen giữ** `ClosedAt`/`ClosedById` (lịch sử lần đóng gần nhất), không xóa.
- `currentContext` **chỉ ở detail**, không đưa vào list (hiệu năng; spec cho phép). Nếu sau này cần ở list, phải làm join 1 query thay vì N+1.
- Error format giữ nguyên convention: `{ status, message, error_code }` (`MAINTENANCE_NOT_COMPLETED_YET`, `MAINTENANCE_CLOSED`, `MAINTENANCE_ALREADY_CLOSED`, `MAINTENANCE_NOT_CLOSED`).
- Đóng không đòi hỏi role riêng — ai sửa được bản ghi (cùng company hoặc Superuser) thì đóng được (bước hoàn tất tự nhiên của luồng).
- `ClosedById` không có FK tới users (nhất quán với `CreatedById` — tránh chặn xóa/soft-delete user).

## ⚠️ Database schema đã thay đổi

- Cột `IsClosed` (boolean NOT NULL default false), `ClosedAt` (timestamp), `ClosedById` (uuid) trên `asset_maintenances`.
- **Không cần chạy lệnh EF** (project không dùng `dotnet ef migrations`) — schema tự áp dụng qua self-heal trong `Program.cs` (đã chạy khi restart resource `server`).
- **Cần restart .NET Aspire AppHost** để resource `server` load build mới (đã restart qua dashboard; port hiện tại `https://localhost:53679`).

