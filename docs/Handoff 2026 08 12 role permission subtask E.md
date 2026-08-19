# Handoff 2026 08 12 — Phân quyền theo nhóm (Role-based Permission): Subtask E (Migration data + test tổng hợp + changelog cuối) 🏁

> Lộ trình module Phân quyền: **A** Backend hardening ✅ → **B** API gán User↔Group + chống self-lockout ✅ → **C** Frontend "Nhóm" ✅ → **D** Frontend "Phân quyền" + hook `usePermission` ✅ → **E** Migration data + test tổng hợp + changelog ✅
> Handoff này là **hạng mục cuối** của module — ĐÓNG module Phân quyền.

---

## 1. Migration dữ liệu cũ → nhóm (không thu hẹp quyền hiện có)

**`Infrastructure/Persistence/PermissionMigration.cs`** (mới) + wire vào startup `Program.cs` (v7):
- **Map role cũ → nhóm mới**: user legacy có flag DB `IsSuperUser = true` (tương đương role Keycloak superuser/admin) được gán vào nhóm hệ thống **"Superuser"** (đã seed đủ 76 permission ở Subtask A).
- **Nguyên tắc an toàn tuyệt đối**: chỉ **THÊM** membership, không gỡ gì, không đổi `UserPermission`/`GroupPermission` hiện có, **idempotent** (chạy lại không trùng lặp) → **KHÔNG BAO GIỜ thu hẹp quyền hiện có**.
- Legacy realm-role user (bypass qua `realm_access`) giữ nguyên bypass (PermissionHandler step 1) — migration không đụng.

**Xác minh trên hệ thống thật** (restart AppHost): user `ndkien` (IsSuperUser=true, trước đó groups=0) → sau startup **tự động có nhóm [Superuser]**; user thường `demo.user`/`nhbkien` không bị đụng.

## 2. Test mới (Subtask E) — `PermissionMigrationTests.cs` (7 test)

| Test | Kiểm chứng |
|---|---|
| `LegacySuperUser_NoGroup_AssignedToSuperuserGroup` | Migration gán đúng user legacy vào nhóm Superuser |
| `LegacySuperUser_AlreadyAssigned_NotDuplicated` | Idempotent — chạy 2 lần không trùng lặp |
| `RegularUser_NotAssigned` | User thường không bị gán |
| `Migration_DoesNotTouchExistingPermissions` | Chỉ thêm UserGroup; UserPermission + 76 GroupPermission không đổi |
| **`LegacySuperUser_AfterMigration_StillHasFullAccess`** | **Regression quan trọng nhất**: admin cũ sau migration vẫn PASS 9 policy đại diện (assets.view/delete, licenses.create, users.edit, customfields.delete, import, reports.view, admin, superuser) — không mất quyền |
| `RegularUser_AccessUnchangedByMigration` | User thường giữ đúng assets.view, không hơn không kém |
| `EndToEnd_SeededSystem_AuthorizationWorks` | **Test tổng hợp**: hệ thống seed đầy đủ — admin(Superuser group) PASS 20 policy đại diện mọi module · viewer(chỉ assets.view) đúng · nobody default deny |

## 3. Xác minh cuối module
- Backend `dotnet build`: **0 lỗi**.
- Backend tests: **132/132 passed** (125 cũ + **7 mới**).
- Frontend `tsc --noEmit`: **0 lỗi** (chưa đổi frontend ở E).
- Migration chạy thật trên startup: `ndkien` → Superuser group (xác nhận qua `GET /users`).

## 🏁 Tổng kết module Phân quyền (A→E)
- **A** — PermissionCatalog (76 policy, fix `customfields.delete`), PermissionHandler dùng `local_user_id` + bỏ auto-create, seed Superuser/Admin + `IsSystem`, `GET /api/v1/permissions`.
- **B** — `PUT /users/{id}/groups`, ActionLog cho mọi thay đổi nhóm/quyền, **PermissionLockoutGuard** (chống tự khóa quyền — chỉ `admin` = khả năng quản lý, bảo vệ cả Admin thường).
- **C** — Frontend "Nhóm": ProTable + Modal, phân quyền theo Resource từ catalog API (hết hardcode thiếu key), sửa serialization `PermissionValue` (int).
- **D** — Frontend "Phân quyền" (dữ liệu thật + gán nhóm), hook `usePermission` wire nút Xóa bảo trì, fix `/permissions/check` claim bug, policy `DELETE /maintenances` → `assets.edit`, sweep toàn diện pattern `sub`/`username` (5 điểm).
- **E** — Migration dữ liệu cũ → nhóm (không thu hẹp quyền) + 7 test + changelog.

**Changelog module**: handoff A `…role permission subtask A.md` · B · C · D · E (file này).
