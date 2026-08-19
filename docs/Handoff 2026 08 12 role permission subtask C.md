# Handoff 2026 08 12 — Phân quyền theo nhóm (Role-based Permission): Subtask C (Frontend trang "Nhóm")

> Lộ trình: **A** Backend hardening ✅ → **B** API gán User↔Group + ActionLog + chống self-lockout ✅ → **C** Frontend trang "Nhóm" ✅ → **D** Frontend "Phân quyền" + hook `usePermission` → **E** Test + migrate data + changelog.
> Handoff này ghi nhận hoàn thành **Subtask C** (frontend) — dừng chờ review trước Subtask D.

---

## 1. Thay đổi frontend

| File | Thay đổi |
|---|---|
| `src/types/groups.ts` | **Mới**: `GroupDto`, `GroupPermissionEntry`, `PermissionResourceGroup`. |
| `src/services/groups.service.ts` | **Mới**: `groupsApi` (list/get/create/update/delete/updatePermissions/**getCatalog**). |
| `src/components/groups/GroupFormModal.tsx` | **Mới**: Modal tạo/sửa nhóm. **Catalog permission lấy từ `GET /api/v1/permissions`** — Collapse theo Resource (assets 7, licenses 5, categories 4, system…), mỗi panel có checkbox tổng (chọn tất cả / indeterminate) + checkbox từng permission (có tooltip mô tả). Nhóm `IsSystem`: ô Tên disabled + Alert "Nhóm hệ thống". |
| `src/pages/GroupListPage.tsx` | **Rewrite**: `ProTable` + `actionRef` (pattern chuẩn), cột Tên/Hệ thống(Tag)/Thành viên/Quyền(tags + `+N`)/Thao tác. **Xóa disabled cho nhóm `IsSystem`**. Toolbar "Tạo nhóm mới" → Modal. Popconfirm xóa. |
| `src/App.tsx` | Bỏ routes `/groups/new` + `/groups/:id/edit` (chuyển sang Modal) + bỏ import. |
| `src/pages/GroupFormPage.tsx` | **Xóa** (thay bằng Modal — xóa luôn danh sách hardcode 42 key thiếu). |

## 2. 🐛 Bug phát hiện & sửa trong quá trình verify (không chỉ frontend)

**Root cause**: backend serialize `PermissionValue` (enum) thành **string** `"Grant"` ở `GET /groups`/`GET /groups/{id}`/`permissions/matrix` (trong khi `permissions/check` trả int) → frontend so sánh `=== 1` fail → **mọi checkbox permission hiển thị rỗng ngay cả khi đã được cấp** (bug có sẵn từ GroupFormPage cũ).

**Fix**:
- Backend: `GroupsController` (GetGroups, GetGroup, logMeta) + `PermissionsController` (matrix) project `Value = (int)p.Value` — trả số nguyên nhất quán.
- Frontend: helper `toPermValue()` normalize (hỗ trợ cả int lẫn string enum) — phòng thủ.
- Sửa luôn deprecation antd v6: Modal `destroyOnClose`→`destroyOnHidden`, Alert `message`→`title`.

## 3. Xác minh (Subtask C) — ảnh chụp thật màn hình `/groups`

- **Catalog đúng từ API**: mở Modal → Collapse theo Resource đầy đủ (assets 7, licenses 5, categories 4, accessories 5, components 5, consumables 5, users 4, companies 4, models 4, manufacturers 4, suppliers 4, departments 4, locations 4, statuslabels 4, depreciations 4, customfields 4, reports 1, import 1, export 1, system 2 = **76 permission**) — **không còn hardcode thiếu ~30 key** như GroupFormPage cũ.
- **Edit Superuser**: dialog "Sửa nhóm: Superuser", ô **Tên nhóm disabled** (IsSystem), panel **assets 7/7 checked** (seed đúng), tổng "76 / 76 quyền được cấp".
- **List**: 2 nhóm hệ thống (Admin, Superuser) hiển thị Tag "Hệ thống", cột Quyền hiển thị tags + `+72`, **nút Xóa disabled** cho nhóm hệ thống.
- **End-to-end CRUD qua UI**: tạo "Test Group C" (tick `assets.view`) → hiện trong list với tag đúng → xóa thành công.
- `tsc --noEmit`: **0 lỗi** · Console trình duyệt: **0 error / 0 warning** · Backend tests: **125/125**.

### Ảnh chụp (docs/screenshots/, viewport 1440×900, đăng nhập admin qua Keycloak)
- 📸 `groups-list-1440.png` — danh sách nhóm (Admin/Superuser + Tag Hệ thống + Quyền tags + Xóa disabled).
- 📸 `groups-create-modal-1440.png` — Modal "Tạo nhóm mới", catalog theo Resource (panel assets mở, 7 checkbox `assets.*`).
- 📸 `groups-edit-modal-1440.png` — Modal "Sửa nhóm: Superuser" (Tên disabled + Alert hệ thống + assets 7/7 checked).

## 4. File backend sửa kèm (chỉ serialization, không đổi logic)
`GroupsController.cs` (GetGroups/GetGroup/logMeta) + `PermissionsController.cs` (matrix) — `Value = (int)p.Value`.

## 🏁 Kết thúc Subtask C — chờ review trước Subtask D
Mốc dừng đúng như đã thống nhất (C: frontend Nhóm có ảnh chụp → D: frontend Phân quyền + hook). Chưa đụng trang "Phân quyền" / hook `usePermission`.
