# Handoff 2026 08 12 — Phân quyền theo nhóm (Role-based Permission): Mục 0 audit + Subtask A (backend hardening)

> Lộ trình tổng thể (đã thống nhất với người dùng — **Phương án 1: giữ nguyên mô hình Group hiện có**): **A** Backend hardening (policy + handler + catalog + seed) → **B** API gán User↔Group + ActionLog + test → **C** Frontend trang "Nhóm" (ProTable + Modal) → **D** Frontend "Phân quyền" + hook `usePermission` → **E** Test + migrate data + changelog.
> Handoff này ghi nhận **mục 0 (audit)** + hoàn thành **Subtask A**.

---

## 1. Kết quả audit mục 0 — KHẢ NĂNG A: DB-lookup permission ĐÃ TỒN TẠI

### Trả lời câu hỏi treo
Hệ thống có **2 cơ chế song song, phân tầng**:
1. **JWT-claim (Keycloak realm role)** — `realm_access` chứa `superuser`/`admin` → **chỉ là bypass toàn quyền** (step 1 `PermissionHandler` + `CompanyScopeService.IsSuperUser()` cho query filter).
2. **DB-lookup permission** — `UserPermission` (Grant/Deny) → `UserGroup` → `GroupPermission` → wildcard `admin` → **Default Deny**. Đây là **nguồn phân quyền thực tế**, đã wire vào toàn bộ controller qua `[Authorize(Policy="...")]` (75 policy đăng ký trong `Program.cs`).

→ **Không cần đổi kiến trúc.** Giữ nguyên `PermissionGroup`/`GroupPermission`/`UserGroup`/`UserPermission` (ánh xạ với thiết kế đề xuất: Role→PermissionGroup, RolePermission→GroupPermission, UserRole→UserGroup, IsSystemRole→IsSystem).

### Policy thực tế (64 key dùng trong 21 controllers; 76 đăng ký sau fix)
- `assets.*`(7) · `accessories.*`(5) · `components.*`(5) · `consumables.*`(5) · `licenses.*`(5)
- `users.*`(4) · `companies.*`(4) · `models.*`(4) · `categories.*`(4) · `manufacturers.*`(4) · `suppliers.*`(4) · `departments.view` · `locations.*`(4) · `statuslabels.view` · `reports.view` · `import` · `customfields.*`(4) · `admin`

### 🔴 Bug phát hiện trong audit
- **`customfields.delete` dùng trong controller nhưng KHÔNG đăng ký policy** → `DELETE /custom-fields/{id}` crash `InvalidOperationException` tại runtime (đã fix trong Subtask A).
- **`PermissionHandler` tra user theo `preferred_username` thay vì `local_user_id`** → đổi username trên Keycloak = mất quyền âm thầm / tạo user ma (đã fix).
- **Nhánh "auto-create user" trong `PermissionHandler`** là side-effect trùng lặp với JIT provisioning (`OnTokenValidated` đã tạo user trước khi authorization chạy) → **đã loại bỏ**.
- **Frontend**: trang "Phân quyền" (`PermissionMatrixPage`) đọc sai shape API (`r.permissions` không tồn tại — API trả `userPermissions`/`groupPermissions`) → hiển thị toàn "NotSet" (sẽ sửa ở Subtask D). Trang "Nhóm" sơ khai, hardcode 42 permission thiếu ~30 (Subtask C).
- **Không có API gán User↔Group** — `user_groups` tồn tại nhưng không endpoint nào quản lý (Subtask B).

---

## 2. Subtask A — Backend hardening

| File | Thay đổi |
|---|---|
| `Infrastructure/Authorization/PermissionCatalog.cs` | **Mới**: 76 `PermissionDefinition` (Code/Resource/Action/Description) — **single source of truth**. |
| `Program.cs` | `AddAuthorization` đăng ký từ `PermissionCatalog` (**fix `customfields.delete`**); self-heal SQL `ALTER TABLE permission_groups ADD COLUMN IF NOT EXISTS "IsSystem"`; **seed 2 system group** "Superuser"/"Admin" (mọi permission Grant, `IsSystem=true`) — **chỉ khi chưa có system group, KHÔNG tự gán user → không đổi quyền hiện có**. |
| `Infrastructure/Authorization/PermissionHandler.cs` | **Fix**: resolve user qua claim `local_user_id` (fallback username cho legacy), **bỏ auto-create** (fail closed), dùng `AsNoTracking`. |
| `Domain/Entities/PermissionGroup.cs` | Thêm `IsSystem` (bool). |
| `Infrastructure/Persistence/AppDbContext.cs` | Fluent config `IsSystem` default false. |
| `Web/Controllers/PermissionsController.cs` | **Mới**: `GET /api/v1/permissions` — catalog group theo resource (`[Authorize]`). |
| `Web/Controllers/GroupsController.cs` | Guard `IsSystem` → **400 `SYSTEM_GROUP_LOCKED`** khi rename/delete; trả `isSystem` trong GET list/detail. |
| `aspire-react.Tests/PermissionTests.cs` | **Mới**: 11 test. |
| `docs/API.md` | Thêm `GET /api/v1/permissions`. |

### Seed mặc định (không thu hẹp quyền)
- Tạo `Superuser` + `Admin` (IsSystem=true) kèm đủ 76 `GroupPermission` Grant — **không gán user nào** → hành vi hiện tại không đổi (bypass cũ qua realm_access + `IsSuperUser` flag vẫn nguyên).
- Idempotent: chỉ chạy khi `!Any(g => g.IsSystem)`; mỗi `GroupPermission` kiểm tra tồn tại trước khi thêm (tránh vi phạm PK `(GroupId, PermissionKey)`).

## 3. Xác minh (Subtask A)
- `dotnet build` Server: **0 error**.
- `dotnet test`: **104/104 passed** (93 cũ + **11 mới**):
  - Superuser realm-role bypass · Local `IsSuperUser` bypass · UserPermission Grant · UserPermission Deny override Group Grant · GroupPermission Grant · admin wildcard · Default deny
  - **`UserResolvedViaLocalUserId_WhenUsernameDiffers`** (regression fix — code cũ FAIL test này) · **`UnknownUser_Fails_WithoutAutoCreate`** (regression bỏ auto-create)
  - Catalog endpoint (đủ 76, không trùng, có `customfields.delete`) · System group không rename/delete được.

## 4. Ghi chú schema
- Entity `PermissionGroup` thay đổi (`IsSystem`) → **Schema DB đã đổi** (cột `permission_groups.IsSystem`). Dự án **không dùng `dotnet ef migrations`** (theo cam kết — raw SQL self-heal). Self-heal SQL đã được thêm trong `Program.cs` startup (`ALTER TABLE ... ADD COLUMN IF NOT EXISTS`).
- **Nhắc**: restart .NET Aspire AppHost để startup block seed + self-heal chạy.

## 🏁 Kết thúc Subtask A — chờ review trước Subtask B
Mốc dừng đúng như đã thống nhất (A: API+handler → B: API gán User↔Group + test). Chưa đụng tới frontend (C/D) hay migrate dữ liệu gán user (E).
