# 13 — Phân quyền: company-scope, nhóm, superuser

> Ai được thấy/sửa gì: company-scope, quyền từ nhóm + trực tiếp, 2 nhóm hệ thống.

## 1. Mục đích / đối tượng đọc

Admin cấu hình truy cập + dev gate đúng: backend policy, frontend `usePermission`.

## 2. Company-scope (multi-tenant)

- Mỗi bản ghi nghiệp vụ có `CompanyId`; user thường chỉ tác động trong công ty
  mình (đọc/ghi đều kiểm tra — global EF filter là no-op, check tường minh
  trong handler).
- `CompanyId = null` = **floater** (không thuộc công ty nào) — user thường được
  tạo cho floater, superuser bỏ qua mọi scope.
- Đọc/sửa/xóa ngoài scope: resource cụ thể → `404` (giấu sự tồn tại); tạo mới
  sai scope → `400 COMPANY_MISMATCH`.
- Cột/bộ lọc "Công ty" chỉ hiện với superuser (ẩn với user thường).

## 3. Cách tính quyền hiệu lực

1. Backend `[Authorize(Policy = "<resource>.<action>")]` — key phải có trong
   `PermissionCatalog` (~85 keys, 20 nhóm dưới đây).
2. Thứ tự check (`PermissionHandler`): superuser claim → quyền trực tiếp
   (`user_permissions`) → quyền qua nhóm (`group_permissions`) → wildcard
   `admin` (mọi quyền trừ chính nó).
3. Frontend: `usePermission('<code>')` (lấy từ `GET /permissions/check`, Grant=1);
   superuser luôn true; fail-closed khi không rõ. Không gate chính bằng
   `isSuperUser()` trừ logic đặc thù superuser.

## 4. Danh mục quyền theo nhóm (từ `PermissionCatalog`)

| Nhóm | Keys (`view/create/edit/delete` + riêng) |
|---|---|
| Tài sản | `assets.*` + `checkout`, `checkin`, `audit` |
| Vật tư | `consumables.*` + `checkout` |
| Linh kiện | `components.*` + `checkout` |
| Phụ kiện | `accessories.*` + `checkout` |
| Bản quyền | `licenses.*` + `checkout` |
| Người dùng | `users.view/create/edit/delete` (edit/delete thêm guard chống self-lockout) |
| Công ty / Model / Danh mục / NSX / NCC / Phòng ban / Địa điểm / Khấu hao / Trường tùy chỉnh / Hệ thống | `*.view/create/edit/delete` từng nhóm |
| Trạng thái | `statuslabels.*` (tính năng đã gỡ — key còn trong catalog) |
| Báo cáo | `reports.view` · Import/export: `import`, `export` |
| Bảo trì checklist | `maintenance.templates`, `maintenance.campaigns`, `maintenance.view` |
| Hệ thống | `admin` (wildcard), `superuser` (bypass tuyệt đối), `system.config` |

(Danh sách đầy đủ từng key + mô tả tiếng Việt: `PermissionCatalog.cs:18-140`.)

## 5. Hai nhóm hệ thống

Seed tự động khi chưa có (`StartupDataSeeder.cs:27-93`): **Superuser** (giữ mọi
quyền catalog) và **Admin** (được grant toàn catalog lúc seed). Cả hai có
`IsSystem=true` → **không rename/xóa** (`SYSTEM_GROUP_LOCKED`).

## 6. Admin tạo nhóm mới (không tự đặt vai trò ngoài DB)

- UI: `/groups` → tạo nhóm (tên là định danh kỹ thuật, "Admin"/"admin" trùng nhau)
  → gán quyền (`PUT /groups/{id}/permissions`) → gán user (`PUT
  /users/{id}/groups`, chống tự khóa `SELF_LOCKOUT`).
- Mọi thao tác nhóm yêu cầu policy `admin` (`GroupsController.cs:24`).
- Tài liệu này KHÔNG định nghĩa vai trò ("kế toán", "thủ kho"...) — vai trò là
  nhóm do admin tự đặt tên và gán quyền trong DB.

## Nguồn tham chiếu

- `aspire-react/aspire-react.Domain/Authorization/PermissionCatalog.cs:16-140` (toàn catalog)
- `aspire-react/aspire-react.Infrastructure/Authorization/PermissionHandler.cs:33-130` (thứ tự check)
- `aspire-react/aspire-react.Infrastructure/Persistence/StartupDataSeeder.cs:27-93` (seed 2 nhóm)
- `aspire-react/aspire-react.Server/Web/Controllers/GroupsController.cs:23-98` (endpoints, policy admin)
- `aspire-react/frontend/src/hooks/usePermission.ts:61-77` (`usePermission`)
- `docs/ERROR_CODES.md` (section Groups, Users)
