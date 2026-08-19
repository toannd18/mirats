# Handoff 2026 08 12 — Phân quyền theo nhóm (Role-based Permission): Subtask B (API gán User↔Group + ActionLog + chống self-lockout)

> Lộ trình: **A** Backend hardening ✅ → **B** API gán User↔Group + ActionLog + chống self-lockout + test ✅ → **C** Frontend trang "Nhóm" → **D** Frontend "Phân quyền" + hook `usePermission` → **E** Test + migrate data + changelog.
> Handoff này ghi nhận hoàn thành **Subtask B** (backend) — dừng chờ review trước Subtask C (frontend).

---

## 1. API mới / mở rộng

| Endpoint | Policy | Hành vi |
|---|---|---|
| `PUT /api/v1/users/{id:guid}/groups` | `admin` | Gán bộ nhóm cho user (thay thế toàn bộ). Validate nhóm tồn tại (`GROUP_NOT_FOUND`). **Chống self-lockout** (`SELF_LOCKOUT`). Ghi ActionLog. |
| `GET /api/v1/users` · `GET /users/me` | `users.view` / auth | `UserDto` thêm `groups: [{groupId, name, isSystem}]`. |

- `GroupsController` (tất cả CRUD + `PUT /{id}/permissions`): **ghi ActionLog** cho mọi thay đổi (Create/Update/Delete/UpdatePermissions) + guard self-lockout trên `UpdateGroupPermissions`.

## 2. Chống self-lockout — cụ thể hóa (đúng yêu cầu)

**Khái niệm "khả năng quản lý phân quyền"** = có hiệu dụng `admin` (qua UserPermission Grant / GroupPermission Grant); `IsSuperUser` flag hoặc realm role `admin`/`superuser` luôn bypass.
> ⚠️ **Fix 2026-08-12 (sau review)**: bỏ `users.edit` khỏi tiêu chí. Cả 2 endpoint được guard bảo vệ đều dùng policy `admin` — `users.edit` KHÔNG mở lại được 2 API này, nên coi nó là "an toàn" tạo lỗ hổng (actor mất `admin` còn `users.edit` bị cho phép gỡ quyền trong khi không thể gọi lại API). Xem comment trong `PermissionLockoutGuard.cs` (lưu ý tương lai nếu tách `groups.manage`).

**Nguyên tắc guard** (`Infrastructure/Authorization/PermissionLockoutGuard.cs`):
1. Chỉ kích hoạt khi **target == actor** (user đang thao tác TỰ gỡ quyền của CHÍNH HỌ).
2. Nếu actor là Superuser (flag DB hoặc realm role) → luôn cho phép (không thể bị khóa).
3. Nếu sau thay đổi actor VẪN còn khả năng (`admin` hoặc `users.edit`) → cho phép.
4. Nếu actor MẤT khả năng → kiểm tra **còn user khác giữ khả năng không**:
   - Còn → cho phép (không phải "admin cuối cùng").
   - **Không còn ai** → **chặn** với `400 { errorCode: "SELF_LOCKOUT" }`.
5. ⚠️ Với `PUT /groups/{id}/permissions`: khả năng của **các user khác trong cùng group** phải tính với **bộ permission MỚI** (cùng bị ảnh hưởng bởi thay đổi) — không dùng trạng thái hiện tại.

**Điểm mạnh theo yêu cầu**: bảo vệ cả **Admin thường** (không chỉ Superuser) khi họ là người duy nhất còn quyền quản trị. Ví dụ: Admin A duy nhất tự gỡ nhóm admin của mình → bị chặn; nếu còn Admin B → cho phép.

## 3. ActionLog cho hành động nhạy cảm

- `ItemType` mở rộng: `PermissionGroup = 9`, `User = 10` (additive, không phá dữ liệu cũ).
- Log mọi: tạo/sửa/xóa group (`PermissionGroup`), sửa permission group, gán nhóm cho user (`User`, `logMeta.changes.groupIds.old/new`). `CreatedBy` = `local_user_id` claim.

## 4. File đã tạo/sửa (đã build + test)

- **Tạo**: `Infrastructure/Authorization/PermissionLockoutGuard.cs`, `aspire-react.Tests/PermissionLockoutTests.cs` (20 test).
- **Sửa**: `UsersController.cs` (endpoint + `groups` trong UserDto list/me + helpers), `GroupsController.cs` (logging + guard), `Domain/Enums/ItemType.cs`, `Application/Users/DTOs/UserDto.cs`, `Program.cs` (register guard), `PermissionTests.cs` (constructor), `docs/API.md`.

## 5. Xác minh (Subtask B)

- `dotnet build` Server: **0 error**.
- `dotnet test`: **125/125 passed** (104 cũ + **21 mới**):
  - **Guard unit (12)**: `SelfRemove_LastAdminGroup_Blocked` · `..._Allowed_WhenAnotherAdminExists` · `SelfRemove_OnlyUsersEditHolder_Blocked` · `SelfRemove_Superuser_Allowed` · `SelfRemove_RealmSuperUser_Allowed` · `RemoveOtherUser_Groups_NotGuarded` · `..._ButOtherSuperUserExists_Allowed` · **`SelfRemove_LosesAdmin_ButKeepsUsersEdit_Blocked`** (đổi từ `..._KeepUsersEdit_Allowed` — guard cũ cho phép SAI) · + 4 test GroupEdit
  - **GroupEdit guard (4)**: `GroupEdit_RemoveAdminFromOwnLastGroup_Blocked` · **`GroupEdit_RemoveAdmin_BlockedWhenOtherAdminInSameGroup`** (bug-fix: 2 admin cùng group đều mất quyền) · `..._Allowed_WhenOtherAdminInDifferentGroup` · `GroupEdit_KeepAdmin_Allowed`
  - **Controller (9)**: `UpdateUserGroups_SelfRemoveLastAdmin_Returns400SelfLockout` · **`UpdateUserGroups_SelfRemoveAdmin_ButKeepUsersEdit_Returns400SelfLockout`** (MỚI — case guard cũ xử lý sai) · `..._Allowed_WhenAnotherAdminExists` · `..._Superuser_Returns200` · `..._RealmSuperUser_Returns200` · `..._InvalidGroup_Returns400GroupNotFound` · `..._AssignOtherUser_LogsActionLog` · `UpdateGroupPermissions_SelfLockout_Returns400` · `CreateGroup_LogsActionLog`

## 🏁 Kết thúc Subtask B — chờ review trước Subtask C
Mốc dừng đúng như đã thống nhất (B: API gán User↔Group + test → C: frontend). Chưa đụng frontend.
