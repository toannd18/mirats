# Handoff 2026 08 12 — Phân quyền theo nhóm (Role-based Permission): Subtask D (Frontend "Phân quyền" + usePermission hook)

> Lộ trình: **A** Backend hardening ✅ → **B** API gán User↔Group + chống self-lockout ✅ → **C** Frontend trang "Nhóm" ✅ → **D** Frontend "Phân quyền" + hook `usePermission` ✅ → **E** Test + migrate data + changelog.
> Handoff này ghi nhận hoàn thành **Subtask D** — dừng chờ review trước Subtask E.

---

## 1. PermissionMatrixPage — sửa lỗi + gán nhóm cho User

| File | Thay đổi |
|---|---|
| `src/pages/PermissionMatrixPage.tsx` | **Rewrite**: ProTable hiển thị **dữ liệu thật** (không còn ma trận "NotSet giả"): cột Người dùng / **Nhóm** (tags thật từ `GET /users`) / **Quyền được cấp** (số lượng thật tính từ `permissions/matrix`) / Thao tác (**Gán nhóm**). Dòng mở rộng hiển thị **danh sách quyền được cấp theo Resource** (từ catalog `GET /permissions`). |
| `src/components/users/...` | Modal **"Gán nhóm"** (inline trong page): multi-select nhóm → `PUT /users/{id}/groups` → hiển thị lỗi **SELF_LOCKOUT** từ backend + reload. |

## 2. Hook `usePermission` + wire vào nút thật

| File | Thay đổi |
|---|---|
| `src/hooks/usePermission.ts` | **Mới**: `usePermission(code)` + `usePermissionMap()` — gọi `GET /permissions/check` (cache module-level, fail-closed khi lỗi), Superuser luôn true, key Grant(1) → true. |
| `MaintenanceTable.tsx` · `AssetMaintenanceSection.tsx` | Nút **Xóa bảo trì** chuyển từ `isSuperUser()` → **`usePermission('assets.edit')`** (khớp policy backend). |

## 3. 🐛 Fix backend phát hiện khi demo (không chỉ frontend)

1. **`GET /permissions/check` claim bug**: lấy `ClaimTypes.NameIdentifier` (= Keycloak `sub` UUID) trước `preferred_username` → không tìm thấy local user → **permissions luôn rỗng** → `usePermission` luôn false. Fix: ưu tiên claim **`local_user_id`** (mirror `PermissionHandler`), fallback `preferred_username`; thêm bypass realm role (`superuser`/`admin`) vào `isSuperUser` + `permissions["superuser"]`.
2. **`DELETE /maintenances/{id}`** chỉ `[Authorize]` (ai cũng xóa được qua API) — mâu thuẫn gate frontend. Fix: **`[Authorize(Policy = "assets.edit")`** (khớp các action khác của controller).

## 3b. 🔍 Sweep toàn diện pattern `sub`/`username` (sau review — lần thứ 3 class bug này xuất hiện)

Đã grep toàn bộ solution (`ClaimTypes.NameIdentifier` / `"sub"` / `preferred_username` / `local_user_id`) và sửa **5 điểm còn sót** cùng pattern (ưu tiên `local_user_id`, fallback `preferred_username`/`sub`):

| File | Trước | Sau |
|---|---|---|
| `ImportExportController.GetCurrentUserId` | chỉ đọc `sub` (Keycloak UUID) | **`local_user_id` ưu tiên** (hiện chưa được gọi — code chết, nhưng là cạm bẫy FK violation) |
| `CompanyScopeService.GetCurrentUserId` | `sub` làm cache key | `local_user_id` ưu tiên |
| `UsersController.GetCurrentUser` (`GET /users/me`) | match theo `Username` | **`local_user_id` ưu tiên** (khỏi query + bền khi đổi username) |
| `ConsumablesController.GetCurrentUserIdAsync` | match theo `Username` | `local_user_id` ưu tiên |
| `ActionLogService` (LogAction + GetCurrentUserIdAsync) | match theo `Username` | `local_user_id` ưu tiên |

**Đã xác minh ĐÚNG (không cần sửa):** `AdminController`/`ComponentsController`/`ComponentUnitsController`/`GroupsController`/`UsersController(GetCurrentUserId)`/`PermissionHandler`/`PermissionsController`/`CurrentUserService` — đều đã ưu tiên `local_user_id`. Frontend `getUserInfo().id` đọc `sub` nhưng **chưa được gọi ở đâu** (code chết).


## 4. Xác minh (Subtask D) — ảnh thật + demo end-to-end

**Demo data đã tạo (ghi rõ để tái hiện):** gán **admin → Superuser group**; tạo user **demoperm** (no group → gán **Viewer group** chỉ có `assets.view`); tạo 1 bản ghi **maintenance demo** (Laptop HP).

- 📸 `permissions-matrix-fixed-1440.png` — trang `/permissions` hiển thị **dữ liệu thật**: admin có nhóm **Superuser**, dòng mở rộng liệt kê quyền theo Resource (`assets:`, `licenses:`, `system: admin superuser`, `users:`...) — **không còn "NotSet giả"**.
- 📸 `groups-list-membercount-1440.png` — `/groups`: **Superuser "Thành viên" = 1** (admin đã gán), Admin = 0 — cột cập nhật đúng.
- 📸 `maintenances-admin-delete-visible-1440.png` — **admin** (có `assets.edit` qua Superuser group) thấy **nút Xóa** trên bản ghi maintenance.
- 📸 `maintenances-demoperm-no-delete-1440.png` — **demoperm** (chỉ `assets.view`) **KHÔNG thấy nút Xóa** nhưng vẫn thấy bản ghi — usePermission ẩn/hiện đúng theo permission thực tế.

`tsc --noEmit` **0 lỗi** · Console trình duyệt (bản cuối) **0 error/warning** · Backend build **0 lỗi** · Backend tests **125/125**.

## 5. File đã tạo/sửa (Subtask D)
- **Tạo**: `src/hooks/usePermission.ts`
- **Rewrite**: `src/pages/PermissionMatrixPage.tsx`
- **Sửa frontend**: `MaintenanceTable.tsx`, `AssetMaintenanceSection.tsx`
- **Sửa backend**: `PermissionsController.cs` (CheckPermissions claim fix + realm bypass), `AssetMaintenancesController.cs` (DELETE policy `assets.edit`)

## 🏁 Kết thúc Subtask D — chờ review trước Subtask E
Mốc dừng đúng như đã thống nhất. Subtask E còn lại: test migration dữ liệu (map role cũ → group, không thu hẹp quyền) + test tổng + changelog.
