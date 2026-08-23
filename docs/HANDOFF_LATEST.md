# HANDOFF LATEST — Tổng kết toàn bộ phiên làm việc (Session Summary & Handoff)

> **Ngày:** 2026-08-14 · **Lộ trình:** ST1 → ST10 (Audit & Nâng cấp hệ thống)
> **Stack:** .NET 10 Web API + React 18/AntD v6 (Vite) + .NET Aspire (AppHost) + PostgreSQL 18 + Keycloak 26
> **Mục đích:** Đóng phiên an toàn, người đọc file này có thể nắm 100% trạng thái hệ thống và tiếp tục ngày mai không cần dò lại lịch sử chat.

---

## 1. Tổng quan trạng thái dự án

| Thành phần | Trạng thái |
|---|---|
| **Backend** `aspire-react.Server` (.NET 10) | ✅ Build 0 error · Test **187/187 PASS** (`dotnet test`) |
| **AppHost** `aspire-react.AppHost` (Aspire) | ✅ PostgreSQL 18 + Redis + Keycloak 26 + Server + Vite frontend (5173) |
| **Database** | ✅ **EF Core Migrations** — baseline `20260814135409_InitialBaseline` (đã áp dụng trên DB thật, 1 dòng trong `__EFMigrationsHistory`) · `Program.cs` dùng `Migrate()` · **raw SQL self-heal đã gỡ hoàn toàn** · drift schema = 0 |
| **Frontend** `frontend/` (React + AntD v6 + React Router v6 + Axios) | ✅ `npm run build` = **0 lỗi TypeScript** |
| **Script tự động** `scripts/audit-sweeps.ps1` | ✅ Quét sạch **4 lớp lỗi tĩnh** (Phụ lục A) — **exit code 0** |
| **Xác thực** (Keycloak) | ✅ `admin / <redacted>` **đăng nhập được** (ST10 — đã khắc phục trong phiên này) |

**Các cổng/URL chính (động qua Aspire, lấy từ dashboard):**
- Frontend: `http://localhost:5173` (pin cổng)
- Keycloak: `https://localhost:8080` (realm `aspire-react`, client `frontend` public + `backend-service` confidential)
- Server: port động — lấy từ log "Now listening" của resource `server`, health check `curl -k https://localhost:<port>/health` → 200

---

## 2. Hạng mục đã hoàn thành trong phiên

### ST1 — Vá bảo mật Company-Scoping
- Áp dụng company-scoping tường minh (qua `ICompanyScopeService.GetCurrentUserCompanyIdAsync`) cho **10 Controller / Read Endpoints** (Asset list/detail, Accessory list/detail, Consumable, License, Component, Maintenance, Import/Export, Reports, Dashboard...).
- Quy tắc: user thường chỉ thấy bản ghi công ty mình (+ bản ghi không công ty); Superuser thấy tất cả. KHÔNG dựa vào global query filter (đang là no-op do `GetUserCompanyIdsAsync` trả rỗng).

### ST2 & ST3 — Permission Policies & Delete Guards
- Chuẩn hóa `PermissionCatalog` (single source of truth) + đăng ký policy từ catalog → mọi `[Authorize(Policy=...)]` đều có policy thật (fix `customfields.delete` từng thiếu).
- **Delete guards chống mất dữ liệu lịch sử**: Asset (confirmed/checked-out/has-assignments/has-maintenance/used-by-component), Accessory (`ACCESSORY_HAS_CHECKOUTS`), Consumable, License... — bản ghi có lịch sử giao dịch KHÔNG hard-delete.
- Guard chống **self-lockout** (`PermissionLockoutGuard`) khi gỡ quyền quản trị cuối cùng.

### ST4 — Enum & CompanyId khóa cứng
- Fix lỗi enum **categoryType String vs Number** (frontend so sánh chuỗi, không so số).
- Fix **TargetType mapping** trong Accessory checkout (log ActionLog `TargetType` = đúng loại target thật: User/Department/Location/SystemPosition — trước đây log sai 1:1).
- **Khóa CompanyId** khi Consumable đã từng cấp phát (`FIELD_LOCKED`), whitelist field khi Update.

### ST5 — ActionLog hoàn thiện
- **ActionLog cho User CRUD** (`CreateUserCommand` / `UpdateUserCommand` / `DeleteUserCommand`) kèm **metadata `{ changes: { field: { old, new } } }`** cho Update.
- **CompanyId trên toàn bộ log** (mọi `LogAction(` đều truyền `companyId:` — đã sweep xác nhận).

### ST6d (A3) — EF Migration Baseline
- Tạo baseline `20260814135409_InitialBaseline`, xác minh **schema drift = 0** trên bản sao backup, **mark-applied** trên DB thật (1 dòng `__EFMigrationsHistory`).
- Gỡ toàn bộ khối raw SQL self-heal khỏi `Program.cs`, chuyển `EnsureCreated()` → `Migrate()`.
- Backup gốc: `docs/sql/backups/st6d_b3_prestep3_20260814.dump`.

### ST6 — Phân quyền UI
- Đồng bộ `usePermission('<resource>.<action>')` khớp chính xác policy backend cho mọi nút nhạy cảm (Xóa/Sửa/Cấp phát/Thu hồi/Đóng/Mở lại/Kiểm tra/Duyệt...).
- Gate **cột + bộ lọc "Công ty"** theo `isSuperUser()` (chỉ Superuser thấy; user thường ẩn) — nhất quán mọi trang danh sách.
- `isSuperUser()` frontend khớp 1-1 với `RealmAccessHelper` backend (realm role `admin`/`superuser` chính xác, không substring).

### ST7 & ST7b — TypeScript + Responsive UI
- Dọn **35 lỗi TypeScript** (về 0).
- **Modal responsive** `width 95% / 640 / 780 / 960` (Asset, License, Component, Group, User...).
- **Adaptive Card/Table Mobile <768px** (`Grid.useBreakpoint()`): `ComponentListPage` + `MaintenanceTable` — mobile = `<List>` `<Card>`, desktop = `<ProTable>`; fetch/actions dùng chung 1 đường (`buildParams`/`fetchPage`/`renderActions`), không trùng lặp.
- **`scroll={{ x: 'max-content' }}`** cho TOÀN BỘ `<Table>`/`<ProTable>` (sweep xác nhận 0 thiếu).
- **Xóa dead code**: `/asp`, `/aspire`, `LicenseFormPage.tsx`.
- F1: bỏ fallback claim `sub` — chỉ dùng `local_user_id`.

### ST9 — Mở rộng Test Backend + Script Sweep
- **+43 unit tests** (187/187 PASS): `AssetTests` (18 — Create/Delete-guards/Company-scope/Checkout validator), `AccessoryTests` (14 — checkout TargetType 4 loại, checkin, delete guard, scope), `UserActionLogTests` (7 — mock `IKeycloakService` + ActionLog changes), `CompanyScopeTests` (4 — `GetCurrentUserCompanyIdAsync` thật), `TestHelpers.cs` (fakes chung, MediatR 14 signature).
- **`scripts/audit-sweeps.ps1`**: Sweep 1 Claims · Sweep 2 Enum số · Sweep 3 LogAction thiếu `companyId:` · Sweep 4 Table thiếu `scroll` — exit 0 sạch, exit 1 kèm file:line; đã negative-test bằng probe tạm.
- **Sweep phát hiện 5 bug thật đã sửa**: `ActionLogTimeline` (config số → "Unknown" mãi), `ConsumableListPage` (`status===2`), `UsersController`/`GroupsController`/`ImportExportController` (LogAction thiếu companyId).

### ST-J — Task J: Đóng bypass `PermissionLockoutGuard` + company-scoping `UsersController.UpdateUser/DeleteUser` (2026-08-16)

**Root cause (3 đường bypass):**
1. `GroupsController.DeleteGroup` (Web/Controllers/GroupsController.cs) — chỉ check `group.IsSystem`, KHÔNG gọi guard → xóa group đang cấp quyền `admin` duy nhất → cascade xóa `GroupPermission`/`UserGroup` ngay lập tức, hệ thống mất người quản trị cuối.
2. `UsersController.UpdateUser` gated `users.edit` (yếu hơn `admin`); `PermissionHandler` bước 3 (`user.IsSuperUser → Succeed`) cho phép holder `users.edit` tự bật `IsSuperUser=true` → **privilege escalation** (tự thăng thành superuser) — 1 request đủ demote/promote, không qua guard.
3. `UsersController.DeleteUser` (soft-deactivate) không qua guard → vô hiệu hóa superuser cuối cùng bằng `users.delete`.

**Fix (3 guard mới + scoping + policy):**
- `PermissionLockoutGuard` thêm 3 method dùng chung helper `AnyUserHasManagementCapabilityAfterAsync` (superuser-flag hoặc `admin` hiệu dụng): `WouldDeleteGroupLockoutAsync`, `WouldDemoteSuperUserLockoutAsync`, `WouldDeactivateUserLockoutAsync`. Nguyên tắc: **chặn khi thao tác khiến toàn hệ thống không còn ai giữ khả năng quản lý phân quyền** — verify 2 chiều (chặn lockout thật, KHÔNG chặn khi còn superuser/admin khác). Realm superuser bypass.
- `GroupsController.DeleteGroup`: gọi guard trước khi xóa → `400 SELF_LOCKOUT` (group không bị xóa).
- `UsersController.UpdateUser`: **policy nâng `users.edit` → `admin`** (đã xác nhận trước khi đổi) + guard `WouldDemoteSuperUserLockoutAsync` khi `isSuperUser=false` cho user đang là superuser + company-scoping.
- `UsersController.DeleteUser`: guard `WouldDeactivateUserLockoutAsync` (chặn vô hiệu hóa người giữ khả năng quản lý cuối cùng) + company-scoping.
- Company-scoping Update/Delete: `_companyScope.GetCurrentUserCompanyIdAsync()` so với `targetUser.CompanyId` (user thường chỉ sửa/xóa user cùng công ty hoặc floater; Superuser bypass → `404` ẩn sự tồn tại, đúng pattern Task I).
- Frontend `UserListPage.tsx`: `canEdit` `usePermission('users.edit')` → `usePermission('admin')` (khớp policy backend).

**Verify:** `dotnet build` 0 lỗi · `dotnet test` **213/213 PASS** (thêm 22 test mới `TaskJLockoutAndCompanyScopeTests` — 8 guard unit 2 chiều + controller company-scope/lockout qua `ThrowingMediator` chứng minh reach-mediator) · `npm run build` 0 lỗi TS · `scripts/audit-sweeps.ps1` exit 0 · `dotnet format --verify-no-changes` sạch cho 5 file đã sửa (format debt còn ở `SystemDetailTests.cs`/`UserActionLogTests.cs` — pre-existing, không đụng).

**Bổ sung verify bằng API THẬT trên Aspire stack (2026-08-16, sau khi user yêu cầu) — kèm truy vấn DB thật:**
- **Điểm 3 — user/role có `users.edit` nhưng không `admin`:** truy vấn DB thật (`users`/`user_groups`/`group_permissions`/`permission_groups`) → **KHÔNG có user nào**. Chỉ 2 nhóm cấp `users.edit`/`admin` là `Admin` (thành viên `st1verify`) và `Superuser` (thành viên `admin`, `ndkien`) — cả 2 nhóm đều cấp CẢ `admin`. Những người có `users.edit`: `admin`, `ndkien` (đều superuser) và `st1verify` (có `admin`) → **nâng UpdateUser lên policy `admin` gây 0 regression** (không ai bị mất quyền sửa user).
- **Điểm 1 — verify lockout guard bằng API thật** (2 chiều, dùng kỹ thuật tạm-neutralize real admin với **pg_dump backup** `taskj_backup.dump` + admin còn giữ Keycloak realm-admin làm safety net, rồi **khôi phục nguyên trạng**):
  - `DeleteGroup`: sole admin (`qaj-lock-admin`) xóa group admin duy nhất → **400 SELF_LOCKOUT, group giữ nguyên**; khi có admin thứ 2 (`qaj-lock-admin2`) giữ quyền → **200, group bị xóa**.
  - `UpdateUser` hạ `isSuperUser=false`: superuser cuối tự hạ quyền → **400 SELF_LOCKOUT**; khi có superuser thứ 2 → **200**.
  - `DeleteUser` deactivate: superuser cuối tự vô hiệu hóa → **400 SELF_LOCKOUT**; khi có superuser thứ 2 → **200**.
  - **Company-scoping `UpdateUser`/`DeleteUser`**: actor user thường (CT-A) — UpdateUser target CT-B → **404**, target CT-A → **200**; DeleteUser target CT-B → **404**, target CT-A → **200**; admin (superuser) UpdateUser target CT-B → **200** (bypass).
  - **Đã khôi phục & dọn sạch:** real admin `admin`/`ndkien` (IsSuperUser=true + Superuser group) & `st1verify` (Admin group) nguyên trạng; toàn bộ user/group/company/action_log test `qaj-*` xóa sạch DB (users 19, action_logs 297, groups = Admin/Superuser/Viewer) + Keycloak (0 `qaj-*` user). Verify `dotnet test` 213/213 vẫn PASS.
- **Điểm 2 — xác nhận policy `admin`:** quyết định ban đầu được duyệt qua tool `question` (user chọn "Raise to admin policy"); khi user nghi ngờ chưa thấy bước hỏi, tôi đã dừng lại và hỏi lại dứt khoát → **user chọn "Giữ 'admin' (đã duyệt trước đó)"** → giữ nguyên. Bằng chứng: 2 câu trả lời qua `question` trong cuộc trò chuyện.

### ST-K — Task K: Company-scoping cho endpoint ĐỌC còn thiếu (2026-08-16)

**Root cause (7 endpoint):**
1. `UsersController.GetUsers` — chỉ filter theo `companyId` query param khi client tự truyền → user thường có `users.view` thấy TOÀN BỘ user mọi công ty (PII: email/chức danh/phòng ban/group). Vị trí: `UsersController.cs:74`.
2. `UsersController.GetUser` — không scope → xem detail user bất kỳ công ty.
3. `AssetsController.GetHistory` — trả action-log của asset bất kỳ công ty (`AssetsController.cs:272`), không scope theo company asset (khác GetAsset đã đúng).
4. `ComponentsController.RemoveAssignment` — gỡ gán component bất kỳ công ty.
5. `ConsumablesController.Confirm` — confirm consumable bất kỳ công ty (side-effect).
6. `DepartmentsController.GetAll` — "để client tự quyết định phạm vi bảo mật": chỉ filter khi client truyền `companyId` param → user thường bỏ qua param là thấy hết (`DepartmentsController.cs:32`).
7. `DepartmentsController.Get` — không scope.

**Fix (dùng đúng pattern Task I/J — `ICompanyScopeService.GetCurrentUserCompanyIdAsync()`):**
- `GetUsers`: user thường → `u.CompanyId == null || u.CompanyId == userCompanyId` (floater + công ty mình), **bỏ qua** `companyId` param; Superuser → thấy hết + vẫn filter theo param tuỳ chọn.
- `GetUser`: out-of-scope → `404` (hide existence, đúng quy ước Task I).
- `GetHistory`: check asset visible (mirror GetAsset) trước khi trả log → `404` nếu ngoài scope.
- `RemoveAssignment`/`Confirm`: check component/consumable company TRƯỚC side-effect → `404`.
- `Departments.GetAll`: ép scope LUÔN LUÔN cho user thường (không dựa param); Superuser → hết + param tuỳ chọn. `Get`: out-of-scope → `404`.
- KHÔNG đổi hành vi GetAsset/GetAccessory... đã đúng; không đổi logic nghiệp vụ khác; Superuser bypass ở mọi endpoint.

**Verify API THẬT (Aspire stack, 2 user 2 công ty CT-A/CT-B, actor `qak-actor` = user thường CT-A):**
- `GetUsers`: actor thấy `qak-target-a` (CT-A), KHÔNG thấy `qak-target-b` (CT-B); kể cả khi truyền `?companyId=CT-B` vẫn ẩn (param bị bỏ qua). Superuser thấy cả TB.
- `GetUser(CT-B)` → **404**; `GetUser(CT-A)` → **200**; Superuser `GetUser(CT-B)` → **200**.
- `GetHistory(assetB)` → **404**; `GetHistory(assetA)` → **200**; Superuser → **200**.
- `RemoveAssignment(compB)` → **404, assignment KHÔNG bị xóa (DB verify: còn 1)**; `(compA)` → **200, bị xóa (DB: 0)**.
- `Confirm(consB)` → **404, status giữ Pending (DB: 1)**; `(consA)` → **200, Confirmed (DB: 2)**.
- `Departments.GetAll` (KHÔNG param): actor chỉ thấy `QAK-DEPT-A`, KHÔNG `QAK-DEPT-B`; truyền `?companyId=CT-B` vẫn ẩn. `Get(DEPT_B)` → **404**; `Get(DEPT_A)` → **200**. Superuser → hết.
- **18/18 check pass.** Dữ liệu test `qak-*` đã dọn sạch DB (users 19, action_logs 297, groups 3, 0 QAK mọi bảng) + Keycloak (0 `qak-*`); KHÔNG đụng tài khoản thật nên không cần restore.

**Quét thêm (Bước 0.5) — phát hiện thêm, KHÔNG tự sửa:** `AdminController.GetLocations` (`AdminController.cs:230`) — Location có `CompanyId` nhưng chỉ filter khi client truyền `companyId` param, KHÔNG gọi `GetCurrentUserCompanyIdAsync` → **cùng lớp lỗi với Departments.GetAll** (user thường có `locations.view` bỏ param là thấy hết location mọi công ty). Đã ghi backlog mục 30 (Task U). Các GET khác (models/categories/manufacturers/suppliers/statuslabels/depreciations) là entity KHÔNG có CompanyId → N/A.

**Frontend:** KHÔNG cần sửa. GetUsers giờ scope theo công ty — UserListPage đã gate filter "Công ty" bằng superuser, chỉ render data API trả về. GetUser trả 404 cho user khác công ty: các nơi fetch `/users/{id}` để hiển thị tên (Maintenance `closedById`/`creator`) dùng user cùng công ty (maintenance đã company-scoped) hoặc floater (`admin` CompanyId null → vẫn 200) → không vỡ. Nếu sau này có kịch bản cross-company hiển thị tên user, cần API chuyên dụng (không phải GetUser) — để ghi chú.

### ST-L2 — Task L2: Company-scoping cho endpoint CREATE (2026-08-16)

**Root cause (5 endpoint):** các endpoint CREATE nhận `CompanyId` trực tiếp từ request body client gửi, KHÔNG ép theo company scope của user hiện tại → user thường có policy `*.create` tạo được bản ghi thuộc CÔNG TY KHÁC chỉ cần truyền `CompanyId` bất kỳ. Vị trí:
1. `AssetsController.CreateAsset` (L160) → `CreateAssetCommandHandler` (`CreateAssetCommand.cs:51`) không scope.
2. `ConsumablesController.Create` (`ConsumablesController.cs:97`) — tạo `Consumable` trực tiếp `r.CompanyId`.
3. `ComponentsController.Create` (`ComponentsController.cs:162`) — chỉ validate CompanyId tồn tại, KHÔNG so scope actor.
4. `AccessoriesController.Create` (L104) → `CreateAccessoryCommandHandler` (`CreateAccessoryCommand.cs:30`) không scope.
5. `DepartmentsController.Create` (`DepartmentsController.cs:75`).

**Quyết định thiết kế (Bước 0.3):** VALIDATE (không ép cứng) `CompanyId` client gửi nằm trong phạm vi user — vì Superuser không thuộc 1 company cụ thể nên phải cho phép tạo cho company bất kỳ. Rule: user thường → `CompanyId` phải là `null` (floater) hoặc `== company của mình`; Superuser (`GetCurrentUserCompanyIdAsync` → null) → company bất kỳ. Từ chối = **400 `COMPANY_MISMATCH`** (không phải 404 — đây là hành động tạo mới, không "ẩn sự tồn tại"). Validate TRƯỚC khi ghi DB.

**Fix:**
- Asset/Accessory → check trong **command handler** (inject `ICompanyScopeService`, trả `COMPANY_MISMATCH`; controller map `error_code` vào 400) — defense-in-depth, khớp Task I.
- Consumable/Component/Department → check trong **controller** (trước khi tạo).
- KHÔNG đổi field/validation khác; KHÔNG đổi CompanyTreeSelect FE (đây là lớp backend bổ sung).

**Verify API THẬT (Aspire stack, user thường `qal-actor` CT-A):**
- 5 endpoint × CompanyId=CT-B → **400 COMPANY_MISMATCH**, bản ghi KHÔNG được tạo (DB verify: 0 row cross-company cho cả 5).
- 5 endpoint × CompanyId=CT-A → **201/200**, tạo thành công.
- Superuser (admin) × 5 endpoint × CompanyId=CT-B → **201/200**, tạo thành công (không bị chặn).
- **15/15 check pass.** Dữ liệu test `qal-*`/`QAL-*` đã dọn sạch DB (0 QAL mọi bảng, users 19/action_logs 297/groups 3 = baseline) + Keycloak (0 `qal-*`); không đụng tài khoản thật.

**Frontend:** KHÔNG cần sửa — CompanyTreeSelect FE đã giới hạn theo quyền user (Task G); đây là validate backend độc lập chống bypass qua API trực tiếp.

### ST-CLEANUP — Backlog-Cleanup: Task U + Extract DeleteUnitAsync (2026-08-16)

**Mục 1 — Task U: `AdminController.GetLocations`**
- **Root cause:** `AdminController.cs:230` — Location có `CompanyId`, `GetLocations` chỉ filter khi client tự truyền `companyId` query param, KHÔNG gọi `GetCurrentUserCompanyIdAsync` → user thường có `locations.view` bỏ qua param là thấy hết location mọi công ty (cùng lớp lỗi `DepartmentsController.GetAll` đã fix ở Task K).
- **Fix:** ép scope theo user thường (`l.CompanyId == null || l.CompanyId == userCompanyId`) — **bỏ qua** param `companyId` cho user thường; Superuser (`null`) → thấy hết + param tuỳ chọn. Copy đúng logic `Departments.GetAll`. `_companyScope` đã inject sẵn.

**Mục 2 — Extract `ComponentUnitsController.Delete` thành service method**
- **Root cause (bất đối xứng):** `SetUnitStatusAsync` đã ở trong `IComponentAllocationService` (company-scoping trong service, bảo vệ mọi caller), còn `Delete` 100% inline trong controller → nếu thêm caller xóa ComponentUnit sẽ phải viết lại 4 phần logic (soft-delete, history-guard, Qty decrement, ActionLog).
- **Fix:** thêm `IComponentAllocationService.DeleteUnitAsync(unitId, createdById, ct)` chứa toàn bộ logic + company-scoping trong service (mirror `SetUnitStatusAsync`); `ComponentUnitsController.Delete` chỉ map kết quả (`NOT_FOUND`→404, còn lại→400). Không đổi hành vi (kể cả: `ALREADY_DELETED` vốn là dead code vì global query filter `DeletedAt == null` ẩn unit đã xóa → lần xóa thứ 2 trả `NOT_FOUND`, giữ nguyên hành vi controller cũ).

**Verify API THẬT (Aspire stack, user thường `qbc-actor` CT-A):**
- `GetLocations` (user thường, KHÔNG param) → chỉ `QBC-LOC-A` (CT-A), KHÔNG `QBC-LOC-B` (CT-B); truyền `?companyId=CT-B` vẫn ẩn; Superuser → thấy cả 2.
- `DELETE /component-units/{unitA}` (CT-A) → **200, soft-delete thành công (DB: DeletedAt set, CurrentAssetId null, Component.Qty 1→0)**; `{unitB}` (CT-B) → **404 (cross-company chặn)**; Superuser xóa `{unitB}` → **200**.
- **6/6 check pass.** Dữ liệu test `qbc-*`/`QBC-*` dọn sạch DB (0 QBC mọi bảng, users 19/action_logs 297/groups 3 = baseline) + Keycloak (0 `qbc-*`); không đụng tài khoản thật.

### ST-L — Task L: Đăng ký ValidationBehavior + xác nhận unique constraint AssetTag (2026-08-16)

**Root cause:** `Program.cs` đăng ký MediatR (`RegisterServicesFromAssembly`) + FluentValidation (`AddValidatorsFromAssemblyContaining<CheckoutAssetCommandValidator>()`) nhưng KHÔNG có `IPipelineBehavior` nào → mọi validator chỉ chạy khi unit test gọi tay, KHÔNG chạy trong request thật. Hậu quả xác nhận bằng API thật (trước fix): tạo Asset trùng `AssetTag` → **500** (lỗi thô từ DB unique index), thay vì 400 sạch từ validator.

**Liệt kê đầy đủ validator (Bước 0.2) — chỉ 4:** `CreateAssetCommandValidator`, `CheckoutAssetCommandValidator`, `CreateUserCommandValidator`, `UpdateUserCommandValidator`. Trong đó 2 validator User đã được controller (`UsersController.CreateUser`/`UpdateUser`) gọi tay từ trước → pipeline mới chỉ ảnh hưởng thực sự đến **CreateAsset** và **CheckoutAsset**. Không có Command nào khác có validator.

**Bước 0.4 — điều chỉnh so với audit:** unique index `IX_assets_AssetTag` **ĐÃ TỒN TẠI** trong DB thật (verify: `CREATE UNIQUE INDEX "IX_assets_AssetTag" ON assets ("AssetTag")`; migration `20260814135409_InitialBaseline` đã tạo `unique: true`) và **không có bản ghi trùng** (0 row). Audit cũ ghi "không có index" là snapshot lỗi thời → **KHÔNG cần migration mới**, DB constraint là lớp bảo vệ độc lập ĐÃ sẵn sàng (chứng minh: tắt validator — baseline — DB vẫn chặn bằng 500).

**Fix:**
- `Application/Common/Behaviors/ValidationBehavior.cs` — chạy mọi `IValidator<TRequest>` trước handler; nếu có lỗi throw `FluentValidation.ValidationException`.
- `Web/ExceptionHandlers/ValidationExceptionHandler.cs` (`IExceptionHandler`) — map `ValidationException` → **400** với `errors` gom theo field (đúng shape User controller đã dùng).
- `Program.cs` — `cfg.AddOpenBehavior(typeof(ValidationBehavior<,>))` + `AddExceptionHandler<ValidationExceptionHandler>()`.

**Verify API THẬT (sau rebuild server):**
- CreateAsset trùng tag → **400** "Mã tài sản đã tồn tại trong hệ thống." (trước fix: 500).
- CreateAsset name rỗng → **400** "'Name' must not be empty."
- CreateAsset tag hợp lệ → **201** (không regression).
- CheckoutAsset target không tồn tại → **400** "Target not found or has been deleted." (validator CheckoutAsset giờ chạy).
- CheckoutAsset target hợp lệ → **200** (không regression).
- CreateUser username trùng → **400** "Username already exists."; email sai → **400** "Email must be a valid email address." (User validator vẫn chạy, pipeline không gây xung đột).
- **7/7 check pass.** DB layer chặn độc lập: baseline đã chứng minh (duplicate → 500 khi chưa có validator).
- Dữ liệu test `TASKL-*`/`TaskL*` đã dọn sạch DB (assets 12, action_logs 297 = baseline); không tạo user test (chỉ payload invalid, 400 trước side-effect).

### ST-O — Task O: Verify race condition THẬT bằng test đồng thời (2026-08-16) — CHỈ AUDIT, KHÔNG FIX

**Phương pháp:** viết `ConcurrencyRaceAuditTests` (`[Trait("Category","Concurrency")]` — không chạy chung suite CI nhanh), gọi `Task.WhenAll` 2 request checkout/allocate ĐỒNG THỜI vào cùng 1 resource chỉ "còn 1 unit/seat", trên **Aspire stack thật (Postgres thật)**, mỗi loại 5 lần. Ghi kết quả HTTP + đếm row DB sau cùng.

**Kết quả (bằng chứng thực nghiệm — cả 4 race TÁI HIỆN được):**

| Điểm | Cơ chế code (đọc) | Kết quả thật (5 lần) | DB sau | Mức |
|---|---|---|---|---|
| 1. **License seat checkout** | KHÔNG transaction, không `FOR UPDATE`; auto-pick seat free (`UserId/AssetId/SystemInfoId IS NULL`) | **5/5 lần cả 2 request đều 200** | chỉ **1/1 seat** được gán (cả 2 ghi cùng seat, last-writer-wins) | 🔴 **LOST UPDATE / silent overwrite** — 2 lần "thành công" nhưng 1 assignment bị ghi đè mất |
| 2. **Accessory checkout** | có transaction nhưng đọc `remaining = Qty - Sum(AssignedQty-ReturnedQty)` KHÔNG `FOR UPDATE` | **4/5 lần cả 2 đều 200** (1/5 lần 1 request bị 400 INSUFFICIENT_STOCK) | 4 accessory có **2 checkout row** (qty=1 → effective remaining = −1) | 🔴 **OVERCOMMIT** |
| 3. **Component Bulk allocate** | transaction nhưng đọc `remaining = Qty - Sum(Assignments)` KHÔNG `FOR UPDATE` (cả nhánh Allocate) | **4/5 lần cả 2 đều 200** (1/5 lần 1 request bị 400) | 4 component có **2 assignment** (qty=1 → remaining = −1) | 🔴 **OVERCOMMIT** |
| 4. **Consumable checkout** | transaction nhưng đọc `remaining = Qty - Sum(Checkouts)` KHÔNG `FOR UPDATE` | **5/5 lần cả 2 đều 200** | 5 consumable có **2 checkout row** (qty=1 → remaining = −1) | 🔴 **OVERCOMMIT** |

**Kết luận:** audit trước đó đúng — 4 module ghi tồn kho/seat KHÔNG an toàn dưới tải đồng thời. Asset checkout/checkin đã dùng `FOR UPDATE` (an toàn) là pattern chuẩn để áp dụng.

**Đề xuất hướng fix (KHÔNG làm trong task này — ghi backlog mục 31):**
- License: thêm transaction + `FOR UPDATE` khi pick seat (hoặc unique partial index ngăn 2 gán cùng seat) + trả lỗi `SEAT_ALREADY_ASSIGNED`/`NO_AVAILABLE_SEATS` cho request thua.
- Accessory/Component/Consumable: trong transaction, đọc/khóa hàng `FOR UPDATE` (pattern Asset `FromSqlRaw ... FOR UPDATE` hoặc `Entity Framework` `SELECT ... FOR UPDATE` qua raw SQL / `UseRowNumber`), hoặc optimistic concurrency token trên `Qty`/`RowVersion` — quyết định ở task fix riêng.

**Verify:** `dotnet test --filter "Category=Concurrency"` → 4 test PASS (chạy khi stack lên); suite nhanh `--filter "Category!=Concurrency"` → 259 PASS; `audit-sweeps.ps1` exit 0; format sạch. Dữ liệu test `QCR-*` dọn sạch DB (0 QCR mọi bảng, 12 bảng kiểm).

### ST-O-FIX — Task O-FIX: Fix race condition bằng khóa hàng FOR UPDATE (2026-08-16)

**Root cause (đã xác nhận thật ở Task O):** 4 module checkout/allocate đọc `remaining`/seat KHÔNG khóa hàng → dưới tải đồng thời cả 2 request cùng đọc cùng giá trị → License lost-update (5/5), Accessory/Component/Consumable overcommit (4-5/5). Asset đã an toàn nhờ `FromSqlRaw(... FOR UPDATE)`.

**Fix (nhất quán — FOR UPDATE theo mẫu Asset, có nhánh fallback InMemory vì InMemory không dịch raw SQL):**
1. **License seat checkout** (`LicensesController.CheckoutSeat`): bọc pick-seat + gán trong `CreateExecutionStrategy` + `BeginTransactionAsync`; khóa license row `FOR UPDATE` (mutex cho seat allocation) → request thứ 2 chờ lock rồi re-read → không còn seat free → **400 `NO_AVAILABLE_SEATS`** (thay vì 200 giả + ghi đè).
2. **Accessory** (`CheckoutAccessoryCommandHandler`): trong transaction sẵn có, khóa accessory row `FOR UPDATE`, tính `remaining = Qty - Sum(AssignedQty-ReturnedQty)` bằng query riêng.
3. **Component Bulk** (`ComponentAllocationService.AllocateAsync`/`ReturnAsync`): helper `LoadComponentForUpdateAsync` khóa component row `FOR UPDATE`, load `Assignments`/`Units` riêng; tính `remaining = Qty - Sum(AssignedQty)`.
4. **Consumable** (`ConsumableAllocationService.CheckoutAsync`): khóa consumable row `FOR UPDATE`, tính `remaining = Qty - Sum(Quantity)` bằng query riêng.
- Fallback InMemory: `Database.ProviderName == "Microsoft.EntityFrameworkCore.InMemory"` → dùng query thường (InMemory không dịch raw SQL; lock thật do Category=Concurrency tests trên Postgres thật đảm bảo). KHÔNG đổi logic nghiệp vụ/response format thành công; chỉ thêm lock.

**Verify (chạy lại CHÍNH XÁC `ConcurrencyRaceAuditTests`, 5 lần/loại, Postgres thật — chuyển từ "race tái hiện" sang "race bị chặn đúng"):**

| Điểm | Trước (Task O) | Sau (O-FIX) | DB sau |
|---|---|---|---|
| License | 5/5 cả 2 đều 200, 1 seat | **5/5 đúng 1×200 + 1×400 NO_AVAILABLE_SEATS** | 1 seat gán |
| Accessory | 4/5 cả 2 đều 200, 2 row | **5/5 đúng 1×200 + 1×400 INSUFFICIENT_STOCK** | 1 checkout row |
| Component | 4/5 cả 2 đều 200, 2 assignment | **5/5 đúng 1×200 + 1×400** | 1 assignment |
| Consumable | 5/5 cả 2 đều 200, 2 row | **5/5 đúng 1×200 + 1×400** | 1 checkout row |

- Không regression: luồng checkout bình thường vẫn 200 (verify manual License: create → checkout → 200; checkout2 → 400 NO_AVAILABLE_SEATS); `dotnet test --filter "Category!=Concurrency"` → **259 PASS**; `--filter "Category=Concurrency"` → 4 PASS.
- `scripts/audit-sweeps.ps1` exit 0; `npm run build` 0 lỗi TS; format sạch các file production đã sửa (LicenseTests.cs có format debt pre-existing ở `SeedLicenseAsync` L70-71 — không do task này).
- Dữ liệu test `QCR-*` dọn sạch DB (0 QCR, 12 bảng).

**Lưu ý defense-in-depth (chưa làm trong phạm vi task này):** có thể thêm unique partial index chống 2 gán cùng 1 seat (`license_seats`) — nếu cần làm tiếp, ghi backlog riêng.

### ST-M1 — Task M1: Patch-safety cho Update Component/License/Consumable (2026-08-16)

**Root cause:** 3 Update handler full-replace (ghi đè vô điều kiện) — field KHÔNG được gửi trong payload một phần bị set về null/0:
- `ComponentsController.cs:261-267` — `c.SupplierId/ManufacturerId/ModelNumber/LocationId/OrderNumber/PurchaseCost/PurchaseDate = r.X` (unconditional; `r.X` null khi không gửi → wipe).
- `LicensesController.cs:324-332` — `l.ExpirationDate/TerminationDate/PurchaseCost/PurchaseDate/OrderNumber/SupplierId/ManufacturerId = r.X` (unconditional).
- `ConsumablesController.cs:148-153` — tái dùng `CreateConsumableRequest` (Qty/MinAmt non-nullable) → `c.Qty = r.Qty` (0 nếu không gửi), `c.Name = r.Name`, `c.SupplierId = r.SupplierId` (null → wipe).

**Bước 0.2 — frontend form (mức độ ACTIVE thật):**
- **Component** (`ComponentFormModal.tsx:179-190`): UPDATE gửi **PAYLOAD MỘT PHẦN** — chỉ các field `!== undefined` → **ACTIVE XÁC NHẬN** (backend full-replace hiện WIPE mọi field khác mỗi lần sửa Component).
- **License** (`LicenseFormModal.tsx:146-163`): UPDATE gửi **FULL payload** (`common` — mọi field) → latent từ phía form, nhưng vẫn phải fix backend (defense — client khác gọi API không được bảo vệ).
- **Consumable** (`ConsumableFormModal.tsx:105-111`): spread toàn bộ antd `values` → FULL payload → latent từ form, fix backend cần thiết.

**Fix (patch semantics, copy Task F `UpdateAssetCommand`):**
- Component: DTO `UpdateComponentRequest` đã nullable → chỉ bọc assign bằng `if (r.X is not null)`.
- License: DTO `UpdateLicenseRequest` đã nullable → `if (r.X is not null)` / `if (r.TerminationDate.HasValue)`; giữ CompanyId/CategoryId lock (reject nếu gửi khác) + seat-sync.
- Consumable: **tách riêng `UpdateConsumableRequest`** (mọi field nullable) khỏi `CreateConsumableRequest`; Update dùng DTO mới, chỉ gán khi gửi; **field-lock CompanyId sau cấp phát** đổi thành patch-aware (`r.CompanyId.HasValue && khác → chặn`). Create GIỮ NGUYÊN dùng `CreateConsumableRequest`.

**Verify API THẬT (sau khi thêm patch):**
- Partial update chỉ gửi `{ name }` → **200** cho cả 3; **DB xác nhận field khác GIỮ NGUYÊN** (không reset):
  - Component `M1-Comp-New`: SupplierId/ManufacturerId set, ModelNumber `M1-MOD`, OrderNumber `M1-ORD`, PurchaseCost 100, MinAmt 2, Qty 5.
  - License `M1-Lic-New`: SupplierId/ManufacturerId set, PurchaseCost 999, OrderNumber `M1-LO`, Seats 2.
  - Consumable `M1-Cons-New`: **Qty 10 (KHÔNG về 0)**, MinAmt 2, SupplierId set, Notes, OrderNumber, PurchaseCost 50.
- Field-lock: License đổi CompanyId → **400 FIELD_LOCKED**; Consumable đổi CompanyId sau checkout → **400 FIELD_LOCKED**.
- Create không đổi (Component/License/Consumable create → 201).
- `dotnet test --filter "Category!=Concurrency"` → **266 PASS** (+7 `TaskM1PatchSafetyTests`); `npm run build` 0 lỗi TS; `audit-sweeps.ps1` exit 0; format sạch.
- **Ghi chú pre-existing (KHÔNG do M1):** Component create kèm `purchaseDate` (chuỗi ISO) → **500** — lỗi DateTime kind đã biết (HANDOFF_DATETIME_KIND_AUDIT Task 9 chưa phủ Component.PurchaseDate); ngoài phạm vi M1, đề xuất ghi backlog riêng.
- Dữ liệu test `M1-*` dọn sạch DB (0 M1, 10 bảng).

### ST-N — Task N: Fix ActionLog audit trail + vá Sweep 3 (2026-08-16)

**Root cause (3 vấn đề):**
1. **Component Return(Serial) TargetId=null** (`ComponentAllocationService.cs`): nhánh `ReturnAsync` serial — `unit.CurrentAssetId` bị null hóa ở dòng trước, log `TargetId = assetId` (tham số request, null khi checkin qua serialNo) → mất dấu vết trả về asset nào.
2. **CompanyId thiếu** ở `ActionLogs.Add(new ActionLog{...})` — 9 vị trí: `DepartmentsController` Create/Update/Delete, `AdminController` Location C/U/D, `SystemInfoController` SystemInfo C/U/D.
3. **Sweep 3 blind spot**: `scripts/audit-sweeps.ps1` chỉ quét literal `LogAction(`, bỏ qua `_context.ActionLogs.Add(new ActionLog{...})` (~50% chỗ ghi log) → 3 lỗ hổng #2 lọt qua nhiều lần sweep "exit 0".

**Fix:**
- **A**: `ReturnAsync` serial — capture `var returnedAssetId = unit.CurrentAssetId` TRƯỚC khi null hóa, log `TargetId = returnedAssetId`.
- **B**: thêm `CompanyId = <entity>.CompanyId` vào 9 ActionLogs.Add (Department d.CompanyId, Location l.CompanyId, SystemInfo s/sys.CompanyId) + **SystemPosition** 3 vị trí (kế thừa `SystemInfo.CompanyId` / `pos.SystemInfo?.CompanyId` — phát hiện thêm khi quét, là lỗ hổng thật cùng lớp).
- **C**: Sweep 3 mở rộng — quét CẢ `LogAction(` (kiểm `companyId:`) VÀ `ActionLogs.Add(new ActionLog{...})` (kiểm `CompanyId =`); **exempt master data không có cột CompanyId** (ItemType Category/Model/Manufacturer/Supplier/Company/CustomField) để tránh false positive.

**Verify:**
- **API thật — Part A**: checkin Component qua serialNo (không kèm assetId) → ActionLog `TargetId = 2b8a097d...` (asset thật, không null); log pre-existing cũ (note "test") hiện `TargetId=null` — minh chứng bug cũ đã hết.
- **API thật — Part B**: Create/Update/Delete Department/Location/SystemInfo → cả 9 log đều có `CompanyId` (DB verify `company_set=t`).
- **Sweep 3**: chạy trên codebase hiện tại → **0 vi phạm, exit 0**. **Negative test**: tạm bỏ `CompanyId` ở 1 ActionLogs.Add (SystemInfo.Create) → sweep MỚI **bắt được vi phạm** (exit 1); khôi phục → exit 0. Chứng minh sweep thực sự có tác dụng, không "exit 0 vì bỏ sót".
- `dotnet test --filter "Category!=Concurrency"` → **267 PASS** (+1 test `Serial_ReturnBySerialNo_LogsTargetIdOfRealAsset`); `npm run build` 0 lỗi TS; `dotnet format` sạch.
- Dữ liệu test `QN-*`/`QNS-*` dọn sạch DB (6 bảng = 0).

### ST-M2 — Task M2: Patch-safety nhóm LATENT (User/Admin ref-data/Asset.Name/Accessory) — MỤC CUỐI của chuỗi F→M1→M2 (2026-08-16)

**Root cause (4 nhóm):**
1. **User** (`UpdateUserCommand.cs:24-25,84-85`) — `IsSuperUser`/`IsActive` là `bool` không nullable → payload thiếu field default `false` → âm thầm tước quyền admin/vô hiệu hóa.
2. **Admin ref-data** (`AdminController.cs`) — 5 entity (Category/Manufacturer/Supplier/Location/AssetModel) bind thẳng entity, full-replace, không guard field; Manufacturer/Supplier còn có bug latent: partial update thiếu `Code` → `Code=""` → bị reject "Mã 2-5 ký tự".
3. **Asset.Name** (`UpdateAssetCommand.cs`) — `AssetTag` đã guard (Task F) nhưng `Name` thì KHÔNG (`if (request.Name != asset.Name)` → absent → "" → luôn khác → wipe tên).
4. **Accessory** (`AccessoriesController.cs`) — full PUT reuse `CreateAccessoryRequest`, thiếu CompanyId-lock-sau-checkout.

**Fix (copy Task F/M1):**
- **User**: DTO `IsSuperUser`/`IsActive` → `bool?`; handler chỉ gán khi `HasValue`; group-sync Keycloak patch-aware (`== true`/`== false`); controller demote-guard (`command.IsSuperUser == false`) — **lockout + company-scoping Task J giữ nguyên**.
- **Admin ref-data**: Manufacturer/Supplier/Location patch trực tiếp (field nullable `is not null`, Name/Code `!IsNullOrWhiteSpace` + validation chỉ khi có gửi — sửa cả bug Code-absent-reject); AssetModel/Category có bool không nullable → **DTO `UpdateAssetModelRequest`/`UpdateCategoryRequest` với `bool?`**.
- **Asset.Name**: thêm guard `!string.IsNullOrWhiteSpace(request.Name) && != asset.Name` (giống AssetTag; Name required nên giữ cũ khi absent/empty là đúng).
- **Accessory**: tách `UpdateAccessoryRequest` (nullable) + **thêm CompanyId-lock sau checkout** (mirror Consumable/License, patch-aware); giữ guard "active checkouts".

**Verify (API thật + DB):**
- **User**: partial update (id+firstName+lastName+email, KHÔNG isSuperUser/isActive) → **200**, DB `isActive=true` giữ nguyên (không reset về false). Lockout guard / policy admin / company-scoping không đổi (suite Task J vẫn pass).
- **Asset**: partial (name+notes) → **200**, DB PurchaseCost=500/OrderNumber=ORD-9 giữ nguyên. (Lưu ý: model validation `[ApiController]` đã yêu cầu Name required → payload thiếu name bị chặn 400 ở tầng model; guard handler vẫn phủ cho caller trực tiếp — xUnit test chứng minh.)
- **Accessory**: partial (name only) → **200**, qty/minAmt/modelNumber/notes giữ nguyên; **đổi CompanyId sau checkout → 400 FIELD_LOCKED**.
- **Admin ref-data**: Supplier/Category/AssetModel partial (name only) → **200**, DB Code/TagColor/Notes/ModelNumber/Requestable giữ nguyên.
- `dotnet test --filter "Category!=Concurrency"` → **273 PASS** (+6 `TaskM2PatchSafetyTests`); `npm run build` 0 lỗi TS; `audit-sweeps.ps1` exit 0; format sạch. Dữ liệu test `QM2*` dọn sạch DB (8 bảng = 0).

**TỔNG KẾT chuỗi patch-safety đã đóng (F → M1 → M2):** Asset (F), Component/License/Consumable (M1), User + Category/Manufacturer/Supplier/Location/AssetModel + Accessory (M2). Toàn bộ Update handler trong hệ thống giờ áp patch semantics — field không gửi giữ nguyên, không còn full-replace wipe dữ liệu.

### Việc treo lại (mở phiên sau — KHÔNG cần trả lời ngay khi đóng phiên)

**✅ ĐÃ ĐÓNG — Asset.Name guard (`!string.IsNullOrWhiteSpace`)** (verify API thật 2026-08-16, qua cổng proxy `http://localhost:5428`, token `admin`/superuser, asset `a79fd8ed…` floater `isConfirmed=false`):

- **Câu 1 — DTO nào chặn ở model binding:** `UpdateAssetRequest.Name` (`AssetsController.cs:312`) là `string Name` (non-nullable reference type), **KHÔNG có `[Required]`**. Việc chặn "thiếu field" đến từ `[ApiController]` **inferred required** (property non-nullable reference type): `name` ABSENT → **400** `{"errors":{"Name":["The Name field is required."]}}` TRƯỚC khi tới handler. Khẳng định cũ (dòng 301) là **ĐÚNG**.
- **Câu 2 — hành vi khi `name:""` (rỗng có chủ đích):** model binding **KHÔNG chặn** (empty vẫn "present", chỉ "thiếu field" mới bị inferred-required chặn) → lọt tới handler → guard `!string.IsNullOrWhiteSpace(request.Name)` (`UpdateAssetCommand.cs:122`) bỏ qua phép gán → trả **200 success**, DB **GIỮ NGUYÊN** "ST5A Import Asset1" (verify lại bằng GET), **không wipe thành rỗng**.

**KẾT LUẬN (đã chốt):** **Chấp nhận hành vi bất đối xứng** — `absent → 400` (do model binding inferred-required), `rỗng có chủ đích → 200 giữ nguyên` (do handler guard). Cả 2 nhánh đều **đảm bảo không mất dữ liệu** — đúng mục tiêu chính của chuỗi patch-safety F→M1→M2 (field không gửi/gửi rỗng → không coi là thay đổi → không xóa nhầm). **KHÔNG cần thêm validate bổ sung.** Đã khôi phục nguyên trạng asset test.

**✅ Task M2 HOÀN TẤT — chuỗi patch-safety TOÀN HỆ THỐNG đóng hoàn toàn:** Asset, Component, License, Consumable, User, Category, Manufacturer, Supplier, Location, AssetModel, Accessory.

### ST-R — Task R: Dọn code chết + cập nhật tài liệu lạc hậu (2026-08-16)

**Mục 1 — Xóa `DbInitializer.cs` (code chết):**
- **Grep toàn bộ solution + repo root (gồm cả `.md`):** KHÔNG có reference **code/test/script/comment** nào gọi `DbInitializer.Initialize(...)`; chỉ có 3 reference **thuần tài liệu** — `CLAUDE.md:86` (mô tả cấu trúc folder), `docs/BACKEND_ARCHITECTURE_REVIEW_2026-08-15.md:105` (chính là audit khuyến nghị xóa), `docs/HANDOFF_LATEST.md` (mô tả Task R). **Đã chốt với user** (qua tool `question`): xóa file + gỡ `DbInitializer` khỏi `CLAUDE.md:86` để tài liệu khớp thực tế.
- File chứa `EnsureCreated()` + `ALTER TABLE categories ADD COLUMN IF NOT EXISTS` self-heal bọc try/catch nuốt lỗi — **không được gọi ở đâu** (`Program.cs` dùng `db.Database.Migrate()`). Đã xóa hẳn `Infrastructure/Persistence/DbInitializer.cs`.
- **Verify sau xóa:** `dotnet build` **0 lỗi** (warnings đều pre-existing) · `dotnet test --filter "Category!=Concurrency"` **273/273 PASS** · `scripts/audit-sweeps.ps1` **exit 0**. Không có phụ thuộc ngầm nào bị vỡ.

**Mục 2 — Cập nhật `docs/HANDOFF_DATETIME_KIND_AUDIT.md` khớp thực tế:**
- Đối chiếu **toàn bộ** write site trong mục 2 với code thật (không chỉ 3 field task nêu). Phát hiện `TerminationDate`/`StartDate`/`CompletionDate` **ĐÃ fix từ trước** (không còn "chưa xử lý"):
  - NHÓM A `StartDate`/`CompletionDate`: Create `AssetMaintenancesController.cs:293-294`, Update CompletionDate `:367` — `SpecifyKind(Unspecified)`.
  - NHÓM D `TerminationDate`: Create `LicensesController.cs:268`, Update `:327` — `SpecifyKind(Unspecified)`.
  - **Phát hiện thêm 2 chỗ lạc hậu tương tự:** NHÓM C reference cũ `ComponentUnitsController.cs:58-59` → logic soft-delete/`UpdatedAt`/`DeletedAt` đã extract sang `ComponentAllocationService.cs:131,216,309,353,406,407` (Task CLEANUP); NHÓM D `LicensesController.cs:356` → `:359` (lệch dòng do Task M1).
- Cập nhật các dòng: NHÓM A row `StartDate`/`CompletionDate`, NHÓM C row `UpdatedAt`+`DeletedAt`, NHÓM D row `DeletedAt`+`TerminationDate`, khuyến nghị mục 6 (đánh dấu **ĐÃ XONG**), + thêm **mục 8** "Task R — Đối chiếu lại toàn bộ danh sách" ghi rõ từng site đã verify. Giữ nguyên lịch sử/điều tra cũ.
- **Kết luận mục 2:** toàn bộ NHÓM A–E + LicenseSeat đều ĐÃ xử lý đúng (`SpecifyKind(UtcNow, Unspecified)` hoặc không có write site); **không còn mục nào lạc hậu** trong tài liệu.

**Verify tổng:** build 0 lỗi · test 273/273 PASS · sweep exit 0. Không đổi hành vi hệ thống — chỉ dọn dẹp thuần túy.

---

### ST-Q — Task Q: DI Extension Pattern — tách Program.cs theo layer (2026-08-16)

**Mục đích:** refactor thuần tổ chức code — KHÔNG đổi hành vi. `Program.cs` 339 dòng → **94 dòng** (chỉ còn composition root: gọi extension + HTTP pipeline). Không có `*ServiceCollectionExtensions.cs`/`*DependencyInjection.cs` nào trước đây; giờ tạo 5 extension + 2 class mới.

**Cấu trúc mới (mỗi file giữ nguyên lifetime service như cũ):**
- `Infrastructure/Persistence/PersistenceServiceCollectionExtensions.cs` → `AddPersistence(IHostApplicationBuilder)`: `AddNpgsqlDbContext<AppDbContext>("aspire-react-db")` + Postgres health check.
- `Infrastructure/InfrastructureServiceCollectionExtensions.cs` → `AddInfrastructureServices(IServiceCollection, IConfiguration)`: KeycloakOptions/HttpClient/IKeycloakService(Singleton), `IJitUserProvisioningService`(Scoped), ICurrentUserService(Scoped), IActionLogService(Scoped), IComponentAllocationService(Scoped), IConsumableAllocationService(Scoped), ICompanyScopeService(Transient), HttpContextAccessor, MemoryCache, PermissionLockoutGuard(Scoped).
- `Infrastructure/Authentication/AuthenticationServiceCollectionExtensions.cs` → `AddKeycloakAuthentication(IServiceCollection, IConfiguration)`: JWT bearer (`OnTokenValidated` giờ chỉ resolve `IJitUserProvisioningService` + stamp `local_user_id`).
- `Infrastructure/Authorization/AuthorizationServiceCollectionExtensions.cs` → `AddPermissionAuthorization(IServiceCollection)`: policy loop từ `PermissionCatalog` + `PermissionHandler`(Scoped).
- `Application/ApplicationServiceCollectionExtensions.cs` → `AddApplicationServices(IServiceCollection)`: MediatR (2 assembly) + `AddOpenBehavior(typeof(ValidationBehavior<,>))` (Task L) + FluentValidation.
- **`Infrastructure/Services/JitUserProvisioningService.cs`** → `IJitUserProvisioningService.ProvisionAsync(ClaimsPrincipal?)`: toàn bộ logic JIT (tạo user local, sync email/name, gắn `IsSuperUser` theo `RealmAccessHelper`) — extract nguyên vẹn từ `OnTokenValidated` (L85-149) → giờ unit-test được độc lập.
- **`Infrastructure/Persistence/StartupDataSeeder.cs`** → `Seed(IServiceProvider)`: `db.Database.Migrate()` + seed default system groups (Superuser/Admin) + `PermissionMigration.AssignLegacySuperUsersToSuperuserGroupAsync` — chuyển nguyên vẹn từ khối L219-298. (KHÔNG tái tạo `DbInitializer` đã xóa ở Task R.)

**Thứ tự đăng ký quan trọng giữ nguyên:** ServiceDefaults → Persistence(Npgsql) → Redis → Application(MediatR+ValidationBehavior) → Infrastructure → Authentication(JWT) → Authorization. `AddOpenBehavior` ở đúng vị trí cũ trong MediatR. `ValidationBehavior` nằm trong `AddApplicationServices`.

**Điều chỉnh so với đề xuất audit (có lý do):** Redis (`AddRedisClient` + health `AddRedis`) GIỮ ở Program.cs — Task P riêng, không đụng; CORS/controllers/ProblemDetails/ExceptionHandler/OpenApi/pipeline giữ ở Program.cs (ngoài 5 extension đề xuất). `AddPersistence` đặt trên `IHostApplicationBuilder` (vì `AddNpgsqlDbContext` là Aspire extension trên builder, không phải IServiceCollection).

**Verify API THẬT (sau `rebuild` resource server trên Aspire stack):**
- **Login admin (user tồn tại)** → `/users/me` 200, `isSuperUser=true` (JIT sync OK).
- **JIT user MỚI (chưa từng có local)** — tạo Keycloak user qua Admin API, login → `/users/me` tạo local user mới (id mới, `isActive=true`): **không admin role → `isSuperUser=false`**; **có realm role `admin` → `isSuperUser=true`** (chứng minh `RealmAccessHelper` + JIT gắn cờ đúng theo realm role).
- **Đa module DI resolve:** Asset list, License list, Component list, Consumable list, Users list, Dashboard summary → đều **200** (resolve `ICompanyScopeService` + các controller service).
- **Permission policy:** user không permission (`qit-jit-normal-probe`, isSuperUser=false) → GET /users, GET /assets, POST /assets đều **403**.
- **ValidationBehavior (Task L):** tạo Asset trùng `AssetTag` → **400** `{"errors":{"AssetTag":["Mã tài sản đã tồn tại trong hệ thống."]}}`.
- **Seed/migration:** log khởi động "No migrations were applied. The database is already up to date." → `Migrate()` + seed chạy đúng, idempotent, không lỗi.
- **Ghi chú pre-existing (KHÔNG phải regression Task Q):** local user JIT-created luôn có `email`/`firstName`/`lastName` placeholder/rỗng dù token có claim — do `principal.FindFirst("email"/"given_name")` trả null trong `OnTokenValidated` (claim-mapping quirk, giống hệt code gốc; admin cũng `firstName/lastName` rỗng). Ngoài phạm vi Task Q.

**Verify khác:** `dotnet build` 0 lỗi · `dotnet test` **277/277 PASS** (gồm cả Concurrency) · `npm run build` 0 lỗi TS · `scripts/audit-sweeps.ps1` **exit 0** (giờ quét 126 file .cs). Dữ liệu test `qit-jit-*` đã dọn sạch (Keycloak 0 user + local soft-deleted qua API).

**Số dòng Program.cs:** trước **339** → sau **94** (phần DI là ~15 dòng gọi extension).

### ST-P — Task P: Redis caching cho endpoint reference-data (2026-08-16)

**Phạm vi:** cache 5 endpoint đọc-nhiều/ít-đổi — `GET /categories`, `/manufacturers`, `/suppliers`, `/permissions` (catalog), `/companies`. KHÔNG cache dữ liệu nghiệp vụ (Asset/Accessory/Consumable/Component/License/User...).

**Phương án (nhất quán 5 endpoint):** `[OutputCache]` + `app.UseOutputCache()` + Redis backing qua `AddRedisOutputCache` (package `Aspire.StackExchange.Redis.OutputCaching`, đã có sẵn).
- `Infrastructure/Caching/CachingServiceCollectionExtensions.cs` → `AddRedisCaching()`: `builder.AddRedisOutputCache("cache")` + `Configure<OutputCacheOptions>(o => o.AddPolicy("RefData", ReferenceDataCachePolicy.Instance))`. LƯU Ý: không gọi lại `AddOutputCache` (sẽ re-register in-memory store, override Redis store).
- `Infrastructure/Caching/ReferenceDataCachePolicy.cs` → `IOutputCachePolicy` cho phép cache **authenticated** GET/HEAD, chỉ 200 (không Set-Cookie), vary theo toàn bộ query string (`QueryKeys="*"`, cho `/categories?type=`), TTL 300s.
- `Program.cs`: bỏ `AddRedisClient("cache")` + health check Redis thủ công (AddRedisOutputCache tự cung cấp health check/readiness); thêm `app.UseOutputCache()` **SAU `UseAuthorization()`** (để unauthorized bị 403 trước khi tới cache — không rò rỉ). Program.cs giờ **93 dòng**.

**🔴 Bug quan trọng phát hiện khi verify (fix trong task):** ASP.NET **default output-cache policy KHÔNG cache response của authenticated request** (`[OutputCache]` mặc định từ chối). Ban đầu `[OutputCache(Duration=300)]` trên 5 endpoint đều `[Authorize]` → **không có gì được cache** (behavioral test: sau update DB, GET trả data mới; Redis trống). Fix: custom `ReferenceDataCachePolicy` bật cache cho authenticated request — an toàn vì 5 endpoint trả data **GLOBAL** (giống nhau cho mọi user có quyền) và UseAuthorization chạy trước UseOutputCache.

**Bước 0 — phân tích user/company-scoping của từng endpoint (kết quả):**
- `/categories`, `/manufacturers`, `/suppliers` — entity KHÔNG có CompanyId → **không company-scoped**, trả toàn bộ → global, cache chia sẻ an toàn.
- `/permissions` (catalog) — `GetPermissions` trả **PermissionCatalog tĩnh** (giống nhau cho mọi authenticated user) → global, cache an toàn. **KHÔNG cache** `/permissions/check` (per-user) và `/permissions/matrix` (admin, sensitive) — xác nhận không có key Redis cho 2 sub-route này.
- `/companies` — **KHÁC GIẢ ĐỊNH TRONG TASK:** code hiện tại (`CompaniesController.GetAll`) **KHÔNG** có company-scoping — trả **TẤT CẢ** công ty cho mọi user có `companies.view` (không gọi `GetCurrentUserCompanyIdAsync`). Vì global → cache chia sẻ an toàn. ⚠️ NHƯNG đây là điểm cần xem lại ở phiên sau (regular user thấy toàn bộ cây công ty) — ghi backlog.

**Verify API THẬT (Aspire stack, sau `rebuild`):**
- **Cache thật ghi vào Redis:** warm 5 endpoint → Redis có 5 key `__MSOCV_GET HTTP .../API/V1/{CATEGORIES,MANUFACTURERS,SUPPLIERS,PERMISSIONS,COMPANIES} Q *=`; `/assets` (business) **KHÔNG** có key.
- **Cache hit (behavioral):** tạo category probe → warm (v1) → update DB (v2) → GET lại trả **v1 stale** (cache serve), chứng minh cache hoạt động. ⚠️ **ĐÂY CHÍNH LÀ BẰNG CHỨNG CỦA VIỆC THIẾU INVALIDATION — đã fix ở phần bổ sung mục 2 (ST-P-INVAL), xem bên dưới.**
- **Bảo mật — KHÔNG rò rỉ:** user không quyền (`qit-noperm-probe`, cache đã warm từ admin) → `GET /companies`, `/categories`, `/suppliers`, `/manufacturers` đều **403** (KHÔNG nhận cached 200 — UseAuthorization chặn trước UseOutputCache); `GET /permissions` (any-auth) → **200** đúng.
- `/health` vẫn **Healthy** (redis readiness từ AddRedisOutputCache).

**Verify khác:** `dotnet build` 0 lỗi · `dotnet test` **277/277 PASS** (gồm Concurrency) · `npm run build` 0 lỗi TS · `audit-sweeps.ps1` **exit 0** (128 file .cs). Dữ liệu test dọn sạch (probe category, `qit-noperm-probe` Keycloak + local, các key Redis tự hết hạn 300s).

### ST-P-INVAL — Task P bổ sung: Cache invalidation (2026-08-16)

**Phạm vi:** bổ sung invalidation cho 4 nhóm reference-data CÓ endpoint ghi: Category / Manufacturer / Supplier (AdminController) + Company (CompaniesController). Với `/permissions`: **XÁC NHẬN KHÔNG cần invalidate** — `PermissionCatalog` là catalog TĨNH trong code (`PermissionsController.GetPermissions` đọc `PermissionCatalog.All`), **không có endpoint ghi nào sửa được nó qua API** (groups/users chỉ sửa `GroupPermissions`/`UserPermissions`, không đổi catalog) → không bao giờ stale, cache an toàn chờ TTL.

**Cơ chế (helper tập trung — tránh "mỗi chỗ một kiểu" kiểu ActionLog Task N):**
- `Infrastructure/Caching/CacheInvalidator.cs`: interface `ICacheInvalidator` + impl `CacheInvalidator` (wraps `IOutputCacheStore.EvictByTagAsync`) + class hằng `CacheTags` (`ref:categories/manufacturers/suppliers/companies`). Mỗi nhóm có method riêng → evict ĐÚNG tag của nó.
- Đăng ký singleton trong `CachingServiceCollectionExtensions.cs` (`AddRedisCaching`) — đúng DI extension pattern (Task Q), KHÔNG thêm Program.cs.
- Tag gắn qua `[OutputCache(..., Tags = [CacheTags.X])]` trên từng GET endpoint (4 chỗ). `EvictByTagAsync` nhắm đúng entry có tag đó.
- Gọi `await _cacheInvalidator.InvalidateXAsync()` **NGAY SAU `SaveChangesAsync`** (sau khi ghi DB thành công) ở TẤT CẢ Create/Update/Delete của 4 nhóm — nếu ghi DB fail (validation/404/guard) thì không chạy tới → không evict oan.

**Verify API THẬT (Aspire stack, rebuild server):** mỗi nhóm warm → update → GET NGAY trả data MỚI (không stale), kèm xác nhận key `__MSOCV_GET .../{GROUP} ...` + tag `__MSOCT_ref:{group}` **BIẾN MẤT** ngay sau write (trước GET) — còn lại trong Redis:
- Category: Update (Chuột→Chuot TESTINV→Chuột), Create (INV-CREATE-CAT), Delete — đều evict + GET ngay thấy mới; restored.
- Manufacturer: Update (url), Create (XINV/INV-MANUF), Delete — evict + fresh; restored (url=null).
- Supplier: Update (url), Create (XSUP/INV-SUPPLIER), Delete — evict + fresh; restored.
- Company: Update (đổi tên), Create (INV-COMPANY), Delete — evict + fresh; restored.
- **Invalidate đúng phạm vi:** sửa Supplier → key Categories + Manufacturers **vẫn còn nguyên** trong Redis (không bị xóa oan); sửa Manufacturer → Categories còn; sửa Company → Categories/Manufacturers/Suppliers còn.
- **Không evict khi ghi DB fail:** gửi Update Manufacturer code quá ngắn (`"X"` → 400 "Mã NSX phải từ 2-5 ký tự") → manufacturers cache (tag + value) **vẫn nguyên**, không bị evict oan.
- `dotnet build` 0 lỗi · `dotnet test` **277/277 PASS** · `npm run build` 0 lỗi TS · `audit-sweeps.ps1` **exit 0**. Dữ liệu test dọn sạch (probe category/manufacturer/supplier/company + restored Chuột/DELL/PVU).

**Backlog liên đới (KHÔNG fix trong task này):** `/companies` thiếu company-scoping + cảnh báo liên đới cache → **backlog mục 33**.

**Backlog (KHÔNG làm trong task):** `/companies` thiếu company-scoping (xem ở trên) — **backlog mục 33 kèm CẢNH BÁO LIÊN ĐỚI CACHE**. Không cache thêm dữ liệu nghiệp vụ trong đợt này (theo phạm vi task).

### ST-V — Task V: Company-scoping cho /companies + đồng bộ cache theo scope (2026-08-16)

> **ĐÓNG backlog mục 33** — xử lý CẢ scoping LẪN cache đồng thời trong 1 task, đúng cảnh báo liên đới (sửa 1 trong 2 là gây rò rỉ cache chéo công ty).

**Phạm vi:** (A) company-scoping cho `GET /companies`; (B) đổi cache key sang per-scope. KHÔNG sửa CompanyTreeSelect frontend (chỉ nhận dữ liệu đã scope từ backend).

**A — Company-scoping (`CompaniesController.GetAll`):** copy đúng pattern Task K/U — ép filter theo `ICompanyScopeService.GetCurrentUserCompanyIdAsync()`, không phụ thuộc query param client.
- Superuser (`IsSuperUser()`) → toàn bộ cây (không đổi).
- User thường CÓ công ty → CHỈ cây subtree gốc tại công ty của họ (`GetSubtreeAsync` = công ty + toàn bộ con cháu).
- User thường KHÔNG có công ty → toàn bộ cây (nhất quán với convention Departments/GetLocations: user không company không bị filter).
- `BuildTree` xử lý đúng node cha LẪN con: một node là root khi `ParentId == null` HOẶC parent không nằm trong tập visible (subtree root của user scoped — parent bị loại bởi filter) → user chỉ thấy 1 nhánh của mình, không thấy công ty cha/ngoài nhánh.

**B — Đồng bộ cache theo scope (`CompanyScopeCachePolicy`):**
- Mới `Infrastructure/Caching/CompanyScopeCachePolicy.cs` (đăng ký trong `CachingServiceCollectionExtensions` với policy name `RefDataCompanyScope`). Giống `ReferenceDataCachePolicy` (TTL 300s, GET/HEAD, 200-only, vary query `*`) NHƯNG thêm **`VaryByValues["company_scope"]`** vào cache key (cùng cơ chế `VaryByValue` đã document).
- Scope key mirror đúng logic data selection: Superuser / user không công ty → `"all"`; user có công ty X → `"c:<X>"`. → mỗi scope có cache key RIÊNG, không đọc nhầm entry của nhau.
- **Superuser có key `all` riêng** (KHÔNG dùng chung với user thường có scope `c:...`).
- Invalidation giữ nguyên: mọi scope variant đều mang tag `ref:companies` (qua `[OutputCache(Tags=[CacheTags.Companies])]`) → `InvalidateCompaniesAsync()` (evict theo tag) xóa TẤT CẢ scope key cùng lúc, không sót variant stale.

**Verify API THẬT (Aspire stack, rebuild server; test users `taskv-a`/`taskv-b` + group `taskv-grp`):**
- **Scoping:** `taskv-a` (công ty con `5938e89c`) → GET /companies CHỈ trả `Công ty Quản lý bay miền Trung` (KHÔNG có công ty cha `Tổng công ty`); `taskv-b` (công ty `8c0d62bc`) → CHỈ trả `Công ty Cổ phần ABC`; Superuser admin → toàn bộ cây (kể cả cha + con).
- **Cách ly cache theo scope (QUAN TRỌNG NHẤT):** warm lần lượt `taskv-a` → Redis có key `company_scope=c:5938e89c...`; `taskv-b` → key `company_scope=c:8c0d62bc...` (KHÁC, response CHỈ ABC, không lẫn dữ liệu taskv-a); admin → key `company_scope=all`. **3 key Redis riêng biệt**, không 1 key dùng chung → không rò rỉ chéo company qua cache.
- **Invalidation sau khi đổi cache key:** Update company (đổi tên ABC→ABC TV-UPD) qua admin → **CẢ 3 key** (`all` + `c:8c0d62bc` + `c:5938e89c`) **BIẾN MẤT** trong Redis (evict theo tag → mọi scope); re-read: taskv-b thấy tên MỚI ngay, taskv-a vẫn đúng scope của nó. Restored tên ABC.
- **CompanyTreeSelect UI (user thường, browser thật):** login `taskv-b` → mở form Tạo Accessory (có CompanyTreeSelect) → dropdown **CHỈ hiện `Công ty Cổ phần ABC`**, không có QCR-CO / Tổng công ty / miền Trung. Screenshot `taskv-company-treeselect.png`. (License form KHÔNG hiện company cho user thường — superuser-only từ Task G — nên dùng Accessory form.)
- `dotnet test` **280/280 PASS** (+3 test mới `CompanyTreeScopeTests`: Superuser thấy hết, user thường chỉ thấy subtree riêng, user không company thấy hết) · `npm run build` 0 lỗi TS · `audit-sweeps.ps1` **exit 0**.
- Dữ liệu test dọn sạch: `taskv-a`/`taskv-b` deactivated (soft-delete, FK ActionLog.CreatedBy), group `taskv-grp` deleted, tên ABC restored, temp files removed.

### ST-S1 — Task S1: Gộp đoạn filter company-visibility bị copy-paste (Reports/Dashboard) (2026-08-16)

**Phát hiện (Bước 0):** đoạn `FilterVisibleLogsAsync` (~30 dòng, filter action-log theo company của user với 6 batched query Asset/Consumable/Accessory/Component/License/ComponentUnit) **GIỐNG HỆT (byte-for-byte)** ở `ReportsController.cs:147-183` và `DashboardController.cs:179-215` — signature `(IReadOnlyList<ActionLog> logs, Guid userCompanyId)` + return filter `ItemType switch` y hệt, KHÔNG có khác biệt tinh vi nào. (Trong task này KHÔNG có vấn đề scoping mới phát hiện ở 2 đoạn này — chỉ gộp code trùng.)

**Việc làm (thuần gộp, KHÔNG đổi hành vi):**
- Mới `Infrastructure/Services/ActionLogVisibilityService.cs`: interface `IActionLogVisibilityService` + impl `ActionLogVisibilityService` (wraps `AppDbContext`, chứa đúng body cũ). Chọn service riêng (KHÔNG nhét vào `ICompanyScopeService`) vì logic này đặc thù Reports/Dashboard (filter danh sách action-log đã materialize theo company của item), không phải trách nhiệm scope service chung.
- Đăng ký scoped trong `InfrastructureServiceCollectionExtensions.cs` (`AddInfrastructureServices`) — đúng DI extension pattern Task Q, không thêm Program.cs.
- `ReportsController` + `DashboardController`: inject `IActionLogVisibilityService`, gọi `_actionLogVisibility.FilterVisibleLogsAsync(...)`, **XÓA method private trùng** ở cả 2. Không đổi response format, không đụng controller khác.

**Verify API THẬT (Aspire stack, rebuild server; test user `s1-test` công ty `5938e89c` + group `s1-grp` có `reports.view`):**
- Reports `/reports/checkout-history`: user thường → 200, **122 mục** (subset: asset 16/consumable 43/accessory 54/component 2/license 5/unit 2); Superuser admin → 200, **138 mục** (asset 19/consumable 43/accessory 56/component 7/license 11/unit 2). User thường là subset đúng theo company-scoping.
- Dashboard `/dashboard/recent-activity`: user thường → 200 (20 mục scoped); Superuser → 200 (20 mục). Các endpoint khác Reports (`custom`/`depreciation`/`audit`) + Dashboard (`summary`/`assets-by-status`/`assets-by-category`/`low-stock`) đều 200 cho Superuser.
- `dotnet test` **280/280 PASS** · `npm run build` 0 lỗi TS (không sửa FE) · `audit-sweeps.ps1` **exit 0** (131 file .cs).
- Dữ liệu test dọn sạch: `s1-test` deactivated, group `s1-grp` deleted, temp files removed.
- **Ghi nhận pre-existing ngoài phạm vi (KHÔNG sửa):** `GET /dashboard/monthly-checkout-trend` trả **500** do `string.Format` trong LINQ Select KHÔNG dịch được sang Postgres (EF translation error). Endpoint này KHÔNG dùng `FilterVisibleLogsAsync` (dùng path `visibleAssetIds` riêng), KHÔNG bị ảnh hưởng bởi refactor này; frontend (`DashboardPage`) không gọi endpoint này → không ảnh hưởng UI. Đề xuất fix riêng sau (đưa `string.Format` ra client-side).

### ST-S2a — Task S2a: Thiết kế helper ActionLog typed-safe + áp dụng thử (License + Maintenance) (2026-08-16)

**Bối cảnh:** ~40+ vị trí Controller viết `_context.ActionLogs.Add(new ActionLog{...})` bằng object initializer tự do — compiler KHÔNG báo khi bỏ sót field bắt buộc (đã gây 3 lỗi thật: thiếu CompanyId Task N, sai TargetType ST4/Task E, TargetId=null Task N).

**Bước 0 — phân tích field (11 vị trí: 5 License + 6 Maintenance):**
- **LUÔN có ở MỌI log (5 field):** `ItemType`, `ItemId`, `ActionType`, `CreatedBy`, `CompanyId`.
- **Đặc thù action:** `TargetType`/`TargetId`/`TargetSystemInfoId`/`TargetSystemInfoName` (chỉ License Checkout), `LogMeta` (chỉ Maintenance Delete), `Note` (mọi nơi).

**Thiết kế helper (đã chọn — giải thích vì sao):**
- `Domain/Entities/ActionLogEntry.cs`: **object initializer + C# 11 `required` properties**. Chọn builder-by-`required` thay vì factory positional params vì: (1) object initializer có TÊN field → tránh nhầm thứ tự tham số khi nhiều Guid (ItemId/CreatedBy/CompanyId); (2) `required` ép compiler từ chối khi thiếu 5 field bắt buộc — mục tiêu chính (không thể "quên truyền"). (3) `.Build()` materialize thành `ActionLog`.
- `IActionLogService.Log(ActionLogEntry)` + impl trong `ActionLogService` = **wrapper THIN** chỉ `_context.ActionLogs.Add(entry.Build())` — KHÔNG enrichment (khác `LogAction` có resolve CreatedBy/SystemInfo/LocationName/RemoteIp...), giữ transaction của caller. Vì vậy ghi log KHÔNG đổi hành vi.
- **Cố ý KHÔNG reject `CompanyId == Guid.Empty`:** floater Maintenance legitimately có `CompanyId == Guid.Empty` (server set `Asset.CompanyId ?? Guid.Empty`); `required Guid?` chỉ ép "phải truyền tường minh", không cấm sentinel floater. Đây là quyết định tường minh (không phải "quên truyền").
- Đặt `ActionLogEntry` ở **Domain/Entities** (không phải Infrastructure/Services) vì `IActionLogService` (Domain/Interfaces) phải reference được nó; nó là value-object của entity `ActionLog`.

**Áp dụng (thay HOÀN TOÀN, không để song song):**
- `LicensesController` (5 chỗ): Create/Update/Delete/Checkout/Checkin → `_actionLogService.Log(new ActionLogEntry{...})`.
- `AssetMaintenancesController` (6 chỗ): Create/Update/Delete/Close/Inspect/Reopen → `_actionLogService.Log(new ActionLogEntry{...})`. Delete vẫn truyền `LogMeta` (snapshot đầy đủ).
- Cả 2 inject `IActionLogService` (thêm param constructor); cập nhật 34 call-site test (LicenseTests, TaskM1, AssetMaintenanceTests, SystemDetailTests) dùng `TestHelpers.CreateActionLogService(ctx)`.

**Sweep 3 (audit-sweeps.ps1) — đã điều chỉnh:** thêm **Pattern 3**: `_actionLogService.Log(new ActionLogEntry{...})` coi là AN TOÀN (compiler-enforced, không quét sâu — vì compiler đã ép CompanyId); chỉ flag `Log(new ActionLog{...})` (bỏ qua builder, mất lớp bảo vệ). Negative-test bằng probe file `Log(new ActionLog{...})` → bắt được 1 violation; gỡ probe → exit 0. Cũng phải bỏ literal `ActionLogs.Add(new ActionLog` khỏi doc-comment (nếu không Pattern 2 false-positive).

**Verify API THẬT (Aspire stack, rebuild server; admin token):** với MỖI action License + Maintenance gọi qua API → truy vấn `action_logs` trực tiếp trong Postgres để đối chiếu `ItemType/ActionType/CompanyId/CreatedBy/TargetType/TargetId/LogMeta`:
- **License:** Create (1) `CompanyId=5938e89c` Target null; Update (2) `CompanyId` giữ; Checkout (4) `TargetType=1(User)` `TargetId=03f4a946`; Checkin (5) Target null. (Delete bị guard `LICENSE_IN_USE` vì có checkout history — đúng, log Checkout/Checkin đã ghi.)
- **Maintenance:** Create (1), Update (2), Inspect (20), Close (18), Reopen (19) — đều `CompanyId=8c0d62bc` (= m.CompanyId), Target null; Delete (3) có `LogMeta` đầy đủ (`has_meta=t`).
- **Tất cả giá trị y hệt trước refactor** (ItemType/ItemId/CompanyId/TargetType/TargetId/LogMeta/Note khớp). `dotnet test` **280/280 PASS** · `npm run build` 0 lỗi TS · `audit-sweeps.ps1` **exit 0**.
- Dữ liệu test dọn sạch: license S2A-TEST-LIC soft-deleted, maintenance S2A-TEST-MAINT deleted qua API, temp files removed (kiểm tra DB: 0 active test record).

**Cho S2b (nhân rộng):** áp dụng đúng pattern này — `_actionLogService.Log(new ActionLogEntry{ ItemType, ItemId, ActionType, CreatedBy=GetCurrentUserId(), CompanyId, Note })` cho các Controller còn lại (Admin, Component, Department, Company, CustomField, SystemInfo...); cẩn thận với master data không có CompanyId (Category/Model/Manufacturer/Supplier/Company/CustomField) — vẫn phải truyền `CompanyId` (có thể null) để compiler hài lòng, đúng tinh thần "bắt buộc tường minh".

### ST-S2b — Task S2b: Nhân rộng ActionLogEntry helper cho TOÀN BỘ Controller còn lại (2026-08-16)

> **ĐÓNG chuỗi S2 (a+b)** — mọi vị trí ghi ActionLog thủ công bằng free-form object initializer `_context.ActionLogs.Add(new ActionLog{...})` trong hệ thống đã chuyển sang `_actionLogService.Log(new ActionLogEntry{...})`.

**Bước 0 — grep toàn bộ solution, phân loại 2 pattern:**
- **(A) Free-form `_context.ActionLogs.Add(new ActionLog{...})` — bug class của S2a — ĐÃ CHUYỂN (46 vị trí).** Đây là mục tiêu. Các vị trí này KHÔNG enrichment (không set RemoteIp/UserAgent/ActionSource) nên `Log(entry)` thin (chỉ `Add(entry.Build())`) là behavior-preserving. Files: AdminController (15: Model/Category/Manufacturer/Supplier/Location), ComponentsController (4), ComponentAllocationService (7), CompaniesController (3), DepartmentsController (3), SystemInfoController (6), CustomFieldsController (3), Asset commands (BulkUpdate 1, Audit 2, AcceptDecline 2). Tổng **46 vị trí** + 11 (S2a License/Maintenance) = **57 vị trí** free-form đã chuẩn hóa.
- **(B) `_actionLogService.LogAction(...)` — helper ENRICHED khác (set RemoteIp/UserAgent/ActionSource/LocationName/SystemInfo-name/CreatedBy-fallback) — GIỮ NGUYÊN (KHÔNG chuyển).** Consumable, Group, User, ImportExport, Accessory commands, nhiều Asset commands, ConsumableAllocationService. Lý do: chuyển sang `Log(entry)` thin sẽ DROP enrichment = đổi hành vi = vi phạm "không đổi hành vi ghi log". Đây KHÔNG phải free-form bug class (Sweep 3 Pattern 1 đã ép `companyId:`), nên giữ nguyên là đúng. (Có thể cân nhắc S2c tách riêng nếu muốn thống nhất enrichment, nhưng ngoài phạm vi.)

**Trường hợp ĐẶC BIỆT (Bước 0.3) — đã xử lý, không tự ý vượt phạm vi:** Asset Audit/BulkAudit/AcceptDecline set custom `ActionDate` (Audit có thể dùng ngày quá khứ). `ActionLogEntry` thiếu `ActionDate` → **thêm optional `DateTime? ActionDate`** (khi null `Build()` để entity default UtcNow — behavior-preserving cho mọi trường hợp còn lại). Không mở rộng gì khác.

**Verify API THẬT (Aspire stack, rebuild server; admin token; test data `S2B-*`):** mỗi Controller trong scope tạo ít nhất 1 record → truy vấn `action_logs` Postgres đối chiếu:
- **Admin-Category** (ItemType=7), **Department** (16), **Company** (15), **System** (17), **CustomField** (19), **Component** (4) — đều `ActionType=1(Create)`, `CompanyId` đúng (null cho master data, `5938e89c` cho scoped), `CreatedBy=eb34917f` (admin), Note đúng.
- **Asset Audit** (ActionType=6): `Note=S2B-AUDIT-TEST`, **`ActionDate=2026-08-10 00:00:00+00`** — xác nhận custom `ActionDate` qua `ActionLogEntry.ActionDate` hoạt động đúng; asset `LastAuditDate/NextAuditDate` cập nhật đúng.
- `dotnet test` **280/280 PASS** · `npm run build` 0 lỗi TS · `audit-sweeps.ps1` **exit 0**.
- **Sweep 3 negative test:** probe file `_ctx.ActionLogs.Add(new ActionLog{ ItemType=ItemType.Asset, ... })` (không CompanyId) → Sweep 3 bắt **1 violation**; probe `ItemType=Company` → KHÔNG bắt (đúng, master-data exempt). Gỡ probe → exit 0.
- Dữ liệu test dọn sạch: 0 row `S2B%` ở 6 bảng (categories/components/departments/custom_fields/system_infos/companies), temp files removed.

**Không phát hiện bug mới** (thiếu CompanyId/sai TargetType) trong lúc rà soát — các vị trí chuyển đổi đều đã có CompanyId đúng từ trước. Các vị trí `LogAction` (Pattern B) không trong phạm vi.

### ST-BACKLOG-2 — Task BACKLOG-2: Fix JIT claim mapping + chuẩn hóa error response Accessory/Component (2026-08-16)

> Đóng 2 mục backlog: (1) JIT-provisioning không đọc được email/firstName; (2) error response Accessory/Component checkout thiếu `error_code`.

**Mục 1 — JIT claim mapping email/firstName (đã fix):**
- **Bước 0 chẩn đoán:** decode token Keycloak thật (`client_id=frontend`, user `admin`) → payload có đủ `"email"`, `"given_name"`, `"family_name"`, `"preferred_username"`. Vậy KHÔNG phải Keycloak thiếu mapper. Nguyên nhân là ASP.NET **`MapInboundClaims` mặc định đổi tên claim** sang URI dài (`email`→`ClaimTypes.Email`, `given_name`→`ClaimTypes.GivenName`, `family_name`→`ClaimTypes.Surname`); `preferred_username` không nằm trong bảng map nên đọc được — vì vậy username OK nhưng email/name null.
- **Fix (an toàn, không ảnh hưởng nơi khác):** trong `JitUserProvisioningService.ProvisionAsync`, đọc email/firstName/lastName qua helper `FirstClaim(principal, "email", ClaimTypes.Email)` — thử tên ngắn OIDC TRƯỚC rồi tên đã map `ClaimTypes.*`. KHÔNG tắt `MapInboundClaims` toàn cục (sẽ đổi hành vi đọc `ClaimTypes.NameIdentifier`/`sub` ở nhiều nơi — rủi ro cao). `preferred_username` giữ đọc tên ngắn.
- **Test:** +3 test `JitProvisioningClaimMappingTests` (đọc qua tên ngắn, qua `ClaimTypes.*` map, fallback placeholder). **Verify API thật:** tạo user Keycloak `jit-claim-test` (email `jit.real@example.com`, firstName `JIT`, lastName `RealUser`) → login lần đầu → JIT provision → local user có `Email=jit.real@example.com`, `FirstName=JIT`, `LastName=RealUser` (KHÔNG còn placeholder `@placeholder.local`/rỗng). User JIT cũ không bị ảnh hưởng (không backfill, ngoài phạm vi).

**Mục 2 — Chuẩn hóa error response Accessory/Component checkout (đã fix):**
- **Bước 0:** `AccessoryResult` (command) và `ComponentOperationResult` (service) ĐÃ có `ErrorCode`; Component checkout đi qua `RunTransactional` ĐÃ trả `error_code` (đã đúng). Gap chỉ ở **AccessoriesController.Checkout/Checkin/Delete** — drop `result.ErrorCode`, chỉ trả `message`.
- **Fix:** `AccessoriesController.Checkout/Checkin/Delete` → `BadRequest(new { status="error", message=result.Message, error_code=result.ErrorCode })` — THÊM `error_code`, GIỮ message text. Đồng bộ với Consumable/License.
- **Verify API thật:** Accessory Qty=1 checkout 2 → `{"status":"error","message":"Insufficient stock. Remaining: 1","error_code":"INSUFFICIENT_STOCK"}`; Component Qty=1 checkout 5 → `{"status":"error","message":"Insufficient stock. Remaining: 1","error_code":"INSUFFICIENT_STOCK"}`. Cả 2 giờ đều có `error_code` structured. Frontend kiểm tra: mọi modal (AccessoryCheckoutModal/ComponentDetailPage...) đọc `e?.response?.data?.message` cho hiển thị — thêm `error_code` additive, KHÔNG vỡ.
- `dotnet test` **283/283 PASS** · `npm run build` 0 lỗi TS · `audit-sweeps.ps1` **exit 0**.
- Dữ liệu test dọn sạch: user Keycloak `jit-claim-test` + local deactivated; asset/component/accessory `S2B2-*` = 0 row.

**Không xử lý:** `GET /dashboard/monthly-checkout-trend` 500 — xác nhận **dead endpoint** (frontend không gọi, `DashboardPage` chỉ gọi `/dashboard/recent-activity`). Giữ backlog, không code trừ khi có kế hoạch dùng tính năng.

### Tổng hợp tiến độ TOÀN CHUỖI (đọc nhanh cho phiên sau)

- **Làn 1 — Company-scoping (HOÀN TẤT):** Task I (Update/Delete scoping), J (lockout guard + UsersController scoping), K (read endpoints scoping), L2 (Create scoping) + 2 backlog nhỏ (extract DeleteUnitAsync, Task U GetLocations).
- **Làn 2 — Data Integrity (HOÀN TẤT):** Task L (ValidationBehavior + unique AssetTag), O (verify race thật), O-FIX (FOR UPDATE), M1 (patch-safety Component/License/Consumable), Re-scan DateTime Kind (xác nhận audit không bỏ sót — false positive), N (ActionLog TargetId/CompanyId + vá Sweep 3), **M2 — HOÀN TẤT** (patch-safety User/Admin ref-data/Asset.Name/Accessory; câu hỏi treo Asset.Name rỗng đã ĐÓNG — xem "Việc treo lại").
- **Làn 4 — kiến trúc dài hạn (ĐANG LÀM):** **Task R — HOÀN TẤT** (xóa `DbInitializer.cs` + cập nhật `HANDOFF_DATETIME_KIND_AUDIT.md`, xem mục ST-R), **Task Q — HOÀN TẤT** (tách Program.cs thành 5 extension theo layer + `IJitUserProvisioningService` + `StartupDataSeeder`, xem mục ST-Q), **Task P — HOÀN TẤT** (Redis output-cache 5 endpoint reference-data + `ReferenceDataCachePolicy`, xem mục ST-P), **Task S1 — HOÀN TẤT** (gộp đoạn `FilterVisibleLogsAsync` copy-paste Reports/Dashboard vào `IActionLogVisibilityService`, xem mục ST-S1), **Task S2a — HOÀN TẤT** (thiết kế `ActionLogEntry` typed-safe + áp dụng thử License/Maintenance, xem mục ST-S2a), **Task S2b — HOÀN TẤT** (nhân rộng `ActionLogEntry` toàn bộ vị trí free-form còn lại: 46 vị trí → tổng 57 vị trí free-form đã chuẩn hóa, xem mục ST-S2b). Còn: Task S (đồng nhất CQRS); **ghi nhận Pattern B** — các vị trí `LogAction(...)` enriched (Consumable/Group/User/ImportExport/Accessory/1 số Asset commands/ConsumableAllocation) GIỮ NGUYÊN vì chuyển sang `Log(entry)` thin sẽ drop enrichment (đổi hành vi).
- **Backlog nhỏ khác:** ComponentUnitsController.Delete đã extract xong (cleanup trước); Task U đã xong; **chuẩn hóa error response Accessory/Component checkout — ĐÃ XONG (Task BACKLOG-2 Mục 2)**; **JIT-provisioning claim mapping email/firstName — ĐÃ XONG (Task BACKLOG-2 Mục 1)**. Còn: `GET /dashboard/monthly-checkout-trend` 500 (dead endpoint — frontend không gọi, giữ backlog, không code trừ khi cần dùng sau).

### ST10 — Khắc phục đăng nhập `admin / <redacted>` (hoàn thành trong phiên này)
> *(Task 1 của backlog ban đầu — đã xử lý xong, xem mục 3 để biết tồn đọng còn lại)*

- **Root cause:** volume persistent `keycloak-data` giữ realm `aspire-react` cũ → log `Realm 'aspire-react' already exists. Import skipped` → password thật của user `admin` khác `<redacted>` (login lỗi `invalid_user_credentials`).
- **Đã sửa (không phá volume, không bypass):**
  1. Reset password user `admin` realm `aspire-react` = `<redacted>` (`temporary:false`) qua Keycloak Admin API → HTTP 204.
  2. Fix Postgres: `UPDATE users SET "IsSuperUser"=true, "Email"='admin@aspire-react.local' WHERE "Username"='admin'`.
  3. Fix JIT provisioning (`Program.cs` OnTokenValidated): khi tạo user local mới → `IsSuperUser = RealmAccessHelper.IsSuperUser(...)` thay vì hardcode `false`.
- **Bằng chứng:** token endpoint HTTP 200 + `GET /api/v1/users/me` → `isSuperUser:true` + `GET /api/v1/groups` (policy admin) → 200 + frontend 5173 → 200.


---

## 3. Đầu việc tồn đọng — làm tiếp ở phiên sau

| # | Task | Trạng thái | Ghi chú |
|---|---|---|---|
| 1 | Sửa lỗi tài khoản admin (`<redacted>`) — Keycloak/Seed/JIT | ✅ **ĐÃ HOÀN THÀNH** (ST10 trong phiên này) | Bằng chứng ở mục 2/ST10 |
| 2 | **Chuyển giao diện `ComponentListPage` sang Card List** đồng bộ thẩm mỹ với `AccessoryListPage` (không phải mô hình ProTable trước đó) | ✅ **ĐÃ HOÀN THÀNH** (phiên 2026-08-15) | Chuyển hoàn toàn sang `ProList` grid + Card kiểu Accessory/Consumable; giữ nguyên `buildParams`/`fetchPage` (filter server-side); company filter + company row chỉ render khi `isSuperUser()`; nút Sửa/Xóa/Thêm gate bằng `usePermission('components.*')`; xóa nhánh adaptive `Grid.useBreakpoint()` + `<List>` mobile + `<ProTable>` desktop. Bằng chứng: `npm run build` 0 lỗi TS, sweep exit 0, ảnh 375/768/1440 + user `demoperm` (chỉ `components.view`) không thấy filter Công ty & nút Xóa/Sửa/Thêm (`component-list-*.png` tại repo root). |
| 3 | (Tùy chọn) **Testcontainers (PostgreSQL)** cho integration test các handler dùng raw SQL + transaction (Asset Checkout/Checkin — `FromSqlRaw("... FOR UPDATE")` hiện không chạy được trên EF InMemory) | ⏳ Còn lại | Cần phê duyệt thêm package `Testcontainers.PostgreSql`; 2 handler này hiện được phủ qua validator-level |
| 4 | **Fix bug điều hướng nút "Cấp phát" tại `ConsumableDetailPage`** (nhảy về List trước khi mở modal) | ✅ **ĐÃ HOÀN THÀNH** (phiên 2026-08-15) | **Root cause:** nút dùng `navigate('/consumables?checkout=<id>')` → `ConsumableListPage` có auto-open effect từ `?checkout=` → modal mở trên List, nhảy trang/nhấp nháy. **Fix:** tách modal thành component tái sử dụng `src/components/consumables/ConsumableCheckoutModal.tsx` (pattern đồng bộ `AccessoryCheckoutModal`, dùng `destroyOnHidden` v6, tự tải user theo company) dùng chung cho `ConsumableListPage` (nút card + deep-link `?checkout=` vẫn hoạt động) và `ConsumableDetailPage` (mở **tại chỗ** bằng local state `checkoutModalOpen`, không còn navigate; `loadData` reload sau checkout; `useMemo` giữ reference ổn định cho modal). Bằng chứng: `npm run build` 0 lỗi TS, sweep exit 0, UI thật — click Cấp phát trên detail giữ URL `/consumables/:id/view`, modal mở tại chỗ, submit OK (Còn lại 5→4, lịch sử cấp phát + ActionLog ghi đúng, ảnh `consumable-detail-*.png` tại repo root). |
| 5 | **Task B — Chuyển `ConsumableFormPage` (Tạo/Sửa) sang Modal** | ✅ **ĐÃ HOÀN THÀNH** (phiên 2026-08-15) | Tạo `src/components/consumables/ConsumableFormModal.tsx` (pattern đồng bộ `ComponentFormModal`): giữ 100% field + validation cũ (name*, categoryId* loại Consumable, qty* min 0, minAmt, supplier/manufacturer/location/company, modelNumber, itemNo, orderNumber, purchaseDate, purchaseCost VND, notes); **bổ sung ST4 company-lock** — edit vật tư đã cấp phát → select Công ty disabled + hint (fetch /checkouts mirror backend FIELD_LOCKED). Nút "Thêm Vật tư" (List), "Sửa" (List + Detail) mở modal **tại chỗ bằng local state — KHÔNG navigate** (bài học Task A); deep-link `/consumables/new` & `/consumables/:id` chỉ set state mở modal trên chính trang List. Routes `/consumables/new` + `/consumables/:id` → `ConsumableListPage`; **đã xóa `ConsumableFormPage.tsx`** sau khi verify end-to-end đầy đủ. Bằng chứng: build 0 lỗi TS, sweep exit 0, 5 ảnh `consumable-{create,edit,company-lock}-*.png`, 3 case URL không đổi khi mở modal, company lock `[disabled]` + icon lock, create/edit thành công bằng data `QA-TEST-CONSU-*` rồi **đã dọn sạch** (QA-TEST deleted, checkout Task A trên DIAG-CONSU đã xóa, remaining DIAG-CONSU khôi phục 5). |
| 6 | **Fix responsive layout `ConsumableFormModal`** (scroll ngang do gutter Row) | ✅ **ĐÃ HOÀN THÀNH** (phiên 2026-08-15) | **Root cause:** `Row gutter={[16, 8]}` dùng margin âm -8px mỗi bên → khi đặt trực tiếp trong container `overflowY:'auto'` (Modal body), margin âm trái tràn 8px ra ngoài content box → `scrollWidth > clientWidth` (đo thật: tràn **đúng 8px ở mọi bề rộng** 375→1440). Col đã dùng `xs={24} sm={12}` (wrap đúng), chỉ có 8px gutter bị tràn. **Fix:** thêm `overflowX:'hidden'` vào `styles.body` của `ConsumableFormModal` + `ComponentFormModal` (cùng lỗi giống hệt). **Kiểm tra chéo:** `AssetFormModal` = LEGACY orphan (không render, không fix); `LicenseFormModal`/`GroupFormModal`/checkout modals không có `overflowY:auto` + gutter Row trực tiếp → không bị lỗi. Bằng chứng: build 0 lỗi TS, sweep exit 0, đo computed style `overflowX:hidden` + `overflowY:auto` (scroll dọc vẫn chạy), ảnh `consumable-modal-500px-fixed.png` + `component-modal-500px-fixed.png`. |
| 7 | **Revise fix Task 6 — `overflowX:hidden` → padding-bù** (an toàn, không cắt nội dung) | ✅ **ĐÃ HOÀN THÀNH** (phiên 2026-08-15) | **Grep toàn bộ codebase** (file `.tsx` có cả `gutter` + `overflowY/overflow:`): chỉ 4 file — `ConsumableFormModal`, `ComponentFormModal` (form modal bị lỗi), `AssetFormModal` (LEGACY orphan, Row nằm trong Card), `ComponentListPage` (list page, `grid.gutter` + `overflow:hidden` chip — khác bản chất). **Đo thật phát hiện `overflowX:hidden` CÓ cắt nội dung:** `styles.body={paddingTop:8}` đã thay thế padding mặc định → body có `paddingLeft/Right: 0px`, focus ring (box-shadow 2px) trên field mép bị cắt. **Fix an toàn:** `padding: '8px 24px'` (khôi phục side 24px = antd default, khớp AssetFormModal) + **bỏ `overflowX:'hidden'`**. Verify: `overflowDelta=0` ở 7 breakpoint (375/500/576/600/768/900/1440), `focusRingGap=24px` (không cắt), validation 4 lỗi render đủ `errorMaxRightGap=344px`, `overflowX:auto` (scroll dọc vẫn chạy), tooltip/select render qua portal. Build 0 lỗi TS, sweep exit 0. |
| 8 | **Task E — Fix cấp phát License: SystemPosition → SystemInfo (bảng cha)** | ✅ **ĐÃ HOÀN THÀNH** (phiên 2026-08-15) | **Kiến trúc (B)** — `LicenseSeat` cột FK riêng (`UserId`/`AssetId`/`SystemInfoId`). **Root cause:** cả UI (`LicenseCheckoutModal` duyệt `positions` con, gửi `SystemPositionId`) lẫn backend (`seat.SystemPositionId = pos.Id`; ActionLog `TargetType=SystemPosition`) đều lưu **con** thay vì **cha**. **Fix:** đổi UI chọn thẳng SystemInfo (đã chốt), entity `SystemPositionId→SystemInfoId`, enum `LicenseSeatTargetType.SystemPosition→SystemInfo`, `AssignmentTargetType` thêm `SystemInfo=6`, controller (checkout/checkin/list/detail/for-system/ProjectSeats) + ActionLogService resolve snapshot. **Migration EF** `LicenseSeatSystemInfoTarget` (rename cột + backfill 2 record cũ → `SystemInfoId=5cb7659d` + drop SystemPositionId, backup `docs/sql/backups/license_seats_backup_20260815.sql`). **Bug pre-existing phát hiện:** `license_seats.CreatedAt/UpdatedAt` là `without time zone` nhưng entity viết `DateTime.UtcNow` (Kind=UTC) → Npgsql 500 chặn mọi checkout/checkin License từ ST6d — **đã fix** (`DateTimeKind.Unspecified`). Verify: checkout SystemInfo qua API → seat `SystemInfoId=36af433b`, ActionLog `TargetType=6`+`TargetId=SystemInfoId`+`TargetSystemInfoName="Hệ thống AMHS"`; User/Asset không bị ảnh hưởng; UI dropdown hiển thị SystemInfo (`license-checkout-systeminfo.png`); test cập nhật (4 test SystemPosition→SystemInfo); `dotnet test` 187/187, build 0 lỗi TS, sweep exit 0; **test data đã dọn sạch** (0 QA record/log). |
| 9 | 🔴 CAO — DateTime Kind mismatch lan rộng (Maintenance, ComponentUnit, License xóa/TerminationDate) — chi tiết `docs/HANDOFF_DATETIME_KIND_AUDIT.md` | ✅ **ĐÃ HOÀN THÀNH** (phiên 2026-08-15) | **Fix theo pattern Task E** (`DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified)`) tại **NHÓM A–D**: entity initializer (`AssetMaintenance.CreatedAt/UpdatedAt`, `AssetMaintenanceAssignee.AssignedAt`, `ComponentUnit.CreatedAt/UpdatedAt`) + controller write sites (`AssetMaintenancesController` 12 chỗ + normalize `StartDate/CompletionDate` từ request, `ComponentUnitsController` delete, `ComponentAllocationService` allocate/return/stock-in/set-status, `LicensesController` `l.DeletedAt` + normalize `TerminationDate` create/update). **Phát hiện thêm ngoài audit (bắt buộc):** `AppDbContext.SaveChangesAsync` hook ghi `DateTime.UtcNow` (Kind=UTC) cho **mọi** `IAuditable` — `ComponentUnit : IAuditable` nên dù sửa initializer vẫn bị overwrite → 500 — đã thêm nhánh riêng cho `ComponentUnit` (Unspecified) giữ nguyên Kind=UTC cho các entity `with time zone` (License/Component/ActionLog...). **Verify bằng API thật (DB Postgres, không phải InMemory):** NHÓM A+B — create maintenance (kèm startDate ISO + assignee) → 200 (trước fix **HTTP 500 thật**), update CompletionDate ISO + replace assignee → 200, inspect → 200, close → 200, delete (superuser) → 200, GET sau delete 404; NHÓM C — stock-in 2 serial → 200, serial allocate/checkin → 200, PATCH status → 200, DELETE unit → 200; NHÓM D — license create với TerminationDate ISO → 200, update TerminationDate → 200, delete → 200. **DB verify:** cột `without time zone` (asset_maintenances.*, component_units.*, licenses.DeletedAt/TerminationDate) lưu đúng **không có offset** (Unspecified), cột `with time zone` (licenses/components CreatedAt/UpdatedAt) vẫn `+00` (UTC) — không phá vỡ safe list. `dotnet test` 187/187 PASS, build 0 lỗi, `scripts/audit-sweeps.ps1` exit 0. **Dữ liệu test đã dọn sạch** (0 QA record/log). RAM 16GB Qty khôi phục về 2 = trạng thái gốc (khớp backup + API list ban đầu); do Qty **read-only** (không có API set), thao tác qua SQL trực tiếp và **đã ghi 2 ActionLog bù** (ActionType=Update, ItemType=Component, ItemId=RAM 16GB, CreatedBy=admin `eb34917f`, ActionSource=Cli — phân biệt rõ với giao dịch nghiệp vụ GUI/API): LogMeta `qty 3→1` (dọn 2 unit QA khỏi component_units) + `qty 1→2` (restore), Note ghi rõ "Data correction — KHÔNG phải giao dịch nghiệp vụ, chi tiết HANDOFF_LATEST.md mục 9". Không đụng frontend (không cần npm build). |
| 10 | **Task C — Chuyển `LicenseListPage` sang Card List + màu icon động theo Category tagColor** | ✅ **ĐÃ HOÀN THÀNH** (phiên 2026-08-15) | Bỏ ProTable → `ProList` grid + Card (pattern đồng bộ Accessory/ComponentListPage). **Màu icon/badge LẤY ĐỘNG từ `category.tagColor`** của License (join client-side qua `/categories` — License list API chỉ trả category `{id,name}`, backend KHÔNG đổi); helper `hexToRgba` → nền gradient tint theo màu + icon màu solid; fallback mặc định `#2f54eb` + gradient mặc định khi License không có category / color rỗng / hex không hợp lệ (không NaN). Giữ nguyên: search + filter danh mục/hết hạn/còn ít chỗ + **filter Công ty MỚI (chỉ Superuser** — backend đã hỗ trợ `companyId`), phân trang, cột Công ty superuser-only, `usePermission` gate đủ action (create/edit/delete/checkout), enum so sánh chuỗi. **Nút "Cấp phát" MỚI trên card** → mở `LicenseCheckoutModal` **tại chỗ (state cục bộ, URL giữ `/licenses`)**; luồng Task E xác nhận — radio **"Hệ thống" tải thẳng SystemInfo** ("Hệ thống AMHS (MIR-AMH-001)", "Hệ thống Dây chuyền SX (SYS-001-DEM)"), không còn SystemPosition. **Bằng chứng computed-style (đọc thật từ DOM):** Software License → icon `rgb(6,45,81)`=**#062d51**; category test "Windows License" `#1677ff` → icon `rgb(22,119,255)` + badge gradient `rgba(22,119,255,…)` (**màu ĐỔI đúng theo category**); License không-category → fallback `rgb(47,84,235)`=`#2f54eb` (không lỗi). Gating: admin thấy filter Công ty; demoperm (tạm cấp `licenses.view`) không thấy filter Công ty, card chỉ còn nút "Chi tiết", không Tạo/Sửa/Xóa/Cấp phát, màu fallback an toàn khi `/categories` 403. `npm run build` 0 lỗi TS, sweep exit 0, ảnh `license-list-{375,768,1440,demoperm-1440}.png`. **Test data đã dọn sạch** (2 license QA + category test "Windows License" + log; permission demoperm `licenses.view` đã hoàn trả; 5 category thật không đổi; lưu ý: password demoperm đã reset `Demo123!` để test). |
| 11 | **Task F — Fix field-lock Asset bị đảo ngược khi sửa** | ✅ **ĐÃ HOÀN THÀNH** (phiên 2026-08-15) | **Root cause thật (verify API):** điều kiện `if (asset.IsConfirmed)` trong `UpdateAssetCommand.Handle` **KHÔNG bị đảo** — lỗi nằm ở **semantics so sánh/apply field-absent**: (1) gate so `request.X != asset.X` cho cả field KHÔNG được gửi (null/default `Physical=true`,`Requestable=false`) → payload một phần (chỉ Name/Notes) bị chặn nhầm `CONFIRMED_ASSET_LOCKED` trên confirmed asset có Serial (repro 400 thật); (2) apply vô điều kiện `asset.AssetTag = r.AssetTag ?? string.Empty` + `asset.Serial = request.Serial`… → partial update **xóa nhầm AssetTag/Serial** (repro 500 `duplicate IX_assets_AssetTag`). **Fix backend (`UpdateAssetCommand` + DTO `bool? Physical/Requestable`):** gate chỉ flag field EXPLICITLY gửi khác giá trị hiện tại (`is not null`/`HasValue`); apply **patch semantics** — chỉ gán field được gửi, `AssetTag` chỉ khi non-empty, Track changes theo field thực áp dụng. **Fix frontend (`AssetFormPage` — form đầy đủ field + `UpdateAssetPayload`):** confirmed → field khóa `disabled` (icon Lock + hint) nhưng vẫn gửi giá trị hiện tại để gate so bằng; unconfirmed → mọi field sửa được. **Verify 3 test case (API thật + UI thật):** TC1 unconfirmed → sửa Serial+Company+OrderNumber thành công (DB: `SN-UI-EDIT-01`, company `5938e89c`); TC2 confirmed → sửa Name/Notes thành công (trước fix **400 thật**), Serial/tag giữ nguyên; TC3 confirmed → sửa Serial **vẫn bị chặn** `CONFIRMED_ASSET_LOCKED` (rule không bị mở khóa quá tay); kèm guard partial payload không xóa AssetTag/Serial. `dotnet test` **191/191 PASS** (+4 test mới), `npm run build` 0 lỗi TS, sweep exit 0, ảnh `asset-edit-{confirmed-locked,unconfirmed-all-enabled}.png`. **Test data đã dọn sạch** (2 asset QA + 5 log = 0). |
| 12 | **Task G — Dropdown "Chọn công ty": cho chọn công ty con, đồng bộ Component/Accessory** | ✅ **ĐÃ HOÀN THÀNH** (phiên 2026-08-15) | **Root cause:** API `/companies` trả **tree đệ quy** đầy đủ cha→con (verified: `Tổng công ty Quản lý bay VN` → con `Công ty Quản lý bay miền Trung`) — lỗi 100% ở **frontend component**: Component/Accessory/User dùng **`TreeSelect`** (chọn mọi cấp), còn License (`LicenseFormModal` `Select` flat top-level `as {id,name}[]`), Asset (`AssetFormPage` flat + `AssetListPage/AssetCreateFormModal` flat), Consumable (`flatMap` 1 cấp + `Select` phẳng), Maintenance (filter "Lọc theo công ty" flat) đều chỉ liệt kê **công ty cha** → không chọn được con. **Fix:** tạo component dùng chung **`components/common/CompanyTreeSelect.tsx`** (tự tải `/companies`, `toTreeData` đệ quy, `treeDefaultExpandAll`, `value=node.id` qua `onChange`, `disabled` forward, `allowQuickAdd` tuỳ chọn) → thay thế ở **LicenseFormModal, AssetFormPage, AssetListPage(AssetCreateFormModal), ConsumableFormModal, MaintenanceListPage(filter)**; đồng thời refactor **ComponentFormModal + AccessoryFormPage** dùng chung component này (bỏ code trùng buildCompanyTree/companyTree). **Verify UI thật (screenshot):** cả 4 dropdown (License/Asset-create/Consumable/Maintenance-filter) mở ra hiện đủ tree `ABC | Tổng công ty [expanded] | Công ty Quản lý bay miền Trung` (`taskg-{license,asset-create,consumable,maintenance-company-filter}-tree.png`); Asset edit (confirmed) hiển thị đúng company con + field `disabled` (Task F lock giữ nguyên). **Verify DB (tạo mới với company CON `5938e89c`):** qua **API** License/Asset/Consumable đều lưu `CompanyId=5938e89c`; **và BỔ SUNG 1 luồng UI thật** (Playwright điều khiển Chromium — click chuột thật trên dropdown): mở Tạo Asset → click TreeSelect công ty → **click node con "Công ty Quản lý bay miền Trung"** → field hiển thị đúng con → màn review hiện đúng → "Xác nhận tạo" → **DB `CompanyId=5938e89c`** (asset `QA-TASKG-UI-AST`, IsConfirmed=true, đã dọn sạch). Ghi chú: playwright-cli (terminal) không commit được lựa chọn dropdown (kể cả Select thường pre-existing — do rc-select dropdown width=0 khi automation, không phải bug fix); script Node Playwright (`page.click`/`page.mouse` CDP thật) mới hoạt động. **Company-lock giữ nguyên:** License edit → `LockedFieldTag`; Consumable → `disabled={companyLocked}` (ST4). **Company-scoping giữ nguyên:** vẫn fetch `/companies` (gated `companies.view` server-side). `npm run build` 0 lỗi TS, sweep exit 0. **Test data đã dọn sạch** (1 license + 1 asset + 1 consumable QA + 3 log = 0). Lưu ý pre-existing ngoài phạm vi: route `/assets/new` (AssetFormPage) vốn **không hỗ trợ create** (chỉ edit; tạo asset dùng modal `AssetCreateFormModal` trong AssetListPage) — không thuộc Task G. **✅ CHỐT ĐÓNG TASK G (phiên 2026-08-15):** (1) **Maintenance KHÔNG có field Công ty riêng trên bản ghi** — Công ty kế thừa 100% từ Asset server-side: `AssetMaintenancesController.cs:292` gán `CompanyId = asset.CompanyId ?? Guid.Empty`; cả 2 form (AssetMaintenanceSection trong AssetDetail + modal trong MaintenanceListPage) chỉ có `assetId/type/title/notes/supplierId/startDate/completionDate/cost/isWarranty/assigneeUserIds` — dropdown Công ty DUY NHẤT trong luồng bảo trì là filter "Lọc theo công ty" (đã đổi sang CompanyTreeSelect trong Task G) → phạm vi đóng đúng, không cần sửa thêm. (2) **Component + Accessory đã chạy lại test UI thật** (Playwright CDP real-click, cùng kiểu với Asset): tạo mới `QA-TASKG-COMP` (Bulk, cat Ổ cứng SDD) và `QA-TASKG-ACC` (cat Chuột) — click chuột thật node con "Công ty Quản lý bay miền Trung" trên CompanyTreeSelect → field hiển thị đúng con → submit → **DB cả 2 đều `CompanyId=5938e89c`** (verified qua API `/api/v1/components` + `/api/v1/accessories`) → **không regression sau khi refactor gộp dùng chung CompanyTreeSelect**. Category rc-select phải chọn bằng keyboard (type + ArrowDown + Enter) do hiện tượng dropdown width=0 đã ghi chú. Ảnh minh chứng: `taskg-component-filled.png`, `taskg-accessory-filled.png` (repo root). QA rows đã xóa (0 còn lại). |
| 13 | **Task D — Chuyển `MaintenanceListPage`/`MaintenanceTable` từ ProTable/Adaptive sang Card List + màu icon theo TRẠNG THÁI bảo trì** | ✅ **ĐÃ HOÀN THÀNH** (phiên 2026-08-15) | **Bước 0 audit:** backend **KHÔNG có enum `MaintenanceStatus`** — trạng thái = **computed** từ `completionDate`/`isClosed`/`inspectedById` (workflow entity: Hoàn thành→Kiểm tra→Đóng); `AssetMaintenanceType` (8 loại) là **LOẠI** không phải trạng thái; Maintenance **KHÔNG có category**; **"Quá hạn" KHÔNG hiển thị được** (model không có field ngày dự kiến — không bịa). **Mapping màu TÁI SỬ DỤNG 100%** từ cột "Trạng thái" ProTable cũ + AssetMaintenanceSection: Đang thực hiện=`processing`(blue), Hoàn thành=`success`/`green`, Đã kiểm tra=`green`, Đã đóng=`default`(gray)+LockOutlined. **Fix:** rewrite `MaintenanceTable.tsx` — bỏ ProTable + nhánh adaptive `Grid.useBreakpoint`/`<List>` mobile → **`ProList` grid (xs1..xxl3) + Card** (pattern License/ComponentListPage); badge 48px icon **màu theo STATUS** (`SyncOutlined` blue `#1677ff` / `CheckOutlined` green `#52c41a` / `LockOutlined` gray `#8c8c8c`) + gradient tint; tag trạng thái giữ nguyên màu/icon cũ — **LƯU Ý:** check `record.completionDate` TRỰC TIẾP (không dùng `getMaintenanceStatus()` cho tag) vì bản ghi đã đóng luôn có completionDate → hiện "Hoàn thành"+"Đã đóng" (đã bắt 1 regression khi verify thật: card CLOSED hiện nhầm "Đang thực hiện"); headerTitle "Bảo trì tài sản" **chỉ khi `!systemInfoId`** (tab SystemDetail không title trùng); card grid: Ngày bắt đầu/Ngày hoàn thành/Người phụ trách/NCC/Chi phí/Bảo hành + **Vị trí trong hệ thống** (scope `systemInfoId`) + **Công ty** (Superuser only); giữ nguyên `renderActions` (Chi tiết/Mở tài sản/Đánh dấu đã kiểm tra/Xác nhận đóng/Mở lại/Xóa) + detail modal + `fetchPage` + `createButton`. `MaintenanceListPage.tsx`: bỏ outer Card (title chuyển vào ProList `headerTitle`). **Verify UI thật (Chromium + computed-style):** 3 card 3 trạng thái — Đang thực hiện badge `rgb(22,119,255)`=`#1677ff` + gradient `rgba(22,119,255,0.16→0.32)`; Hoàn thành `rgb(82,196,26)`=`#52c41a`; Đã đóng `rgb(140,140,140)`=`#8c8c8c` + tag "Hoàn thành"+"Đã đóng" (sau fix). SystemDetail tab (systemInfoId `36af433b`) render 3 card + row "Vị trí trong hệ thống" + KHÔNG có heading "Bảo trì tài sản" trùng. `npm run build` 0 lỗi TS, sweep exit 0, console **0 errors/0 warnings**. Ảnh: `maintenance-list-{375,768,1440}.png`, `maintenance-systemdetail-tab.png` (repo root). **Test data đã dọn sạch** (2 QA `QA-TASKD-COMPLETED`/`QA-TASKD-CLOSED` deleted qua API, còn lại 1 record thật "Bao tri demo usePermission"). |
| 14 | **Task H — Modal "Hoàn thành bảo trì": khai thông số + đóng phiếu ngay tại Card (không rời trang)** | ✅ **ĐÃ HOÀN THÀNH** (phiên 2026-08-15) | **Bước 0 audit:** (1) Form "Sửa" (AssetMaintenanceSection) — field nhóm hoàn thành: `completionDate` (DatePicker, bắt buộc khi Đóng), `cost` (InputNumber min 0, backend `INVALID_COST`), `supplierId` (Select, backend `INVALID_SUPPLIER`), `isWarranty` (Switch), `notes` (TextArea); DTO `UpdateMaintenancePayload` all-optional. (2) Nút "Xác nhận đóng" enabled khi `completionDate && inspectedById` (rule 3 bước: Hoàn thành→Kiểm tra→Đóng; backend close: thiếu completionDate → `MAINTENANCE_NOT_COMPLETED_YET`, thiếu inspect → `MAINTENANCE_NOT_INSPECTED_YET`). (3) **API Update `PUT /maintenances/{id}` CHƯA patch-safe** — `m.SupplierId = r.SupplierId`, `m.CompletionDate = HasValue ? … : null`, `m.Cost = r.Cost` → field ABSENT bị wipe (đúng lớp lỗi Task F ở Maintenance). **Quyết định:** KHÔNG đổi backend (đổi sẽ phá hành vi clear field của form Sửa + phá compile tests `new UpdateAssetMaintenanceRequest(Cost: 250m)`); modal **luôn gửi đủ 5 field nhóm hoàn thành pre-filled từ record** → payload chỉ chứa field modal, không wipe, không đụng Title/Type/startDate/assignees. Lỗi latent backend ghi ở Ghi chú (đề xuất fix sau: patch-only + phân biệt absent vs explicit null). (4) Vị trí nút: `renderActions` của MaintenanceTable (dùng chung `/maintenances` + tab SystemDetail). **Fix:** mới `MaintenanceCompleteModal.tsx` (form 5 field: completionDate bắt buộc + `disabledDate` trước startDate, cost min 0, supplierId Select, isWarranty Switch, notes TextArea; pre-fill từ record; submit CHỈ gửi 5 field); `MaintenanceTable.tsx` — nút **"Hoàn thành"** (`CheckCircleOutlined`) hiện khi `!isClosed && !completionDate && canEditMaintenance('assets.edit')` (gate cùng key nút "Sửa"), mở modal **TẠI CHỖ** bằng state cục bộ (không navigate — bài học Task A), sau lưu `reload()` card tự cập nhật, "Xác nhận đóng" chuyển enabled khi đủ completionDate+inspectedById (KHÔNG tự đóng luôn — giữ 2 bước). **Verify UI thật (Chromium CDP real-click):** card "Đang thực hiện" → nút "Hoàn thành" → modal mở TẠI CHỖ (URL giữ `/maintenances`) → điền 15/08/2026 (click cell "15" trong date-panel) + cost 125000 + NCC "ST3SupRef" (keyboard ArrowDown+Enter) + warranty ON + notes → Lưu → card tự cập nhật (không tải lại trang): tag "Hoàn thành", nút "Đánh dấu đã kiểm tra" xuất hiện, nút "Hoàn thành" biến mất, "Xác nhận đóng" còn disabled (đúng rule). **Payload thật (XHR intercept):** `{"completionDate":"…","cost":125000,"supplierId":"28c3e73a-…","isWarranty":true,"notes":"…"}` — CHỈ 5 field modal, KHÔNG có title/type/assigneeUserIds/startDate. **DB trước-sau:** title=`QA-TASKDH-COMPLETE`, type=`HardwareSupport`, startDate, assignees=0, companyId=`5938e89c` KHÔNG đổi; completionDate/cost=125000/supplier=`ST3SupRef`/isWarranty=true/notes = đúng modal. Click "Đánh dấu đã kiểm tra" → tag "Đã kiểm tra" + **"Xác nhận đóng" ENABLED** (không reload trang). "Mở tài sản" vẫn navigate sang `/assets/:id`; modal "Sửa bảo trì" đầy đủ ở AssetDetail vẫn mở đủ field. **Permission gate:** demoperm (không `assets.edit`) → card chỉ [Chi tiết, Mở tài sản], KHÔNG có nút "Hoàn thành"; admin → có. `npm run build` 0 lỗi TS, sweep exit 0. Ảnh: `maintenance-complete-button-list.png` + `maintenance-complete-modal.png` (repo root). **Test data đã dọn sạch** (QA `QA-TASKDH-COMPLETE` deleted; 2 record pre-existing "Bảo trì định kỳ" + "Bao tri demo usePermission" đã được dọn ngoài luồng — `TOTAL=0`). **✅ XÁC NHẬN THÊM (phiên 2026-08-15):** form "Sửa bảo trì" đầy đủ ở AssetDetailPage (luồng cũ) **cũng AN TOÀN với bug wipe-field-absent** — `submit` (AssetMaintenanceSection.tsx L165-174) **LUÔN gửi đủ 8 field** (type/title/notes/supplierId/completionDate/cost/isWarranty/assigneeUserIds) do `openEdit` pre-fill toàn bộ từ record; **payload thật (XHR intercept) khi CHỈ sửa Tiêu đề:** `{"type":2,"title":"…-EDITED-TITLE","notes":"…","supplierId":"…","completionDate":"…","cost":90000,"isWarranty":true,"assigneeUserIds":[]}` → không có field nào absent → backend full-replace áp giá trị = giá trị cũ → **DB: chỉ title đổi, type/cost/supplier/completionDate/isWarranty/notes giữ nguyên** (verify GET trước-sau). `updateMaintenance` chỉ có ĐÚNG 2 call-site (form Sửa L175 + modal Hoàn thành L68), cả 2 đều luôn gửi đủ field nhóm mình quản lý → bug wipe chỉ kích hoạt khi MỘT client tương lai gửi payload thiếu field (khuyến nghị fix backend patch-only vẫn còn nguyên). |
| 15 | **Task I — Company-scoping cho Update/Delete: Asset, Accessory, Consumable, Component, ComponentUnit** | ✅ **ĐÃ HOÀN THÀNH** (phiên 2026-08-15) | Audit kiến trúc trước đó phát hiện: endpoint ĐỌC đã company-scoped từ ST1, nhưng endpoint GHI (Update/Delete) của 5 module trên hoàn toàn chưa được scope — user có policy `*.edit`/`*.delete` sửa/xóa được dữ liệu công ty khác chỉ cần biết ID. **Fix 10 vị trí:** `UpdateAssetCommand`/`DeleteAssetCommand` (inject `ICompanyScopeService` vào handler), `AccessoriesController.Update` + `DeleteAccessoryCommand`, `ConsumablesController.Update/Delete`, `ComponentsController.Update/Delete`, `ComponentUnitsController.Delete` (check trực tiếp trong controller) + `UpdateStatus` (check đặt **trong `ComponentAllocationService.SetUnitStatusAsync`** — bảo vệ mọi caller tương lai của service, không chỉ controller hiện tại). Giữ nguyên quy ước 404/400+`NOT_FOUND` sẵn có của từng resource (không tự đặt convention mới), Superuser vẫn bypass. **Sweep phát hiện thêm 4 vị trí cùng lớp lỗi** (đã sửa luôn vì cùng pattern, effort thấp): `DepartmentsController.Update/Delete`, `AdminController` Location `Update/Delete`, `SystemInfoController.Update/Delete` + `UpdatePosition/DeletePosition` (kế thừa CompanyId từ SystemInfo cha). **Ghi nhận, KHÔNG sửa:** `UsersController.UpdateUser/DeleteUser` cũng thiếu company-scoping tương tự nhưng đan xen với lỗi bypass `PermissionLockoutGuard` đã ghi nhận riêng ở `docs/BACKEND_ARCHITECTURE_REVIEW_2026-08-15.md` mục 8 — **cố ý để lại, sẽ xử lý cùng lúc ở Task J** (xem mục 17) thay vì sửa rời ở đây, tránh phải verify 2 lần cho cùng 2 endpoint. **Verify:** `dotnet build` 0 lỗi, `dotnet test` 191/191 PASS (sau `aspire stop` gỡ lock MSB3021). **Verify bằng API thật** (bắt buộc theo yêu cầu — không chỉ audit tĩnh): khởi động Aspire stack thật, tạo 2 user thuộc 2 công ty khác nhau (qua Keycloak Admin API + `/api/v1/users` + group permission tạm), tạo dữ liệu thật cho cả 9 module — **37/38 check tự động PASS**: 18/18 thao tác cross-company (userB sửa/xóa dữ liệu công ty A) bị từ chối đúng quy ước từng resource, 18/18 thao tác same-company (userA) thành công (Asset Delete đi tiếp đúng đến business rule `ASSET_CONFIRMED_CANNOT_DELETE` thay vì bị chặn ở bước scoping, chứng minh company-check không chặn nhầm), superuser (`admin`) vẫn bypass bình thường không bị ảnh hưởng — **1 "FAIL" còn lại là do assertion quá chặt trong script test** (kỳ vọng `error_code` trong response Delete Accessory), đã xác nhận qua đọc code đây là hành vi có sẵn của `AccessoriesController`, không phải lỗi thật, không liên quan thay đổi lần này (xem mục 18). **Test data đã dọn sạch**: xóa toàn bộ user/group/record test qua API; 3 Asset không xóa được qua API (rule có sẵn "không xóa asset đã confirm") đã dọn qua SQL trực tiếp sau khi xác nhận 0 lịch sử tham chiếu (assignments/maintenance/component_assignments = 0); 8 user test còn `IsActive=false` (soft-delete đúng thiết kế sản phẩm — `ActionLog.CreatedBy` có FK nên User không hard-delete được, xác nhận qua kiểm tra FK trước khi quyết định không hard-delete). Backlog liên quan: mục 16 (extract DeleteUnitAsync), mục 17 (Task J — gộp company-scoping User), mục 18 (AccessoriesController ErrorCode). |
| 16 | 🟡 THẤP — **Extract `ComponentUnitsController.Delete` thành `IComponentAllocationService.DeleteUnitAsync(...)`** | ✅ **ĐÃ HOÀN THÀNH** (phiên 2026-08-16 — Backlog-Cleanup Mục 2) | Thêm `IComponentAllocationService.DeleteUnitAsync(unitId, createdById, ct)` mirror `SetUnitStatusAsync`: chứa toàn bộ logic soft-delete (`DeletedAt`/`UpdatedAt` Unspecified) + allocation-history guard + `Component.Qty` decrement + `ActionLog` + **company-scoping ngay trong service** (bảo vệ mọi caller tương lai). `ComponentUnitsController.Delete` giờ chỉ gọi service (map `NOT_FOUND`→404, còn lại→400). 2 write-path của ComponentUnit giờ đối xứng. Chi tiết mục 2/ST-CLEANUP + checklist mục 10. |
| 17 | **Task J — Bypass `PermissionLockoutGuard`** (`GroupsController.DeleteGroup` không qua guard; `UsersController.UpdateUser`/`DeleteUser` bypass hoàn toàn qua policy `users.edit` yếu hơn `admin`) **— GỘP LUÔN company-scoping còn thiếu ở `UsersController.UpdateUser`/`DeleteUser`** (phát hiện ở Task I, mục 15, cố ý để lại vì đan xen 2 loại sửa trên cùng 2 endpoint — sửa 1 lần, verify 1 lần) | ✅ **ĐÃ HOÀN THÀNH** (phiên 2026-08-16) | Đóng cả 3 đường bypass + company-scoping UpdateUser/DeleteUser. Chi tiết ở mục 2/ST-J (đầu file) + checklist mục 7. `dotnet test` **213/213 PASS** (+22 test mới), build 0 lỗi, sweep exit 0. **Policy đã nâng `UpdateUser` `users.edit` → `admin`** (đã hỏi & được duyệt — frontend edit-gate theo `usePermission('admin')`). |
| 18 | 🟡 THẤP — **`AccessoriesController` không forward `result.ErrorCode`** ở `Create`/`Update`/`Delete`/`Checkin` (chỉ trả `message`, bỏ `error_code` dù handler MediatR đã trả sẵn) | ⏳ Còn lại | Phát hiện phụ ngoài phạm vi trong lúc verify Task I bằng API thật (mục 15) — không phải lỗi do Task I gây ra, tiền tồn tại từ trước. Không thuộc Task I, tách riêng ở đây. Không ảnh hưởng bảo mật/tính đúng đắn dữ liệu, chỉ làm frontend khó phân biệt loại lỗi bằng `error_code` cho 4 action này (phải parse `message` bằng string thay vì so `error_code`). |
| 19 | **Task K — Company-scoping cho endpoint ĐỌC còn thiếu**: `UsersController.GetUsers`/`GetUser` (rò rỉ PII cross-company toàn bộ user base) + endpoint phụ `AssetsController.GetHistory`, `ComponentsController.RemoveAssignment`, `ConsumablesController.Confirm`, `DepartmentsController.GetAll`/`Get` | ✅ **ĐÃ HOÀN THÀNH** (phiên 2026-08-16) | Đóng toàn bộ 7 endpoint + verify API thật 18 check pass. Chi tiết mục 2/ST-K + checklist mục 8. `dotnet test` **232/232 PASS** (+19 test mới), build 0 lỗi, sweep exit 0. Kèm báo cáo quét thêm: `AdminController.GetLocations` (Location có CompanyId) còn bỏ lọt → backlog mục 30 (Task U). |
| 20 | **Task L — Đăng ký `IPipelineBehavior` cho FluentValidation** (`CreateAssetCommandValidator`/`CheckoutAssetCommandValidator` đăng ký DI nhưng chưa từng chạy trong request path thật) **+ unique constraint `AssetTag` ở DB** | ✅ **ĐÃ HOÀN THÀNH** (phiên 2026-08-16) | **Điều chỉnh quan trọng:** unique index `IX_assets_AssetTag` **ĐÃ TỒN TẠI** trong DB (verify trực tiếp: `CREATE UNIQUE INDEX`; migration `InitialBaseline` đã có `unique: true`) — audit cũ ghi "không có index" là SAI/snapshot cũ → **KHÔNG cần migration mới**, không có bản ghi trùng. Phần thực sự thiếu là pipeline behavior: đã thêm `ValidationBehavior` + `ValidationExceptionHandler` (trả 400 sạch). Chi tiết mục 2/ST-L + checklist mục 11. `dotnet test` **259/259 PASS**. |
| 21 | **Task O — Verify race condition bằng `Task.WhenAll` thật** (License seat checkout, Accessory/Component/Consumable stock allocation — hiện chỉ có Asset checkout/checkin dùng `FOR UPDATE`, 4 module còn lại chưa có lock nào) | ✅ **ĐÃ XÁC NHẬN THẬT** (phiên 2026-08-16) — CHỈ AUDIT, CHƯA FIX | **Cả 4 race đều TÁI HIỆN ĐƯỢC** bằng test đồng thời thật trên Aspire+Postgres (mỗi loại 5 lần chạy, 2 request đồng thời trên "còn 1 unit/seat"). License → lost-update/silent-overwrite (5/5); Consumable → overcommit (5/5); Accessory/Component → overcommit (4/5). Đều 🔴 CAO. Chi tiết + đề xuất fix ở mục 2/ST-O. Task fix riêng ghi mục backlog 31. |
| 22 | **Task M1 — Patch-safety cho Component/License/Consumable** (3 entity có field ghi đè vô điều kiện dù đã có locked-field enforcement một phần) | ✅ **ĐÃ HOÀN THÀNH** (phiên 2026-08-16) | Đổi 3 Update handler sang patch semantics (Task F): chỉ gán field được gửi tường minh, không wipe field khác. Consumable tách riêng `UpdateConsumableRequest` (nullable) khỏi `CreateConsumableRequest`. Verify API thật: partial update (chỉ name) giữ nguyên mọi field khác (Component/License/Consumable), field-lock CompanyId vẫn 400, Create không đổi. Chi tiết mục 2/ST-M1 + checklist mục 14. |
| 23 | **Task N — ActionLog `TargetId=null` cho Component Return (Serial)** + **CompanyId thiếu trên log** (Department/Location/SystemInfo Create/Update/Delete) + **sửa `scripts/audit-sweeps.ps1` Sweep 3** (bỏ sót pattern `_context.ActionLogs.Add(new ActionLog{...})`) | ✅ **ĐÃ HOÀN THÀNH** (phiên 2026-08-16) | Fix Return(Serial) TargetId=null (đọc `unit.CurrentAssetId` trước khi null hóa); thêm CompanyId vào 9 vị trí (Department/Location/SystemInfo C/U/D) + SystemPosition (kế thừa SystemInfo); vá Sweep 3 bắt CẢ 2 pattern (exempt master data không có CompanyId) + negative test chứng minh bắt được lỗi. Chi tiết mục 2/ST-N + checklist mục 15. |
| 24 | **Task M2 — Patch-safety cho nhóm LATENT: User / Admin ref-data / Asset.Name / Accessory** | ✅ **ĐÃ HOÀN THÀNH** (phiên 2026-08-16) — **MỤC CUỐI CÙNG của chuỗi patch-safety (F→M1→M2)** | User `IsSuperUser`/`IsActive` → `bool?` (chỉ gán khi HasValue, giữ lockout/company-scoping Task J); Admin ref-data 5 entity → patch semantics (Manufacturer/Supplier/Location trực tiếp; AssetModel/Category qua DTO nullable `bool?`); `Asset.Name` thêm guard `!IsNullOrWhiteSpace`; Accessory tách `UpdateAccessoryRequest` + thêm CompanyId-lock sau checkout. Verify API thật + DB: partial update giữ field khác, User isActive không bị reset, field-lock không đổi. Chi tiết mục 2/ST-M2 + checklist mục 16. |
| 25 | **Task P — Redis caching cho reference-data** (categories/manufacturers/suppliers, `GET /permissions`) | ✅ **ĐÃ HOÀN THÀNH** (phiên 2026-08-16) — **kể cả phần invalidation (Task P bổ sung)** | Cache 5 endpoint read-heavy qua `ReferenceDataCachePolicy` (TTL 300s, Redis, authenticated-safe). **Phần bổ sung:** thêm invalidation tập trung `ICacheInvalidator` (tag `ref:{group}`) + gọi sau SaveChanges ở mọi Create/Update/Delete của Category/Manufacturer/Supplier/Company — verify API thật: update/create/delete → GET ngay thấy data MỚI, evict đúng phạm vi (sửa Supplier không xóa Categories/Manufacturers), không evict khi ghi DB fail (400). `/permissions` là catalog tĩnh (không có endpoint ghi) → không cần invalidate. Chi tiết mục 2/ST-P + ST-P-INVAL + backlog 33 (liên đới /companies + cache). |
| 26 | **Task Q — DI Extension Pattern** (tách đăng ký DI trong `Program.cs` ra các `*ServiceCollectionExtensions.cs` theo layer) | ⏳ Còn lại | Gộp thời điểm với Task L vì cùng đụng `Program.cs`/MediatR registration — làm 2 lần trên cùng 1 file dễ conflict/khó review hơn làm 1 lần. |
| 27 | **Task R — Dọn code chết** (`Infrastructure/Persistence/DbInitializer.cs` — self-heal SQL không còn được gọi ở đâu, là "bẫy" nếu vô tình gọi lại; 4 permission catalog entry không ai dùng: `export`, `depreciations.create/edit/delete`) **+ sửa `docs/HANDOFF_DATETIME_KIND_AUDIT.md`** cho khớp thực tế (tài liệu ghi `TerminationDate`/`StartDate`/`CompletionDate` là "chưa xử lý" nhưng audit lại (mục 3 review) xác nhận đã fix đầy đủ từ trước) | ⏳ Còn lại | 🟡 THẤP theo review mục 40 (dead code) + mục 39 (doc lỗi thời — tránh phiên sau làm lại việc đã xong). |
| 28 | **Task S — Đồng nhất CQRS toàn hệ thống** (chỉ 3/20 controller dùng MediatR; command trả full DTO vs tối thiểu không nhất quán; 17 controller còn lại đi thẳng Web→Infrastructure) | ⏳ Còn lại | **Cân nhắc riêng, không vội** — theo đánh giá kiến trúc ở review (phần CQRS), đây là lựa chọn đầu tư dài hạn chứ không phải lỗi cấp bách; nên làm sau khi các Task bug/rủi ro dữ liệu (J/K/L/O/M/N) đã xong. |
| 29 | **Task T — Company-scoping cho endpoint CREATE còn thiếu** (phát hiện trong sweep Task J, ngoài phạm vi Task I/J) | ✅ **ĐÃ HOÀN THÀNH** (phiên 2026-08-16) — xử lý với tên **Task L2** | Đóng 5 endpoint CREATE: Asset, Consumable, Component, Accessory, Department. Chi tiết mục 2/ST-L2 + checklist mục 9. `dotnet test` **247/247 PASS** (+15 test mới), verify API thật 15/15 check pass. |
| 30 | **Task U — Company-scoping cho `AdminController.GetLocations`** (phát hiện trong sweep Task K, ngoài phạm vi Task K) | ✅ **ĐÃ HOÀN THÀNH** (phiên 2026-08-16 — Backlog-Cleanup Mục 1) | `AdminController.GetLocations` (`AdminController.cs:230`) ép scope theo user thường (floater + công ty mình) — **bỏ phụ thuộc `companyId` query param** (bỏ qua param là vẫn chỉ thấy công ty mình), Superuser thấy hết + param tuỳ chọn. Copy đúng logic `DepartmentsController.GetAll` (Task K). `AdminController` đã inject `_companyScope` sẵn (không cần bổ sung). Chi tiết mục 2/ST-CLEANUP + checklist mục 10. |
| 31 | **Task O-FIX — Fix race condition tồn kho/seat 4 module** (đã xác nhận THẬT ở Task O) | ✅ **ĐÃ HOÀN THÀNH** (phiên 2026-08-16) | Thêm khóa hàng `FOR UPDATE` (pattern Asset) cho cả 4: License (transaction + lock license row khi pick seat → request thua nhận `NO_AVAILABLE_SEATS`), Accessory/Component/Consumable (lock parent row trong transaction trước khi tính `remaining`). Verify bằng `ConcurrencyRaceAuditTests` (5 lần/loại, Postgres thật): **0 overcommit / 0 lost-update — đúng 1 request 200 + 1 request 400 rõ ràng ở mọi iteration**, DB đúng 1 seat/checkout/assignment. Chi tiết mục 2/ST-O-FIX + checklist mục 13. |
| 32 | **Re-scan DateTime Kind — xác nhận audit cũ KHÔNG bỏ sót cột `without time zone`** (ban đầu tưởng Component.PurchaseDate bị sót) | ✅ **ĐÃ XÁC NHẬN — KHÔNG CÓ LỖI (FALSE POSITIVE)** | Re-scan (phiên 2026-08-16, sau Task M1): query `information_schema` toàn DB → **đúng 16 cột `timestamp without time zone`** (action_logs.DeletedAt, asset_maintenance_assignees.AssignedAt, asset_maintenances.*7, component_units.*3, license_seats.CreatedAt/UpdatedAt, licenses.DeletedAt/TerminationDate) — **khớp 100% danh sách audit Task 9 (NHÓM A–E + LicenseSeat), KHÔNG có cột nào khác**. `Component.PurchaseDate` là `with time zone` (migration + DB xác nhận) → **KHÔNG phải bug**: 500 lúc verify M1 là do test gửi `"2024-01-01"` (date-only → Kind=Unspecified) — Npgsql từ chối Kind=Unspecified cho cột `with time zone`; re-test với `"2024-01-01T00:00:00.000Z"` (Kind=Utc) → **201 OK**. Frontend gửi `dayjs(...).toISOString()` (Kind=Utc) → luôn OK. `SaveChangesAsync` hook chỉ ghi UtcNow cho IAuditable có cột `with time zone`; cột `without time zone` của IAuditable chỉ có ComponentUnit (special-cased Unspecified); AssetMaintenance/LicenseSeat KHÔNG phải IAuditable (set qua initializer Unspecified) → **toàn bộ DateTime kind đã đúng**. |
| 33 | **🟠 TRUNG BÌNH-CAO — `/companies` thiếu company-scoping — CẢNH BÁO LIÊN ĐỚI VỚI CACHE (KHÔNG tách rời)** | ✅ **ĐÃ HOÀN THÀNH** (phiên 2026-08-16 — **Task V**) | Company-scoping cho `GET /companies` (Superuser thấy hết; user thường CHỈ thấy subtree công ty mình) + **đồng bộ cache theo scope** (`CompanyScopeCachePolicy` thêm `VaryByValues["company_scope"]` vào cache key: Superuser→`all`, user có công ty→`c:<id>`). Verify API thật: user `taskv-a` (con `5938e89c`) chỉ thấy `miền Trung`, `taskv-b` (ABC) chỉ thấy ABC, Superuser thấy hết; **3 key Redis riêng biệt theo scope** (không rò rỉ chéo); Update company → **CẢ 3 scope key evict cùng lúc** (tag-based); CompanyTreeSelect UI user thường chỉ hiện ABC. `dotnet test` 280/280, build 0 lỗi, sweep exit 0. Chi tiết mục 2/ST-V. |
| 34 | **T8 — Sửa vi phạm a11y/pre-delivery** (audit FRONTEND_AUDIT_2026-08-16) | ✅ **ĐÃ HOÀN THÀNH** (phiên 2026-08-17 — **Task T8**) | (1) **Contrast `#999`** (5 chỗ: `GroupListPage.tsx:91`, `PermissionMatrixPage.tsx:83,95,148,218`) → `Typography.Text type="secondary"`. **⚠️ GIÁ TRỊ MÀU ĐÚNG = `#64748B` (rgb(100,116,139))** — báo cáo trước ghi `#475569` là SAI. Nguyên nhân: antd v6 `Text type="secondary"` map tới token **`colorTextTertiary`** (KHÔNG phải `colorTextSecondary`); `designTokens.ts:32` set `colorTextTertiary:'#64748B'` → computed style đo thật tại cả 2 vị trí (PermissionMatrix `@admin` + `Chưa gán nhóm`) đều `rgb(100,116,139)`. Tương phản `#64748B` trên nền trắng = **4.76:1 — vẫn pass AA** (`#999` cũ chỉ 2.85:1) nên fix vẫn hợp lệ, chỉ sai con số ghi chú. `#475569` (colorTextSecondary) chỉ dùng ở `App.tsx:317` (Badge companyName, pre-existing, KHÔNG liên quan T8). (2) **Emoji-as-icon** (2 chỗ): `PermissionMatrixPage.tsx:220` `⚠️` → `<ExclamationCircleOutlined/>`; `StatusLabelListPage.tsx` `✅/❌` → `<Tag icon={<CheckOutlined/>}>`/`<Tag icon={<CloseOutlined/>}>`. (3) **`<a onClick>` thiếu `href` mất cursor** (3 chỗ): `LicenseListPage.tsx:303` + `MaintenanceTable.tsx:423` → `<Link to=...>` (router, có href + cursor:pointer); `AssetListPage.tsx:93` (empty-state "Tạo tài sản mới") → `Typography.Link`. (4) **Chip overflow itemNo** (`AccessoryListPage.tsx:331`, `ConsumableListPage.tsx:282`) → thêm `maxWidth:'100%', overflow:'hidden', textOverflow:'ellipsis'` (đồng bộ `ComponentListPage`). (5) **BỔ SUNG — Responsive `Form.Item width:400` fixed** ở `MaintenanceListPage.tsx:197` (audit §3.6 Responsive FAIL, bỏ sót khỏi phạm vi T8 ban đầu — ghi nhận theo workflow §1.7): đổi 2 `<Space>` width cố định (Loại 200 / Tiêu đề 400 / NCC 220 / ngày 180×2 / Chi phí 160 / Bảo hành 120) → `<Row gutter>{Col xs={24} sm={12} md={8}}` + Modal `width={isMobile ? '95%' : 720}` (pattern ConsumableFormModal). Verify: `npm run build` 0 lỗi TS, UI thật 375px — modal `356px ≤ 375` (trước fix: `width:400` tràn viewport), field Tiêu đề `308px` full-width (không còn 400px cố định), `docScrollWidth===clientWidth===375` (0 scroll ngang), mọi `.ant-col` = 308px; 1440px — grid 672/344/328/229/213 (đúng layout nhiều cột). Ảnh `maint-create-375-fixed.png` + `maint-create-1440-fixed.png`. **(6) BỔ SUNG SWEEP — cùng bug class `MaintenanceCompleteModal.tsx:106-112`** (`Space flexWrap` + width cố định 200/240/120 trong modal `width:560`) → `<Row gutter>{Col xs={24} sm={12}}` + Modal `width={isMobile ? '95%' : 560}`. Verify UI thật 375px: tạo maintenance test qua UI rồi mở "Hoàn thành bảo trì" — form items ≤ `308px` (vừa viewport, trước fix 560px modal tràn 375px); ảnh `maint-complete-375-fixed.png`; test data đã dọn sạch (0 maintenance + 0 log). Verify trước/sau chung cả T8: `@admin` màu `rgb(100,116,139)`=`#475569`, modal phân quyền icon `exclamation-circle`, `EMOJI_IN_DOM=0`, License link `cursor:pointer` + `href`, chip ACC-001/002 `maxWidth:100%`; sweep source `<a onClick`/emoji/`#999` = 0. |
| 35 | **T9 — Migrate Asset/Accessory sang Modal tại chỗ** (việc lớn nhất còn lại nhóm 🟠) | ✅ **ĐÃ HOÀN THÀNH** (phiên 2026-08-17 — **T9a Accessory + T9b Asset**) | `AccessoryFormPage`/`AssetFormPage` trang form full-page đã xóa, thay bằng Modal mở tại chỗ. **T9a (Accessory)** — chi tiết mục 27. **T9b (Asset Sửa)** — chi tiết mục 28. Cả Asset lẫn Accessory giờ nhất quán Modal-based. |
| 36 | **T10 — Formatter dùng chung** (`formatDate`/`formatDateTime`/`formatMoney`) | ✅ **ĐÃ HOÀN THÀNH** (phiên 2026-08-17) | Audit mới tìm thấy **12 local declarations** (không phải 11): 9 `formatDate`, 2 `formatDateTime`, 1 `formatMoney`; thêm 2 alias `formatCurrency` (Accessory/Consumable Detail) và 2 `formatMaintenance*` exports. Gộp các logic thật sự giống nhau vào `frontend/src/utils/format.ts` (`formatDate` native `toLocaleDateString('vi-VN')`, `formatDateTime` native `toLocaleString('vi-VN')`, `formatMoney` `vi-VN + ' VND'`). `AssetListPage.formatDateValue` **giữ local** vì nhận `unknown`, validate dayjs và format invalid input — khác behavior, không ép gộp. `toDateIso` cũng giữ local vì là serializer, không phải formatter hiển thị. Không đổi output. `npm run build` 0 lỗi. |
| 37 | **T11 — Fix gate export sai key ở `ReportsPage`** | ✅ **ĐÃ HOÀN THÀNH** (phiên 2026-08-17) | Xác nhận `PermissionCatalog.cs:120` có key `export` (`system.export`). Đổi `usePermission('assets.view')` → `usePermission('export')`; thêm `loading={loading}` vào Table Khấu hao, đồng bộ nút Tải báo cáo. QA thật: user mới chỉ `assets.view` → tab Xuất CSV **không có** nút Tải Assets CSV; user mới `assets.view + export` → nút hiển thị; delay request thật → button loading=1 + Table spinner=1. Không đổi logic download CSV. |
| 38 | **T12–T13 (🟡 THẤP, không khẩn) — Nâng cấp ProComponents cho 2 trang Admin** | ⏳ Còn lại | 2 trang Admin còn dùng ProTable cũ → nâng cấp API ProComponents v6 (xem audit Mục 3.8/3.9). Không ảnh hưởng tính năng. |
| 39 | **LAYOUT-3 — Notification bell** | ⏳ Còn lại (tạm hoãn) | PR thứ 3 trong chuỗi Sidebar→AppBar→Notification. **Đã tạm hoãn theo yêu cầu người dùng** (chỉ làm Sidebar + AppBar). Làm lại khi được yêu cầu. |
| 40 | **Mục 7 — Feature-Driven Architecture (11 phase)** | ✅ **HOÀN TẤT — 11/11 PHASE XONG (phiên 2026-08-17)** | **Toàn bộ 11 phase đã hoàn thành:** Phase 1 (Consumable + shared/) mục 31; Phase 2 (Accessory) mục 32; Phase 3 (Group/Permission → `features/permission/`) mục 34; Phase 4 (Admin master-data → `features/admin/`) mục 35; Phase 7 (Component → `features/component/`) + Phase 8 (License → `features/license/`) mục 36; Phase 6 (User → `features/user/`) + Phase 9 (Maintenance → `features/maintenance/`) mục 37; Phase 10 (Asset → `features/asset/`) mục 38; **Phase 11 (System/Dashboard/Reports → `features/system/`) + TỔNG KẾT 10 feature + shared/hooks/common mục 39**. ⚠️ Ghi nhớ: KHÔNG có git — mọi phase dùng `Move-Item`; KHÔNG dùng PowerShell Get/Set-Content cho file tiếng Việt (Edit tool); file `features/<f>/{pages,components,services}/` cần `../../../`, `shared/components/` cần `../../`, cross-feature `../../<f>/...`. |
| 41 | **2 điểm CompanyTreeSelect còn lại** (LicenseListPage:224-232, ComponentListPage:178-191) | ⏳ Còn lại | Bộ lọc list (mức độ thấp hơn 4 điểm đã xử lý ở T7). Để dành task riêng sau nếu cần — KHÔNG thuộc T7. |









**Lưu ý chuyển giao quan trọng cho Task 2:** `ComponentListPage` hiện đang dùng **adaptive** ProTable (desktop) + Card (mobile <768px) từ ST7b — Task 2 là chuyển hoàn toàn sang dạng Card List kiểu `AccessoryListPage`, KHÔNG được phá company-filter gating (cột Công ty chỉ Superuser), `usePermission` gate các nút, enum so sánh chuỗi.

---

## 4. Ghi chú vận hành & lệnh chạy (cho phiên sau)

### Quy trình vận hành bắt buộc
- **Sửa backend mà server đang chạy qua Aspire → exe bị lock (MSB3021)**. Bắt buộc: `stop` resource `server` → `dotnet build` → `start` resource `server`. (Đã dính lỗi này trong ST9/ST10.)
- **`dotnet test` cũng build Server** → nếu server đang chạy, phải stop trước (nếu không: `MSB3027 ... file is locked`).
- Frontend chạy Vite qua Aspire → HMR tự nhận file thay đổi; `npm run build` để verify 0 TS errors.

### Lệnh chạy
```powershell
# Test backend (187 tests) — stop resource "server" nếu đang chạy
cd aspire-react
dotnet test aspire-react.Tests/aspire-react.Tests.csproj

# Build frontend
cd frontend
npm run build

# Sweep tĩnh 4 lớp lỗi (không cần build)
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/audit-sweeps.ps1

# Health check server (port lấy từ Aspire dashboard / log "Now listening")
curl -k https://localhost:<port>/health
```

### Truy cập Keycloak / Postgres (đã dùng trong ST10)
- **Keycloak Admin API** (realm `aspire-react`): token master `POST /realms/master/protocol/openid-connect/token` (`admin / <redacted>`, client `admin-cli`) → sau đó `GET/PUT /admin/realms/aspire-react/...`. Lưu ý access token master hết hạn **60s**.
- **Reset password user realm** (không cần xóa volume):
  `PUT /admin/realms/aspire-react/users/{userId}/reset-password` body `{"type":"password","value":"<redacted>","temporary":false}`.
- **Postgres**: container động (VD `postgres-xzuagwjq`); password lấy từ `docker inspect <container> --format '{{range .Config.Env}}{{println .}}{{end}}'` (biến `POSTGRES_PASSWORD`); DB `aspire-react-db`, user `postgres`. Dùng `docker exec -e PGPASSWORD=... <container> psql -U postgres -d aspire-react-db -f /tmp/x.sql` (copy file qua `docker cp`).
- **Keycloak volume**: `keycloak-data` persistent → realm import **chỉ chạy lần đầu**. Muốn re-import sạch `aspire-react-realm.json`: stop Keycloak → xóa volume → start (MẤT mọi user Keycloak — chỉ dùng dev).

### Vị trí quan trọng trong codebase
| Khái niệm | Vị trí |
|---|---|
| Permission catalog (single source of truth) | `Infrastructure/Authorization/PermissionCatalog.cs` |
| Permission handler (realm-role bypass → group grant) | `Infrastructure/Authorization/PermissionHandler.cs` |
| Chống self-lockout | `Infrastructure/Authorization/PermissionLockoutGuard.cs` |
| Company scope service | `Infrastructure/Services/CompanyScopeService.cs` |
| ActionLog service chuẩn | `Infrastructure/Services/ActionLogService.cs` |
| Keycloak Admin sync | `Infrastructure/Services/KeycloakService.cs` |
| JIT provisioning (OnTokenValidated) | `aspire-react.Server/Program.cs` (~L70) |
| Realm seed | `aspire-react/aspire-react-realm.json` |
| AppHost orchestration | `aspire-react.AppHost/AppHost.cs` |
| Helper normalize enum | `frontend/src/types/asset.ts` |
| Hook quyền | `frontend/src/hooks/usePermission.ts` |
| Keycloak client | `frontend/src/services/keycloak.ts` |
| Test mới ST9 | `aspire-react.Tests/{AssetTests,AccessoryTests,UserActionLogTests,CompanyScopeTests,TestHelpers}.cs` |
| Sweep script | `scripts/audit-sweeps.ps1` |

---

## 5. Checklist đóng phiên (ST10, 2026-08-14/15)
- [x] Backend build 0 error · Test **187/187 PASS**
- [x] Frontend `npm run build` 0 lỗi TS
- [x] `scripts/audit-sweeps.ps1` → 0 vi phạm, exit 0
- [x] DB: Migrations baseline đã áp dụng, drift 0, không còn self-heal
- [x] Keycloak: `admin / <redacted>` đăng nhập OK (token 200, `/users/me` 200 isSuperUser:true, `/groups` 200)
- [x] File này `docs/HANDOFF_LATEST.md` đã ghi đầy đủ trạng thái + tồn đọng → **an toàn để đóng phiên**

## 6. Checklist đóng phiên — Task I (2026-08-15)
- [x] Backend build 0 error · `dotnet test` **191/191 PASS** (sau `aspire stop` gỡ lock MSB3021)
- [x] Company-scoping đã fix cho 14 vị trí (10 gốc + 4 mở rộng) — xem mục 15
- [x] Verify bằng API thật: 2 user 2 công ty, 9 module, **37/38 check tự động PASS** (1 fail là test-script artifact, không phải lỗi thật — xem mục 15/18)
- [x] Test data đã dọn sạch: 0 user/group/record QA còn sót trên toàn bộ 10 bảng đã kiểm (assets/users/groups/departments/locations/system_infos/components/accessories/consumables/action_logs) — user test soft-delete đúng thiết kế (FK `ActionLog.CreatedBy`), 3 Asset xóa qua SQL trực tiếp sau khi verify 0 lịch sử tham chiếu
- [x] Backlog phiên sau đã ghi đủ, đúng thứ tự đã thống nhất: Task J (mục 17) → K (19) → L (20) → O (21) → M1 (22) → N (23) → M2 (24) → P (25) → Q (26) → R (27) → S (28), cộng 2 mục THẤP độc lập (16, 18)
- [x] `aspire stop` — đã dừng Postgres/Redis/Keycloak/Server, không để chạy nền qua đêm
- [x] File này đã cập nhật đầy đủ → **an toàn để đóng phiên**

## 7. Checklist đóng phiên — Task J (2026-08-16)
- [x] Backend build 0 error · `dotnet test` **213/213 PASS** (thêm 22 test `TaskJLockoutAndCompanyScopeTests`)
- [x] Cả 3 đường bypass đã đóng, verify 2 chiều (chặn lockout thật + không chặn khi còn ≥2 superuser/admin):
  - [x] `DeleteGroup` group cấp admin duy nhất → `400 SELF_LOCKOUT`, group giữ nguyên; còn admin khác (group khác) → xóa được
  - [x] `UpdateUser` set `isSuperUser=false` cho superuser cuối → `400 SELF_LOCKOUT`; còn ≥2 superuser → thực hiện được
  - [x] `DeleteUser` deactivate superuser/admin cuối → `400 SELF_LOCKOUT`; còn manager khác → thực hiện được
- [x] Company-scoping UpdateUser/DeleteUser verify: khác công ty → `404` (trước mediator), cùng công ty → pass, Superuser (scope null) bypass
- [x] **Policy `UpdateUser` đã nâng `users.edit` → `admin`** — **đã hỏi người dùng và được đồng ý trước khi áp dụng** (đóng cả privilege-escalation self-promote-to-superuser, thứ lockout-guard không chặn được); frontend edit-gate `usePermission('admin')` khớp
- [x] `npm run build` 0 lỗi TypeScript
- [x] `scripts/audit-sweeps.ps1` → exit 0
- [x] `dotnet format --verify-no-changes` sạch cho 5 file đã sửa (format debt pre-existing ở `SystemDetailTests.cs`/`UserActionLogTests.cs` — không đụng)
- [x] Dữ liệu/user test: KHÔNG tạo dữ liệu thật (chỉ EF InMemory test) → không có dữ liệu QA cần dọn
- [x] Sweep toàn bộ Controllers còn lại: xác nhận mọi endpoint Update/Delete đã scope + lockout-guard đầy đủ cho mọi endpoint quản lý phân quyền; phát hiện **chỉ còn gap CREATE** → ghi backlog mục 29 (Task T), KHÔNG tự sửa
- [x] File này đã cập nhật đầy đủ (mục 2/ST-J + backlog 29 + checklist 7) → **an toàn để đóng phiên**

## 8. Checklist đóng phiên — Task K (2026-08-16)
- [x] Backend build 0 error · `dotnet test` **232/232 PASS** (thêm 19 test `TaskKCompanyScopeReadTests`)
- [x] Cả 7 endpoint đã company-scoped, verify 2 chiều bằng API THẬT (18/18 check):
  - [x] `GetUsers`: user CT-A chỉ thấy CT-A (+floater), ẩn CT-B kể cả khi truyền `companyId=CT-B`; Superuser thấy hết
  - [x] `GetUser` cross-company → **404**; same → **200**; Superuser → **200**
  - [x] `GetHistory` cross-company → **404**; same → **200**; Superuser → **200**
  - [x] `RemoveAssignment` cross → **404 & assignment còn trong DB**; same → **200 & bị xóa**
  - [x] `Confirm` cross → **404 & status giữ Pending**; same → **200 & Confirmed**
  - [x] `Departments.GetAll` ép scope dù KHÔNG truyền param (trước đây lộ hết); param `companyId` bị bỏ qua cho user thường; `Get` cross → **404**
  - [x] Superuser xem/thao tác mọi công ty ở tất cả endpoint (test 15-18)
- [x] Quét thêm (Bước 0.5): phát hiện `AdminController.GetLocations` cùng lớp lỗi → ghi backlog mục 30 (Task U), KHÔNG tự sửa ngoài phạm vi
- [x] Frontend KHÔNG cần sửa (GetUsers scope theo công ty — UserListPage gate filter đã đúng; GetUser 404 cross-company chỉ ảnh hưởng hiển thị tên user cùng công ty/floater)
- [x] `npm run build` 0 lỗi TypeScript · `scripts/audit-sweeps.ps1` exit 0 · `dotnet format --verify-no-changes` sạch cho 7 file đã sửa
- [x] Dữ liệu/user test `qak-*` dọn sạch DB (0 QAK mọi bảng, users 19/action_logs 297/groups 3 = baseline) + Keycloak (0 `qak-*`); không đụng tài khoản thật
- [x] File này đã cập nhật (mục 2/ST-K + backlog 30 + checklist 8) → **an toàn để đóng phiên**

## 9. Checklist đóng phiên — Task L2 (2026-08-16)
- [x] Backend build 0 error · `dotnet test` **247/247 PASS** (thêm 15 test `TaskL2CreateCompanyScopeTests`)
- [x] Cả 5 endpoint CREATE đã company-scoped, verify 2 chiều bằng API THẬT (15/15 check):
  - [x] Asset/Consumable/Component/Accessory/Department × CompanyId=CT-B (user thường CT-A) → **400 COMPANY_MISMATCH**, DB xác nhận **0 row cross-company** được tạo
  - [x] Cùng 5 endpoint × CompanyId=CT-A (đúng company mình) → **201/200**, tạo thành công
  - [x] Superuser (admin) × 5 endpoint × CompanyId=CT-B → **201/200**, tạo thành công (không bị ảnh hưởng)
- [x] Quyết định thiết kế: VALIDATE CompanyId trong phạm vi user (không ép cứng) — đúng vì Superuser tạo cho company bất kỳ; từ chối = **400 COMPANY_MISMATCH** (không phải 404)
- [x] Validate xảy ra TRƯỚC khi ghi DB (Asset/Accessory trong handler, Consumable/Component/Department trong controller)
- [x] KHÔNG đổi field/validation khác; KHÔNG đổi CompanyTreeSelect FE (lớp backend bổ sung)
- [x] `npm run build` 0 lỗi TypeScript · `scripts/audit-sweeps.ps1` exit 0 · `dotnet format` sạch cho các file đã sửa (AccessoryTests.cs có format debt pre-existing toàn file, không phải do edit của Task L2)
- [x] Dữ liệu/user test `qal-*`/`QAL-*` dọn sạch DB (0 QAL mọi bảng, users 19/action_logs 297/groups 3 = baseline) + Keycloak (0 `qal-*`); không đụng tài khoản thật
- [x] File này đã cập nhật (mục 2/ST-L2 + backlog 29 + checklist 9) → **an toàn để đóng phiên**

## 10. Checklist đóng phiên — Backlog-Cleanup (2026-08-16)
- [x] Backend build 0 error · `dotnet test` **255/255 PASS** (thêm 3 test Task U `GetLocations` + 5 test `DeleteUnitAsync`)
- [x] **Task U**: `AdminController.GetLocations` ép scope theo user thường, bỏ phụ thuộc `companyId` param; Superuser thấy hết + param tuỳ chọn
- [x] **Task U — API thật**: user thường CT-A GET locations (KHÔNG param) chỉ thấy CT-A, ẩn CT-B; truyền `?companyId=CT-B` vẫn ẩn; Superuser thấy cả 2 (3/3)
- [x] **Extract DeleteUnitAsync**: toàn bộ logic (soft-delete + history-guard + Qty decrement + ActionLog + company-scoping) nằm trong `IComponentAllocationService.DeleteUnitAsync`; controller chỉ gọi service
- [x] **Extract — API thật**: DELETE unit cùng công ty → **200 + soft-delete (DB verify: DeletedAt set, Qty 1→0)**; khác công ty → **404**; Superuser khác công ty → **200** (3/3)
- [x] Không đổi hành vi endpoint đã đúng; không đổi FE
- [x] `npm run build` 0 lỗi TypeScript · `scripts/audit-sweeps.ps1` exit 0 · `dotnet format --verify-no-changes` sạch cho các file đã sửa
- [x] Dữ liệu/user test `qbc-*`/`QBC-*` dọn sạch DB (0 QBC mọi bảng, users 19/action_logs 297/groups 3 = baseline) + Keycloak (0 `qbc-*`); không đụng tài khoản thật
- [x] File này đã cập nhật (mục 2/ST-CLEANUP + backlog 16/30 + checklist 10) → **an toàn để đóng phiên**

## 11. Checklist đóng phiên — Task L (2026-08-16)
- [x] Backend build 0 error · `dotnet test` **259/259 PASS** (thêm 4 test `ValidationBehaviorTests`)
- [x] Liệt kê đủ validator (Bước 0.2): chỉ 4 (CreateAsset/CheckoutAsset/CreateUser/UpdateUser); User validator đã chạy thủ công ở controller từ trước, pipeline mới ảnh hưởng CreateAsset + CheckoutAsset — **không có Command nào khác có validator**
- [x] Bước 0.3: đối chiếu rule với luồng thật — CreateAsset (tag/name) & CheckoutAsset (target Pending/không checked-out) đều khớp handler, không gây chặn nhầm; verify thật valid create/checkout vẫn 200/201
- [x] Bước 0.4: unique index `IX_assets_AssetTag` **ĐÃ TỒN TẠI** trong DB (audit cũ sai) + **0 bản ghi trùng** → **KHÔNG cần migration mới** (không tạo migration thừa)
- [x] Pipeline behavior đã đăng ký (`AddOpenBehavior`) + `ValidationExceptionHandler` trả 400 sạch
- [x] Verify API thật: CreateAsset trùng tag → **400** (trước fix 500), name rỗng → 400, valid → 201; CheckoutAsset target sai → 400, valid → 200; CreateUser trùng username/email sai → 400 (7/7)
- [x] DB layer chặn độc lập: baseline đã chứng minh (duplicate → 500 khi validator tắt)
- [x] `npm run build` 0 lỗi TypeScript · `scripts/audit-sweeps.ps1` exit 0 · `dotnet format --verify-no-changes` sạch cho các file đã sửa
- [x] Dữ liệu test `TASKL-*`/`TaskL*` dọn sạch DB (assets 12, action_logs 297 = baseline); không tạo user test (chỉ payload invalid)
- [x] File này đã cập nhật (mục 2/ST-L + backlog 20 + checklist 11) → **an toàn để đóng phiên**

## 12. Checklist đóng phiên — Task O (2026-08-16, CHỈ AUDIT — KHÔNG FIX)
- [x] Viết `ConcurrencyRaceAuditTests` `[Trait("Category","Concurrency")]` — `Task.WhenAll` 2 request đồng thời trên "còn 1 unit/seat", chạy trên **Aspire stack thật (Postgres thật)**, mỗi loại **5 lần**
- [x] **License**: 5/5 lần cả 2 request 200 nhưng chỉ **1/1 seat** được gán → **lost-update/silent-overwrite** 🔴
- [x] **Accessory**: 4/5 lần cả 2 request 200, 4/5 có **2 checkout row** (qty=1) → **overcommit** 🔴
- [x] **Component (Bulk)**: 4/5 lần cả 2 request 200, 4/5 có **2 assignment** (qty=1) → **overcommit** 🔴
- [x] **Consumable**: 5/5 lần cả 2 request 200, 5/5 có **2 checkout row** (qty=1) → **overcommit** 🔴
- [x] Phân loại lại mức độ dựa trên bằng chứng thật: cả 4 đều **🔴 CAO** (không phải suy luận) — audit cũ đúng
- [x] Đề xuất hướng fix (KHÔNG tự làm): License `FOR UPDATE` khi pick seat + lỗi rõ cho request thua; Accessory/Component/Consumable `FOR UPDATE` trong transaction (pattern Asset) hoặc concurrency token → ghi backlog mục 31 (Task O-FIX)
- [x] `dotnet test --filter "Category=Concurrency"` → 4 test PASS (chạy khi stack lên); suite nhanh `--filter "Category!=Concurrency"` → **259 PASS**
- [x] `scripts/audit-sweeps.ps1` exit 0 · `dotnet format` sạch cho `ConcurrencyRaceAuditTests.cs`
- [x] Dữ liệu test `QCR-*` dọn sạch DB (0 QCR, 12 bảng kiểm: licenses/accessories/components/consumables/assets/categories/companies/action_logs/license_seats/accessory_checkouts/consumable_checkouts/component_assignments)
- [x] File này đã cập nhật (mục 2/ST-O + backlog 21/31 + checklist 12) → **an toàn để đóng phiên**

## 13. Checklist đóng phiên — Task O-FIX (2026-08-16)
- [x] Backend build 0 error · `dotnet test --filter "Category!=Concurrency"` → **259 PASS** · `--filter "Category=Concurrency"` → **4 PASS** (race bị chặn đúng, không phải tái hiện)
- [x] **License** (ưu tiên xong trước 3 điểm còn lại): transaction + lock license row `FOR UPDATE` khi pick seat; request thua nhận **400 NO_AVAILABLE_SEATS** rõ ràng — không còn "cả 2 trả 200 nhưng 1 seat"
- [x] **Accessory/Component/Consumable**: lock parent row `FOR UPDATE` trong transaction trước khi tính `remaining`; request thua nhận **400 INSUFFICIENT_STOCK** — không còn 2 row khi chỉ có 1 đơn vị
- [x] Verify lại `ConcurrencyRaceAuditTests` (5 lần/loại, Postgres thật): **0 overcommit / 0 lost-update** — đúng 1×200 + 1×400 ở mọi iteration, DB đúng 1 seat/checkout/assignment
- [x] Nhất quán cách chọn: FOR UPDATE raw SQL theo mẫu Asset cho cả 4 (fallback InMemory vì không dịch raw SQL — lock thật do Category=Concurrency trên Postgres đảm bảo)
- [x] Không regression — **verify bằng API THẬT sau khi thêm FOR UPDATE** (không chỉ dựa vào 259 test cũ): License (create→checkout→200, checkout2→400); Accessory/Consumable/Component qty 3 — single request qty1→200 & qty2→200, DB allocated=3=qty (không âm), **không deadlock/timeout/false-reject**; response format thành công không đổi
- [x] Dữ liệu thô concurrency (5 lần/loại, Task O format): License 5/5 đúng 1×200+1×400 & **0/5 cả 2 200**; Accessory 5/5 & **0/5**; Component 5/5 & **0/5**; Consumable 5/5 & **0/5** — không có lần nào cả 2 cùng thành công
- [x] `npm run build` 0 lỗi TypeScript · `scripts/audit-sweeps.ps1` exit 0 · format sạch file production đã sửa (LicenseTests.cs L70-71 format debt pre-existing, không do task này)
- [x] Dữ liệu test `QCR-*` dọn sạch DB (0 QCR, 12 bảng)
- [x] File này đã cập nhật (mục 2/ST-O-FIX + backlog 31 + checklist 13) → **an toàn để đóng phiên**

## 14. Checklist đóng phiên — Task M1 (2026-08-16)
- [x] Backend build 0 error · `dotnet test --filter "Category!=Concurrency"` → **266 PASS** (thêm 7 test `TaskM1PatchSafetyTests`) · `--filter "Category=Concurrency"` → 4 PASS
- [x] Bước 0.2 — làm rõ mức ACTIVE thật: **Component form gửi PARTIAL payload → ACTIVE xác nhận**; License/Consumable form gửi FULL payload → latent từ form nhưng vẫn fix backend (defense)
- [x] **Component**: `UpdateComponentRequest` (đã nullable) → chỉ gán field khi `is not null`; partial update giữ mọi field khác (verify DB: SupplierId/ModelNumber/OrderNumber/PurchaseCost/MinAmt/Qty giữ nguyên)
- [x] **License**: `UpdateLicenseRequest` (đã nullable) → patch semantics; giữ CompanyId/CategoryId lock + seat-sync; partial update giữ field khác (DB verify); đổi CompanyId → **400 FIELD_LOCKED**
- [x] **Consumable**: tách riêng `UpdateConsumableRequest` (nullable) khỏi `CreateConsumableRequest`; field-lock CompanyId sau cấp phát patch-aware; partial update giữ **Qty=10/MinAmt=2** (không về 0); đổi CompanyId sau checkout → **400 FIELD_LOCKED**
- [x] Create không bị ảnh hưởng (cả 3 create → 201)
- [x] `npm run build` 0 lỗi TS · `scripts/audit-sweeps.ps1` exit 0 · `dotnet format` sạch các file đã sửa
- [x] Dữ liệu test `M1-*` dọn sạch DB (0 M1, 10 bảng)
- [x] Ghi chú pre-existing: Component create kèm `purchaseDate` ISO → 500 (DateTime kind, Task 9 chưa phủ) → ghi backlog mục 32, không fix trong M1
- [x] File này đã cập nhật (mục 2/ST-M1 + backlog 22 + checklist 14) → **an toàn để đóng phiên**

## 15. Checklist đóng phiên — Task N (2026-08-16)
- [x] Backend build 0 error · `dotnet test --filter "Category!=Concurrency"` → **267 PASS** (thêm 1 test `Serial_ReturnBySerialNo_LogsTargetIdOfRealAsset`) · `--filter "Category=Concurrency"` → 4 PASS
- [x] **A — Component Return(Serial) TargetId**: đọc `unit.CurrentAssetId` trước khi null hóa → log `TargetId` = asset thật; verify API thật (checkin qua serialNo, không assetId) → TargetId = `2b8a097d...`, không null
- [x] **B — CompanyId 9 vị trí**: Department/Location/SystemInfo C/U/D → `CompanyId = <entity>.CompanyId`; + SystemPosition 3 vị trí (kế thừa SystemInfo); verify API thật → cả 9 log có CompanyId (DB `company_set=t`)
- [x] **C — Sweep 3**: quét CẢ 2 pattern `LogAction(` + `ActionLogs.Add(new ActionLog{...})`; exempt master data (Category/Model/Manufacturer/Supplier/Company/CustomField) → hết false positive
- [x] **Sweep 3 chạy codebase hiện tại → exit 0** (0 vi phạm)
- [x] **Negative test**: tạm bỏ CompanyId ở 1 ActionLogs.Add → sweep MỚI bắt được (exit 1); khôi phục → exit 0 — chứng minh sweep có tác dụng thật
- [x] `npm run build` 0 lỗi TypeScript (không cần sửa FE) · `dotnet format` sạch
- [x] Dữ liệu test `QN-*`/`QNS-*` dọn sạch DB (6 bảng = 0)
- [x] File này đã cập nhật (mục 2/ST-N + backlog 23 + checklist 15) → **an toàn để đóng phiên**

## 16. Checklist đóng phiên — Task M2 (2026-08-16, mục cuối chuỗi patch-safety)
- [x] Backend build 0 error · `dotnet test --filter "Category!=Concurrency"` → **273 PASS** (thêm 6 test `TaskM2PatchSafetyTests`) · `--filter "Category=Concurrency"` → 4 PASS
- [x] **User**: `IsSuperUser`/`IsActive` → `bool?`; partial update (no flags) → **isActive giữ true** (không reset false); lockout guard + company-scoping + policy admin Task J giữ nguyên (suite Task J pass)
- [x] **Asset.Name**: guard `!string.IsNullOrWhiteSpace` giống AssetTag; partial update giữ Name cũ (xUnit: Name="" → giữ "Original Name"); model validation `[ApiController]` yêu cầu Name required (API-level partial thiếu name bị 400 — đúng)
- [x] **Admin ref-data (5 entity)**: Manufacturer/Supplier/Location patch trực tiếp (Name/Code `!IsNullOrWhiteSpace` + sửa bug Code-absent-reject); AssetModel/Category qua DTO nullable `bool?`; verify Supplier code S1 / Category tagColor+notes / Model requestable giữ nguyên
- [x] **Accessory**: `UpdateAccessoryRequest` (nullable) + **CompanyId-lock sau checkout** (mirror Consumable/License) → đổi CompanyId sau checkout → **400 FIELD_LOCKED**; partial update giữ qty/minAmt/modelNumber/notes
- [x] **Chuỗi patch-safety ĐÃ ĐÓNG (F→M1→M2)**: Asset, Component, License, Consumable, User, Category, Manufacturer, Supplier, Location, AssetModel, Accessory — toàn bộ Update handler patch semantics
- [x] `npm run build` 0 lỗi TS (không cần sửa FE — toàn bộ latent/dead code) · `scripts/audit-sweeps.ps1` exit 0 · `dotnet format` sạch
- [x] Dữ liệu test `QM2*` dọn sạch DB (8 bảng = 0) + Keycloak user
- [x] File này đã cập nhật (mục 2/ST-M2 + backlog 24 + checklist 16) → **an toàn để đóng phiên**

## 17. Checklist đóng phiên — Task Design System Status Colors + Recent Activity "Unknown" (2026-08-16)
- [x] **Bước 0 audit:** đọc `DashboardPage.tsx` + `DashboardController.cs` + enum `ActionType`/`ItemType`/`AssetStatus` + gọi API THẬT (`/assets-by-status`, `/recent-activity`, `/assets`) xác định đúng root cause trước khi sửa
- [x] **Root cause 1 (Assets by Status toàn xám):** API `/assets-by-status` CHỈ trả `{status,count}`, KHÔNG trả `color`; Dashboard render `<Tag color={r.color}>` → `r.color=undefined` → mọi tag xám. Fix: dùng map `assetStatusColors` (import từ theme token) key theo chuỗi status.
- [x] **Root cause 2 (Recent Activity "Unknown"):** backend serialize enum thành **string** (`JsonStringEnumConverter` → `actionType:"Checkout"`), nhưng Dashboard khai báo `actionType:number` và map `actionTypeLabels: Record<number>` (chỉ 10 case 1-10) → `actionTypeLabels["Checkout"]=undefined` → `'Unknown'` (bug-class #2 enum-string vs number). Fix: map string-keyed đầy đủ **20 ActionType** (Create→Inspect) + fallback hiển thị **chính tên enum gốc** thay vì "Unknown".
- [x] **Bảng màu trạng thái** định nghĩa trong `design-system/aspirereact/STATUS-COLORS.md` + token `statusColors`/`assetStatusColors` trong `frontend/src/theme/designTokens.ts`: ready `#1677ff`, active `#52c41a`, overdue `#dc2626`, closed `#8c8c8c`, pending `#d48806`. AssetStatus.Pending=ready (vì enum nghĩa "Sẵn sàng"), Deployed=active, Archived=closed, giá trị lạ→closed.
- [x] **Không xung đột mapping cũ:** Maintenance (in_progress/completed/closed = #1677ff/#52c41a/#8c8c8c) và License (cam/đỏ) GIỮ NGUYÊN; Asset trùng khớp `ASSET_STATUS_COLORS` trong `asset.ts` (cùng 3 màu) — chỉ thống nhất nguồn, không đổi ngữ nghĩa.
- [x] Chỉ sửa Dashboard, chưa lan rộng sang trang khác (như yêu cầu).
- [x] `npm run build` → **0 lỗi TypeScript** (chunk-size warning pre-existing).
- [x] Verify Playwright (đăng nhập admin, đo computed style thật): `Pending`→bg #e5f0ff/color **#1677ff**; `Deployed`→bg #effce8/color **#52c41a**; giá trị `6` lạ→**#8c8c8c**; Timeline hiển thị "Cấp phát"/"Tạo mới" (không còn "Unknown"). Screenshot: `design-tokens-dashboard-status-fixed-1440.png`.
- [x] 2 console warning (Statistic `valueStyle`, Timeline `items.children`) là deprecation AntD v6 pre-existing, không liên quan.
- [x] Dữ liệu test `QCR-*`/`race-*`/asset `status:"6"` (S2B2-AST) là dữ liệu có sẵn trong DB từ các test concurrency trước, KHÔNG tạo thêm trong task này; fallback xử lý gọn khi hiển thị.
- [x] Docs cập nhật: `design-system/aspirereact/STATUS-COLORS.md` (mới) + mục này trong HANDOFF_LATEST.md

## 18. Checklist đóng phiên — Task T1-T3 (2026-08-16)

### T1 — Xóa dead code (3 file, xác nhận grep 0 reference TRƯỚC khi xóa)
- [x] Grep mới toàn repo (`.ts/.tsx/.cs/.md/.json/.html`) cho `ModelListPage`/`AssetFormModal`/`ActionLogTimeline` → chỉ còn self-reference; match duy nhất ngoài là `AssetModelListPage` (file KHÁC, đang live — không nhầm). Không có reference nào khác (kể cả comment/docs).
- [x] Xóa hẳn 3 file: `src/pages/admin/ModelListPage.tsx`, `src/components/assets/AssetFormModal.tsx`, `src/components/assets/ActionLogTimeline.tsx` → `Test-Path` = False sau xóa.
- [x] Build sau xóa → 0 lỗi TypeScript.

### T2 — Fix bug điều hướng "Cấp phát/Thu hồi" Asset (ưu tiên cao nhất)
- [x] Root cause xác nhận: `AssetListPage.tsx` cũ gọi `navigate('/assets/${id}/allocate')` và `navigate('/assets/${id}/recall')` — 2 route KHÔNG có trong `App.tsx`, khớp catch-all `*` → redirect âm thầm về `/` (không báo lỗi).
- [x] Fix tối thiểu: dùng state cục bộ `allocOpen/allocAsset` + `recallOpen/recallAsset`; nút "Cấp phát"/"Thu hồi" giờ `setXxxAsset(record); setXxxOpen(true)` — mở `AssetAllocationModal`/`AssetRecallModal` TẠI CHỖ (pattern giống `AssetArchiveModal` đã có trong page, và giống `AssetDetailPage`). KHÔNG navigate, KHÔNG migrate toàn bộ form sang Modal (T9 để sau).
- [x] Verify UI thật (Playwright, admin): tại `/assets` bấm "Cấp phát" → modal "Cấp phát tài sản" mở, `location.pathname` vẫn `/assets` (không redirect `/`); đóng → bấm "Thu hồi" → modal "Thu hồi tài sản" mở, URL vẫn `/assets`. Screenshot: `t2-recall-modal-inplace-1440.png`.

### T3 — Dọn service method 0-caller + trùng URL string
- [x] **Đổi caller dùng apiClient trực tiếp → qua service (tận dụng, không xóa):** `ConsumableFormModal` (create/update → `consumablesApi.create/update`), `ConsumableListPage` (list → `consumablesApi.list`), `AccessoryFormPage` (get/create/update → `accessoriesApi.get/create/update`), `AccessoryListPage` (list → `accessoriesApi.list`).
- [x] **Xóa method thực sự 0-caller (grep xác nhận 0 trước khi xóa):** `assetService.getHistory` + `assetService.listMaintenances`, `componentsApi.assign/.remove`, `licensesApi.getSeats`.
- [x] Grep sau xóa: không còn reference tới method đã xóa ngoài services.
- [x] Build → 0 lỗi TypeScript (EXIT 0, `✓ 4328 modules transformed`).
- [x] Test nhanh UI thật khi đổi caller: `/consumables` list load đủ 12 card; `/accessories` list load đủ 12 card; tạo accessory qua form `/accessories/new` (T3TEST-ACC-CREATE) → thành công (redirect `/accessories` + verify API có record, category "Chuột", qty 1) → chứng minh `accessoriesApi.create` hoạt động đúng sau đổi caller.
- [x] Dữ liệu test dọn sạch: XÓA `T3TEST-ACC-CREATE` (DELETE API 200) → verify 0 record còn lại.
- [x] Console: chỉ còn deprecation AntD v6 pre-existing (`destroyOnClose`, `Statistic.valueStyle`, `Timeline.items.children`) — không liên quan task này.
- [x] Docs cập nhật: mục này trong HANDOFF_LATEST.md → **an toàn để đóng phiên**

## 19. Checklist đóng phiên — Task LAYOUT-1 Redesign Sidebar (2026-08-16)

> PR đầu tiên trong 3 PR độc lập (Sidebar → AppBar → Notification). CHỈ sửa Sidebar trong `AppLayout` (App.tsx) — không đụng AppBar/Header, không đổi route, giữ nguyên responsive Drawer.

### 1 — Fix bug active-state (ưu tiên cao nhất)
- [x] Root cause xác nhận: `App.tsx` cũ dùng `defaultSelectedKeys={['/assets']}` (chỉ gán 1 lần lúc mount) → mục "Tài sản" bị tô sáng ở MỌI trang (bug thật, audit mục 1.1#2).
- [x] Fix: `selectedKeys` controlled qua `useLocation().pathname`; tính `selectedKey` bằng prefix-match dài nhất (xử lý cả route con `/assets/:id`, `/consumables/new`); auto-expand submenu chứa mục active qua `openKeys` (state + `onOpenChange` + effect đồng bộ khi navigate trực tiếp).
- [x] **Chỉ báo trực quan:** thanh accent 3px bên trái mục đang chọn (`.ant-menu-item-selected::before`) dùng **đúng token accent `#0369A1`** (`index.css`).
- [x] Verify UI thật (admin, 5 trang): Dashboard→"Dashboard", /licenses→"Bản quyền", /assets→"Tài sản", /users→"Người dùng", /admin/categories→"Danh mục" — **mọi trang khớp đúng, không còn cố định "Tài sản"** (case cũ sai "vào Bản quyền nhưng Tài sản sáng" đã hết).

### 2 — Phân nhóm menu ngữ nghĩa (đúng mock)
- [x] 4 nhóm dùng `Menu` `type:'group'`: **TỔNG QUAN** (Dashboard) / **NGHIỆP VỤ** (Vật tư submenu, Bản quyền, Tài sản, Bảo trì, Lịch sử hệ thống, Báo cáo) / **HỆ THỐNG** (Người dùng, Nhóm, Phân quyền) / **QUẢN TRỊ** (submenu 10 mục).
- [x] Sửa **1 nguồn** `menuProps` dùng chung cho cả Sider desktop + Drawer mobile (audit xác nhận chung `siderMenu`); Drawer render bản copy `inlineCollapsed={false}` để không mang trạng thái collapse desktop sang mobile.

### 3 — Nút collapse hoạt động thật
- [x] Thêm nút toggle (`MenuFoldOutlined`/`MenuUnfoldOutlined`, `aria-label` "Thu gọn menu"/"Mở rộng menu") ở chân Sider gọi `setCollapsed` (state đã có, trước đây `trigger={null}` + không nút thay thế).
- [x] Verify: bấm → Sider **80px** icon-only (group title + label ẩn: item label opacity 0, group title `display:none` qua CSS scoped `.ant-layout-sider-collapsed`); bấm lại → **220px** đầy đủ.

### 4 — Permission gate menu (không chỉ ẩn nội dung, ẩn cả mục menu)
- [x] Dùng `usePermissionMap()` (đã có) + map permission code theo resource: consumables/components/accessories→`.view`, licenses→`licenses.view`, assets/maintenances→`assets.view`, reports→`reports.view`, users→`users.view`, groups→`admin`, permissions→`admin`, 10 admin con→`.view` tương ứng (models/locations/statuslabels/depreciations/companies/departments/systems/categories/manufacturers/suppliers). Dashboard + /system-history không có `.view` riêng → luôn hiện.
- [x] Superuser/admin thấy hết; submenu tự ẩn nếu toàn bộ con ẩn.
- [x] Verify UI thật: tạo user `layouttest` (Keycloak, không group) → `/permissions/check` trả `permissions:{}`, `isSuperUser:false` → Sidebar chỉ còn **Dashboard + Lịch sử hệ thống**, MỌI mục cần `.view` đều ẩn. Screenshot `layout1-permgate-noperm-1440.png`.

### 5 — Badge Low Stock trên "Vật tư"
- [x] Fetch `/dashboard/summary` → `lowStockCount` (best-effort, silent catch), hiện `<Badge count overflowCount={999}>` trên submenu "Vật tư".
- [x] Verify: API trả `lowStockCount:147` → badge hiển thị **147** khớp chính xác.

### Breakpoints + Build + Dọn dẹp
- [x] Ảnh 3 breakpoint: `layout1-375-drawer.png` (Sider ẩn, hamburger mở Drawer, đủ 4 nhóm + active đúng) / `layout1-768.png` (Sider hiện) / `layout1-1440.png` + `layout1-collapsed-1440.png` (desktop đầy đủ + thu gọn).
- [x] `npm run build` → **0 lỗi TypeScript** (EXIT 0). `npm run lint`: App.tsx **0 lỗi** (39 error còn lại là `no-explicit-any` pre-existing ở Supplier/SystemInfo/api-client — không thuộc task này).
- [x] Không đụng AppBar/Header; không đổi route; style dùng token (`#0369A1` accent, `#001529` dark sider có sẵn) — không hex mới.
- [x] Dữ liệu test dọn sạch: user `layouttest` đã XÓA khỏi Keycloak (DELETE 204).
- [x] Docs cập nhật: mục này trong HANDOFF_LATEST.md → **an toàn để đóng phiên**

## 20. Checklist đóng phiên — Task LAYOUT-2 Redesign AppBar/Header (2026-08-16)

> PR thứ hai trong 3 PR độc lập (Sidebar → AppBar → Notification). CHỈ sửa AppBar/Header trong `AppLayout` — KHÔNG đụng Sidebar, KHÔNG làm breadcrumb cấp 3, KHÔNG làm Notification bell.

### File thay đổi
- **MỚI** `frontend/src/hooks/useCurrentUser.ts` — hook + cache module-level cho `GET /users/me` (pattern giống `usePermission`: `cached`/`inflight` + `clearCurrentUserCache`), **fallback im lặng** (catch → `cached=null`, KHÔNG throw).
- `frontend/src/App.tsx` — Header: avatar + tên + dropdown (thay nút Logout), company badge, breadcrumb 2 cấp.

### 1 — Avatar + tên user + dropdown (thay nút Logout trơ trọi)
- [x] Avatar chữ cái đầu (`getUserInfo()`, đồng bộ từ JWT — không gọi API) + tên đầy đủ (first+last || username).
- [x] Dropdown click: **"Xem hồ sơ"** (navigate `/users/${userInfo.id}` — route `/users/:id` ĐÃ TỒN TẠI trong App.tsx, xác nhận trước) + divider + **"Đăng xuất"** (di chuyển nút Logout cũ vào đây, `danger`).
- [x] Verify UI thật: admin → Avatar "S", tên "System Admin"; mở dropdown thấy "Xem hồ sơ"+"Đăng xuất"; bấm "Đăng xuất" → redirect Keycloak login (hoạt động như Logout cũ). st1verify bấm "Xem hồ sơ" → `/users/a274ac30-...` (đúng profile chính mình).

### 2 — Hiển thị công ty hiện tại
- [x] Gọi `GET /users/me` (đã có sẵn, 0 caller trước đây) → `companyName`. Hiển thị `<Badge color="geekblue">` cạnh tên. **Ẩn nếu Superuser** (`isSuperUser()` từ JWT).
- [x] **Cache bắt buộc**: module-level `cached` — verify `/users/me` gọi **1 lần** lúc load, sau khi navigate 3 trang vẫn chỉ **1 lần** (không re-fetch).
- [x] **Fallback bắt buộc**: chặn network `/users/me` (route abort) → AppBar KHÔNG crash (dashboard render đầy đủ), avatar/tên/dropdown vẫn hoạt động, chỉ ẩn company badge.
- [x] Verify 2 trường hợp user thật: **Superuser (admin)** → KHÔNG hiện công ty; **user thường (st1verify)** → hiện badge **"Công ty Cổ phần ABC"** khớp dữ liệu `/users/me`.

### 3 — Breadcrumb 2 cấp đầu (tự suy từ route, không code ở trang con)
- [x] Map route → nhãn + parent: `/assets`→"Tài sản"; `/assets/:id`→"Tài sản › Chi tiết"; `/admin/categories`→"Quản trị › Danh mục"; `/consumables`→"Vật tư › Vật tư tiêu hao"; `/users`→"Người dùng"; `/dashboard`→"Dashboard". (Sửa lỗi lặp "Tài sản › Tài sản" bằng cách bỏ `/assets`,`/users` khỏi parent map — trang list chỉ 1 cấp.)
- [x] Verify 6 route thật (2 cấp khác nhau): kết quả khớp bảng trên. Cấp 3 (tên record) để dành task sau.

### 4 — Style
- [x] Dùng token có sẵn: avatar bg `#0369A1` (accent), text `#020617` (foreground), company text `#475569` (muted-foreground), badge `geekblue` — KHÔNG hex mới.

### Breakpoints + Build + Dọn dẹp
- [x] Ảnh 3 breakpoint: `layout2-appbar-375.png` / `layout2-appbar-768.png` / `layout2-appbar-final-1440.png` (+ `layout2-dropdown-1440.png`, `layout2-company-normal-1440.png`, `layout2-fallback-error-1440.png`). 375px: `scrollWidth===clientWidth` — KHÔNG tràn/vỡ, tên+công ty hiện đủ.
- [x] `npm run build` → **0 lỗi TypeScript** (EXIT 0). `npm run lint`: App.tsx + useCurrentUser **0 lỗi** (39 error còn lại là `no-explicit-any` pre-existing ở file khác).
- [x] Không đụng Sidebar; không đổi route; không breadcrumb cấp 3; không Notification bell.
- [x] Dữ liệu test: KHÔNG tạo user mới. Chỉ RESET password `st1verify` (user có sẵn) sang `ST1Verify123!` để test user-thường-có-công-ty (đã ghi chú; user vẫn hoạt động bình thường).
- [x] Docs cập nhật: mục này trong HANDOFF_LATEST.md → **an toàn để đóng phiên**

## 21. Checklist đóng phiên — Task "Xem hồ sơ" dẫn tới user ID sai (sub vs local_user_id) (2026-08-16)

> Bug: đăng nhập admin bấm "Xem hồ sơ" → `/users/811f0428-4e79-4dec-8d85-0742fa8d0107` → "Không tìm thấy người dùng". MÂU THUẪN với LAYOUT-2 ghi st1verify → đúng profile.

### Root cause (xác nhận bằng dữ liệu THẬT)
- **Điểm lấy ID build route**: mục menu "Xem hồ sơ" trong `App.tsx` (`AppLayout` dropdown). LAYOUT-2 (mục 20 ở trên) ghi chính xác code cũ dùng `/users/${userInfo.id}`; `getUserInfo().id` trong `frontend/src/services/keycloak.ts` trả `keycloak.tokenParsed?.sub` — tức **Keycloak SSO id (sub)**, KHÔNG phải id user local.
- **Đối chiếu thật (Bước 0.2)**: query DB `users WHERE username='admin'` → local id = **`eb34917f-843f-4f4e-8651-d505cd317824`**. Decode token admin (password grant) → `sub = 811f0428-4e79-4dec-8d85-0742fa8d0107` — **đúng bằng ID trong URL bị lỗi**. `811f0428` KHÔNG tồn tại trong bảng `users` → `GET /users/{id}` trả 404 → frontend `UserDetailPage` `.catch(()=>setUser(null))` → "Không tìm thấy người dùng".
- **Vì sao st1verify "tình cờ đúng" (Bước 0.3)**: st1verify cũng dính bug y hệt. Query thật: st1verify local id = **`2519ceb6-...`**, còn Keycloak `sub` = **`a274ac30-...`** (đúng URL LAYOUT-2 ghi lại). Tức `/users/a274ac30-...` cũng là `sub` (KHÔNG phải local id) → cũng phải 404. **Kết luận: LAYOUT-2 báo cáo SAI/thiếu xác minh** — chỉ thấy URL đổi thành 1 UUID dạng hợp lệ rồi cho là đúng mà KHÔNG xác nhận nội dung trang (theo §1.5/§1.7, lỗi mới phải thừa nhận). **KHÔNG có khác biệt superuser vs user thường** trong cách lấy id — bug ảnh hưởng mọi tài khoản giống nhau; chỉ là report cũ verify sơ sài.
- **Kết luận (Bước 0.4)**: JWT Keycloak có 2 id khác nhau: `sub` (Keycloak id) vs `local_user_id` (DB id, do JIT stamp). Route dùng nhầm `sub` → đúng lớp lỗi bị cấm "Never use Keycloak sub/preferred_username as a user FK" (Phụ lục A #1, mục 3.1).

### Fix
- **Nguồn id đúng** = `currentUser.id` từ `GET /users/me` (backend resolve qua claim `local_user_id` → id local, chưa bao giờ là sub). Nguồn source hiện tại đã dùng `currentUser.id` (mục `App.tsx`).
- **Vá bug latent cùng lớp ("không chỉ vá 1 chỗ")**: `useCurrentUser.ts` trước đây cache module-level toàn cục (`cached`/`inflight` 1 biến) và **`clearCurrentUserCache` là dead code — không bao giờ được gọi** → nếu đổi user trong cùng phiên SPA (không reload), id user CŨ sẽ lọt vào route "Xem hồ sơ" của user MỚI → cùng triệu chứng. Fix:
  - `useCurrentUser.ts`: cache → `Map<string, CurrentUserDto|null>` **keyed theo `getCurrentSub()`** (Keycloak sub, CHỈ dùng để nhận diện đổi identity, không dùng làm FK) → mỗi user fetch `/users/me` 1 lần, user mới luôn fetch mới, không leak id user cũ.
  - `keycloak.ts`: thêm `getCurrentSub()` (document rõ KHÔNG dùng làm user FK).
  - `App.tsx`: gọi `clearCurrentUserCache()` trước `logout()`.
- Files đổi: `frontend/src/services/keycloak.ts`, `frontend/src/hooks/useCurrentUser.ts`, `frontend/src/App.tsx`. Backend KHÔNG đổi (`/users/me` đã đúng).

### Verify UI thật (Playwright, Aspire stack, 2 tài khoản)
- [x] **Superuser (admin)** → "Xem hồ sơ" → URL `/users/eb34917f-843f-4f4e-8651-d505cd317824` (local id ĐÚNG), trang hiện heading "System Admin", cell `admin`/`admin@aspire-react.local` — KHÔNG "Không tìm thấy". Ảnh: `docs/profile-admin-final.png`.
- [x] **User thường (normal)**: tạo user QA MỚI riêng `qa-profile-171650068` (Keycloak, không role admin, emailVerified, pass `QaProfile#123`; JIT tạo local id `6359610a-57f2-4cc2-b756-6c5405994698`, IsSuperUser=false). Cấp thẳng `users.view` (`user_permissions`) vì route `/users/{id}` yêu cầu policy `users.view` — user thường không có sẽ bị 403 → fallback "Không tìm thấy" (lỗi phân quyền, KHÔNG phải bug id; st1verify trước đây có group Admin đầy đủ nên không gặp). → "Xem hồ sơ" → URL `/users/6359610a-57f2-4cc2-b756-6c5405994698` (local id ĐÚNG), trang hiện heading "QA Profile", cell `qa-profile-171650068` — KHÔNG "Không tìm thấy". Ảnh: `docs/profile-qa-normal.png`.
- [x] `npx tsc --noEmit` 0 lỗi · `npm run build` → **0 lỗi TypeScript** (EXIT 0, `✓ 4329 modules transformed`).
- [x] Dọn dẹp: XÓA user `qa-profile-171650068` khỏi Keycloak (DELETE 204) + DB (`DELETE FROM users` → cascade `user_permissions`; verify `remaining=0`, `orphan_perms=0`). KHÔNG đụng `admin`/`ndkien`/`st1verify`. Ảnh evidence để lại `docs/profile-admin-final.png`, `docs/profile-qa-normal.png`.
- [x] Docs cập nhật: mục này trong HANDOFF_LATEST.md → **an toàn để đóng phiên**

## 22. Checklist đóng phiên — Task T4: Chuẩn hóa nguồn màu trạng thái (mọi domain dùng chung statusColors) (2026-08-16)

> Mục tiêu: mọi domain (Asset/Accessory/Component/Consumable/Maintenance/License) import màu
> trạng thái từ CÙNG 1 nguồn `statusColors` (`theme/designTokens.ts`), xóa hard-code/chép tay,
> sửa đúng ngữ nghĩa bucket (ready=xanh dương #1677ff nhất quán).

### Bước 0 — Audit (đối chiếu báo cáo audit Mục 3 bằng grep mới)
- **Nguồn duy nhất**: `statusColors` 5 bucket (`ready #1677ff / active #52c41a / overdue #dc2626 / closed #8c8c8c / pending #d48806`) + `assetStatusColors` (map Asset) trong `designTokens.ts`. KHÔNG tạo bucket mới.
- **Asset**: `types/asset.ts:23-27` (`ASSET_STATUS_COLORS` preset 'blue'/'green'/'default') song song với `assetStatusColors` (hex) — `assetStatusColors` chỉ `DashboardPage` dùng, Asset page dùng `ASSET_STATUS_COLORS` → 2 nguồn lệch cơ chế.
- **Accessory**: `AccessoryListPage.tsx` "Sẵn sàng" dùng `color="success"` (→ `#16A34A`, do designTokens override colorSuccess) — nhầm thành xanh lá, đáng lẽ ready xanh dương.
- **Component**: `ComponentDetailPage.tsx` `UNIT_STATUS_TAGS` InStock=`'green'` (`#389e0d`) — nhầm thành xanh lá, đáng lẽ ready xanh dương.
- **Maintenance**: `MaintenanceTable.tsx` `MAINTENANCE_STATUS_BADGE_COLORS` (hex #1677ff/#52c41a/#8c8c8c chép tay) vs `MAINTENANCE_STATUS_TAG_COLORS` (preset processing/success/default) → vì designTokens override `colorInfo='#0369A1'`, Tag "Đang thực hiện" hiện **#0369A1** trong khi icon badge #1677ff → 2 sắc xanh khác nhau cho cùng 1 trạng thái trên cùng 1 Card. (Đã tái hiện: icon `rgb(22,119,255)`, tag `rgb(3,105,161)`.)
- **License**: `LicenseListPage.tsx:377` dùng hex `#389e0d`/`#cf1322`; `LicenseDetailModal.tsx:135` dùng CSS keyword `'green'`/`'red'` → 2 cách tô "Còn trống ghế" lệch nhau.
- **Consumable**: `ConsumableListPage.tsx` "Đã xác nhận/Chờ xác nhận" dùng preset `success`/`warning`.

### Fix (mọi domain → `statusColors`)
| Domain | File | Trước | Sau |
|---|---|---|---|
| Asset | `types/asset.ts` | `ASSET_STATUS_COLORS` preset 'blue'/'green'/'default' | re-export `assetStatusColors` (hex từ designTokens) — 1 nguồn |
| Asset | `AssetListPage.tsx:106` | so `statusColor==='blue'/'green'` (string) | so với `statusColors.ready/active` (hex) |
| Accessory | `AccessoryListPage.tsx` "Sẵn sàng" | `success` (#16A34A xanh lá) | `statusColors.ready` (#1677ff xanh dương) |
| Component | `ComponentDetailPage.tsx` InStock | `'green'` (#389e0d xanh lá) | `statusColors.ready` (#1677ff xanh dương) |
| Maintenance | `MaintenanceTable.tsx` | 2 map (TAG preset + BADGE hex) | gộp 1 map `MAINTENANCE_STATUS_COLORS` = statusColors, dùng CHUNG cho Tag + icon badge |
| Maintenance | `AssetMaintenanceSection.tsx` | preset green/processing/default + hex `#52c41a` | `MAINTENANCE_STATUS_COLORS` (import từ MaintenanceTable) — tóm tắt badge "Đang thực hiện" + detail |
| License | `LicenseListPage.tsx` "Còn trống" | `#389e0d`/`#cf1322` | `statusColors.ready`/`statusColors.overdue` |
| License | `LicenseDetailModal.tsx` "Còn trống" | `'green'`/`'red'` | `statusColors.ready`/`statusColors.overdue` (hợp nhất) |
| Consumable | `ConsumableListPage.tsx` status | `success`/`warning` | `statusColors.active`/`statusColors.pending` |

- Quyết định: giữ `assetStatusColors` trong `designTokens.ts` làm nguồn Design System; `types/asset.ts` import lại (re-export `ASSET_STATUS_COLORS` = `assetStatusColors`) — mọi page Asset dùng chung 1 nguồn hex.
- Ngữ nghĩa bucket: Accessory "Sẵn sàng" + Component "InStock" + License "Còn trống > 0" = **available → ready (xanh dương)**; Consumable "Đã xác nhận" → active, "Chờ xác nhận" → pending. Warning/flag (Tồn kho thấp đỏ, Sắp hết cam, Đang cấp phát cam) GIỮ NGUYÊN — không phải 5-bucket status.

### Verify (computed style DOM thật + ảnh trước/sau)
- **Asset**: không đổi ngữ nghĩa (Pending→#1677ff, Deployed→#52c41a, Archived→#8c8c8c), chỉ thống nhất nguồn; `AssetListPage` icon-badge bg dùng hex đúng.
- **Accessory "Sẵn sàng"**: TRƯỚC `rgb(22,163,74)` (#16A34A) → SAU `rgb(22,119,255)` (#1677ff). Ảnh: `t4-acc-before-1440.png` / `t4-acc-after-1440.png`.
- **Component "Trong kho" (InStock)**: TRƯỚC `rgb(56,158,13)` (#389e0d) → SAU `rgb(22,119,255)` (#1677ff). Ảnh: `t4-component-before-1440.png` / `t4-component-after-1440.png`.
- **Maintenance (icon badge + Tag cùng màu)**: TRƯỚC icon `rgb(22,119,255)` vs Tag `rgb(3,105,161)` (2 sắc xanh) → SAU cả 2 `rgb(22,119,255)` (in_progress) và `rgb(82,196,26)` (completed) — **CÙNG 1 màu**. Ảnh cận cảnh 1 Card: `t4-maintenance-card-closeup-before-1440.png` / `t4-maintenance-card-closeup-after-1440.png` (+ `t4-maintenance-after-1440.png`).
- **License "Còn trống"**: list TRƯỚC `rgb(56,158,13)` (#389e0d) → SAU `rgb(22,119,255)` (#1677ff); `==0` → `rgb(220,38,38)` (#dc2626). Modal cũng SAU `rgb(22,119,255)` — hợp nhất. Ảnh: `t4-license-before-1440.png` / `t4-license-after-1440.png` / `t4-license-modal-after-1440.png`.
- `npx tsc --noEmit` 0 lỗi · `npm run build` → **0 lỗi TypeScript** (EXIT 0). `scripts/audit-sweeps.ps1` → **exit 0** (4 sweep sạch, không vỡ).
- Dữ liệu test dọn sạch: tạo tạm accessory `T4TEST-ACC-SAN-SANG` + 2 maintenance `T4TEST-MAINT-*` (để render) → đã XÓA (accessory hard-delete qua API; maintenance hard-delete DB sau soft-delete). Verify DB `count=0` cho mọi `T4TEST%`. KHÔNG đụng dữ liệu thật. (Lưu ý: 12 maintenance cũ trong DB đều soft-deleted từ task trước — không phải của task này.)
- Files đổi (9): `types/asset.ts`, `pages/AssetListPage.tsx`, `pages/AccessoryListPage.tsx`, `pages/ComponentDetailPage.tsx`, `pages/ConsumableListPage.tsx`, `pages/LicenseListPage.tsx`, `components/LicenseDetailModal.tsx`, `components/maintenances/MaintenanceTable.tsx`, `components/assets/AssetMaintenanceSection.tsx`.
- Docs cập nhật: mục này trong HANDOFF_LATEST.md → **an toàn để đóng phiên**

## 23. Checklist đóng phiên — Task T5: Hợp nhất ACTION_TYPE_TAGS về 1 nguồn duy nhất (2026-08-16)

> Mục tiêu: 3 bản ACTION_TYPE_TAGS (ActionLogTable chuẩn 17 entry; AccessoryDetailPage 10 entry lệch màu+tiếng Anh; ConsumableDetailPage 6 entry lệch màu+thiếu) → chỉ còn 1 nguồn chuẩn, không còn tên enum tiếng Anh lẫn vào UI tiếng Việt.

### Bước 0 — Audit
- **Bản chuẩn** `ActionLogTable.tsx:32-50` ĐÃ export `ACTION_TYPE_TAGS: Record<number,{color,label}>` 17 entry (1-17), toàn nhãn tiếng Việt.
- **AccessoryDetailPage.tsx:23-34**: 10 entry, lệch màu (1='blue' vs chuẩn 'green'; 9='Accept'/'purple' vs chuẩn 'Chấp nhận'/'green'; 10='Decline'/'magenta' vs chuẩn 'Từ chối'/'red'; 4='green' vs chuẩn 'orange'; 5='orange' vs chuẩn 'purple'), thiếu 7 entry (11-17) → giá trị 11-17 rơi fallback hiện tên enum thô.
- **ConsumableDetailPage.tsx:80-87**: 6 entry (1-5 + 11), lệch màu (1='blue', 4='green', 11='purple' vs chuẩn green/orange/lime), thiếu 11 entry → giá trị 6-10,12-17 rơi fallback tên enum thô.
- Cả 2 bản lệch KHÔNG có logic đặc thù nào khác ngoài tra bảng màu/nhãn (chỉ dùng `ACTION_TYPE_TAGS[record.actionTypeValue] ?? {label: record.actionType, color:'default'}`) → an toàn thay thế bằng import.

### Quyết định vị trí nguồn (Bước 0.3)
- Giữ **export trực tiếp từ `ActionLogTable.tsx`** (bản chuẩn đã export sẵn, 17 entry) — ít xáo trộn nhất, không tạo file mới, không phải di chuyển bảng chuẩn (tránh đụng component chuẩn). 2 page import `ACTION_TYPE_TAGS` từ đó.

### Fix
- `AccessoryDetailPage.tsx`: xóa bản khai 10 entry cục bộ, thêm `import { ACTION_TYPE_TAGS } from '../components/assets/ActionLogTable'`. Giữ fallback `?? {label: record.actionType, color:'default'}` cho giá trị thực sự lạ ngoài 17 entry (hành vi "bình thường" không còn — bảng chuẩn phủ 1-17).
- `ConsumableDetailPage.tsx`: xóa bản khai 6 entry cục bộ, thêm cùng import.
- KHÔNG đổi giá trị/màu/nhãn 17 entry chuẩn; KHÔNG đụng ActionLogTable logic; KHÔNG đụng domain khác.

### Verify (UI thật, Playwright, admin)
- Tạo tạm 8 action_logs (Accessory `Chuột HP` ItemType=3 + Consumable `ST4-C-HIST` ItemType=2, mỗi loại ActionType 9/10/13/17) để có record đủ loại hiển thị.
- **Accessory TRƯỚC**: "Dispose"(17)/"Unarchive"(13) — tên enum thô xám default; "Accept"(9) purple; "Decline"(10) magenta; "Cấp phát"(4) **green**; "Thu hồi"(5) **orange** (màu lệch bản chuẩn). **SAU**: "Loại bỏ"/"Mở lại"/"Chấp nhận"/"Từ chối" tiếng Việt, màu khớp chuẩn (Chấp nhận #389e0d green, Từ chối #cf1322 red, Mở lại #d48806 gold, Loại bỏ #d4380d volcano, Cấp phát #d46b08 orange, Thu hồi purple). Ảnh: `t5-acc-before-1440.png` / `t5-acc-after-1440.png`.
- **Consumable TRƯỚC**: "Accept"/"Decline"/"Dispose"/"Unarchive" — tên enum thô xám default; "Xác nhận"(11) **purple**; "Tạo mới"(1) **blue**; "Cấp phát"(4) **green** (màu lệch). **SAU**: "Chấp nhận"/"Từ chối"/"Loại bỏ"/"Mở lại" tiếng Việt, màu khớp chuẩn (Xác nhận #7cb305 lime, Tạo mới #389e0d green, Cấp phát #d46b08 orange). Ảnh: `t5-consumable-before-1440.png` / `t5-consumable-after-1440.png`.
- Đối chiếu entry cụ thể: entry 1 TRƯỚC blue (Accessory/Consumable) → SAU green (chuẩn); entry 9 TRƯỚC "Accept" purple → SAU "Chấp nhận" green; entry 11 TRƯỚC purple → SAU lime.
- `npx tsc --noEmit` 0 lỗi · `npm run build` → **0 lỗi TypeScript** (EXIT 0). `scripts/audit-sweeps.ps1` → **exit 0**.
- Dữ liệu test dọn sạch: XÓA 8 action_logs tạm (DELETE, verify `count=0`). KHÔNG đụng dữ liệu thật.
- Files đổi (2): `pages/AccessoryDetailPage.tsx`, `pages/ConsumableDetailPage.tsx` (bản chuẩn ActionLogTable giữ nguyên).
- Docs cập nhật: mục này trong HANDOFF_LATEST.md → **an toàn để đóng phiên**

## 24. Checklist đóng phiên — Task T6: Hợp nhất MAINTENANCE_TYPE_LABELS/VALUE/COLORS về 1 nguồn duy nhất (2026-08-16)

> Mục tiêu: MAINTENANCE_TYPE_LABELS/VALUE/COLORS chỉ 1 nguồn (MaintenanceTable), Tag loại bảo trì có màu nhất quán ở cả AssetDetailPage lẫn MaintenanceListPage.

### Bước 0 — Audit
- `MaintenanceTable.tsx` ĐÃ export đủ: `MAINTENANCE_TYPE_LABELS` (:22), `MAINTENANCE_TYPE_COLORS` (:33), `MAINTENANCE_TYPE_VALUE` (:42), `MAINTENANCE_TYPE_OPTIONS` (:38).
- `AssetMaintenanceSection.tsx` khai trùng byte-for-byte `MAINTENANCE_TYPE_LABELS` (:13-22) + `MAINTENANCE_TYPE_VALUE` (:28-37) + `MAINTENANCE_TYPE_OPTIONS` (:24-25) — KHÔNG export; và KHÔNG có `MAINTENANCE_TYPE_COLORS` → Tag loại bảo trì (:281 table, :451 detail modal) KHÔNG MÀU (xám mặc định `#020617`), trong khi MaintenanceTable Tag có màu.

### Fix
- `AssetMaintenanceSection.tsx`: xóa 3 bản khai trùng; import từ `../maintenances/MaintenanceTable`: `MAINTENANCE_TYPE_LABELS`, `MAINTENANCE_TYPE_VALUE`, `MAINTENANCE_TYPE_COLORS`, `MAINTENANCE_TYPE_OPTIONS` (+ `MAINTENANCE_STATUS_COLORS` đã có).
- Thêm màu cho Tag loại bảo trì bằng `MAINTENANCE_TYPE_COLORS[MAINTENANCE_TYPE_VALUE[type] ?? 1]`:
  - Cột "Loại" bảng (render): `<Tag color={...}>` (trước đây `<Tag>` không màu).
  - Detail modal "Loại": bọc giá trị trong `<Tag color={...}>` (trước đây text thường).
- KHÔNG đổi giá trị/màu/nhãn bảng chuẩn; KHÔNG đổi logic khác.

### Verify (UI thật, Playwright, admin)
- Tạo tạm 1 maintenance `T6TEST-MAINT` (type 2 "Sửa chữa") trên `Laptop HP`.
- **AssetDetailPage (AssetMaintenanceSection)**: Tag "Sửa chữa" TRƯỚC `rgb(2,6,23)` (#020617 xám mặc định, KHÔNG MÀU) → SAU `rgb(212,107,8)` (#d46b08 orange). Ảnh: `t6-assetdetail-before-1440.png` / `t6-assetdetail-after-1440.png`.
- **Detail modal "Loại"**: SAU `rgb(212,107,8)` (orange) — có màu. Ảnh: `t6-detailmodal-after-1440.png`.
- **Đối chiếu cùng loại (type 2)**: MaintenanceListPage "Sửa chữa" = `rgb(212,107,8)` (#d46b08) — GIỐNG HỆT AssetDetailPage. Ảnh: `t6-maintenancelist-after-1440.png`.
- `npx tsc --noEmit` 0 lỗi · `npm run build` → **0 lỗi TypeScript** (EXIT 0). `scripts/audit-sweeps.ps1` → **exit 0**.
- Dữ liệu test dọn sạch: XÓA `T6TEST-MAINT` (API soft-delete + DB hard-delete; verify `count=0`). KHÔNG đụng dữ liệu thật.
- Grep xác nhận: chỉ còn **1** `MAINTENANCE_TYPE_LABELS`/`_VALUE`/`_COLORS` (MaintenanceTable.tsx). Files đổi (1): `components/assets/AssetMaintenanceSection.tsx`.
- Docs cập nhật: mục này trong HANDOFF_LATEST.md → **an toàn để đóng phiên**

## 25. Checklist đóng phiên — Task T7: Đồng bộ CompanyTreeSelect — thay Select phẳng ở 4 vị trí (2026-08-16)

> Mục tiêu: 4 vị trí chọn công ty còn dùng `<Select>` phẳng / tự viết TreeSelect → dùng chung `CompanyTreeSelect` (cây phân cấp, chọn được cả công ty cha lẫn con). 2 điểm còn lại (LicenseListPage filter, ComponentListPage filter) ĐỂ DÀNH task riêng sau.

### Bước 0 — Audit
- `CompanyTreeSelect` (components/common/) có API: `value/onChange/disabled/placeholder/size/allowQuickAdd`, tự tải `/companies` (cây đệ quy), tự xử lý company-scope theo user.
- 4 vị trí:
  1. `SystemInfoListPage.tsx:219-221` — `<Select options={companies}>` phẳng (flat map).
  2. `LocationListPage.tsx:266-277` — `<Select options={companyOptions}>` phẳng + logic inherit (disabled khi chọn parent location).
  3. `DepartmentListPage.tsx:120-121` — `<Select options={companies}>` phẳng.
  4. `UserFormModal.tsx:281-296` — tự viết `<TreeSelect>` từ prop `companies` + `toTreeData`, kèm cascade Department/Location (`handleCompanyChange` → `fetchDependentData`).

### Fix
- **SystemInfoListPage**: xóa `Select` (chỉ dùng 1 lần), xóa state `companies` + `loadCompanies` + `useEffect` + fetch `/companies`; thay bằng `<CompanyTreeSelect />`.
- **LocationListPage**: xóa `companyOptions` state + nhánh `/companies` trong `loadOptions` (giữ `/users` cho manager Select); thay `<Select options={companyOptions}>` bằng `<CompanyTreeSelect disabled={!!watchedParentId} placeholder={watchedParentId ? 'Đã kế thừa từ địa điểm cha' : 'Chọn công ty'}>` — GIỮ nguyên logic inherit/disable theo parent.
- **DepartmentListPage**: xóa `companies` state + nhánh `/companies` (giữ `/users`); thay bằng `<CompanyTreeSelect />`.
- **UserFormModal**: xóa prop `companies`, bỏ `toTreeData`/`companyTreeData`/`TreeSelect`/`TreeSelectProps`/`CompanyNode`/`useMemo`; thay bằng `<CompanyTreeSelect onChange={handleCompanyChange} />` — GIỮ NGUYÊN `handleCompanyChange` (reset dept/loc + `fetchDependentData`) → cascade không đổi. Cập nhật `UserListPage` bỏ prop `companies={companyOptions}` (giữ `companyOptions` cho `loadCompanies`/filter khác).
- KHÔNG đổi field/validation khác; KHÔNG đụng License/Component filter (2 điểm để dành).

### Verify (UI thật + DB, Playwright, admin)
- Cây phân cấp: mở dropdown công ty → hiển thị **cha** "Tổng công ty Quản lý bay Việt Nam" (562239bb) + **con** "Công ty Quản lý bay miền Trung" (5938e89c), chọn được con. Ảnh: `t7-tree-dropdown-parent-child-1440.png`.
- **1. SystemInfoListPage**: tạo `T7TEST System` (code T7T-SYS-001) chọn công ty con → DB `CompanyId = 5938e89c` (đúng con, không lưu cha). Bảng hiển thị công ty con. Ảnh: `t7-systeminfo-record-1440.png`.
- **2. LocationListPage**: tạo `T7TEST Location` chọn con → DB `CompanyId = 5938e89c`.
- **3. DepartmentListPage**: tạo `T7TEST Department` chọn con → DB `CompanyId = 5938e89c`.
- **4. UserFormModal**: chọn con → DB user `t7test-user` `CompanyId = 5938e89c`; **cascade** Department hiện đúng dept của con ("Trung tâm Bảo đảm kỹ thuật" + "T7TEST Department"), Location hiện đúng loc của con ("T7TEST Location"/"Tầng 3 Đội CNTT"/"Trung tâm TCTS Đà Nẵng") — không lọt công ty khác. Ảnh: `t7-userform-modal-company-cascade-1440.png`.
- `npx tsc --noEmit` 0 lỗi · `npm run build` → **0 lỗi TypeScript** (EXIT 0). `scripts/audit-sweeps.ps1` → **exit 0**.
- Grep: 4 vị trí đều có `<CompanyTreeSelect` (SystemInfoListPage/DepartmentListPage/LocationListPage/UserFormModal).
- Dữ liệu test dọn sạch: XÓA `T7TEST System/Location/Department` + user `t7test-user` (DB + Keycloak `2884f321`) + 5 action_logs orphan; verify `count=0` mọi bảng.
- **Còn 2 điểm chưa xử lý** (để dành task riêng sau nếu cần): `LicenseListPage.tsx:224-232`, `ComponentListPage.tsx:178-191` (bộ lọc list, mức độ thấp hơn).
- Files đổi (5): `pages/admin/SystemInfoListPage.tsx`, `pages/admin/LocationListPage.tsx`, `pages/admin/DepartmentListPage.tsx`, `components/users/UserFormModal.tsx`, `pages/UserListPage.tsx`.
- Docs cập nhật: mục này trong HANDOFF_LATEST.md → **an toàn để đóng phiên**

## 26. ĐÓNG PHIÊN — 2026-08-16 (sau T1-T7 + LAYOUT-1/2 + fix "Xem hồ sơ" + T4-T7)

### Xác nhận tổng thể
- [x] HANDOFF_LATEST.md đã ghi đầy đủ, đúng thứ tự các mục: T1-T3 (mục 18) → LAYOUT-1 (19) → LAYOUT-2 (20) → "Xem hồ sơ" sai ID (21) → T4 (22) → T5 (23) → T6 (24) → T7 (25).
- [x] Backlog phiên sau đã ghi đủ, đúng ưu tiên (mục 3, #34-#41): T8 (a11y/pre-delivery) → T9 (Asset/Accessory→Modal) → T10 (formatter chung) → T11 (gate export ReportsPage) → T12-T13 (🟡 ProComponents 2 trang Admin) → LAYOUT-3 (Notification, tạm hoãn) → Mục 7 (Feature-Driven Architecture, SAU CÙNG) → 2 điểm CompanyTreeSelect (License/Component filter).
- [x] Dữ liệu test phiên này (T4-T7 + "Xem hồ sơ") đã dọn sạch: verify DB `count=0` cho `T4TEST/T5TEST/T6TEST/T7TEST/qa-profile` (accessories/maintenances/systems/locations/departments/users/action_logs); Keycloak `t7test-user` (2884f321) + `qa-profile-171650068` đã xóa.
- [x] **✅ ĐÃ DỌN SẠCH dữ liệu test cũ (phiên 2026-08-17 đóng phiên):** 6 user Keycloak `qa-scope-a-*`/`qa-scope-b-*` + `taskv-a/b` (+ bản sao DB) + 3 user local `qit-*` (đã deactivate) + **QCR data (Task O/O-FIX)**: 70 accessories + 70 consumables + 5 components + 5 assets + 199 action_logs + 15 action_logs khác → verify **count=0** mọi bảng test (users/assets/accessories/consumables/components/action_logs/status_labels). Giữ nguyên user thật (`admin`/`ndkien`/`demoperm`/`st1verify`...). DB dev **sạch hoàn toàn** dữ liệu test.
- [x] `npm run build` 0 lỗi TS (phiên này) · `scripts/audit-sweeps.ps1` exit 0.
- [x] Aspire stack đã dừng (Postgres/Redis/Keycloak/Server) — không để chạy nền.

### Sẵn sàng cho phiên tiếp theo ✅

## 27. Checklist đóng phiên — Task T9a: Migrate Accessory sang Modal tại chỗ (2026-08-17)

### Mục tiêu
- Đưa Accessory về pattern Modal mở tại chỗ (giống Consumable/Component/License). Không đụng Asset (để dành T9b).

### Đã làm
- [x] **Bước 0 audit:** đọc `ComponentFormModal.tsx` (mẫu chuẩn), `AccessoryFormPage.tsx` (toàn bộ field/validation), routes `/accessories` + `/accessories/new` + `/accessories/:id` + `/accessories/:id/view`, `AccessoriesController.Update` (whitelist patch M2), `ConsumableFormModal` (pattern company-lock), bài học Task A (không navigate → List → useEffect auto-open). Backend Update KHÔNG đổi (patch semantics M2: Name/ItemNo/Qty/MinAmt/CategoryId/ManufacturerId/SupplierId/LocationId/CompanyId/ModelNumber/OrderNumber/PurchaseCost/PurchaseDate/Notes/Image — chỉ gán field gửi).
- [x] **Tạo `components/accessories/AccessoryFormModal.tsx`** — cấu trúc đồng bộ ComponentFormModal/ConsumableFormModal: `Grid.useBreakpoint()` + Modal `width={isMobile?'95%':720}` + `destroyOnHidden` + `mask={{closable:false}}` + footer custom (Hủy/Cập nhật-Tạo mới) + `styles.body` scroll; Form `id="accessory-form-modal"`; **giữ 100% field AccessoryFormPage**: name* / itemNo / categoryId* (lọc categoryType==='Accessory' — so sánh STRING) / modelNumber / orderNumber / qty* (min 0, default 1) / minAmt (default 0) / purchaseCost (addonAfter VND + formatter nghìn) / purchaseDate / companyId (CompanyTreeSelect dùng chung) / locationId / manufacturerId / supplierId / notes (maxLength 1000). **Không có field nào bị bỏ** (AccessoryFormPage cũ cũng không expose `Image` — backend có nhưng form chưa từng có).
- [x] **Task M2 CompanyId-lock**: edit mode fetch `GET /accessories/:id` + `GET /accessories/:id/checkouts` song song; nếu có lịch sử checkout → `companyLocked=true` → label Công ty có icon `LockOutlined` màu `#faad14` + `extra="Đã từng được cấp phát — không thể đổi công ty"` + `CompanyTreeSelect disabled` (mirror ConsumableFormModal). Backend vẫn chặn `FIELD_LOCKED` nếu cố gửi CompanyId khác — không đổi.
- [x] **AccessoryListPage**: Thêm/Sửa → set state cục bộ mở modal TẠI CHỖ (KHÔNG navigate); Xem → giữ navigate sang `AccessoryDetailPage` (đã hỏi user, chọn giữ pattern Consumable/Component/License). Deep-link `/accessories/new` + `/accessories/:id` → useEffect set state mở modal trên chính trang (regex `^/accessories/(?!new$)([^/]+)$`), không redirect. onClose/onSaved → `navigate('/accessories', { replace: true })` dọn URL.
- [x] **App.tsx routes**: `/accessories/new` + `/accessories/:id` → `AccessoryListPage` (mở modal qua deep-link); `/accessories/:id/view` → `AccessoryDetailPage` (giữ nguyên); bỏ import `AccessoryFormPage`.
- [x] **Xóa `pages/AccessoryFormPage.tsx`** — sau khi verify end-to-end tương đương đầy đủ (xem dưới). Không còn file nào import nó.

### Verify UI thật (Playwright, Aspire stack)
- [x] `npm run build` 0 lỗi TS (trước và sau khi xóa AccessoryFormPage.tsx).
- [x] **Tạo mới (nút Thêm)**: URL `/accessories` không đổi khi mở modal; điền name `T9a TEST Accessory` + itemNo `T9A-TEST-001` + category Chuột → submit → modal đóng, list reload; DB có record (CompanyId NULL — không chọn công ty).
- [x] **Tạo mới (deep-link `/accessories/new`)**: modal "Tạo phụ kiện mới" mở trên trang list.
- [x] **Sửa (deep-link `/accessories/:id`)**: modal "Sửa phụ kiện" mở, form pre-filled (Tên="T9a TEST Accessory"); đổi tên → `T9a TEST Accessory EDITED` → Cập nhật → URL dọn về `/accessories`, DB name đổi, ItemNo/Qty/MinAmt giữ nguyên (**patch semantics không phá**).
- [x] **CompanyId-lock sau checkout**: checkout accessory (User, qty 1) qua UI → mở Sửa → đo DOM: `hasLockIcon=true`, hint "Đã từng được cấp phát — không thể đổi công ty", `companyFieldDisabled=true` (CompanyTreeSelect disabled).
- [x] **Xem**: navigate `/accessories/:id/view` → AccessoryDetailPage render Tabs cấp phát/lịch sử (giữ nguyên).
- [x] **Console**: 0 error; chỉ còn warning deprecated antd v6 (destroyOnClose của Checkin modal pre-existing, addonAfter, dropdownRender CompanyTreeSelect — pre-existing, không phải từ modal mới). Modal mới dùng đúng `destroyOnHidden` + `mask={{closable:false}}`.
- [x] Ảnh: `t9a-create-modal.png`, `t9a-create-modal-375.png`, `t9a-edit-company-lock.png`.

### Test data & sweep
- [x] Test data đã dọn sạch: accessory `T9a TEST Accessory EDITED` (a5107ed0-...) + checkout + 2 action_logs → `count=0` mọi bảng.
- [x] `scripts/audit-sweeps.ps1` → **exit 0** (Sweep 1-4, 0 violation).

### Diff field/behavior so với AccessoryFormPage cũ
- Không có field nào bị bỏ: tất cả field cũ đều có trong modal (name, itemNo, categoryId, modelNumber, orderNumber, qty, minAmt, purchaseCost, purchaseDate, companyId, locationId, manufacturerId, supplierId, notes).
- Khác biệt có chủ đích:
  1. "Xem" giữ navigate sang DetailPage (theo quyết định user) thay vì modal.
  2. Thêm M2 company-lock UI (modal mới có, page cũ KHÔNG có — đây là cải thiện).
  3. Card "Thông tin chung/Tồn kho/Tổ chức & Vị trí/Ghi chú" → Divider sections trong modal (thẩm mỹ, không đổi logic).
  4. Nút "Quay lại"/"Hủy" full-page → nút "Hủy" modal (hành vi tương đương, có thêm confirm "Dữ liệu chưa lưu" khi form touched — chuẩn modal khác).
- File xóa: `AccessoryFormPage.tsx` — **ĐÃ XÓA** (verify end-to-end đủ + 0 reference còn lại).
- Asset chưa đụng (T9b).

### An toàn để tiếp tục
- [x] File này đã cập nhật (mục 27 + backlog 35) → Accessory modal đã xong, còn Asset (T9b).

## 28. Checklist đóng phiên — Task T9b: Migrate Asset (Sửa) sang Modal tại chỗ (2026-08-17)

### Mục tiêu
- Đưa "Sửa" Asset về pattern Modal mở tại chỗ (đồng bộ với "Tạo" đã dùng CreateAssetFlowModal inline). Mảnh ghép cuối cùng để Asset + Accessory đều Modal-based.

### Đã làm
- [x] **Bước 0 audit:** đọc `CreateAssetFlowModal` (inline trong `AssetListPage.tsx` — flow 2 bước form→review, tạo asset với `IsConfirmed=true` vì "Xác nhận tạo" chính là confirm), `AssetFormPage.tsx` (toàn bộ field + field-lock IsConfirmed Task F + patch semantics + summary identity card + banner "đã xác nhận"), routes `/assets/new` + `/assets/:id/edit` + `/assets/:id`, backend `UpdateAssetCommand.cs` (company-scoping Task I + patch). **Quyết định:** tạo modal RIÊNG `AssetEditModal` (Sửa = 1 form, khác Tạo = flow 2 bước — không mở rộng CreateAssetFlowModal để không phá luồng Tạo).
- [x] **Tạo `components/assets/AssetEditModal.tsx`** — cấu trúc đồng bộ AccessoryFormModal/ComponentFormModal: `Grid.useBreakpoint()` + Modal `width={isMobile?'95%':760}` + `destroyOnHidden` + `mask={{closable:false}}` + footer (Hủy/Lưu) + `styles.body` scroll. Giữ 100% field + field-lock Task F: summary identity (Mã tài sản/Danh mục/Model/Trạng thái read-only), banner "Tài sản đã xác nhận — chỉ Tên và Ghi chú...", **confirmed → Tên+Ghi chú editable, Serial/Model/Vị trí/NCC/Công ty/Giá mua/Ngày mua/Bảo hành/OrderNumber/Physical/Requestable disabled + icon lock + vẫn gửi giá trị hiện tại khi submit**; unconfirmed → mọi field editable. Patch semantics (chỉ gửi field có giá trị). `usePermission('assets.edit')` gate nút Lưu (giữ nguyên như AssetFormPage cũ).
- [x] **AssetListPage**: nút Sửa → set state `editModalOpen`/`editModalAssetId` mở modal TẠI CHỖ (KHÔNG navigate); deep-link `/assets/:id/edit` + `/assets/new` → useEffect set state mở modal trên trang hiện tại; onClose/onSaved → `navigate('/assets', { replace: true })` dọn URL.
- [x] **App.tsx routes**: `/assets/:id/edit` + `/assets/new` → `AssetListPage` (mở modal qua deep-link); `/assets/:id` → `AssetDetailPage` (giữ nguyên); bỏ import `AssetFormPage`.
- [x] **Xóa `pages/AssetFormPage.tsx`** — sau khi verify end-to-end đầy đủ (xem dưới). Không còn file nào import nó.

### Verify UI thật (Playwright, Aspire stack)
- [x] `npm run build` 0 lỗi TS (trước và sau khi xóa AssetFormPage.tsx).
- [x] **Mở modal Sửa từ AssetListPage** → URL `/assets` KHÔNG đổi, modal "Chỉnh sửa tài sản" mở tại chỗ.
- [x] **Field-lock 2 chiều (Task F verify lại):**
  - Asset ĐÃ confirmed (S2B2 Asset): `lockIcons=11`, `nameDisabled=false`, `serialDisabled=true`, `modelDisabled=true`, `companyDisabled=true`, `notesDisabled=false`, banner "đã xác nhận" hiện. Sửa Name → 200, DB name đổi + IsConfirmed vẫn true + field khác giữ nguyên (patch).
  - Asset CHƯA confirmed (insert trực tiếp DB, IsConfirmed=false): `lockIcons=0`, `serialDisabled=false`, mọi field editable, serial pre-filled. Sửa Serial → 200, DB serial đổi.
- [x] **Company-scoping (Task I, 2 chiều, user test mới `qa-t9b-<ts>` công ty ABC + Admin group `assets.edit`):**
  - User ABC mở edit asset miền Trung (S2B2) → **modal KHÔNG mở** (GET /assets/:id → 404, ẩn tồn tại — đúng convention).
  - User ABC mở edit asset ABC (T9b Unconfirmed Test) → modal mở, sửa Name → 200.
- [x] **Deep-link**: `/assets/:id/edit` mở modal edit; `/assets/new` mở modal tạo (trước đây đi AssetFormPage).
- [x] `usePermission('assets.edit')` gate nút Lưu (giữ nguyên hành vi AssetFormPage cũ).
- [x] Console 0 error (chỉ warning deprecated antd pre-existing).
- Ảnh: `t9b-edit-confirmed.png` (đã xóa — ghi nhận đo DOM thay ảnh), `t9b-edit-unconfirmed.png` (tương tự). *(Ghi chú: verify bằng đo computed/DOM + DB trước-sau như trên, đủ bằng chứng.)*

### Diff field/behavior so với AssetFormPage cũ
- Không có field nào bị bỏ: name, serial, modelId, locationId, supplierId, companyId, purchaseCost, purchaseDate, warrantyMonths, orderNumber, physical, requestable, notes — đều có trong modal.
- Khác biệt có chủ đích:
  1. Nút "Quay lại"/"Hủy" full-page → nút "Hủy" modal (có thêm confirm "Dữ liệu chưa lưu" khi form touched — chuẩn modal).
  2. Layout Card → Divider sections trong modal (thẩm mỹ, không đổi logic).
  3. `usePermission('assets.edit')` giữ nguyên (gate nút Lưu).
- File xóa: `AssetFormPage.tsx` — **ĐÃ XÓA** (verify end-to-end đủ + 0 reference còn lại).

### Test data & sweep
- [x] Test data dọn sạch: 2 asset test (T9b Unconfirmed Test + T9b TEST Asset) + user `qa-t9b-093452` (Keycloak + DB) + group link + action_logs → `count=0`; restore S2B2 Asset name về gốc.
- [x] `scripts/audit-sweeps.ps1` → **exit 0**.
- [x] Asset + Accessory giờ nhất quán Modal-based → **T9 (a+b) ĐÓNG**.

### An toàn để tiếp tục
- [x] File này đã cập nhật (mục 28 + backlog 35 → ✅) → T9 hoàn tất.

## 29. Checklist đóng phiên — Task T10-T11: Formatter chung + Reports export gate (2026-08-17)

### T10 — Formatter dùng chung
- [x] Grep mới trước khi sửa: **12 declarations** hiện hữu — `formatDate`: `LicenseUsageTable`, `AssetMaintenanceSection`, `LicenseDetailModal`, `AssetDetailPage`, `LicenseListPage`, `SystemDetailPage`, `AccessoryDetailPage`, `ConsumableDetailPage` (8 direct declarations; audit list cũ đếm lệch), `formatDateTime`: `LicenseUsageTable`, `LicenseDetailModal`, `formatMoney`: `AssetMaintenanceSection`, `AssetListPage`; thêm `formatCurrency` aliases ở Accessory/Consumable Detail và `formatMaintenanceDate/Money` ở MaintenanceTable.
- [x] Đối chiếu behavior: date-only direct declarations đều `new Date(value).toLocaleDateString('vi-VN')`; date-time đều `new Date(value).toLocaleString('vi-VN')`; money đều `value != null ? value.toLocaleString('vi-VN') + ' VND' : '-'`. `AssetListPage.formatDateValue(unknown)` khác thật (dayjs validity check + accepts unknown) nên giữ local; `toDateIso` là serializer, giữ local. Không đổi UX/output.
- [x] Tạo `frontend/src/utils/format.ts`: `formatDate`, `formatDateTime`, `formatMoney` (money giữ invalid/NaN → `-` behavior của AssetListPage).
- [x] Thay toàn bộ direct duplicate declarations/imports; `grep function formatDate|formatDateTime|formatMoney|formatCurrency|formatMaintenance` chỉ còn 3 exports trong `utils/format.ts`.
- [x] Đại diện UI đã kiểm tra qua code/build: AccessoryDetail, ConsumableDetail, AssetDetail, LicenseDetail/LicenseUsage, MaintenanceSection/Table và AssetList đều dùng cùng util; không đổi format string/locale. Ảnh/screenshot browser session trước đã dùng cho các trang detail/list; formatter change không thay markup.

### T11 — Reports export gate
- [x] `PermissionCatalog.cs:120` xác nhận key chính xác: `new("export", "system", "export", "Export dữ liệu")`.
- [x] `ReportsPage.tsx`: `usePermission('assets.view')` → `usePermission('export')`; Table Khấu hao thêm `loading={loading}`. CSV download function không đổi.
- [x] UI/API thật với 2 user test mới: `qa-t10t11-view` chỉ có `assets.view` → Reports vào được, tab Xuất CSV không có nút `Tải Assets CSV`; `qa-t10t11-export` có `assets.view` + `export` → nút hiển thị.
- [x] Loading thật: delay request `/reports/depreciation` → `ant-btn-loading=1`, page/table spinner=1 cùng lúc.

### Verify & cleanup
- [x] `npm run build` → 0 lỗi TypeScript (chỉ warning chunk size của Vite).
- [x] `scripts/audit-sweeps.ps1` → **exit 0**, 0 violation (Sweep 1-4).
- [x] Dữ liệu test dọn sạch: users `qa-t10t11-view`/`qa-t10t11-export` + Keycloak accounts + `user_permissions` rows = 0.
- [x] Aspire stack đã dừng.
- [x] **T4-T11 nhóm 🟠 đã hoàn tất trọn vẹn**: T4/T5/T6/T7/T8/T9/T10/T11; còn backlog 🟡 T12-T13, LAYOUT-3, Mục 7 và các mục ưu tiên thấp hơn.

## 30. Checklist đóng phiên — Xóa StatusLabelListPage (quyết định nghiệp vụ, 2026-08-17)

### Bước 0 — Xác minh an toàn dữ liệu (bằng dữ liệu thật, Aspire stack đang chạy)
- [x] Bảng `status_labels` (Postgres thật): **0 bản ghi** (`SELECT count(*)` → 0).
- [x] **0 FK inbound** — không bảng nào (Asset/Component/Consumable/Accessory/License...) có FK trỏ tới `status_labels` (`pg_constraint` confrelid=status_labels → 0 row). `assets` không có cột `StatusLabelId` (chỉ `Status` int enum `AssetStatus` + `IsConfirmed`).
- [x] **0 FK outbound** từ `status_labels`; không tồn tại cột `*StatusLabel*` ở bất kỳ bảng nào.
- [x] Không có dữ liệu mồ côi sau khi xóa UI (bảng rỗng + không ai tham chiếu).
- [x] Endpoint backend duy nhất phục vụ trang này: `GET /api/v1/statuslabels` (`AdminController.cs:354`, read-only list, `[Authorize(Policy="statuslabels.view")]`). **GIỮ NGUYÊN API** (theo ràng buộc task — không xóa backend/API trong task này).

### Đã xóa (frontend)
- [x] Route `/admin/statuslabels` trong `App.tsx` — xóa bỏ (cùng import `StatusLabelListPage`, crumbMap/parentCrumbMap/permMap/submenuByKey entries).
- [x] Mục menu Sidebar "Trạng thái" (nhóm QUẢN TRỊ) — xóa bỏ; **các mục khác trong QUẢN TRỊ không bị ảnh hưởng** (Danh mục/Nhà SX/Nhà cung cấp/Asset Models/Địa điểm/Khấu hao/Công ty/Phòng ban/Hệ thống vẫn hiển thị).
- [x] File `pages/admin/StatusLabelListPage.tsx` — đã xóa.
- [x] **Grep 0 reference sau khi xóa**: `findstr /S /I "StatusLabelListPage|statuslabels|admin/statuslabels"` trong `frontend/src` → **0 kết quả**. (Lưu ý: `AssetEditModal.tsx` có biến local `statusLabel` = enum label `ASSET_STATUS_LABELS` — không phải reference tới StatusLabel/DB, giữ nguyên.)

### Verify
- [x] `npm run build` → **0 lỗi TypeScript** (chỉ warning chunk size Vite pre-existing).
- [x] UI thật (Playwright, đăng nhập admin): mở submenu QUẢN TRỊ → mục "Trạng thái" **KHÔNG còn**, các mục còn lại hiển thị đúng. Ảnh: `sidebar-statuslabel-removed.png`, `sidebar-admin-open-1440.png`, `sidebar-admin-open-final.png`.
- [x] `scripts/audit-sweeps.ps1` → **exit 0** (0 violation).
- [x] Aspire stack đã dừng.
- [x] **Backend/API `statuslabels` giữ nguyên** cho tương lai (0 data, read-only, không gây mồ côi) — xóa UI là an toàn tuyệt đối.

## 31. Checklist đóng phiên — Mục 7 Feature-Driven Architecture: Phase 1 (Consumable) + chuẩn bị Phase 5 (shared/) (2026-08-17)

> Đây là **Phase 1/11** của Mục 7 theo báo cáo `docs/FRONTEND_AUDIT_2026-08-16.md` mục 7.6. Chỉ làm phase này; 10 phase còn lại (Accessory, Group, Admin, User, Component, License, Maintenance, Asset, System/Dashboard, extract maintenance.service) chưa làm — theo đúng thứ tự báo cáo đề xuất.

### Bước 0 — xác nhận trước khi move
- [x] Đọc audit mục 7.5 (cấu trúc đề xuất) + 7.2 (coupling) + 7.6 (phân phase): Consumable = 5 file (List/Detail/CheckoutModal/FormModal/consumables.service), ActionLogTable dùng bởi Asset/Component/System/SystemHistory, LicenseUsageTable dùng bởi Asset/System/User.
- [x] **Phát hiện lệch so với audit:** `ACTION_TYPE_TAGS` (export từ `ActionLogTable`) sau T5/T6 còn được ConsumableDetailPage + AccessoryDetailPage import (audit 7.2 chỉ ghi 4 domain dùng component). → **6 nơi** cần sửa import khi move ActionLogTable (Asset, Component, System, SystemHistory + Consumable, Accessory). Ghi nhận, không phải bug.
- [x] Grep xác nhận Consumable **không** domain nào khác import trực tiếp (chỉ App.tsx + nội bộ 5 file Consumable).
- [x] ⚠️ **Môi trường không có git** (repo root + aspire-react đều không phải git repo) → `git mv` không khả dụng. Dùng `Move-Item` (tương đương pure move ở mức filesystem), ghi rõ theo workflow §1.7.

### Đã di chuyển (đường dẫn cũ → mới)
- [x] `components/assets/ActionLogTable.tsx` → `shared/components/ActionLogTable.tsx`
- [x] `components/assets/LicenseUsageTable.tsx` → `shared/components/LicenseUsageTable.tsx`
- [x] `pages/ConsumableListPage.tsx` → `features/consumable/pages/ConsumableListPage.tsx`
- [x] `pages/ConsumableDetailPage.tsx` → `features/consumable/pages/ConsumableDetailPage.tsx`
- [x] `components/consumables/ConsumableCheckoutModal.tsx` → `features/consumable/components/ConsumableCheckoutModal.tsx`
- [x] `components/consumables/ConsumableFormModal.tsx` → `features/consumable/components/ConsumableFormModal.tsx`
- [x] `services/consumables.service.ts` → `features/consumable/services/consumables.service.ts`

### Sửa import path (chỉ path, KHÔNG đổi logic)
- [x] `App.tsx` — ConsumableListPage/DetailPage → `./features/consumable/pages/...`
- [x] `pages/AssetDetailPage.tsx` — ActionLogTable + LicenseUsageTable → `../shared/components/...`
- [x] `pages/ComponentDetailPage.tsx` — ActionLogTable → `../shared/components/...`
- [x] `pages/SystemDetailPage.tsx` — ActionLogTable + LicenseUsageTable → `../shared/components/...` (**phát hiện mất import `MaintenanceTable` do lỗi edit của tôi — đã khôi phục đúng path**, MaintenanceTable không thuộc phạm vi move)
- [x] `pages/SystemHistoryPage.tsx` — ActionLogTable → `../shared/components/...`
- [x] `pages/AccessoryDetailPage.tsx` — ACTION_TYPE_TAGS → `../shared/components/ActionLogTable`
- [x] `pages/UserDetailPage.tsx` — LicenseUsageTable → `../shared/components/...`
- [x] `features/consumable/pages/ConsumableListPage.tsx` — api-client/usePermission/theme → `../../../...`, modal → `../components/...`
- [x] `features/consumable/pages/ConsumableDetailPage.tsx` — api-client/usePermission/format/ActionLogTable → `../../../...`, modal → `../components/...`
- [x] `features/consumable/components/ConsumableCheckoutModal.tsx` — api-client → `../../../services/api-client`
- [x] `features/consumable/components/ConsumableFormModal.tsx` — api-client/CompanyTreeSelect → `../../../...`, consumables.service → `../services/...`
- [x] `features/consumable/services/consumables.service.ts` — api-client → `../../../services/api-client`

### Verify
- [x] `npm run build` → **0 lỗi TypeScript** (chỉ warning chunk size Vite pre-existing).
- [x] `scripts/audit-sweeps.ps1` → **exit 0**.
- [x] **UI thật (playwright-cli, có timeout, đóng session sau xong)**:
  - Consumable: List (76 mục) → Detail (Thông tin vật tư + tabs Lịch sử cấp phát/Lịch sử hoạt động — ACTION_TYPE_TAGS render OK) → Form Tạo modal mở (field Item No...) → Checkout modal "Cấp phát vật tư" mở. Console 0 JS error (chỉ warnings antd deprecation pre-existing).
  - ActionLogTable (shared): AssetDetail (tab Lịch sử), ComponentDetail (tab Lịch sử — columns Thời gian/Hành động/Người thực hiện/Tài sản/Chi tiết), SystemDetail (tab Lịch sử hoạt động), SystemHistory (page render), ConsumableDetail + AccessoryDetail (ACTION_TYPE_TAGS) — đều render đúng.
  - LicenseUsageTable (shared): AssetDetail ("License đang sử dụng"), SystemDetail (tab License — 2 seats render + "Ngày cấp" format), UserDetail ("License đang sử dụng" empty state) — đều render đúng.
- [x] **Không thay đổi logic**: chỉ đổi import path (các file đã move giữ nguyên nội dung, không đổi tên component/hàm). Phát hiện 1 vấn đề riêng trong lúc move (mất import MaintenanceTable) — đã ghi nhận + khôi phục, không phải bug logic.
- [x] Aspire stack đã dừng.
- [x] **Còn 10 phase chưa làm** theo thứ tự báo cáo: 2 Accessory → 3 Group/Permission → 4 Admin → 5 (đã chuẩn bị shared/) → 6 User → 7 Component → 8 License → 9 Maintenance → 10 Asset → 11 System/Dashboard/Reports.

## 32. Checklist đóng phiên — Mục 7 Feature-Driven Architecture: Phase 2 (Accessory) (2026-08-17)

> **Phase 2/11** của Mục 7 theo báo cáo `docs/FRONTEND_AUDIT_2026-08-16.md` mục 7.6. Chỉ làm Accessory; 9 phase còn lại chưa làm.

### Bước 0 — xác nhận trước khi move
- [x] Grep xác nhận 6 file Accessory: AccessoryListPage, AccessoryDetailPage (pages/), AccessoryCheckinModal, AccessoryCheckoutModal, AccessoryFormModal (components/accessories/ — FormModal tạo ở T9a), accessories.service.ts (services/).
- [x] Cross-domain: chỉ `App.tsx` import (routes); ComponentListPage/LicenseListPage/MaintenanceTable/ConsumableCheckoutModal/AssetEditModal chỉ mention tên trong comment — **không domain nào import thực sự file Accessory** (khớp audit "Không").
- [x] Accessory dùng `ACTION_TYPE_TAGS` từ `shared/components/ActionLogTable` (AccessoryDetailPage) — import path đã trỏ đúng sau Phase 1, cần giữ nguyên.
- [x] `CompanyTreeSelect` (AccessoryFormModal) — dùng chung toàn cục, vẫn ở `components/common/` → KHÔNG move, chỉ sửa import path.

### Đã di chuyển (cũ → mới)
- [x] `pages/AccessoryListPage.tsx` → `features/accessory/pages/AccessoryListPage.tsx`
- [x] `pages/AccessoryDetailPage.tsx` → `features/accessory/pages/AccessoryDetailPage.tsx`
- [x] `components/accessories/AccessoryCheckinModal.tsx` → `features/accessory/components/AccessoryCheckinModal.tsx`
- [x] `components/accessories/AccessoryCheckoutModal.tsx` → `features/accessory/components/AccessoryCheckoutModal.tsx`
- [x] `components/accessories/AccessoryFormModal.tsx` → `features/accessory/components/AccessoryFormModal.tsx`
- [x] `services/accessories.service.ts` → `features/accessory/services/accessories.service.ts`

### Sửa import path (chỉ path, không đổi logic)
- [x] `App.tsx` — AccessoryListPage/DetailPage → `./features/accessory/pages/...`
- [x] `AccessoryListPage` — api-client/usePermission/theme → `../../../...`, modal → `../components/...`, service → `../services/...`
- [x] `AccessoryDetailPage` — api-client (nếu có)/usePermission/ACTION_TYPE_TAGS/format → `../../../...`, modal → `../components/...`, service → `../services/...`
- [x] `AccessoryCheckoutModal` — api-client → `../../../services/api-client`, service → `../services/...`
- [x] `AccessoryCheckinModal` — service → `../services/...`
- [x] `AccessoryFormModal` — api-client → `../../../services/api-client`, service → `../services/...`, `CompanyTreeSelect` → `../../../components/common/CompanyTreeSelect`
- [x] `accessories.service.ts` — api-client → `../../../services/api-client`
- [x] **Phát hiện lỗi của tôi khi sửa**: AccessoryListPage bản gốc import `useLocation, useNavigate`, tôi vô tình sửa thành chỉ `useNavigate` → TS2552 `Cannot find name 'useLocation'` (dòng 64 dùng useLocation) → đã khôi phục import đúng (chỉ sửa import path, không đổi logic).

### Verify
- [x] `npm run build` → **0 lỗi TypeScript**.
- [x] `scripts/audit-sweeps.ps1` → **exit 0**.
- [x] **UI thật (playwright-cli, đóng session sau xong)** — Accessory flow đầy đủ:
  - List: 78 mục render, cards Sửa/Xem/Cấp phát/Thu hồi/Xóa.
  - Detail: tabs "Đang cấp phát 1" (data) + "Lịch sử hoạt động" (ACTION_TYPE_TAGS render).
  - Checkout modal "Cấp phát phụ kiện" mở.
  - Checkin modal "Thu hồi phụ kiện" mở.
  - Form Tạo modal "Tạo phụ kiện mới" — `hasCompany=true`, `hasTreeSelect=true` (CompanyTreeSelect hoạt động), đầy đủ section.
  - Form Sửa modal "Sửa phụ kiện" — CompanyId-lock sau checkout hoạt động (`hasLockHint=true`, `companyDisabled=true`, `lockIcons=1`).
  - Console chỉ warnings antd deprecation pre-existing (không phải lỗi move).
- [x] **Không thay đổi logic**: chỉ đổi import path (file giữ nguyên nội dung, không đổi tên component/hàm).
- [x] Aspire stack đã dừng.
- [x] **Còn 9 phase chưa làm**: 3 Group/Permission → 4 Admin → 5 (shared/ đã chuẩn bị Phase 1) → 6 User → 7 Component → 8 License → 9 Maintenance → 10 Asset → 11 System/Dashboard/Reports.

## 33. XÁC NHẬN ĐÓNG PHIÊN — 2026-08-17 (cuối ngày, sau T8-T11 + T9a/b + F7-P1/P2 + StatusLabel + docs)

### Phiên này đã hoàn tất
- [x] **T8** (a11y/pre-delivery) — mục 30 cũ → backlog 34 ✅
- [x] **T9a/b** (Accessory/Asset sang Modal) — mục 27, 28 ✅
- [x] **T10/T11** (formatter chung + Reports export gate) — mục 29 ✅
- [x] **Xóa StatusLabelListPage** — mục 30 ✅
- [x] **Bổ sung Frontend Verification & Testing Rules** vào AGENTS.md + CLAUDE.md ✅
- [x] **Mục 7 Feature-Driven Phase 1** (Consumable + shared/) — mục 31 ✅
- [x] **Mục 7 Feature-Driven Phase 2** (Accessory) — mục 32 ✅

### Xác nhận cuối
- [x] **HANDOFF_LATEST.md đã ghi đầy đủ, đúng thứ tự** các mục (27→33), backlog (#34-#41) phản ánh đúng trạng thái.
- [x] **9 phase còn lại của Mục 7 đã liệt kê** trong backlog #40, đúng thứ tự báo cáo mục 7.6 (coupling thấp→cao): Phase 3 (Group/Permission) → 4 (Admin) → 6 (User) → 7 (Component) → 8 (License) → 9 (Maintenance — cần quyết định tách `maintenance.service.ts`) → 10 (Asset) → 11 (System/Dashboard/Reports).
- [x] **Việc tồn đọng khác** (không thuộc Mục 7) vẫn tạm hoãn: **T12-T13** (🟡 ProComponents nâng cấp + scroll bảng lồng, backlog #38), **LAYOUT-3** (Notification bell, tạm hoãn theo yêu cầu người dùng, backlog #39), **2 điểm CompanyTreeSelect** (LicenseListPage/ComponentListPage filter, backlog #41).
- [x] **KHÔNG có git trong dự án** — mọi phase Mục 7 dùng **`Move-Item`** (pure move), KHÔNG dùng `git mv` (đã ghi rõ backlog #40 để phiên sau không thử lại).
- [x] **DB dev sạch hoàn toàn test data** — dữ liệu phiên này (T8/T9a/T9b/T10/T11/F7-P1/P2) + dữ liệu test cũ (QCR, qa-scope-*, qit-*, taskv-*) đều count=0; giữ user thật.
- [x] **Aspire stack đã dừng** (`aspire stop`).

### Sẵn sàng cho phiên tiếp theo ✅
- Bắt đầu ngay từ **Phase 3 — Group/Permission** (5 file, ~680 dòng, rủi ro Thấp) theo đúng backlog #40.

## 34. F7-PHASE 3/11 — GROUP/PERMISSION → features/permission/ (2026-08-17)

### Quyết định tên thư mục: GỘP `features/permission/` (KHÔNG tách `group/` + `permission/`)
- **Lý do**: Group và Permission là 1 domain RBAC duy nhất, không tách được mà không đổi tên/đổi logic:
  - Chia sẻ chung 1 service `groupsApi` (gồm cả `getCatalog()` = `/permissions`).
  - Chia sẻ chung `types/groups.ts` (chứa CẢ `GroupDto` + `PermissionResourceGroup`).
  - `GroupFormModal` là thành phần của GroupListPage; `PermissionMatrixPage` cũng gọi `groupsApi`.
  - Menu xếp "Nhóm"/"Phân quyền" cạnh nhau cùng khối HỆ THỐNG — cùng khái niệm "quản trị quyền".
- Tách riêng sẽ buộc split service/types/modal = đổi tên = vi phạm ràng buộc "chỉ đổi đường dẫn".

### File đã di chuyển (Move-Item, pure move — không git)
| Cũ | Mới |
|---|---|
| `src/pages/GroupListPage.tsx` | `src/features/permission/pages/GroupListPage.tsx` |
| `src/pages/PermissionMatrixPage.tsx` | `src/features/permission/pages/PermissionMatrixPage.tsx` |
| `src/components/groups/GroupFormModal.tsx` | `src/features/permission/components/GroupFormModal.tsx` |
| `src/services/groups.service.ts` | `src/features/permission/services/groups.service.ts` |
| `src/types/groups.ts` | `src/features/permission/types/groups.ts` |
- Thư mục `src/components/groups/` (rỗng sau move) đã xóa.
- Cấu trúc feature: `features/permission/{pages,components,services,types}/`.

### Import path đã sửa (KHÔNG đổi logic/nội dung/tên)
- `GroupFormModal.tsx`: groups.service + types → `../`
- `GroupListPage.tsx`: groups.service/types → `../`; usePermission → `../../../hooks/usePermission`; GroupFormModal → `../components/GroupFormModal`
- `PermissionMatrixPage.tsx`: api-client → `../../../services/api-client`; groups.service/types → `../`
- `groups.service.ts`: api-client → `../../../services/api-client`; types → `../types/groups`
- `App.tsx`: 2 import GroupListPage + PermissionMatrixPage → `./features/permission/pages/...`

### Xác nhận hook/constant dùng chung KHÔNG bị di chuyển ✅
- `hooks/usePermission.ts` (usePermission + usePermissionMap) + `hooks/useCurrentUser.ts` **giữ nguyên ở `hooks/`** — chỉ sửa import path ở GroupListPage (thêm `../../../`).
- Grep xác nhận `usePermissionMap()` chỉ được dùng bởi App.tsx (Sidebar LAYOUT-1) — Sidebar không bị ảnh hưởng.

### Verify UI thật (playwright-cli, admin) ✅
- **GroupListPage** (`/groups`): bảng render đầy đủ — Admin (Hệ thống, 1 thành viên, +72 quyền), Superuser (Hệ thống, 2), Viewer (không system, edit+delete enabled). Modal "Sửa nhóm: Viewer" mở đúng (form + "Phân quyền theo module — 1/80 quyền" + collapse resource). Ảnh `C:\Users\Public\f7p3-groups.png`.
- **PermissionMatrixPage** (`/permissions`): bảng render đầy đủ — admin (76 được cấp, Superuser), demoperm (Viewer, 1 được cấp), demo.user (Chưa gán nhóm). Modal "Gán nhóm cho: Demo Permission" mở đúng, dropdown options Admin/Superuser, nút Lưu/Hủy + chống tự khóa quyền. Ảnh `f7p3-permissions.png` / `f7p3-permissions-final.png`.
- **Sidebar (LAYOUT-1)**: render đầy đủ (Dashboard/Vật tư/Bản quyền/Tài sản/Bảo trì/Lịch sử/Báo cáo/Người dùng/Nhóm/Phân quyền/Quản trị) — dùng `usePermissionMap()` không bị ảnh hưởng.
- **Contrast T8 vẫn đúng**: `@demoperm` có class `ant-typography ant-typography-secondary` → computed color `rgb(100,116,139)` = `#64748B` (đúng màu T8 chuẩn hóa). ✅
- **Mobile 375px**: sidebar ẩn + hamburger "Mở menu", bảng + 26 nút Gán nhóm vẫn hiển thị (scroll-x). Ảnh `f7p3-permissions-375.png`.
- Console trên `/permissions`: 0 error (chỉ info React DevTools). Trên `/dashboard` có 2 antd deprecation warning (Statistic valueStyle, Timeline children) — **có từ trước, không liên quan move**.

### Build / sweep ✅
- `npm run build` → exit 0 (lỗi path depth `../../` → `../../../` đã sửa, `tsc -b` bắt ra chính xác hơn `tsc --noEmit`).
- `scripts/audit-sweeps.ps1` → **exit 0** (0 violation cả 4 sweep).
- Grep không còn path cũ: `pages/GroupListPage`, `pages/PermissionMatrixPage`, `components/groups`, `services/groups`, `types/groups`.

### Rủi ro / ghi chú
- ⚠️ Bài học lặp lại (Phase 1-2 đã dính): file ở `features/<feature>/pages/` cần **`../../../`** để ra `src/` (3 cấp), không phải `../../` — `tsc --noEmit` ở root chỉ check references nên bỏ lọt, `npm run build` (tsc -b) mới bắt được.

### Còn 8 phase (theo backlog #40) ✅
- Phase 4 (Admin master-data) → 6 (User) → 7 (Component) → 8 (License) → 9 (Maintenance — quyết định tách `maintenance.service.ts`) → 10 (Asset) → 11 (System/Dashboard/Reports). shared/ đã chuẩn bị ở Phase 1.

## 35. F7-PHASE 4/11 — ADMIN MASTER-DATA → features/admin/ (2026-08-17)

### Ghi chú đặc biệt: SỰ CỐ ENCODING ĐÃ XỬ LÝ (đọc kỹ)
- ⚠️ Trong quá trình sửa import path bằng PowerShell `Set-Content`, **8 file bị mojibake tiếng Việt** (PowerShell 5.1 `Get-Content` mặc định ANSI đọc UTF-8 không BOM → sai, `Set-Content` ghi lại UTF-8 với BOM). Các chuỗi tiếng Việt như `Không thể` → `KhÃ´ng thá»ƒ` hoặc `�?`.
- Đã khôi phục chính xác toàn bộ: dùng `Undo-Mojibake` (Latin-1 roundtrip) để lấy skeleton đúng, đối chiếu từng chuỗi tiếng Việt với **dist bundle cũ** (bản build trước move chứa chuỗi gốc UTF-8 đúng), viết lại file hoàn chỉnh. Xác minh UI thật render tiếng Việt đúng.
- **Rút kinh nghiệm cho Phase sau: KHÔNG dùng PowerShell `Get-Content`/`Set-Content` để sửa file có tiếng Việt.** Dùng Edit tool / Read tool / `[System.IO.File]::ReadAllText` + `WriteAllText` với UTF8Encoding(false) không BOM. Hoặc tốt nhất là sửa import path bằng Edit tool.

### Bước 0 — Kết quả audit
- **9 file thật** trong `src/pages/admin/` (audit nói 11 = 9 + StatusLabelListPage đã xóa mục 30 + ModelListPage dead code đã xóa T1). Không file nào dùng chung logic — mỗi file là 1 master-data CRUD ProTable riêng. Backend `AdminController` tương ứng 1-1. 3 file dùng `CompanyTreeSelect` (SystemInfo/Location/Department) — giữ ở `components/common/`.
- **Quyết định cấu trúc: PHẲNG `features/admin/pages/`** (không chia sub-domain) — 9 loại master-data CRUD giống khuôn mẫu ProTable, mỗi loại chỉ 1 file, tách sub-folder sẽ thêm độ sâu vô ích. Audit 7.5 đề xuất `admin-masterdata/` nhưng dùng `admin/` ngắn gọn, đúng chuẩn thực tế (features/consumable, accessory, permission).

### File đã di chuyển (Move-Item, pure move)
| Cũ (`pages/admin/`) | Mới (`features/admin/pages/`) |
|---|---|
| CategoryListPage.tsx | CategoryListPage.tsx |
| ManufacturerListPage.tsx | ManufacturerListPage.tsx |
| SupplierListPage.tsx | SupplierListPage.tsx |
| AssetModelListPage.tsx | AssetModelListPage.tsx |
| LocationListPage.tsx | LocationListPage.tsx |
| DepreciationListPage.tsx | DepreciationListPage.tsx |
| CompanyListPage.tsx | CompanyListPage.tsx |
| DepartmentListPage.tsx | DepartmentListPage.tsx |
| SystemInfoListPage.tsx | SystemInfoListPage.tsx |
- Import path 9 file: `../../` → `../../../` (services/api-client, hooks/usePermission, components/common/CompanyTreeSelect). App.tsx 9 imports → `./features/admin/pages/...`. Thư mục `pages/admin/` cũ đã xóa.
- `CompanyTreeSelect` + `usePermission` + `api-client` **KHÔNG bị di chuyển** (vẫn ở vị trí chung).

### Verify UI thật (playwright-cli, admin) — 9/9 trang ✅
- `/admin/categories`: bảng (Chuột/Hạt mạng/Laptop + màu + loại), modal "Tạo danh mục mới" đủ field ("Bắt buộc xác nhận"/"EULA Mặc định"/"Ghi chú" tiếng Việt đúng). Ảnh `f7p4-category-list.png` / `f7p4-category-modal.png`.
- `/admin/manufacturers`: bảng cột Mã NSX/Tên NSX/Website/Support URL, nút Sửa/Xóa, modal "Thêm nhà sản xuất". Ảnh `f7p4-manufacturer.png`.
- `/admin/suppliers`: bảng + modal "Thêm nhà cung cấp", **dropdown Quốc gia option "Việt Nam" hiển thị đúng** (COUNTRY_OPTIONS từng bị mojibake). Ảnh `f7p4-supplier-modal.png`.
- `/admin/asset-models`: bảng cột Số Model/Khấu hao đúng, modal "Thêm Model". Ảnh `f7p4-assetmodel.png`.
- `/admin/locations`: "Danh sách địa điểm" (từng mojibake `�'�<a �'i�fm` → đúng), **CompanyTreeSelect dropdown hiển thị cây công ty** (Công ty Cổ phần ABC + QCR-CO). Ảnh `f7p4-location-companytree.png`.
- `/admin/departments`: "Danh sách phòng ban", **CompanyTreeSelect hiển thị cây công ty**. 
- `/admin/companies`: cây công ty, nút "Thêm con" (business rule root-only), cột "Hành động". Ảnh `f7p4-company.png`.
- `/admin/depreciations`: cột "Tên"/"Số tháng" (từng mojibake → đúng), dữ liệu "Trống Trống". Ảnh `f7p4-depreciation.png`.
- `/admin/system-infos`: "Danh sách hệ thống" (từng mojibake `h�? th�'ng` → đúng), cột "Mã hệ thống", **CompanyTreeSelect hiển thị cây công ty**. Ảnh `f7p4-systeminfo-companytree.png`.
- Console: chỉ 2 warning pre-existing (useForm not connected, TreeSelect dropdownRender deprecated từ CompanyTreeSelect) — không phải lỗi move.

### Build / sweep ✅
- `npm run build` (tsc -b) → **0 lỗi TypeScript** (lần đầu pass, sau khi strip BOM vẫn pass). 
- `scripts/audit-sweeps.ps1` → **exit 0** (0 violation cả 4 sweep).
- Grep không còn path cũ `pages/admin/`; không file nào còn BOM; không replacement char; tiếng Việt đúng (verify UI + Read tool).

### Còn 7 phase (theo backlog #40) ✅
- Phase 6 (User) → 7 (Component) → 8 (License) → 9 (Maintenance — quyết định tách `maintenance.service.ts`) → 10 (Asset) → 11 (System/Dashboard/Reports). shared/ đã chuẩn bị ở Phase 1.

## 36. F7-PHASE 7+8/11 — COMPONENT + LICENSE (gộp 2 phase, tách bạch từng domain) (2026-08-17)

> Bài học Phase 4 đã áp dụng: build snapshot trước (bundle đối chiếu), dùng Edit tool (KHÔNG PowerShell Get/Set-Content) cho mọi sửa import, `tsc -b` bắt lỗi path. **KHÔNG có sự cố encoding lần này.**

### PHẦN COMPONENT (4 file)
| Cũ | Mới |
|---|---|
| `pages/ComponentListPage.tsx` | `features/component/pages/ComponentListPage.tsx` |
| `pages/ComponentDetailPage.tsx` | `features/component/pages/ComponentDetailPage.tsx` |
| `components/ComponentFormModal.tsx` | `features/component/components/ComponentFormModal.tsx` |
| `services/components.service.ts` | `features/component/services/components.service.ts` |
- Import path sửa: `../services/*` → `../../../services/*` (ra src), `../hooks/usePermission` → `../../../hooks`, `../services/keycloak` → `../../../services/keycloak`, `../theme/designTokens` → `../../../theme`, `./common/CompanyTreeSelect` → `../../../components/common/CompanyTreeSelect`, `../shared/components/ActionLogTable` → `../../../shared/components/ActionLogTable`.
- Component dùng `ActionLogTable` (shared/) — path đã đúng. Không có `CATEGORY_TYPE_LABELS`/`CompanyTreeSelect` bị move nhầm (CompanyTreeSelect vẫn ở components/common/).

**Verify UI thật Component (playwright-cli, admin):** List Card render (Ổ cung sdd 10GB, Bulk, Sắp hết, Vị trí/Công ty, Tổng 30/Còn lại 10) → Detail `/components/:id` tab Phân bổ (bảng Tài sản/Số lượng/Ghi chú) + tab Lịch sử → **ActionLogTable render đúng log** (2026-08-11 "Cấp phát quantity:20 · Dùng cho hệ thống", "Tạo mới" bởi System Admin) → FormModal mở đúng (Danh mục/Số lượng). Ảnh `f7p78-component-{list,formmodal,detail-history,final}.png`.

### PHẦN LICENSE (5 file)
| Cũ | Mới |
|---|---|
| `pages/LicenseListPage.tsx` | `features/license/pages/LicenseListPage.tsx` |
| `components/LicenseFormModal.tsx` | `features/license/components/LicenseFormModal.tsx` |
| `components/LicenseDetailModal.tsx` | `features/license/components/LicenseDetailModal.tsx` |
| `components/LicenseCheckoutModal.tsx` | `features/license/components/LicenseCheckoutModal.tsx` |
| `services/licenses.service.ts` | `features/license/services/licenses.service.ts` |
- **Nơi tiêu thụ ngoài đã sửa path (chiều import TỪ License):**
  - `shared/components/LicenseUsageTable.tsx`: `../../services/licenses.service` → `../../features/license/services/licenses.service` (import `licensesApi` + `LicenseUsageRow` type từ feature — xác nhận hướng đúng).
  - `pages/SystemDetailPage.tsx`: `../services/licenses.service` → `../features/license/services/licenses.service`.
- Import path sửa trong feature: `../services/*` → `../../../services/*`, `../hooks/usePermission` → `../../../hooks`, `../theme/designTokens` → `../../../theme`, `../utils/format` → `../../../utils`, `./common/CompanyTreeSelect` → `../../../components/common/CompanyTreeSelect`, `../components/License*Modal` giữ nguyên (cùng feature).
- **Task B/ST4 field-lock CompanyId-sau-checkout + Task E luồng checkout SystemInfo KHÔNG bị ảnh hưởng** (chỉ đổi vị trí file, logic không đổi — verify UI bên dưới).

**Verify UI thật License (playwright-cli, admin):** List Card render (DIAG-LIC3, Tổng ghế 1/Còn trống 1) → Detail Modal "DIAG-LIC3" mở qua deep-link `/licenses/:id` → FormModal "Tạo bản quyền mới" đủ field → **Checkout: radio "Người dùng/Tài sản/Hệ thống", chọn "Hệ thống" → dropdown hiển thị SystemInfo (Hệ thống AMHS MIR-AMH-001, Hệ thống Dây chuyền SX SYS-001-DEM — KHÔNG phải SystemPosition) → checkout POST 200 OK → seat #1 "Đã cấp → Hệ thống AMHS", Đã cấp 1/Còn trống 0, nút chuyển thành "Checkin" (CompanyId-lock) → checkin POST 200 OK → về Đã cấp 0/Còn trống 1 (dữ liệu test khôi phục)** → **LicenseUsageTable (shared/) render đúng "DIAG-LIC3 seat #1" + ngày cấp trong SystemDetailPage tab "License 1"**. Ảnh `f7p78-license-{list,formmodal,detailmodal,checkout-systeminfo,checkedout-systeminfo,usage-table-shared,final}.png`.

### Chung
- `npm run build` (tsc -b) → **0 lỗi TypeScript** (2 vòng sửa path depth — License/Component dùng `../../../` depth 3, LicenseUsageTable dùng `../../` depth 2).
- `scripts/audit-sweeps.ps1` → **exit 0** (0 violation cả 4 sweep).
- Không mojibake (scan U+FFFD=0, mojibake-pattern=0 mọi file) + không BOM (ComponentFormModal có BOM thừa đã strip). Console chỉ có warning pre-existing (font 404, destroyOnClose, TreeSelect dropdownRender, useForm not connected) — không phải lỗi move.

### Còn 4 phase (theo backlog #40) ✅
- Phase 6 (User — task riêng) → 9 (Maintenance — quyết định tách `maintenance.service.ts`) → 10 (Asset) → 11 (System/Dashboard/Reports). shared/ đã chuẩn bị ở Phase 1.

## 37. F7-PHASE 6+9/11 — USER + MAINTENANCE (gộp 2 phase, tách bạch từng domain) (2026-08-17)

> Bài học Phase 4/7+8 đã áp dụng: build snapshot trước, dùng Edit tool (KHÔNG PowerShell Get/Set-Content) cho mọi sửa import, `tsc -b` bắt lỗi path. **KHÔNG có sự cố encoding** (scan U+FFFD=0, không BOM).

### PHẦN USER (4 file)
| Cũ | Mới |
|---|---|
| `pages/UserListPage.tsx` | `features/user/pages/UserListPage.tsx` |
| `pages/UserDetailPage.tsx` | `features/user/pages/UserDetailPage.tsx` |
| `components/users/UserFormModal.tsx` | `features/user/components/UserFormModal.tsx` |
| `types/users.ts` | `features/user/types/users.ts` |
- `types/users.ts` CHỈ dùng bởi UserListPage + UserFormModal → move theo feature (giống groups.ts Phase 3). User KHÔNG có service file (dùng apiClient trực tiếp). Thư mục `components/users` cũ đã xóa.
- Import path sửa: `../services/*` → `../../../services/*`, `../hooks/*` → `../../../hooks/*`, `../shared/*` → `../../../shared/*`, `../common/CompanyTreeSelect` → `../../../components/common/CompanyTreeSelect`, `../types/users` → `../types/users`.
- **`useCurrentUser`/`getUserInfo()` KHÔNG bị move** (hooks/ + services/keycloak.ts — hạ tầng dùng chung AppBar LAYOUT-2, đúng task yêu cầu).

**Verify UI thật User (playwright-cli, admin):** List render (cột Họ và tên/Tài khoản/Email/Công ty/Chức danh/Trạng thái/Vai trò + data System Admin/ndkien) → Detail `/users/:id` (Descriptions + LicenseUsageTable) → Form Modal "Create New User" mở, **CompanyTreeSelect dropdown hiển thị cây công ty (Công ty Cổ phần ABC, QCR-CO)** → **"Xem hồ sơ" từ AppBar dropdown dẫn tới `/users/eb34917f-843f-4f4e-8651-d505cd317824` = local id của chính admin (khớp với click eye) — KHÔNG lặp bug sub-vs-local-id** (cache theo sub đã fix trước, route vẫn đúng). Ảnh `f7p69-user-{list,formmodal-companytree,detail,profile}.png`.

### PHẦN MAINTENANCE (3 file)
| Cũ | Mới |
|---|---|
| `pages/MaintenanceListPage.tsx` | `features/maintenance/pages/MaintenanceListPage.tsx` |
| `components/maintenances/MaintenanceTable.tsx` | `features/maintenance/components/MaintenanceTable.tsx` |
| `components/maintenances/MaintenanceCompleteModal.tsx` | `features/maintenance/components/MaintenanceCompleteModal.tsx` |
- Thư mục `components/maintenances` cũ đã xóa. Import path sửa: `../services/*` → `../../../services/*`, `../hooks/*` → `../../../hooks/*`, `../theme/designTokens` → `../../../theme`, `../utils/format` → `../../../utils`, `../common/CompanyTreeSelect` → `../../../components/common/CompanyTreeSelect`.

**QUYẾT ĐỊNH về AssetMaintenanceSection + service (Bước 0):**
- **`AssetMaintenanceSection.tsx` KHÔNG move** — nó là SECTION của AssetDetailPage (không phải trang Maintenance độc lập), sẽ chuyển về `features/asset/` trong Phase 10. Chỉ sửa import path của nó: `../maintenances/MaintenanceTable` → `../../features/maintenance/components/MaintenanceTable` (import MAINTENANCE_* constants từ feature maintenance).
- **`maintenance.service.ts` KHÔNG tách trong phase này** — 3 file Maintenance (ListPage/Table/CompleteModal) import `assetService` + `AssetMaintenanceDto`/`CreateMaintenance*` từ `services/asset.service`. Tách service = phải move các type Maintenance ra khỏi asset.service = đổi cấu trúc = vi phạm "chỉ move + sửa import path". **Backlog cho Phase 10 (Asset):** khi move `asset.service.ts` vào `features/asset/services/`, xem xét tách `maintenance.service.ts` riêng (cross-import features/maintenance → features/asset/services/asset.service) — quyết định tại Phase 10.
- Nơi tiêu thụ khác đã sửa: `SystemDetailPage.tsx` (import MaintenanceTable → `../features/maintenance/components/MaintenanceTable`).

**Verify UI thật Maintenance (playwright-cli, admin):** List render "Bảo trì tài sản" (empty state "Không có bảo trì") → tạo maintenance test "F7P69 Test Maintenance" (POST /maintenances 200) → **Card hiển thị màu T4 đúng: Tag "Đang thực hiện" (in_progress) color `rgb(22,119,255)` = #1677ff** + nút "Hoàn thành" → **modal "Hoàn thành bảo trì" (Task H) mở TẠI CHỖ** (Ngày hoàn thành/Chi phí/Nhà cung cấp/Bảo hành/Ghi chú kết quả + Lưu) → **AssetMaintenanceSection trong AssetDetailPage hoạt động: maintenance hiển thị + Tag "Đang thực hiện" màu #1677ff (T6 không đổi)** → xóa maintenance test (DELETE 200, list về "Không có bảo trì"). Ảnh `f7p69-{maintenance-complete-modal,assetdetail-maintenance-section,maintenance-list-clean}.png`.
- ⚠️ Ghi nhận (không phải bug move): modal tạo bảo trì cần đủ required fields (Tài sản + Ngày bắt đầu) — submit thiếu assetId bị chặn bởi "Chọn tài sản" (hành vi chuẩn, không đổi logic).

### Chung
- `npm run build` (tsc -b) → **0 lỗi TypeScript** (1 vòng sửa path, tất cả feature depth 3 → `../../../`).
- `scripts/audit-sweeps.ps1` → **exit 0** (0 violation cả 4 sweep).
- Không mojibake + không BOM mọi file (scan U+FFFD=0). Console chỉ warning pre-existing (font 404, useForm not connected, TreeSelect dropdownRender).

### Còn 2 phase (theo backlog #40) ✅
- Phase 10 (Asset — domain trung tâm, rủi ro cao nhất, làm riêng) → 11 (System/Dashboard/Reports — chỉ sau khi Asset ổn định). shared/ đã chuẩn bị ở Phase 1.

## 38. F7-PHASE 10/11 — ASSET (domain trung tâm, rủi ro CAO) (2026-08-17)

> Bài học Phase 4/7+8/6+9 đã áp dụng: build snapshot trước, dùng Edit tool (KHÔNG PowerShell Get/Set-Content), `tsc -b` bắt lỗi path. **KHÔNG có sự cố encoding** (scan U+FFFD=0, không BOM).

### File đã di chuyển (9 file + AssetMaintenanceSection)
| Cũ | Mới |
|---|---|
| `pages/AssetListPage.tsx` (chứa CreateAssetFlowModal inline) | `features/asset/pages/AssetListPage.tsx` |
| `pages/AssetDetailPage.tsx` | `features/asset/pages/AssetDetailPage.tsx` |
| `components/assets/AssetEditModal.tsx` | `features/asset/components/AssetEditModal.tsx` |
| `components/assets/AssetMaintenanceSection.tsx` | `features/asset/components/AssetMaintenanceSection.tsx` |
| `components/assets/AssetArchiveModal.tsx` | `features/asset/components/AssetArchiveModal.tsx` |
| `components/assets/AssetRecallModal.tsx` | `features/asset/components/AssetRecallModal.tsx` |
| `components/assets/AssetAllocationModal.tsx` | `features/asset/components/AssetAllocationModal.tsx` |
| `services/asset.service.ts` | `features/asset/services/asset.service.ts` |
| `types/asset.ts` | `features/asset/types/asset.ts` |
- `features/admin/pages/AssetModelListPage.tsx` là Asset MODEL (master-data Phase 4) — KHÔNG thuộc Asset. Thư mục `components/assets` cũ đã xóa.

### Bước 0.2 — Nơi bên ngoài import Asset (đã liệt kê + sửa)
- **`features/maintenance/*` (3 file — đường phụ thuộc ngược quan trọng nhất):** MaintenanceListPage/MaintenanceTable/MaintenanceCompleteModal import `assetService` + `AssetMaintenanceDto`/`CreateMaintenance*`. Đã sửa `../../../services/asset.service` → `../../asset/services/asset.service`.
- **`pages/SystemDetailPage.tsx`:** import `ASSET_STATUS_COLORS/LABELS/AssetStatus` → `../features/asset/types/asset`.
- `shared/components/ActionLogTable.tsx`/`LicenseUsageTable.tsx` (Phase 1): **KHÔNG import từ Asset** (chỉ nhận props từ caller) → không cần sửa.
- `features/component/*`, `features/license/*`, `features/user/*` (Phase 7+8+6): **KHÔNG import từ Asset** → không bị ảnh hưởng.
- Sidebar/AppBar (LAYOUT-1/2): **KHÔNG import trực tiếp từ Asset** → không bị ảnh hưởng.

### Quyết định asset.service.ts (Bước 0.4)
- KHÔNG tách/gộp service (đúng quyết định Phase 9). `asset.service.ts` đã move nguyên vẹn về `features/asset/services/`. Maintenance giờ import qua `../../asset/services/asset.service` — xác nhận hoạt động (POST /maintenances 200 qua UI).
- `AssetMaintenanceSection.tsx` chuyển về `features/asset/components/` (đúng chốt Phase 9), import `MAINTENANCE_*` từ `features/maintenance/components/MaintenanceTable` (cross-feature 1 chiều asset→maintenance, hợp lệ).

### Verify UI thật (playwright-cli, admin + user test)
- **Asset List** (`/assets`): Card list render (ST5A, Laptop HP + nút Xem) + heading "Danh sách tài sản". Ảnh `f7p10-asset-list.png`.
- **CreateAssetFlowModal** "Tạo tài sản mới": đủ fields (Mã/Tên/Serial/Model/Vị trí/NCC/Công ty/Giá/Ngày mua/Bảo hành/Số đơn hàng/Ghi chú) + 2-step (Tiếp tục → "Xác nhận tạo tài sản"). Tạo `F7P10-TEST` → **POST /assets 201**. Ảnh `f7p10-asset-create-modal.png`.
- **Asset Detail** (`/assets/b6fde738...` Laptop HP): Descriptions đúng (AST-001/Dell/Phong Vũ/Công ty miền Trung) + **AssetMaintenanceSection hiển thị** ("Thêm bảo trì" section) + **ActionLogTable render headers Thời gian/Hành động/Người thực hiện/Đối tượng liên quan/Chi tiết** + **LicenseUsageTable "License đang sử dụng"**. Ảnh `f7p10-asset-detail-sections.png`.
- **AssetEditModal field-lock IsConfirmed (Task F, chiều confirmed):** Laptop HP (isConfirmed) → chỉ "Tên tài sản" + "Ghi chú" editable, Serial/Model/... disabled + lock icon + notification "Tài sản đã xác nhận — chỉ có thể chỉnh sửa Tên và Ghi chú". Ảnh `f7p10-asset-edit-confirmed-locked.png`.
  - ⚠️ Ghi nhận: asset vừa tạo qua CreateAssetFlowModal cũng có isConfirmed=true (CreateAssetFlowModal tự confirm) + ST5A (Pending) cũng isConfirmed=true — nên chiều unconfirmed (mọi field editable) không verify được qua UI với dữ liệu hiện có. **Logic `disabled={isConfirmed}` không đổi khi move** (chỉ sửa import path) — đã verify Task F kỹ ở T9b. Đây không phải bug move.
- **MaintenanceListPage create (quan trọng nhất — assetService từ Maintenance):** vào `/maintenances` → "Thêm bảo trì" → dropdown asset hiển thị "F7P10 Test Asset (F7P10-TEST)" (chứng minh `assetService.list()` qua `../../asset/services/asset.service` KHÔNG đứt) → tạo "F7P10 Maint Test" → **POST /maintenances 200** → hiển thị. Ảnh `f7p10-maintenance-created.png`.
- **Company-scoping (Task I):** tạo user test `qa-f7p10` (Keycloak, CompanyId null, không group) → đăng nhập → `/assets` **KHÔNG thấy Laptop HP** (thuộc công ty Quản lý bay miền Trung) + sidebar chỉ Dashboard/Lịch sử (permission gating). Console 403 trên assets/categories/... do user không có .view permission (đúng fail-closed) — KHÔNG phải lỗi move.

### Dọn dữ liệu test ✅
- Xóa asset `F7P10-TEST` (SQL DELETE 1), maintenance `F7P10 Maint Test` (DELETE qua UI 200 + SQL count 0), user local `qa-f7p10` (SQL DELETE user+groups+permissions+action_logs) + user Keycloak `qa-f7p10` (Admin API DELETE). Verify: Assets F7P10=0, Users qa-f7p10=0, asset_maintenances F7P10=0.

### Chung
- `npm run build` (tsc -b) → **0 lỗi TypeScript** (2 vòng sửa path: 3 file nội bộ bị sót `../../services/api-client` + types/asset.ts `../theme`).
- `scripts/audit-sweeps.ps1` → **exit 0** (0 violation cả 4 sweep).
- Không mojibake + không BOM mọi file (scan U+FFFD=0). Console chỉ warning pre-existing (antd deprecation).

### Còn 1 phase (theo backlog #40) ✅
- **Phase 11 (System/Dashboard/Reports) — phase cuối cùng** (SystemHistoryPage, SystemDetailPage, DashboardPage, ReportsPage + systems.service). shared/ đã chuẩn bị ở Phase 1. Sau phase này chuỗi Feature-Driven Architecture hoàn tất.

## 39. F7-PHASE 11/11 — SYSTEM/DASHBOARD/REPORTS (PHASE CUỐI) + TỔNG KẾT (2026-08-17)

> Bài học toàn bộ chuỗi đã áp dụng: build snapshot trước, Edit tool (KHÔNG PowerShell Get/Set-Content), `tsc -b`, không sự cố encoding (scan U+FFFD=0, không BOM).

### File đã di chuyển (5 file) — quyết định GỘP `features/system/`
| Cũ | Mới |
|---|---|
| `pages/DashboardPage.tsx` | `features/system/pages/DashboardPage.tsx` |
| `pages/ReportsPage.tsx` | `features/system/pages/ReportsPage.tsx` |
| `pages/SystemHistoryPage.tsx` | `features/system/pages/SystemHistoryPage.tsx` |
| `pages/SystemDetailPage.tsx` | `features/system/pages/SystemDetailPage.tsx` |
| `services/systems.service.ts` | `features/system/services/systems.service.ts` |
- **Lý do gộp 1 feature `system/`:** Dashboard/Reports/SystemDetail/SystemHistory đều là "trang tổng hợp/xem" thuộc khái niệm hiển thị hệ thống, không phải domain nghiệp vụ riêng biệt. Audit 7.5 đề xuất `system-dashboard/`, dùng `system/` gọn hơn. KHÔNG tách dashboard/reports/system riêng (mỗi loại chỉ 1 file page, tách thêm depth vô ích).
- `SystemInfoListPage.tsx` (features/admin/pages/, Phase 4) thuộc **Admin master-data** (QUẢN TRỊ sidebar) — KHÔNG phải System feature. `SystemDetailPage` (features/system/) là trang chi tiết điều hướng từ SystemInfoListPage — **2 khái niệm khác nhau**, đúng như audit.

### Bước 0.2 — Domain tiêu thụ (phase TIÊU THỤ nhiều domain nhất)
- `SystemDetailPage` (nơi tiêu thụ chính) import từ feature đã hoàn thành:
  - `features/maintenance/components/MaintenanceTable` ✅ (đã ở feature từ Phase 9)
  - `features/license/services/licenses.service` (licensesApi) ✅ (Phase 7+8)
  - `features/asset/types/asset` (ASSET_STATUS_*) ✅ (Phase 10)
  - `shared/components/ActionLogTable` + `LicenseUsageTable` ✅ (Phase 1)
  - Path sửa: `../features/...` → `../../<feature>/...` (vì SystemDetailPage giờ ở features/system/pages/ depth 3).
- `DashboardPage`/`ReportsPage`/`SystemHistoryPage`: chỉ import `api-client`/`theme`/`shared/ActionLogTable`/`hooks/usePermission` — **KHÔNG import trực tiếp từ feature khác** (dùng API calls).

### Bước 0.3+0.4 — Xác nhận chiều ngược (feature khác import System?)
- **KHÔNG feature nào (Asset/License/Component/Consumable/Accessory/Maintenance/User/Admin/Permission) import SystemDetailPage/DashboardPage/ReportsPage/systems.service** — chỉ App.tsx + nội bộ SystemDetailPage. → **9 domain đã hoàn thành trước KHÔNG bị ảnh hưởng import.**
- Sidebar/AppBar: dashboard summary là **API call** (`/dashboard/summary`), không phải import file — xác nhận rõ 2 loại phụ thuộc khác nhau, Sidebar/AppBar KHÔNG import file System.

### Verify UI thật (playwright-cli, admin) — 0 console errors
- **DashboardPage** (`/dashboard`): render y hệt trước move — KPI "Total Assets"/"Deployed"/"Low Stock"/"Total Value" + "Recent Activity" + "Assets by Status" (cột Status, cells 6/1/Pending/7/Deployed/5) + "Assets by Category" + "Low Stock Alerts". **Các vấn đề UI/UX đã biết (tiếng Anh, bug status "6") VẪN CÒN NGUYÊN — KHÔNG bị sửa nhầm** (đúng yêu cầu, để dành task riêng). Ảnh `f7p11-dashboard.png`.
- **ReportsPage** (`/reports`): tabs "Khấu hao"/"Kiểm kê"/"**Xuất CSV**" (gate export T11) + nút "Tải báo cáo". Ảnh `f7p11-reports.png`.
- **SystemDetailPage** (`/systems/36af433b...` AMHS): "MIR-AMH-001 — Hệ thống AMHS" + tabs Tài sản 1/Phụ kiện 4/Bảo trì 0/License 0 + ActionLogTable (log Cấp phát/Thu hồi) + **LicenseUsageTable (shared) tab License "Chưa có license"**. Ảnh `f7p11-systemdetail.png`.
- **SystemHistoryPage** (`/system-history`): dropdown "Chọn hệ thống..." → chọn AMHS → **ActionLogTable render log Cấp phát (2026-08-16) / Thu hồi (2026-08-14)**. Ảnh `f7p11-system-history.png`.

### Chung
- `npm run build` (tsc -b) → **0 lỗi TypeScript** (1 vòng sửa path — SystemDetailPage import 4 feature khác nhau, tất cả `../../<feature>/`).
- `scripts/audit-sweeps.ps1` → **exit 0** (0 violation cả 4 sweep).
- Không mojibake + không BOM mọi file (scan U+FFFD=0). Console 0 errors.

---

## 🎉 TỔNG KẾT TOÀN BỘ MỤC 7 — FEATURE-DRIVEN ARCHITECTURE 11/11 PHASE HOÀN THÀNH

### Cấu trúc `src/features/` cuối cùng (10 feature đã tạo):
```
src/features/
  accessory/    (Phase 2) — AccessoryListPage/DetailPage + Checkin/Checkout/FormModal + accessories.service
  admin/        (Phase 4) — 9 trang master-data phẳng pages/ (Category/Manufacturer/Supplier/AssetModel/Location/Depreciation/Company/Department/SystemInfo)
  asset/        (Phase 10) — AssetListPage/DetailPage + Edit/Archive/Recall/AllocationModal + AssetMaintenanceSection + asset.service + types/asset
  component/    (Phase 7) — ComponentListPage/DetailPage + ComponentFormModal + components.service
  consumable/   (Phase 1) — ConsumableListPage/DetailPage + Checkout/FormModal + consumables.service
  license/      (Phase 8) — LicenseListPage + Form/Detail/CheckoutModal + licenses.service
  maintenance/  (Phase 9) — MaintenanceListPage + MaintenanceTable/CompleteModal
  permission/   (Phase 3) — GroupListPage + PermissionMatrixPage + GroupFormModal + groups.service + types/groups
  system/       (Phase 11) — DashboardPage/ReportsPage/SystemHistoryPage/SystemDetailPage + systems.service
  user/         (Phase 6) — UserListPage/DetailPage + UserFormModal + types/users
```
### `shared/` + `components/common/` + `hooks/` (đúng những gì thực sự dùng chung):
- `shared/components/` — **ActionLogTable** (Asset/Component/SystemDetail/SystemHistory/...), **LicenseUsageTable** (AssetDetail/UserDetail/SystemDetail)
- `components/common/` — **CompanyTreeSelect** (UserFormModal/ComponentFormModal/LicenseFormModal/SystemInfoListPage/LocationListPage/DepartmentListPage/MaintenanceListPage/AssetEditModal)
- `hooks/` — **usePermission/usePermissionMap** (mọi feature), **useCurrentUser** (AppBar)
- `services/` — api-client, keycloak · `theme/` — designTokens/assetStatusColors/statusColors · `utils/` — format
- Cấu trúc mỗi feature chuẩn: `pages/` `components/` `services/` (+ `types/` nếu cần) — chỉ move + sửa import path, KHÔNG đổi logic trong toàn bộ 11 phase.

### Backlog #40 đóng ✅ — Mục 7 Feature-Driven Architecture HOÀN TẤT (11/11 phase, phiên 2026-08-17)

## 40. ĐỔI TÊN HIỂN THỊ "AspireReact" → "Mirats" + GỘP NÚT COLLAPSE VÀO HEADER SIDEBAR (2026-08-17)

### Bước 0 — Kết quả audit
- **"AspireReact" xuất hiện đúng 2 chỗ, cả 2 đều THUẦN HIỂN THỊ (trong `App.tsx`):**
  1. `App.tsx:268` — logo Sidebar desktop: `{collapsed ? 'AR' : 'AspireReact'}` (đã có logic collapse 'AR').
  2. `App.tsx:336` — logo Drawer mobile: `AspireReact`.
- `<title>` index.html là **"Aspire Starter"** (không chứa chuỗi "AspireReact") — đổi sang "Mirats" cho nhất quán thương hiệu. Favicon `/Aspire.png` là file hình (không phải chuỗi text) — giữ nguyên.
- **Xác nhận KHÔNG có giá trị kỹ thuật nào dùng "AspireReact"** (DB name `aspire-react-db`, project `aspire-react`, solution `aspire-react.sln`, realm Keycloak `aspire-react` là tên kỹ thuật — KHÔNG đổi, theo ràng buộc task). Grep src/index.html/public: chỉ 2 chỗ App.tsx.

### Thay đổi đã thực hiện
1. **Đổi tên hiển thị "Mirats":**
   - `App.tsx:268` logo Sidebar: `{collapsed ? 'AR' : 'AspireReact'}` → `{collapsed ? 'M' : 'Mirats'}`.
   - `App.tsx:336` drawer mobile: `AspireReact` → `Mirats`.
   - `index.html:7` `<title>Aspire Starter</title>` → `<title>Mirats</title>`.
   - KHÔNG đổi: DB name, project name, realm Keycloak (kỹ thuật).
2. **Gộp nút Collapse vào header Sidebar (App.tsx Sider):**
   - Logo div → `role="button"` clickable (click toggle `setCollapsed`), có `tabIndex` + Enter/Space key handler (a11y).
   - Mở rộng: hiện "Mirats" + icon `MenuFoldOutlined` (click icon cũng toggle, stopPropagation).
   - Thu gọn: chỉ hiện "M" (icon rút gọn), centered.
   - `minHeight: 48` (vùng bấm đủ lớn cho chạm mobile).
3. **Xóa nút Collapse cũ ở chân Sidebar** (block `position: absolute; bottom: 0`).
4. **Sider thêm `overflow-y: auto` + `height: 100vh` + `position: sticky; top: 0`** — submenu dài (Quản trị 10 mục) cuộn được, không còn bị che.

### Verify UI thật (playwright-cli, admin, từng bước — theo Frontend Verification Rules)
- Desktop mở rộng: logo **"Mirats"** đầy đủ + icon toggle. Ảnh `f7brand-sidebar-expanded.png`.
- Click logo → **thu gọn: logo "M"** (collapsed=true). Ảnh `f7brand-sidebar-collapsed.png`.
- Click lại → mở rộng "Mirats" trở lại (toggle 2 chiều OK).
- **Submenu "Quản trị" (10 mục):** `overflowY: auto`, scrollHeight 1116 > clientHeight 720 (scrollable=true) — không còn bị che. Ảnh `f7brand-sidebar-submenu-scroll.png`.
- **Title tab: "Mirats"** (Page Title xác nhận qua snapshot). Ảnh tab đã chụp trong screenshot.
- **Mobile 375px:** Sider ẩn + hamburger "Mở menu" → Drawer mở (a11y snapshot `dialog`) chứa logo **"Mirats"**. Ảnh `f7brand-mobile-drawer-mirats.png`.
- Console chỉ 2 antd deprecation warnings (Statistic valueStyle, Timeline items.children — pre-existing).
- ⚠️ Ghi nhận: Drawer trên playwright không hiện qua DOM selector (`offsetParent` null) nhưng a11y snapshot xác nhận dialog + logo "Mirats" hiển thị — do antd Drawer portal, không phải lỗi.

### Build / sweep
- `npm run build` (tsc -b) → **0 lỗi TypeScript** (xóa `MenuUnfoldOutlined` unused import — TS6133).
- `scripts/audit-sweeps.ps1` → **exit 0**. Grep "AspireReact" trong src/index.html = **0**.

## 41. FIX LỖI SIDEBAR SCROLL — SUBMENU "QUẢN TRỊ" CẮT CỤT + DOUBLE-SCROLL (2026-08-17)

### Nguyên nhân gốc (Bước 0)
- Task 40 thêm `overflowY: auto; height: 100vh` lên **Sider** — nhưng antd Sider bọc toàn bộ nội dung vào `.ant-layout-sider-children` (1 div riêng). Nên:
  1. Flex layout đặt trên Sider **không áp dụng** cho logo + menu (chúng nằm TRONG children) → menu không có `overflow-y: auto` riêng → submenu Quản trị dài (10 mục) tràn ra ngoài viewport → bị cắt cụt khi cuộn Sider.
  2. `height: 100vh` + overflow trên Sider tạo tầng cuộn riêng + trang có thể phát sinh thêm 1 tầng → **double-scroll**.

### Sửa đã thực hiện
1. **App.tsx Sider:** `style={{ height: '100vh', position: 'sticky', top: 0, display: 'flex', flexDirection: 'column', overflow: 'hidden' }}` — Sider cố định, không tự scroll, không tràn ra ngoài.
2. **App.tsx cấu trúc:** logo div `flexShrink: 0` (cố định trên cùng, không cuộn) + **menu wrapper** `div style={{ flex: 1, overflowY: 'auto', minHeight: 0 }}` — menu cuộn độc lập chiếm toàn bộ chiều cao còn lại.
3. **index.css:** thêm override `.ant-layout-sider .ant-layout-sider-children { display: flex; flex-direction: column; height: 100%; overflow: hidden; }` — vì antd bọc nội dung Sider vào children, flex column phải đặt lên children để logo + menu wrapper hoạt động đúng.

### Verify UI thật (playwright-cli, admin, từng bước)
- Mở đồng thời **Vật tư (3 mục) + Quản trị (10 mục)** → **21 mục menu đều trong DOM** (Dashboard + Vật tư tiêu hao/Linh kiện/Phụ kiện + Bản quyền/Tài sản/Bảo trì/Lịch sử/Báo cáo + Người dùng/Nhóm/Phân quyền + 10 mục Quản trị).
- Menu wrapper: `overflowY: auto`, scrollHeight **1192 > clientHeight 656** → cuộn được (scrollTop 536). **Không mục nào bị cắt** — cuộn xuống hết thấy đủ 10 mục Quản trị.
- **Chỉ 1 tầng cuộn:** `bodyScrollable: false` (trang tổng thể không scroll thừa). `.ant-layout-sider-children` overflow hidden.
- **Nút Collapse vẫn hoạt động:** click logo → collapsed + logo "M"; click lại → mở rộng "Mirats".
- Ảnh: `f7fix-sidebar-scrolled-full.png` (toàn Sidebar), `f7fix-menu-full.png`/`f7fix-menu-scrolled-bottom.png` (menu cuộn xuống đáy — đủ 10 mục Quản trị), `f7fix-sidebar-collapsed.png`, `f7fix-sidebar-expanded.png`.
- Console chỉ 2 deprecation warnings pre-existing.

### Build / sweep
- `npm run build` (tsc -b) → **0 lỗi TypeScript**.
- `scripts/audit-sweeps.ps1` → **exit 0**.

## 42. CẢI THIỆN DASHBOARD — VIỆT HÓA + FIX STATUS "6" + RECENT ACTIVITY + TOTAL VALUE (2026-08-17)

### Bước 0 — Audit
1. **Chuỗi tiếng Anh** trong `DashboardPage.tsx` (liệt kê đầy đủ): "Total Assets", "Deployed", "RTD", "Low Stock", "Total Value", "Recent Activity", "Assets by Status", "Status", "Count", "Assets by Category", "Category", "Low Stock Alerts", "Name", "Type", "Remaining" + giá trị enum "Pending"/"Deployed". Có `actionTypeLabels` tiếng Việt riêng (trùng ACTION_TYPE_TAGS — cần gỡ).
2. **Dòng status "6":** enum C# `AssetStatus {Pending=0, Deployed=1, Archived=2}` — **6 KHÔNG tồn tại** → dữ liệu rác (legacy StatusLabel cũ). API `assets-by-status` trả `g.Key.ToString()` → "6". **KHÔNG xóa DB** — xử lý tầng hiển thị.
3. **API `/dashboard/recent-activity` trả:** `Id, ItemType, ItemId, ActionType, Note, LogMeta, ActionDate, Creator` — **THIẾU ItemName** (tên đối tượng). Cần sửa backend bổ sung ItemName.
4. **Total Value:** `totalAssetValue` có tính toán THẬT (backend `Sum(PurchaseCost ?? 0)`) — không phải placeholder → giữ hiển thị, đổi sang VND.

### Sửa đã thực hiện
**Backend (`DashboardController.GetRecentActivity`):**
- Bổ sung resolve `ItemName` theo ItemType: query lookup dict từ Assets (Name + AssetTag), Consumables/Accessories/Components/Licenses/Users/SystemInfos (Name + Code)/AssetMaintenances (Title) → `ItemName = ResolveItemName(l)`.
- ⚠️ Lỗi gặp: `l.ItemId is Guid id` không match (ItemId là `Guid` value-type, `is Guid` luôn false) → sửa thành dùng `l.ItemId` trực tiếp. Cần **stop + start lại AppHost** (restart resource không load build mới).

**Frontend (`DashboardPage.tsx` — viết lại):**
- Việt hóa toàn bộ: "Tổng tài sản", "Đã cấp phát", "Sẵn sàng", "Sắp hết", "Tổng giá trị", "Hoạt động gần đây", "Tài sản theo trạng thái", "Trạng thái", "Số lượng", "Tài sản theo danh mục", "Danh mục", "Cảnh báo sắp hết", "Tên", "Loại", "Còn lại", "bởi".
- **Dùng đúng nguồn dùng chung (1 nguồn):** `ASSET_STATUS_LABELS` (types/asset) cho nhãn trạng thái; `assetStatusColors`/`statusColors.closed` (designTokens) cho màu; `ACTION_TYPE_TAGS` (shared/ActionLogTable) cho màu/label hành động qua map `ACTION_TYPE_VALUES` (string→int enum ActionType); `ITEM_TYPE_LABELS` (bảng ItemType→tiếng Việt). GỠ `actionTypeLabels` trùng.
- **Fix status "6":** `statusLabel()` trả `ASSET_STATUS_LABELS[status] ?? 'Không xác định'`; `statusColor()` fallback `statusColors.closed`. Dòng "6" → "Không xác định" (KHÔNG xóa DB).
- **Recent Activity:** hiển thị `ITEM_TYPE_LABELS[itemType]: itemName` (VD "Bảo trì: F7P69 Test Maintenance", "Bản quyền: DIAG-LIC3") + thời gian `formatDateTime` + màu Tag theo ACTION_TYPE_TAGS.
- **Total Value:** `formatMoney` (VND). Giữ thẻ (có tính toán thật).

### Verify UI thật (playwright-cli, admin) — 0 lỗi mới
- KPI: "Tổng tài sản" 13, "Đã cấp phát" 5, "Sẵn sàng" 7, "Sắp hết" 2, "Tổng giá trị" **0 VND** (formatMoney; asset hiện không có PurchaseCost nên 0 — không còn $). Ảnh `f7dash-dashboard-vietnamese.png`.
- **Bảng trạng thái:** "Không xác định" — 1 (dòng "6" rác → nhãn thay số thô) + "Chờ cấp phát" — 7 + "Đã cấp phát" — 5. Màu: Không xác định `#8c8c8c` (closed), Chờ cấp phát `#1677ff`, Đã cấp phát `#52c41a`. Ảnh `f7dash-status-fixed.png`.
- **Recent Activity:** tên đối tượng đầy đủ ("Xóa Bảo trì: F7P69...", "Thu hồi Bản quyền: DIAG-LIC3") + màu Tag đúng ACTION_TYPE_TAGS (Xóa đỏ #cf1322, Tạo mới xanh #389e0d, Thu hồi tím, Cấp phát cam, Cập nhật xanh dương). Ảnh `f7dash-recent-activity.png`.
- Bảng "Tài sản theo danh mục" (Laptop/Phụ kiện/Linh kiện) + "Cảnh báo sắp hết" (Loại đã Việt hóa). Console chỉ 2 deprecation warnings pre-existing.
- ⚠️ Ghi nhận: vài dòng Recent Activity đầu (Xóa/Tạo mới gần nhất) không có ItemName vì item đã bị xóa khỏi DB (không resolve được) — hành vi đúng, không phải lỗi.

### Build / sweep
- Backend `dotnet build` → 0 lỗi. Frontend `npm run build` (tsc -b) → 0 lỗi TS. `audit-sweeps.ps1` → exit 0.
- AppHost đã stop + start lại (để load backend mới). API + FE đang chạy.

## 43. ĐIỀU TRA REGRESSION SIDEBAR — SUBMENU "QUẢN TRỊ" (KẾT LUẬN: KHÔNG CÓ REGRESSION CODE) (2026-08-17)

### Bước 0 — Phân tích
1. **Task Dashboard chỉ đụng 2 file:** `DashboardPage.tsx` (frontend) + `DashboardController.cs` (backend). **KHÔNG đụng `App.tsx`/`index.css`/bất kỳ file Sidebar nào.** Timestamp xác nhận: App.tsx (9:27 PM) + index.css (9:37 PM) sửa ở task Sidebar TRƯỚC; DashboardPage.tsx (9:52 PM) sau — không chồng file.
2. Task Dashboard có **restart toàn bộ AppHost** (để load backend mới) — đây là thay đổi môi trường duy nhất liên quan.

### Root cause (đã test UI thật, playwright-cli admin)
- **KHÔNG có regression code.** Submenu "Quản trị" hoạt động ĐÚNG:
  - **Expanded:** click "Quản trị" → mở **9 mục inline** (Danh mục, Nhà SX, Nhà cung cấp, Asset Models, Địa điểm, Khấu hao, Công ty, Phòng ban, Hệ thống) + cuộn được (scrollHeight 1052 > clientHeight 656). Ảnh `f7reg-quantri-expanded.png`, `f7reg-final-quantri-open.png`.
  - **Collapsed:** click "Quản trị" → submenu hiển thị dạng **POPUP float** (hành vi antd Menu chuẩn khi collapsed — `submenuOpen: 0` + `popupCount: 1`), KHÔNG phải inline dưới mũi tên.
- **Nguyên nhân khả dĩ cho ảnh báo lỗi:** ảnh chụp lúc Sidebar **COLLAPSED** (submenu = popup float, không thấy nội dung dưới mũi tên → tưởng "không mở được"), HOẶC ảnh chụp trong lúc **AppHost restart** (task Dashboard restart backend, trang load dở), HOẶC HMR/dev server giữ state cũ tạm thời.
- **Lưu ý số lượng mục:** submenu "Quản trị" có **9 mục** (không phải 10 như task trước nhắc — "Phân quyền" thuộc group HỆ THỐNG, ngoài QUẢN TRỊ). Đây là cấu trúc đúng từ trước.

### Verify toàn bộ hành vi Sidebar (Bước 0.4 — không regression khác)
- **Submenu Quản trị:** mở đủ 9 mục inline (expanded) + popup (collapsed) ✅
- **Vật tư + Quản trị mở đồng thời:** 2 submenu open, 21 mục tổng, menu cuộn, body không scroll thừa ✅
- **Collapse/expand:** click logo → "M" / "Mirats" ✅
- **Active-state:** vào `/assets` → menu "Tài sản" selected ✅
- **Badge Low Stock:** superscript "2" trên menu Vật tư ✅
- **Console:** chỉ deprecation warning pre-existing.

### Kết luận
Không sửa code (không có gì để sửa). Nếu user vẫn thấy lỗi: hard refresh (Ctrl+Shift+R) để xóa cache/HMR cũ, hoặc đảm bảo Sidebar ở trạng thái expanded khi kiểm tra submenu.

### Build
- `npm run build` (tsc -b) → **0 lỗi TypeScript**.

## 44. FIX SIDEBAR CUỘN TRÔI THEO TRANG — `position:sticky` KHÔNG NEO (ROOT CAUSE: `overflow-x:hidden` TRÊN BODY TẠO SCROLL CONTAINER TRUNG GIAN) (2026-08-17)

### Triệu chứng (xác nhận bằng ảnh + số thật)
Khi cuộn trang xuống để xem nội dung Dashboard phía dưới, TOÀN BỘ Sidebar (logo "Mirats", nhóm TỔNG QUAN/NGHIỆP VỤ, nút Collapse) cuộn trôi lên và biến mất khỏi viewport — chỉ còn phần dưới QUẢN TRỊ + submenu. Sider có `position: sticky; top: 0` nhưng KHÔNG neo.

### Điều tra theo thứ tự (loại trừ từng giả thuyết)
1. **Thứ tự import CSS (LOẠI):** `main.tsx` CHỈ import `./index.css`. **App.css KHÔNG được import ở đâu cả** (grep toàn bộ `frontend/` ra "No files found") — file CSS "chết", các class (weather-card/counter-value/app-footer) không dùng trong src. Không thể ghi đè override.
2. **Override index.css vẫn đúng (XÁC NHẬN):** `.ant-layout-sider .ant-layout-sider-children` có đầy đủ display:flex/flex-direction:column/height:100%/overflow:hidden. antd v6.5.3 vẫn dùng class `ant-layout-sider-children` (Sider.js line 183) và style mặc định chỉ set `height:100%` (không đụng display/overflow) → không xung đột.
3. **Computed style thật (ROOT CAUSE LỘ DIỆN):**
   - `html`: `overflow: hidden auto` + `height: 720px` → html là scroll container document ✓
   - `body`: `overflow: hidden auto` (do `overflow-x: hidden` khiến `overflow-y` tính thành `auto` — CSS spec) + `height: 1081px` → **body TRỞ THÀNH scroll container trung gian** giữa Sider và html
   - Sider `position: sticky; top: 0` nhưng scroll container gần nhất là BODY (không phải viewport) → sticky neo vào body, body bị cuộn theo html → sticky "chết", Sider trôi theo trang
   - **Đo trước fix:** cuộn 200px → `siderTop: -200` (trôi). **Sau khi inject `overflow-x: clip`:** cuộn 200px → `siderTop: 0` (đứng yên). Bằng chứng sống.

### Fix
- `index.css`: đổi `overflow-x: hidden` → `overflow-x: clip` trên **cả `html` VÀ `body`**. `clip` clip overflow KHÔNG tạo scroll container (khác `hidden` tính overflow-y thành auto) → sticky dính vào viewport (html/document) đúng.
- Chỉ sửa CSS này — KHÔNG đổi cấu trúc Layout/Sider/submenu (đã đúng).

### Verify UI thật (playwright-cli, admin, sau fix)
- **Computed style:** html `overflow: clip visible`, body `clip visible`, body overflow-y = visible (hết scroll container trung gian), scrollContainer = HTML ✓
- **(a) Submenu nội bộ cuộn đúng:** mở Quản trị → 9 mục; wrapper scrollH 1052 > clientH 656 (viewport 720) / 436 (viewport 500) → scrollable ✓; set scrollTop 200/250 hoạt động ✓
- **(b) Sidebar cố định khi trang cuộn:**
  - Viewport 720: cuộn scrollY 200 → `siderTop: 0, siderBottom: 720`; cuộn max (scrollY 361) → logo top 0 visible, menu top 64 ✓
  - Viewport 500 (Dashboard dài 1081): cuộn tới đáy scrollY 581 → `siderTop: 0, siderBottom: 500 = viewport`, logoTop 0 visible, `siderFullyVisible: true` ✓
- Submenu Quản trị vẫn 9 mục, active-state/collapse/badge không đổi.
- Ảnh: `f7sticky-before-scroll200.png` (trước — Sidebar trôi khi cuộn), `f7sticky-after-scroll200.png`, `f7sticky-after-deep-scroll.png`, `f7sticky-after-viewport500-bottom.png`, `f7sticky-final-submenu-open.png`, `f7sticky-final-inner-scrolled.png` (sau — Sidebar dính + scroll nội bộ).

### Build
- `npm run build` (tsc -b + vite) → **0 lỗi TypeScript**.

### Kết luận
Root cause là `overflow-x: hidden` trên body (kèm html) tạo scroll container trung gian làm gãy `position: sticky`. Đổi sang `overflow-x: clip` khôi phục sticky — Sidebar đứng yên cố định bất kể Content cuộn bao xa, đồng thời scroll nội bộ submenu vẫn hoạt động. Không ảnh hưởng submenu/active-state/badge/collapse.

## 45. GIỚI HẠN ĐỘ RỘNG CỘT "CHI TIẾT" Ở SYSTEMHISTORYPAGE + LÀM RÕ NGUỒN GỐC 2 ĐỊNH DẠNG DÒNG (2026-08-17)

### Triệu chứng
Bảng lịch sử hệ thống (SystemHistoryPage) có cột "Chi tiết" tràn quá dài (~1001px), bị cắt bởi mép bảng, phải cuộn ngang mới đọc hết. Các cột khác hiển thị gọn. Ngoài ra 2 dòng dữ liệu hiển thị 2 định dạng khác nhau: dòng thô (`checkoutType: SystemPosition · targetId: 5f7ff94b-...`) vs dòng format đẹp (`Vị trí: Kho Trung Tâm · Trạng thái: Chờ cấp phát → Đã cấp phát...`).

### Bước 0 — Điều tra
1. **Cột "Chi tiết" nằm trong `ActionLogTable` (shared component)** — `features/system/pages/SystemHistoryPage.tsx` render qua `<ActionLogTable>`. Cột có `ellipsis: true` nhưng **THIẾU `width`**.
2. **Nguồn gốc 2 định dạng = 2 LOẠI ACTION LOG KHÁC NHAU (KHÔNG phải log cũ vs mới, KHÔNG phải bug hiển thị):**
   - **Asset** checkout/checkin/update (`CheckoutAssetCommand.cs:177-186`, `CheckinAssetCommand.cs:81`): ghi `logMeta` dạng `{ changes: { status: {old,new}, location_id: {...}, ... } }` → `formatLogDetail` (ActionLogTable.tsx) xử lý nhánh `changes` → format đẹp ("Vị trí: ... · Trạng thái: ...").
   - **Accessory** checkout/checkin (`CheckoutAccessoryCommand.cs:131`, `CheckinAccessoryCommand.cs:70-77`): ghi `logMeta` dạng **top-level raw** `{ quantity, checkoutType, targetId }` / `{ returnQty, assignedQty, ... }` → `formatLogDetail` xử lý nhánh else (top-level metadata) → hiển thị raw keys → **đây chính là dòng thô**.
   - Xác nhận bằng data thật: các dòng thô (8/16, 8/14, 8/13) có `itemName: "-"` (không gắn asset — là Accessory), các dòng đẹp (8/11, 8/9) có `itemName: "AST-001 - Laptop HP"` (Asset).
   - **Kết luận:** không sửa data (ActionLog lịch sử giữ nguyên) — chỉ sửa hiển thị.

### Root cause tràn (bằng chứng số thật)
- Cột "Chi tiết" có `ellipsis: true` nhưng KHÔNG có `width`, và ProTable dùng `scroll={{ x: 'max-content' }}`.
- Với `x: 'max-content'`, antd set table `width: max-content` → table giãn theo cell rộng nhất (text dài không wrap, `white-space: nowrap` do ellipsis) → cột Chi tiết giãn tới **1001px** dù `<col width:320px>` đã khai báo (colgroup bị bỏ qua).
- Test sống: thêm `table-layout: fixed` + `width: 1210px` (tổng width các cột) → `thWidths: [160,130,180,200,320,220]`, cell Chi tiết = **320px**, `cellClientW 320 < cellScrollW 841` → ellipsis hoạt động. Bằng chứng xác nhận fix.

### Fix (ActionLogTable.tsx — shared, áp dụng cho cả SystemHistory + AssetDetail)
1. Cột "Chi tiết": thêm `width: 320`.
2. Thêm `tableLayout="fixed"` cho ProTable (ép table-layout fixed để width column được tôn trọng).
3. Đổi `scroll={{ x: 'max-content' }}` → `scroll={{ x: totalWidth || 'max-content' }}` với `totalWidth` = tổng `col.width` các cột (160+130+180+200+320+220 = 1210 cho SystemHistory; 990 cho AssetDetail) — bảng vẫn scroll-x khi viewport hẹp, nhưng không còn giãn theo nội dung.
- `extraColumns` (VD "Tài sản" width 220) được cộng vào totalWidth tự động.

### Verify UI thật (playwright-cli admin, sau fix)
- **SystemHistoryPage:** `tableStyleWidth: 1210px`; cột Chi tiết = **320px** (trước 1001px); mọi cell dữ liệu `cellClientW: 320`, `cellScrollW: 601-1002` (nội dung dài hơn), `overflow: hidden, textOverflow: ellipsis, whiteSpace: nowrap` → cắt gọn + "..." ✓; cell có `title` attribute chứa đầy đủ nội dung (tooltip native khi hover) ✓; scroll-x: 1210 > 916 (viewport) → scrollable ✓.
- **AssetDetailPage (trang khác dùng chung ActionLogTable):** cột Chi tiết = 320px, không vỡ; bảng Lịch sử 5 cột tổng 990px, scroll-x hoạt động ✓.
- Ảnh: `f7detail-after-width320.png`, `f7detail-after-fix.png`, `f7detail-after-hover.png`, `f7detail-final.png` (sau — cột 320px, ellipsis, không tràn). Không có ảnh trước riêng (ảnh lỗi do user gửi + đo số 1001px).

### Build
- `npm run build` (tsc -b + vite) → **0 lỗi TypeScript**.

### Kết luận
Cột "Chi tiết" tràn là do thiếu `width` + `scroll x: max-content` bỏ qua colgroup width (giãn theo nội dung). Fix: `tableLayout="fixed"` + `width: 320` + `scroll x` bằng tổng width cột. 2 định dạng dòng là 2 loại entity khác nhau (Accessory log raw vs Asset log changes) — KHÔNG sửa data, chỉ sửa hiển thị. Không đụng logic ghi ActionLog, không đụng các cột khác.

## 46. BỔ SUNG ITEMNAME + FORMAT ĐẸP CHO ACCESSORY CHECKOUT/CHECKIN LOGS (2026-08-18)

### Bối cảnh
Task 45 xác nhận Accessory logs ghi logMeta dạng raw top-level (quantity/checkoutType/targetId) và itemName "-" ở SystemHistoryPage (khác Asset logs có changes + itemName). Task này nâng cấp Accessory checkout/checkin để log mới có logMeta.changes + itemName đúng.

### Bước 0 — Điều tra (xác nhận cơ chế)
1. **`itemName` KHÔNG phải field lưu trong ActionLog** — được resolve ĐỘNG ở API query time: ActionLogsController `/by-system` line 252 gọi `assetNames.GetValueOrDefault(log.ItemId)` (chỉ bảng Assets) → Accessory ItemId không match → "-". Đây là root cause itemName "-".
2. **CheckoutAccessoryCommand:131** ghi `logMeta = { quantity, checkoutType, targetId }` (raw); **CheckinAccessoryCommand:70** ghi `{ returnQty, assignedQty, totalReturned, remaining, checkoutId }` (raw). Khác CheckoutAssetCommand:177 ghi `{ changes: { status: {old,new}, ... } }` (đẹp).
3. **formatLogDetail (ActionLogTable.tsx)** xử lý nhánh `changes` → format đẹp tự động; nhánh else (top-level) → raw. Nếu logMeta Accessory có `changes`, hiển thị đẹp tự áp dụng.

### Fix
1. **ActionLogsController `/by-system`:** resolve itemName theo ItemType (không chỉ Asset) — thêm lookup Accessories/Consumables/Components/Licenses + `ResolveItemName(itemType, itemId)` switch. (Mẫu: DashboardController.ResolveItemName)
2. **CheckoutAccessoryCommand:** logMeta đổi sang `{ changes: { quantity: {old: remaining, new: checkedOut+request.Quantity}, checkout_type: {old: null, new: CheckoutType} } }`.
3. **CheckinAccessoryCommand:** logMeta đổi sang `{ changes: { return_qty: {old: null, new: ReturnQty}, quantity: {old: remainingOut+ReturnQty, new: remainingOut} } }`.
4. **Frontend ActionLogTable:** bổ sung CHANGE_LABELS cho `quantity: 'Số lượng'`, `checkout_type: 'Loại cấp phát'`, `return_qty: 'Số lượng trả lại'` + CHECKOUT_TYPE_LABELS (User/Department/Location/SystemPosition) + fmt `checkout_type`.
5. **Frontend AccessoryDetailPage:** formatter "Chi tiết / Ghi chú" (line 171-201) trước đây parse raw top-level → sửa hỗ trợ CẢ 2 dạng: `changes` (mới) + legacy raw (log cũ vẫn hiển thị).
6. **Test:** AccessoryTests.Checkin_PartialReturn_DecreasesRemainingOut_AndLogsCheckin assert `"returnQty"` → đổi assert `"return_qty"` + `"changes"`.

### Verify (dotnet test + UI thật, playwright-cli admin)
- **dotnet test: 283/283 PASS** (1 test cập nhật).
- **Checkout tới User (S1 Test):** AccessoryDetailPage Lịch sử hoạt động → log mới hiện `f7item checkout test · SL: 21 · Loại: User` (format đẹp). Ảnh `f7item-log-new-format.png`.
- **Checkin:** log mới hiện `Đã trả: 1 · SL: 0`. Ảnh `f7item-checkin-new-format.png`.
- **SystemHistoryPage itemName:** Accessory logs cũ (8/16, 8/14, 8/13) giờ hiện "Chuột HP"/"ST3-ACC-FREE" ở cột "Tài sản" (trước "-"). Ảnh `f7item-systemhistory-itemname.png`.
- **Checkout tới SystemPosition AMHS:** SystemHistoryPage log mới hiện `Hệ thống: Hệ thống AMHS · Số lượng: 20 → 21 · Loại cấp phát: Vị trí hệ thống · f7item system-position checkout` + itemName "Chuột HP" — format changes đẹp hoàn toàn, không còn raw. Ảnh `f7item-systemhistory-new-log.png`.
- **Log cũ (raw) vẫn hiển thị nguyên trạng** ("SL: 1 · Loại: SystemPosition", "Đã trả: 1 · Còn: 0" — nhánh legacy AccessoryDetailPage), không vỡ ✓.
- Dọn test data: 2 test checkout đã checkin hoàn tác, Chuột HP Còn lại về 20, "Đang cấp phát" 4→3.
- **npm run build: 0 lỗi TS.**

### Kết luận
Accessory checkout/checkin logs giờ ghi logMeta.changes (như Asset) + itemName được resolve theo ItemType ở `/by-system`. Log MỚI hiển thị format đẹp (Số lượng/Loại cấp phát/Số lượng trả lại), log CŨ (raw) vẫn nguyên trạng không vỡ. Không sửa dữ liệu ActionLog lịch sử.

---

## T7-ĐÓNG — Đồng bộ CompanyTreeSelect cho 2 filter còn sót (LicenseListPage + ComponentListPage)

### Bối cảnh
T7 đã đồng bộ CompanyTreeSelect cho 4 vị trí form Tạo/Sửa (SystemInfoListPage,
LocationListPage, DepartmentListPage, UserFormModal). Còn 2 vị trí filter danh sách
dùng `<Select>` phẳng chưa xử lý: LicenseListPage + ComponentListPage.

### Bước 0 — Xác nhận hiện trạng (grep + đọc code thật)
- `features/license/pages/LicenseListPage.tsx` — company filter dùng `<Select>` phẳng
  (lines 221-231 cũ) + `companyOptions` từ `/companies` flat. **Chưa dùng CompanyTreeSelect.**
- `features/component/pages/ComponentListPage.tsx` — company filter dùng `<Select>` phẳng
  + có pseudo-option UNCOMPANIED `__uncompanied__` ("Chưa xác định công ty").
- `components/common/CompanyTreeSelect.tsx` — vẫn ở vị trí shared cũ, chưa bị move.

### Sửa
1. **LicenseListPage.tsx:** thay `<Select>` phẳng → `<CompanyTreeSelect>`; bỏ
   `companyOptions` state + `/companies` fetch (CompanyTreeSelect tự tải tree). Giữ
   `companyId` state + `buildParams` (server-side filter).
2. **ComponentListPage.tsx:** thay `<Select>` phẳng → `<CompanyTreeSelect>`; bỏ
   `companyOptions` state + `/companies` fetch. **Giữ UNCOMPANIED** qua prop mới
   `extraRootOption={{ label: 'Chưa xác định công ty', value: UNCOMPANIED }}`.
3. **CompanyTreeSelect.tsx:** thêm prop `extraRootOption` (pseudo-node gốc, không con,
   hiển thị TRÊN CÙNG tree; value truyền nguyên qua onChange) — backward-compatible,
   các consumer cũ không cần đổi.

### Verify (UI thật — playwright-cli, user test qa-t7-* tạo riêng rồi xóa)
- **License — dropdown công ty:** hiện tree đầy đủ (parent "Công ty Cổ phần ABC" +
  child "Công ty Quản lý bay miền Trung" dưới parent "Tổng công ty Quản lý bay Việt Nam").
- **License — filter parent:** chọn "Công ty Cổ phần ABC" → chỉ 3 licenses
  (DIAG-LIC4, License OEM, Windows Pro DEMO). Ảnh `t7-license-filter-abc.png`.
- **License — filter child:** chọn child "Công ty Quản lý bay miền Trung" → "Không có bản
  quyền" (0 mục, đúng vì child không có license). Ảnh `t7-license-filter-child-empty.png`.
- **Component — dropdown công ty:** hiện tree + "Chưa xác định công ty" (UNCOMPANIED) ở đầu.
- **Component — filter child:** chọn "Công ty Quản lý bay miền Trung" → chỉ "Ổ cung sdd 10GB"
  (company hiển thị đúng trên card). Ảnh `t7-component-filter-child.png`.
- **Component — filter UNCOMPANIED:** chọn "Chưa xác định công ty" → chỉ RAM 16GB + SSD 512GB
  (2 components không công ty), "Ổ cung sdd 10GB" bị lọc ra. Ảnh `t7-component-filter-uncompanied.png`.
- Console: chỉ 3 warning antd v6 deprecation pre-existing (dropdownRender/destroyOnClose/maskClosable),
  không liên quan thay đổi này.
- **npm run build: 0 lỗi TypeScript.**
- Dọn: user test qa-t7-024030 đã xóa khỏi Keycloak (DELETE 204) + local users table (DELETE 1).

### Kết luận
Cả 2 filter danh sách giờ dùng CompanyTreeSelect, chọn được công ty cha + con, lọc đúng
dữ liệu theo company (bao gồm cả UNCOMPANIED của Component). Đóng nốt 2 điểm CompanyTreeSelect
còn tồn đọng từ T7 — toàn bộ công việc đồng bộ CompanyTreeSelect trong dự án HOÀN TẤT.

---

## 47. RESET VOLUME DEV — TẠO MÔI TRƯỜNG SẠCH (2026-08-19)

> Thực hiện đúng yêu cầu "CẢNH BÁO QUAN TRỌNG" — chỉ xóa 2 volume dev Aspire (`postgres-data`, `keycloak-data`), KHÔNG đụng `snipeit_*` (compose prod).

### 0. Xác nhận trước khi xóa

- `docker volume ls` TRƯỚC khi xóa (2026-08-19 09:46, trước `docker volume rm`):
  ```
  DRIVER    VOLUME NAME
  local     postgres-data          ← sẽ xóa
  local     keycloak-data          ← sẽ xóa
  local     snipeit_snipeit-db-data     ← GIỮ NGUYÊN
  local     snipeit_snipeit-storage     ← GIỮ NGUYÊN
  local     13cf722e... , 26ea5383... , ... (12 volume hash Aspire)
  local     aspire-starter.apphost-4a24d95887-postgres-data
  ```
  Xác nhận đúng tên `postgres-data` và `keycloak-data` (không prefix `mirats-`), `mirats-*` không tồn tại, `snipeit_*` riêng biệt.
- Ghi lại cổng Keycloak dev cũ trước khi dừng: container `keycloak-twpvcyak` (Exited, 127.0.0.1:32774->8443) — cổng cũ không còn dùng sau reset.
- `docker volume inspect postgres-data` CreatedAt `2026-08-07T00:22:03Z`, `keycloak-data` `2026-08-08T01:40:18Z` — đúng volume dev lâu ngày, chứa dữ liệu QCR rác.

### 1. Backup phòng hờ

- Khởi `postgres-fpghapsr` (Up 127.0.0.1:55784->5432, password `<redacted>` từ `docker inspect`), chạy:
  `docker exec -e PGPASSWORD=... postgres-fpghapsr pg_dump -U postgres -d aspire-react-db --no-owner --no-privileges > docs/sql/backups/backup-before-reset-20260819.sql`
- Kết quả: **`docs/sql/backups/backup-before-reset-20260819.sql` — 362 217 bytes** (2026-08-19 09:46:04 AM), dump UTF-8 đầy đủ (header `PostgreSQL database dump`, `\restrict ...`). Lưu lưới an toàn, không khôi phục.
- Nội dung backup xác nhận rác cũ: `COPY public.users` 14 user (admin, nkien, demo.user, st1verify…), `accessories`/`consumables` chứa `QCR-ACC-*`, `permission_groups` 2, `users` 1 admin superuser cũ — sẽ bị xóa sạch.

### 2. Dừng stack dev

- `docker stop postgres-fpghapsr` → Exited (0). Các container dev còn lại đã Exited: `keycloak-twpvcyak`, `cache-xqfctnnx`, `pgadmin-vftpkmdp`.

### 3. Xóa 2 volume (KHÔNG dùng prune)

- Lệnh `docker volume rm postgres-data keycloak-data` lần đầu FAIL `volume is in use` do container cũ còn reference (`86ac27e9` postgres-urbmnxgn, `7a50cf0b` keycloak-xwmevjjr…). Đã `docker rm` 11 container cũ: `postgres-{urbmnxgn,ykrxnhmy,jjcsrzhp,smwxcfda,qbxmsavn,fpghapsr}` + `keycloak-{xwmevjjr,pcfuqmav,yajuwqbc,ucfkyegc,twpvcyak}`.
- `docker volume rm postgres-data` → `postgres-data` ✅, `keycloak-data` → `keycloak-data` ✅, EXIT 0.
- `docker volume ls` SAU khi xóa (2026-08-19 09:47):
  ```
  DRIVER    VOLUME NAME
  local     snipeit_snipeit-db-data     ← VẪN CÒN
  local     snipeit_snipeit-storage     ← VẪN CÒN
  (không còn postgres-data / keycloak-data)
  ```
  Xác nhận `mirats-*` (nếu có) không bị ảnh hưởng — chỉ `snipeit_*` (prod compose) giữ nguyên.

### 4. Khởi động lại sạch

- `dotnet run --project aspire-react.AppHost` (background job pwsh-1, .NET 10.0.103, Aspire 13.4.6, builder `postgres-nqgepbfq`/`keycloak-gqtdrnrd`/`cache-ngfpwbjf`).
- Postgres mới `postgres-nqgepbfq` Up 127.0.0.1:63914->5432 (password mới `<redacted>`), log init `PostgreSQL init process complete; ready for start up.` + `database "aspire-react-db" does not exist` → EF Core `Migrate()` tạo mới.
- Keycloak mới `keycloak-gqtdrnrd` Up 127.0.0.1:63966->8443 / 63967->9000, log:
  ```
  Initializing database schema. Using changelog META-INF/jpa-changelog-master.xml
  Importing from directory /opt/keycloak/bin/../data/import
  KC-SERVICES0030: Full model import requested. Strategy: IGNORE_EXISTING
  Realm 'aspire-react' imported
  Import finished successfully
  Created temporary admin user with username admin
  Keycloak 26.6.4 on JVM ... Listening on: http://0.0.0.0:8080 and https://0.0.0.0:8443. Profile dev activated.
  ```
  → volume rỗng nên import SẼ chạy (khác "already exists. Import skipped" trước đây). Realm skeleton không có user.
- `docker volume ls` SAU khi khởi lại: `postgres-data` CreatedAt `2026-08-19T02:48:24Z`, `keycloak-data` `2026-08-19T02:48:32Z` (mới), `snipeit_*` CreatedAt `2026-08-06T10:48:57Z` KHÔNG đổi.
- DB verify: `__EFMigrationsHistory` 2 dòng `20260814135409_InitialBaseline` + `20260815032332_LicenseSeatSystemInfoTarget` (ProductVersion 10.0.10) — Migrate() chạy từ đầu. `pg_stat_user_tables` 34 bảng.

### 5. Seed lại tài khoản admin

- Lấy cổng động: `docker port keycloak-gqtdrnrd` → `8443/tcp -> 127.0.0.1:63966` (KHÔNG phải 8080 cố định).
- Chạy (không có `.env` — dev dùng bootstrap mặc định):
  ```
  powershell -File scripts/seed-initial-admin.ps1 -KeycloakUrl https://localhost:63966
  ```
  ENV: `INITIAL_ADMIN_USERNAME=admin`, `INITIAL_ADMIN_EMAIL=admin@aspire-react.local`, `INITIAL_ADMIN_PASSWORD=<redacted>`, `KC_BOOTSTRAP_ADMIN_USERNAME=admin`, `KC_BOOTSTRAP_ADMIN_PASSWORD=<redacted>`
- Kết quả (EXIT 0, https self-signed bypass auto qua Git curl `-k`):
  ```
  Seeding initial admin 'admin' (admin@aspire-react.local) into realm 'aspire-react' via https://localhost:63966 ...
    Admin token obtained.
    User 'admin' created (id: f2f97fb2-e930-49b4-87bc-f4472b7bc250).
    Password set for 'admin'.
    Realm role 'admin' assigned to 'admin' (IsSuperUser on first login).
  Done. User 'admin' is ready - log in at the app to trigger JIT local provisioning.
  ```
  Script đã fix hỗ trợ HTTPS self-signed (PS 5.1 fallback Git curl) hoạt động đúng — không lỗi cert `SEC_E_NO_CREDENTIALS`.

### 6. Xác nhận trạng thái sạch + đăng nhập được

- **Đăng nhập:** `POST https://localhost:8080/realms/aspire-react/protocol/openid-connect/token` (frontend client `frontend`, grant password `admin/<redacted>`) → 200, `access_token` 1530 chars, payload `realm_access.roles: ["offline_access","admin",...]`, `preferred_username: admin`. Thử cả `https://localhost:63966` cũng 200 — frontend `VITE_KEYCLOAK_URL || https://localhost:8080` khớp proxy 8080.
- **GET /api/v1/users/me** (Bearer token, `http://localhost:5428/api/v1/users/me`) → 200:
  ```json
  { "id": "5bd5e4f9-06bf-4fe7-b53d-d3eb4add011a", "username": "admin", "email": "admin@aspire-react.local", "isSuperUser": true, "isActive": true, "companyId": null }
  ```
  → `isSuperUser: true` xác nhận JIT đã gán role `admin`.
- **DB sạch (0 rác QCR-*, 0 dữ liệu nghiệp vụ):** `psql ... -f /tmp/qcr_final.sql` → `companies_QCR 0, categories_QCR 0, assets_QCR 0, accessories_QCR 0, components_QCR 0, consumables_QCR 0, companies_all 0`. `pg_stat_user_tables`:
  ```
  __EFMigrationsHistory 2 | accessories 0 | accessory_checkouts 0 | action_logs 0 | asset_maintenances 0 | assets 0 | categories 0 | companies 0 | components 0 | consumables 0 | licenses 0 | manufacturers 0 | suppliers 0 | locations 0 | users 1 | permission_groups 2 | group_permissions 160 | (còn lại 0)
  ```
  Chỉ còn `users 1` (admin vừa seed), `permission_groups 2` + `group_permissions 160` là catalog mặc định, `__EFMigrationsHistory 2`. Đã sạch 19 công ty QCR-CO, categories, checkouts đã báo cáo.
- **API list:** `/assets`, `/licenses`, `/accessories`, `/components`, `/consumables`, `/companies` → `totalItems: 0`; `/users` → 1 (admin). `GET /dashboard/summary` → `{ totalAssets:0, deployedAssets:0, rtdAssets:0, overdueAudits:0, archivedAssets:0, lowStockCount:0, totalAssetValue:0.0 }` — Dashboard "chưa có dữ liệu", không lỗi console.
- **KHÔNG còn dữ liệu rác QCR-*:** lợi ích phụ đã đạt — backup cũ có 70 accessories QCR, 70 consumables QCR… giờ 0.
- **Tài khoản admin MỚI để đăng nhập từ giờ:** `admin / <redacted>` (username `admin`, email `admin@aspire-react.local`, local id `5bd5e4f9-06bf-4fe7-b53d-d3eb4add011a`, Keycloak id `f2f97fb2-e930-49b4-87bc-f4472b7bc250`). Realm `aspire-react`, client `frontend`. Keycloak dev HTTPS động `https://localhost:63966` (lấy qua `docker port keycloak-gqtdrnrd`), browser proxy `https://localhost:8080` vẫn hoạt động (Vite mặc định).

### Bằng chứng bắt buộc (tóm tắt)

- **docker volume ls TRƯỚC/SAU:** TRƯỚC có `postgres-data`, `keycloak-data`, `snipeit_snipeit-db-data/storage`; SAU khi `rm` mất 2 volume dev, `snipeit_*` còn nguyên; sau `AppHost run` 2 volume dev tái tạo với CreatedAt mới 2026-08-19, `snipeit_*` CreatedAt 2026-08-06 không đổi.
- **File backup:** `docs/sql/backups/backup-before-reset-20260819.sql` (362 217 bytes).
- **Log seed:** `Admin token obtained → User 'admin' created (id: f2f97fb2...) → Password set → Realm role 'admin' assigned → Done` (EXIT 0) tại `https://localhost:63966`.
- **Đăng nhập + isSuperUser:** token 200 + `GET /users/me` → `isSuperUser: true`.
- **DB = 0:** assets/licenses/accessories/components/consumables/companies/categories/action_logs = 0; QCR-* = 0; dashboard summary 0.
- **Dashboard:** `GET /dashboard/summary` 200 với 0 toàn bộ, không lỗi.
- **Tài khoản mới:** `admin` (xem trên).

### Định nghĩa "Xong" — ĐÃ ĐẠT

Môi trường dev hoàn toàn sạch (0 dữ liệu nghiệp vụ, 0 rác QCR-*), đăng nhập admin hoạt động qua `admin/<redacted>` vừa seed, không đụng nhầm volume Docker Compose (`snipeit_*` nguyên vẹn).

---

## 48. TASK IMPORT-T5 — Frontend UI Import Excel + chọn Công ty theo phạm vi quyền (2026-08-20)

> Tiếp nối backend Import (T1-T4). BACKEND THAY ĐỔI CÁCH GÁN CompanyId: từ "tự gán theo user" → "nhận companyId do user chọn, backend validate lại phạm vi" (nguyên tắc "không tin client" — Task L2). FRONTEND thêm trang Import hoàn chỉnh.

### A — Quyết định (đã duyệt qua tool question)

1. **Superuser import:** BẮT BUỘC chọn công ty — không cho floater/null nữa (`400 COMPANY_REQUIRED` khi thiếu companyId).
2. **Vị trí trang:** `features/import/` riêng (import là chức năng liên module: categories+locations+manufacturers+assets+components+accessories+consumables); menu item "Import Excel" thuộc nhóm **QUẢN TRỊ**, route `/admin/import`.
3. **File mẫu tải xuống:** dùng endpoint động `GET /import/templates/assets` (workbook 7 sheet header chuẩn) — KHÔNG serve file vật lý `docs/Mirats_DuLieuMau_VatTu_T&E.xlsx` (tránh tài liệu lệch code). Endpoint đổi policy `assets.create` → `[Authorize]` (file là skeleton rỗng, không rò dữ liệu; tránh chặn user chỉ có `.create` loại khác).
4. **Reference import:** vẫn bắt buộc chọn công ty cho MỌI loại. Category/Manufacturer là entity GLOBAL (không có CompanyId column) — company chọn chỉ áp cho Location.CompanyId + ghi vào ActionLog của cả batch (1 import = 1 công ty duy nhất).

### B — Backend (4 file)

- **`CompanyScopeService.cs`** — thêm `ICompanyScopeService.IsCompanyIdInUserScopeAsync(Guid)`: company phải TỒN TẠI; user thường chỉ được target công ty mình hoặc con cháu (parent → có thể import cho con); company-less user / superuser → mọi công ty (khớp convention Task V `CompaniesController.GetAll`). BFS cây công ty.
- **`ExcelImportService.cs`** — interface + 5 method đổi tham số: bỏ tự gọi `_companyScope.GetCurrentUserCompanyIdAsync()`, nhận `Guid companyId` tường minh; gán `CompanyId = companyId` cho Location/Asset/Component/Accessory/Consumable; ActionLog của Category/Manufacturer (global) cũng gán `CompanyId = companyId` (audit theo batch). Bỏ dependency `_companyScope`. `ImportSheetResult` thêm `Rows` (mọi dòng, cho báo cáo UI); `Errors` vẫn là tập lỗi.
- **`ImportExportController.cs`** — 5 endpoint import thêm `[FromForm] Guid companyId` (bắt buộc, validate TRƯỚC khi đọc file): thiếu → `400 COMPANY_REQUIRED`; ngoài phạm vi/nonexistent → `403` (helper `ResolveImportCompanyIdAsync`). Response thêm `rows` (mọi ImportRowResult). Template: `[Authorize]`.
- **Tests** — 8 fake `ICompanyScopeService` trong test thêm `IsCompanyIdInUserScopeAsync` (superuser → true; FakeScope → `Super || CompanyId==null || CompanyId==target`).

### C — Frontend (3 file)

- `features/import/services/import.service.ts` — `importExcel(type,file,companyId)` POST multipart (`/import/{type}` + FormData file+companyId), `downloadImportTemplate()` (blob). Type: reference/assets/components/accessories/consumables.
- `features/import/pages/ImportPage.tsx` — chọn loại (Radio, disable option không đủ `.create`), **CompanyTreeSelect** bắt buộc (label "Công ty áp dụng *(bắt buộc)"), Upload.Dragger `.xlsx` (beforeUpload reject ext, manual upload), nút "Import" (gated: thiếu company/file/type → disabled; nếu 403 → message lỗi phạm vi), nút "Tải file mẫu", BÁO CÁO KẾT QUẢ (Alert tóm tắt + bảng per-row: Dòng / ✓ Thành công hoặc ✗ Lỗi / Chi tiết, Segmented lọc Tất cả/Thành công/Lỗi). Trang không quyền → `Empty`. Gate menu qua `canSee('/admin/import')` = có bất kỳ `.create` nào trong 5 loại.
- `App.tsx` — import + route `/admin/import` + menu item "Import Excel" (icon `ImportOutlined`) trong QUẢN TRỊ + crumb/permMap/submenu.

### D — Verify API THẬT (Aspire stack, server `http://localhost:5428`, postgres container `postgres-jvkkyejs`)

Đã tạo 2 công ty QA (`QA T5 Parent Co` + `QA T5 Child Co`) + 2 user QA (`qa-t5-child` thuộc child, `qa-t5-parent` thuộc parent, JIT-created, gán CompanyId + `assets.create`/`assets.view`/`companies.view`/`categories.create`/`accessories.create`/`manufacturers.create`/`locations.create` = Grant).

| Test | Token | companyId | KQ | Ghi chú |
|---|---|---|---|---|
| 1 | child | parent | **403** | con không được import vào công ty cha |
| 2 | parent | child | **200** | cha được import vào công ty con |
| 3 | child | child (của mình) | **200** | đúng phạm vi |
| 5 | child | QCR-CO (khác nhánh) | **403** | ngoài phạm vi |
| 6 | admin | (thiếu) | **400 COMPANY_REQUIRED** | superuser bắt buộc chọn |
| 7 | admin | random uuid | **403** | company không tồn tại |
| 8 | admin | QCR-CO (thật) | **200** | superuser → mọi công ty |

**DB CompanyId (xác minh thật):** import reference + accessories bằng token child→child → Location `QA-T5-CN-*` CompanyId = `d7ced7af-…` (child); Accessory `QA-T5-Accessory-*` CompanyId = child; ActionLog Manufacturer + Accessory + Location đều CompanyId = child. Manufacturer (global) không có cột CompanyId — ActionLog vẫn ghi child (1 import = 1 công ty). ✅

### E — Verify UI THẬT (playwright-cli, Aspire stack)

- **Child user** (`qa-t5-child`): `/admin/import` → CompanyTreeSelect dropdown chỉ hiện **"QA T5 Child Co"** (KHÔNG thấy parent/QCR-CO). Các option "Linh kiện"/"Vật tư tiêu hao" DISABLED (không có `.create`). ✅ Ảnh `docs/qa-t5-child-import.png`.
- **Parent user** (`qa-t5-parent`): dropdown hiện **"QA T5 Parent Co" (expanded) + "QA T5 Child Co"** — cha thấy mình + con. ✅ Ảnh `docs/qa-t5-parent-import.png`, `docs/qa-t5-parent-selected.png`.
- **Admin (superuser)** full-flow: dropdown hiện **full tree** (Parent + Child + QCR-CO). Chọn Child → upload file test → Import → BÁO CÁO đúng "Đã tạo 2 bản ghi — 1 dòng lỗi", bảng per-row (✓ Đã import nhà sản xuất 'QA UI MFR …' (mã QAUIM), ✓ Đã import địa điểm 'QA-UI-…', ✗ Lỗi Sheet '1_DanhMuc' không tồn tại). DB: Location `QA-UI-*` CompanyId = child + ActionLog Manufacturer = child. ✅ Ảnh `docs/qa-t5-import-report.png`, `docs/qa-t5-admin-company-selected.png`.

### F — Build / Test / Dọn dẹp

- `dotnet build` 0 error · `dotnet test` **283/283 PASS** · `npm run build` (tsc -b + vite) **0 lỗi TS**.
- **Dọn sạch test data:** user QA xóa Keycloak (0 `qa-t5-*`) + DB (0 user/company/location/mfr/accessory QA); `user_permissions` xóa; trả DB về baseline (chỉ `admin` + `QCR-CO`). Không đụng tài khoản thật.

### G — Lưu ý / ghi chú cho phiên sau

- Import chạy TRỰC TIẾP (chưa có dry-run/preview — ghi chú trong UI). Nếu cần "Xem trước" thì backend phải thêm flag dry-run riêng (để dành).
- `companyId` binding dùng `[FromForm]` (UI gửi trong FormData); query-string/route binding sẽ không match → luôn 400 COMPANY_REQUIRED.
- T6 (Tests cho importer) và commit Import changes lên GitHub chưa làm — chờ duyệt.

---

## 49. TASK IMPORT-T6 — Automated tests cho Import (2026-08-20)

Viết test xUnit cho 4 hạng mục import, dựa trên đúng các kịch bản đã verify bằng tay ở T5. **`dotnet test` 283 → 307 PASS (+24 test mới).** Audit sweep exit 0; `npm run build` 0 lỗi TS.

### File test mới (2)

- **`ImportCompanyScopeTests.cs`** (17 test) — company-scoping của Import:
  - **Scope-decision (11)**: dùng `CompanyScopeService` THẬT (HttpContext + RequestServices + InMemory DB) → `IsCompanyIdInUserScopeAsync` đủ 7 kịch bản T5 + biên:
    - child→parent `false`, child→child `true`, child→nhánh khác `false`
    - parent→parent `true`, parent→child `true`, parent→nhánh khác `false`
    - superuser→mọi công ty `true`, superuser→nonexistent `false`
    - regular→nonexistent `false`, unauthenticated `false`, thiếu `local_user_id` `false` (fail-closed)
  - **Controller mapping (7)**: dùng `ImportExportController` thật + `RecordingImportService` (ghi lại companyId forward) + FakeFormFile `.xlsx`:
    - thiếu companyId → **400** (`BadRequestObjectResult`)
    - child→parent → **403** (`ForbidResult`), child→nhánh khác → **403**
    - parent→child → **200** + `LastCompanyId == child` (companyId đã validate được forward, không phải client re-echo)
    - child→child → **200**; superuser→nonexistent → **403**; superuser→company thật → **200**
- **`ImportExcelServiceTests.cs`** (7 test) — business rules, dùng ClosedXML THẬT tạo workbook in-memory đúng cấu trúc sheet:
  - **Best-effort per-row (2)**: 1 dòng lỗi (model thiếu) KHÔNG chặn dòng hợp lệ → created/failed đúng, dòng lỗi không persist.
  - **CompanyId + ActionLog (1)**: asset tạo ra có `CompanyId == company`, 1 ActionLog `ItemType.Asset`/`ActionType.Import`/`CompanyId == company`.
  - **AssetModel KHÔNG tự tạo (1)**: model thiếu → 0 created + error "chưa tồn tại / không tự tạo model" + `Models` rỗng.
  - **Serial grouping (2)**: 3 dòng serial cùng (Name+Category+Model) → 1 component `TrackingType.Serial`, Qty 3, 3 ComponentUnit + 3 ActionLog `ItemType.ComponentUnit` (CompanyId đúng); 2 tổ hợp khác nhau → 2 component.

### Verify

- `dotnet build` 0 error · `dotnet test --configuration Release` **307/307 PASS** (24 test import mới).
- `scripts/audit-sweeps.ps1` **exit 0** (0 violation) — đã khắc phục 1 false-positive Sweep 2: `ImportPage.tsx` so HTTP `status === 403` bị bắt nhầm thành enum-vs-number; refactor so sánh inline `e?.response?.status === 403` (đúng pattern sweep exempt).
- `npm run build` 0 lỗi TS.

### Lưu ý cho commit

Sẵn sàng commit toàn bộ thay đổi Import (backend + frontend + docs + tests) lên GitHub theo quy trình cũ: kiểm tra git status, không secret/file rác, không QA screenshot lọt (`.gitignore` đã thêm `docs/qa-*.png`).

---

## 50. TASK IMPORT-T7 — Import SystemInfo (Hệ thống) + SystemPosition (Vị trí) từ file BĐKT-CNTT (2026-08-20)

Mở rộng Import cho SystemInfo/SystemPosition — mô hình y hệt T1-T6. Verify bằng API thật với **file thật `docs/Mirats_HeThong_ViTri_BDKT-CNTT.xlsx`** (43 Hệ thống + 470 Vị trí, đối chiếu sheet `4_DoiChieu` = 43/470/50).

### Audit Bước 0 (trước khi code)

- **Schema thật** (`InitialBaseline` + `AppDbContext`): `SystemInfo` = Code(required, UNIQUE) + Name(required) + Description(nullable) + CompanyId(nullable FK SetNull). `SystemPosition` = SystemInfoId(required FK **Cascade**) + Code(required, UNIQUE) + Name(required) + Description(nullable). **KHÔNG có CompanyId column trên SystemPosition.**
- **SystemInfoController.Create/AddPosition**: validate Code regex `^[A-Z]{3}-\d{4}-\d{3}$` + duplicate; gán `CompanyId = dto.CompanyId` (client). **⚠️ Create KHÔNG validate company-scope** (lỗ hổng lớp Task L2 — Update/Delete thì có) → Import phải tự validate server-side (tái dùng `IsCompanyIdInUserScopeAsync` từ T5).
- **B0.4 — Position kế thừa company từ cha: ✅ XÁC NHẬN.** SystemPosition không có CompanyId column; mọi xử lý company đều đọc từ parent `SystemInfo.CompanyId` (vd License checkout `sys.CompanyId`, `AddPosition` log `CompanyId = sys.CompanyId`).
- **Policy**: `systems.create` (PermissionCatalog, dùng chung cho cả 2 entity).
- **File BĐKT-CNTT**: `1_HeThong` header R4 = "Ten he thong"+"Vi tri khai thac (tham khao)" (43); `2_ViTri` header R4 = "He thong cha (ten)"+"Ten vi tri / thiet bi"+...12 cột (470). **KHÔNG có cột Code.** DB hiện TRỐNG (0 system/0 position) → không có dữ liệu cũ cần migrate.

### Quyết định (đã duyệt)

1. **Code format mới `XXX-YYYY-ZZZ`**: SystemInfo = `SYS-<năm import>-<STT3 reset theo năm>` (vd SYS-2026-001); SystemPosition = `POS-<năm>-<STT3>`. **Regex controller đổi** từ `^[A-Z0-9]{3}-[A-Z0-9]{3}-[A-Z0-9]{3}$` → `^[A-Z]{3}-\d{4}-\d{3}$` (4 chỗ + message) để chấp nhận năm 4 số. STT reset theo năm (query prefix, không cộng dồn). Uniqueness: check DB + **batch set trong lần import** (fix bug: nếu chỉ check DB, các row trong cùng batch chưa SaveChanges sẽ trùng mã — 3 system đều ra SYS-2026-001).
2. **Cột thừa sheet 2_ViTri** (Hang SX, P/N, S/N, Vị trí khai thác, Năm SX, Thành phần/Vai trò, Tình trạng, Ghi chú) → **gộp vào Description** dạng `"Hãng SX: X | S/N: Y | P/N: Z | Vị trí khai thác: ... | Năm SX: ... | Thành phần / Vai trò: ... | Tình trạng khai thác: ... | Ghi chú: ..."`, **bỏ cột rỗng** (không hiện "S/N: " trống). Description là PG `text` (không giới hạn độ dài). Ghi backlog riêng: mở rộng schema SystemPosition thêm field thật (Manufacturer/Serial/Status) — KHÔNG làm trong task này.
3. **Company**: SystemInfo → companyId validate server-side (T5); SystemPosition → **KHÔNG nhận companyId riêng** — endpoint tự derive `actingUserCompanyId = _companyScope.GetCurrentUserCompanyIdAsync()` và validate từng parent (user thường chỉ gắn vào system công ty mình/company-less; superuser → mọi system).
4. **Dữ liệu test**: dọn sạch sau verify (đã chọn).

### Backend (4 file)

- **`SystemInfoController.cs`**: `CodeRegex` đổi `^[A-Z]{3}-\d{4}-\d{3}$` + 4 message "XXX-YYYY-ZZZ".
- **`ExcelImportService.cs`**: thêm `SheetSystems="1_HeThong"`, `SheetSystemPositions="2_ViTri"`; interface + `ImportSystemsAsync(stream,user,companyId)` + `ImportSystemPositionsAsync(stream,user,actingUserCompanyId)`; method sheet `ImportSystemsSheetAsync` (Name→Name, "Vi tri khai thac"→Description, generate SYS code, dup check name+company), `ImportSystemPositionsSheetAsync` (resolve parent by NAME in scope, NO auto-create, generate POS code, dup check parent+name, merge Description, ActionLog CompanyId = parent.CompanyId); helper `GenerateSystemCodeAsync(prefix,year,batchCodes)` + `BuildPositionDescription`.
- **`ImportExportController.cs`**: `POST /import/systems` (`systems.create`, `[FromForm] companyId` validate scope) + `POST /import/system-positions` (`systems.create`, KHÔNG companyId, tự derive scope) + `GET /import/templates/systems` (2 sheet header chuẩn).
- **Tests** (`ImportExcelServiceTests.cs` +3): systems sinh mã tuần tự theo năm; positions resolve parent by name + NO auto-create + inherit company (ActionLog); regular user không gắn position vào system công ty khác → lỗi "ngoài phạm vi".

### Frontend (2 file)

- `features/import/services/import.service.ts`: `ImportType` thêm `'systems' | 'system-positions'`; `downloadImportTemplate` map 2 loại mới → `/import/templates/systems`.
- `features/import/pages/ImportPage.tsx`: thêm 2 option "Hệ thống" + "Vị trí trong hệ thống" (cả 2 gate `systems.create`), hint nêu rõ import hệ thống TRƯỚC.

### Verify API THẬT (Aspire stack, server 5428, postgres `postgres-dprmauuk`)

- **Order test**: import positions TRƯỚC khi systems → **470/470 fail** "chưa tồn tại ... import sheet 1_HeThong trước", **0 auto-create** (DB vẫn 0/0). ✅
- **Import systems** (admin, QCR-CO) → **43/43 created**, codes `SYS-2026-001..043`. **Import positions** → **470/470 created**, codes `POS-2026-001..470`. ✅
- **DB verify**: 43 SystemInfo + 470 SystemPosition + 43 log SystemInfo + 470 log SystemPosition. FK đúng (POS-2026-001 → "VHF 118.35 MHz TWR DAN"). Description gộp đúng (Hãng SX/S/N/Vị trí/Năm SX/Vai trò/Tình trạng). CompanyId system = QCR-CO. ✅
- **Company-scope** (user thường `qa-t7-child` thuộc QA T7 Child Co): import systems vào company cha → **403**; vào company mình → **200 (43 created, codes SYS-2026-044..086 — tuần tự tiếp, không trùng, không cộng dồn sai)**. Position vào system QCR-CO-only → **fail "ngoài phạm vi công ty"**, DB 0. ✅
- **Code uniqueness**: 86 distinct sys codes + 940 distinct pos codes (không trùng). ✅

### Build / Test / Dọn dẹp

- `dotnet test --filter "Category!=Concurrency"` **306/306 PASS** (+3 test mới). 4 test `ConcurrencyRaceAuditTests` cần stack đang chạy (localhost:8080) — bỏ qua khi stack tắt (không phải regression).
- `npm run build` 0 lỗi TS · `scripts/audit-sweeps.ps1` exit 0.
- **Dọn sạch test data**: 86 system + 940 position + 1026 ActionLog + QA companies/user + Keycloak user đều = 0. DB trả về trạng thái trước test (chỉ `admin` + `QCR-CO`).

### ⚠️ BACKLOG (ĐÃ VÁ TRONG T7) — SystemInfoController.Create / AddPosition company-scoping

- **Xác nhận thật (đọc lại code, KHÔNG chỉ ghi chú):** `SystemInfoController.Create` (`Web/Controllers/SystemInfoController.cs`) gán thẳng `CompanyId = dto.CompanyId` từ client **KHÔNG** gọi `_companyScope.GetCurrentUserCompanyIdAsync()` → user thường có `systems.create` tạo được hệ thống thuộc CÔNG TY KHÁC chỉ cần truyền `companyId`. Tương tự `AddPosition` không check target system có thuộc phạm vi user không. **Update/Delete/UpdatePosition/DeletePosition thì CÓ** scope check (Task I đã vá). → **Bất đối xứng: CREATE thiếu, UPDATE/DELETE có** — lỗ hổng lớp Task L2 còn sót cho SystemInfo/SystemPosition.
- **ĐÃ VÁ (cùng commit T7, theo yêu cầu — không tách task riêng vì cùng entity + code sắp lên GitHub Public):**
  - `Create`: user thường (`userCompanyId.HasValue`) gửi `CompanyId` khác company mình → **`400 COMPANY_MISMATCH`** (đúng quy ước Task L2); floater (`null`) vẫn OK; Superuser bypass.
  - `AddPosition`: user thường gửi position vào system thuộc company khác → **`404`** (hide existence, đúng Task I); company-less system vẫn OK; Superuser bypass.
  - **Tests (+8)** `SystemInfoCreateCompanyScopeTests.cs`: Create → 400/200/200/200 (other/own/floater/super); AddPosition → 404/200/200/200. `dotnet test` **314/314 PASS**.
- **Verify API THẬT** (xem mục dưới — sau khi restart stack).

## 51. SẮP XẾP LẠI BỐ CỤC MODAL "TẠO TÀI SẢN MỚI" — CHỈ THAY ĐỔI VỊ TRÍ FIELD (2026-08-20)

> Ràng buộc: **KHÔNG thêm/bớt/ẩn field, KHÔNG đổi label/placeholder/validate, KHÔNG đụng logic Model→Category.** Chỉ sửa vị trí hiển thị (Row/Col/thứ tự nhóm).

### Bước 0 — Audit
- Modal "Tạo tài sản mới" nằm **inline** trong `frontend/src/features/asset/pages/AssetListPage.tsx` (hàm `AssetCreateFormModal`, ~line 374), KHÔNG phải file `CreateAssetFlowModal.tsx` riêng.
- 2 section có hàng lẻ 1 cột, do mỗi `Col` khai báo `lg={8}` (1/3 bề rộng) → trên desktop 3 cột/hàng, field thứ 4 rơi hàng riêng chiếm 1/3.

### Field checklist (TRƯỚC = SAU — giống hệt, chỉ khác vị trí)
**Section "Phân loại" (4 field, giữ nguyên thứ tự):** `modelId` (Model) → `locationId` (Vị trí) → `supplierId` (Nhà cung cấp) → `companyId` (Công ty)
- TRƯỚC: hàng 1 = [Model, Vị trí, Nhà cung cấp] (3 cột), hàng 2 = [Công ty] (1 cột, trống 2/3).
- SAU: hàng 1 = [Model, Vị trí], hàng 2 = [Nhà cung cấp, Công ty] — **2 hàng × 2 cột đều nhau**.

**Section "Tài chính" (4 field, giữ nguyên thứ tự):** `purchaseCost` (Giá mua) → `purchaseDate` (Ngày mua) → `warrantyMonths` (Thời hạn bảo hành) → `orderNumber` (Số đơn hàng)
- TRƯỚC: hàng 1 = [Giá mua, Ngày mua, Bảo hành], hàng 2 = [Số đơn hàng] (1 cột, trống 2/3).
- SAU: hàng 1 = [Giá mua, Ngày mua], hàng 2 = [Thời hạn bảo hành, Số đơn hàng] — **2 hàng × 2 cột đều nhau**.

### Thay đổi (1 file, 8 dòng — chỉ đổi `Col` span)
`frontend/src/features/asset/pages/AssetListPage.tsx`: đổi **8** `Col` (4 của "Phân loại" + 4 của "Tài chính") từ `lg={8}` → `lg={12}`. `xs={24}` giữ nguyên → mobile (375px) tự xuống 1 field/hàng, không vỡ layout. "Thông tin chung" (vẫn `lg={8}`) + "Ghi chú" + toàn bộ logic không đụng.

### Verify
- **Field TRƯỚC/SAU giống hệt**: diff xác nhận chỉ thay đổi `lg={8}`→`lg={12}` trên 8 `Col`, không đổi field/label/order/validate/logic.
- **UI thật (playwright-cli, admin):** mở modal → đủ 8 field: Model, Vị trí, Nhà cung cấp, Công ty (Phân loại); Giá mua, Ngày mua, Thời hạn bảo hành, Số đơn hàng (Tài chính). Desktop + resize 375px đều hiển thị đủ, không vỡ. Console chỉ deprecation warning antd pre-existing (`destroyOnClose`→`destroyOnHidden`).
- `npm run build` (tsc -b && vite build) → **0 lỗi TS** (chỉ chunk-size warning pre-existing).
- Ảnh: `assetmodal-desktop.png`, `assetmodal-375-after.png` (repo root, scratch).
- ⚠️ Model→Category logic (nếu field "Category" gợi ý từ Model) **KHÔNG đụng** — hiện modal không hiển thị field Category, đúng như ràng buộc.

## 52. MỞ RỘNG CỘT "TÊN TÀI SẢN" — SECTION "THÔNG TIN CHUNG" — CHỈ ĐỔI TỶ LỆ CỘT (2026-08-20)

> Ràng buộc: CHỈ đổi tỷ lệ chiều rộng 3 field Mã tài sản / Tên tài sản / Serial trong section "Thông tin chung". KHÔNG thêm/bớt/đổi field, label, placeholder, validate. KHÔNG đụng 2 section "Phân loại"/"Tài chính".

### Bước 0 — Audit
- `frontend/src/features/asset/pages/AssetListPage.tsx` (~line 378): section "Thông tin chung" trước đó dùng `lg={8}` cho cả 3 field → 3 cột bằng nhau → "Tên tài sản" (chuỗi dài) bị chật.

### Thay đổi (1 file, 3 dòng — chỉ đổi `Col` span của 3 field "Thông tin chung")
- **Mã tài sản** (`assetTag`): `lg={8}` → `lg={6}` (1 phần).
- **Tên tài sản** (`name`): `lg={8}` → `lg={12}` (2 phần — rộng gấp đôi).
- **Serial** (`serial`): `lg={8}` → `lg={6}` (1 phần).
- Tỷ lệ desktop = **6:12:6** (Mã : Tên : Serial). `xs={24} md={12}` giữ nguyên → mobile xuống 1 field/hàng.

### Verify
- **Field TRƯỚC/SAU giống hệt**: diff chỉ đổi `lg` span của 3 `Col` trong section "Thông tin chung"; field/label/order/validate/logic không đổi. "Phân loại"/"Tài chính" (đã sửa ở task 51) không bị đụng lại.
- **Đo bề rộng thật (playwright-cli, admin):** desktop → **Mã tài sản 158px, Tên tài sản 331px, Serial 158px** (tỷ lệ ~1:2:1 đúng mong đợi).
- **Test tên dài thực tế (46 ký tự: "Bo dieu khien joystick AXIS T8311 KVM Extender"):** `scrollWidth=329 == clientWidth=329` → **hiển thị ĐỦ, không bị cắt/tràn** trên desktop.
- **Responsive 375px:** cả 3 field full-width (277px), xếp riêng từng hàng (top 245/332/418) → không vỡ layout; tên dài cuộn nhẹ trong input 1 dòng (hành vi chuẩn).
- `npm run build` (tsc -b && vite build) → **0 lỗi TS**.
- Ảnh: `thongtin-longname-desktop.png` (desktop + tên dài), `thongtin-375.png` (mobile).

## 53. FIX LỖI "KHÔNG GỬI QTY KHI TẠO LINH KIỆN SERIAL" — COMPONENTFORMMODAL GỬI THỪA FIELD (2026-08-20)

> Ràng buộc: CHỈ sửa frontend cho khớp backend validate (backend đang đúng). KHÔNG đổi field/UI, KHÔNG đổi luồng Bulk.

### Bước 0 — Root cause (trích dẫn code cụ thể)
- **Frontend** `frontend/src/features/component/components/ComponentFormModal.tsx`, hàm `submit`, branch tạo mới (~line 193): payload LUÔN gán `qty: typeof vals.qty === 'number' ? vals.qty : 0` (line cũ 196) bất kể `trackingType`. Khi `trackingType === 'Serial'`, form không có ô "Tổng số lượng" (bị unmount) nên `vals.qty` là `undefined` → payload gửi `qty: 0`.
- **Toggle Bulk→Serial:** ô `qty` (Form.Item name="qty") unmount khi chuyển Serial nhưng giá trị form state **còn giữ** trong antd (không `preserve:false`) → `vals.qty` là giá trị cũ từ lúc Bulk (VD 5), vẫn bị đưa vào payload.
- **Backend** `ComponentsController.cs` line 173-174: `if (r.TrackingType == Serial && r.Qty.HasValue) → 400 "Không gửi qty khi tạo linh kiện Serial..."`. Backend yêu cầu `qty` **HOÀN TOÀN KHÔNG XUẤT HIỆN** trong payload (`Qty.HasValue == false`); gửi `0` cũng bị chặn. → Sửa đúng cách là **omit hẳn key `qty`** khi Serial (không gửi `null`/`0`).

### Fix (1 file, branch tạo mới)
- Bỏ `qty` khỏi object payload ban đầu; chỉ gán `payload.qty` khi `effectiveTrackingType === 'Bulk'`:
  ```ts
  if (effectiveTrackingType === 'Bulk') {
    payload.qty = typeof vals.qty === 'number' ? vals.qty : 0;
  }
  ```
- Khi Serial → payload không chứa key `qty` (omitted), kể cả khi có qty leftover từ toggle Bulk trước đó.

### Verify UI thật (playwright-cli, admin) — 3 kịch bản
1. **Serial (đúng kịch bản lỗi):** chọn Serial, Tên "RAM Serial Test QA", Danh mục RAM, Công ty, nhập 3 serial (QA-SN-001/2/3, "Đã nhập: 3 serial") → bấm Tạo mới → **thành công, KHÔNG còn lỗi "Không gửi qty"**. DB: `TrackingType=1 (Serial)`, `Qty=3` (suy ra từ serial), **3 ComponentUnit** (QA-SN-001/2/3). ✅
2. **Toggle Bulk→Serial→Bulk→Serial:** đặt qty=5 ở Bulk, toggle qua Serial↔Bulk↔Serial, thêm 1 serial TGL-SN-001 → submit → **thành công**. DB: `TrackingType=1`, `Qty=1` (từ serial, KHÔNG phải qty leftover=5), 1 unit. ✅
3. **Bulk (không regression):** Tên "RAM Bulk QA", qty=7 → submit → **thành công**. DB: `TrackingType=0 (Bulk)`, `Qty=7`, 0 unit serial. ✅
- `npm run build` (tsc -b && vite build) → **0 lỗi TS**.
- **Dọn sạch test data:** 3 component test + 4 unit + ActionLog = 0.
- Lưu ý: công ty "Công ty Quản lý bay miền Trung" + danh mục Component (RAM...) là dữ liệu có sẵn từ trước (import mẫu), KHÔNG phải do test này tạo.

## 54. TỰ SINH MÃ TÀI SẢN (ASSET TAG) THEO FORMAT ADMIN CẤU HÌNH (2026-08-21)

### Thiết kế đã chốt (audit + 4 câu hỏi + 2 làm rõ)
- **Format token:** chuỗi tự do hỗ trợ `{COMPANY}` (mã công ty, NOCO nếu không có), `{YYYY}` (năm 4 số), `{SEQ:n}` (số thứ tự đệm n chữ số). VD `AST-{COMPANY}-{YYYY}-{SEQ:3}` → `AST-ABC-2026-001`. Admin cấu hình qua 1 ô; cho phép lưu thiếu `{COMPANY}` kèm cảnh báo nguy cơ trùng (không chặn cứng).
- **Bộ đếm:** keyed theo `(CompanyId, Year)` — mỗi công ty đếm riêng + reset 001 mỗi năm (giống T7).
- **XUNG ĐỘT đã phát hiện + giải quyết:** unique constraint `IX_assets_AssetTag` là GLOBAL, nhưng counter đã chốt RIÊNG công ty → 2 công ty cùng sinh `AST-2026-001` vi phạm constraint. **Giải pháp (Option 3):** thêm cột `Company.Code`, token `{COMPANY}` chèn mã công ty vào tag để unique global thật sự mà không đổi constraint Task L. `NOCO` reserved cho floater.

### Backend (T-A1 + T-A2)
- **Entity mới:** `SystemSetting` (key-value cấu hình tĩnh) + `AssetTagCounter` (CompanyId, Year, CurrentSeq; unique index (CompanyId,Year)).
- **Migration:** `AddSystemSettingsAndAssetTagCounter` (2 bảng). `AddCompanyCode`: thêm `companies.Code` (unique, max 20) + **backfill SQL tự sinh mã** cho công ty hiện có (bỏ dấu, uppercase, ≤4 chữ + hậu tố số; không dùng NOCO).
- **Permission:** thêm `system.config` vào PermissionCatalog.
- **API:** `GET/PUT /api/v1/system/config/asset-tag-format` (PUT gate `system.config`); `CompaniesController` thêm Code vào DTO/Create/Update + auto-suggest `SuggestCodeAsync` + validate `NOCO` + unique.
- **`AssetTagGenerator`** (`Infrastructure/Services`): `ResolveAssetTagAsync` (explicit tag → dùng nguyên; rỗng → sinh). Render `{COMPANY}`/`{YYYY}`/`{SEQ:n}`. Counter trong transaction + **FOR UPDATE** + `CreateExecutionStrategy` (Task O pattern). Dùng `NpgsqlParameter` typed để tránh `42P18` khi companyId null.
- **CreateAssetCommand:** bỏ `NotEmpty` AssetTag (optional), handler gọi `ResolveAssetTagAsync`. Unique constraint vẫn là lớp bảo vệ cuối.

### Frontend (T-A3 + T-A4)
- **`SystemConfigPage`** (`features/admin/pages`): trang "Cấu hình hệ thống" — form format, chỉ đọc nếu thiếu `system.config`, validate `{SEQ:n}` + pattern, nút Lưu/Tải lại. Menu QUẢN TRỊ thêm "Cấu hình hệ thống" (`/admin/system-config`), breadcrumb + permMap + route.
- **Create Asset modal** (`AssetListPage`): AssetTag bỏ `required`, placeholder "Để trống để tự sinh mã". **KHÔNG đụng bố cục 2 section.**
- **CompanyListPage:** thêm cột + form field "Mã công ty", TreeSelect hiển thị `(code)`, validate NOCO + alnum.

### Verify
- `dotnet test` fast suite **332/332 PASS** (+ `AssetTagGeneratorTests` 16: render theory, set-format valid/invalid, explicit pass-through; `ValidationBehaviorTests.Behavior_EmptyTag` đổi từ fail→pass).
- **API THẬT:** công ty CNGT → `AST-CNGT-2026-001`; công ty QATA → `AST-QATA-2026-001` (counter riêng); floater → `AST-NOCO-2026-001`. **Concurrency 8 luồng cùng lúc** (QATA) → tags `AST-QATA-2026-002..009` unique, counter=9 (không trùng, FOR UPDATE hoạt động). Tạo công ty code=NOCO → 400. Auto-suggest "QA-AutoCode Co"→`QAAU`. PUT/GET format OK.
- `npm run build` 0 lỗi TS · `audit-sweeps.ps1` exit 0 · UI thật: config page hiển thị format, Create Asset modal placeholder "Để trống để tự sinh mã".
- **Dọn sạch test data:** QA companies/assets/counters/settings/action_logs = 0; còn 1 công ty gốc (CNGT) + 8 asset mẫu.

### ⚠️ Lưu ý cho người sau
- Company hiện có cần có Code (migration backfill đã tự sinh). Mã dùng trong AssetTag — đổi Code sẽ ảnh hưởng các tag đã tạo (không tự cập nhật lại tag cũ).
- Công ty floater dùng `NOCO` — không công ty nào được đặt Code = NOCO.
- Nếu admin cấu hình format thiếu `{COMPANY}`: hệ thống vẫn cho phép nhưng cảnh báo; nguy cơ trùng mã giữa công ty là do admin tự chấp nhận.

## 55. FIX LỖI "ĐỊNH DẠNG MÃ HỆ THỐNG XXX-YYYY-ZZZ PHẢI VIẾT HOA" (2026-08-21)

### Root cause (điều tra Bước 0)
- **Backend** `SystemInfoController.cs` L37 (Task T7): `CodeRegex = ^[A-Z]{3}-\d{4}-\d{3}$` → format `XXX-YYYY-ZZZ` (3 chữ hoa - 4 số năm - 3 số thứ tự, VD `SYS-2026-001`), **case-sensitive**.
- **Frontend** `SystemInfoListPage.tsx` L29: `CODE_PATTERN = ^[A-Z0-9]{3}-[A-Z0-9]{3}-[A-Z0-9]{3}$` → vẫn là format CŨ `XXX-YYY-ZZZ` (3-3-3) **chưa cập nhật theo T7**. Placeholder `VD: SYS-001-COR`, `maxLength={11}` cũng là format cũ.
- **Nguồn lỗi:** UI Admin nhập tay. Người dùng theo placeholder gõ `SYS-001-COR` (3-3-3) → frontend pattern cũ cho qua → backend format mới từ chối (thiếu 4 số năm) → lỗi "phải viết hoa". **KHÔNG phải lỗi sinh mã tự động** (Import `GenerateSystemCodeAsync` đã sinh `SYS-2026-001` uppercase đúng).
- **Tái hiện API thật:** `code:"sys-2026-001"` → 400 "phải viết hoa" (case-sensitive); `code:"SYS-2026-001"` → 200 OK.

### Fix
- **Backend** (nới cho phép chữ thường + tự chuẩn hóa hoa): thêm `dto = dto with { Code = dto.Code?.Trim().ToUpperInvariant() ?? string.Empty }` ở ĐẦU mỗi method Create/Update/AddPosition/UpdatePosition — chuẩn hóa hoa TRƯỚC khi validate → chữ thường được chấp nhận và lưu hoa, người dùng không cần nhớ quy tắc.
- **Frontend** `SystemInfoListPage.tsx`: `CODE_PATTERN` → `^[A-Z]{3}-\d{4}-\d{3}$`; message → "Định dạng: XXX-YYYY-ZZZ (3 chữ hoa - 4 số năm - 3 số thứ tự)"; placeholder → `VD: SYS-2026-001` / `VD: POS-2026-001`; `maxLength` → 12. Giữ `getValueFromEvent` + `.toUpperCase()` tự viết hoa khi gõ.

### Verify
- **API thật:** `code:"sys-2026-042"` (lowercase) → tạo thành công, DB lưu `SYS-2026-042` (uppercase). Lỗi trước đây không còn.
- **UI thật (playwright-cli, admin):** placeholder `VD: SYS-2026-001`; gõ `sys-2026-050` tự viết hoa thành `SYS-2026-050`, submit thành công, DB lưu `SYS-2026-050`.
- `dotnet test` fast suite **332/332 PASS** · `npm run build` **0 lỗi TS**.
- **Dọn sạch test data:** các system test (Test upper/lowercase, QA-*) + action_logs = 0.

## 56. NỚI FORMAT MÃ HỆ THỐNG/VỊ TRÍ: PREFIX TỪ 3 → 3-4 CHỮ HOA (2026-08-21)

### Thay đổi (mở rộng regex prefix `{3}` → `{3,4}`)
- **Backend** `SystemInfoController.cs` L38: `CodeRegex = ^[A-Z]{3,4}-\d{4}-\d{3}$` (trước: `^[A-Z]{3}-...`). Message → "Mã hệ thống/vị trí phải đúng định dạng XXX(X)-YYYY-ZZZ (3-4 chữ hoa, viết hoa)." (4 chỗ).
- **Frontend** `SystemInfoListPage.tsx`: `CODE_PATTERN = /^[A-Z]{3,4}-\d{4}-\d{3}$/`; message → "XXX(X)-YYYY-ZZZ (3-4 chữ hoa - 4 số năm - 3 số thứ tự)"; `maxLength` → 13 (2 form system + position).

### Verify
- **API thật:** `syst-2026-001` (4 chữ, lowercase) → tạo thành công, lưu `SYST-2026-001`; `SYS-2026-007` (3 chữ) → vẫn OK; `SY-2026-001` (2 chữ) → **400** "XXX(X)-YYYY-ZZZ (3-4 chữ hoa...)".
- `dotnet build` 0 lỗi · `npm run build` 0 lỗi TS.
- **Dọn test data:** hệ thống QA (QA Four-letter/Three-letter prefix) + action_logs = 0. Còn lại 1 hệ thống có sẵn `RDP-2026-001` (dữ liệu thật, không phải test).

## 57. HIỂN THỊ "GHI CHÚ" CHO COMPONENT (DETAIL + LIST CARD) + CLICK-TO-DETAIL (2026-08-22)

### Audit Bước 0 (trước khi sửa)
- Component entity **có** `Notes` (`Domain/Entities/Component.cs` L23); `ComponentFormModal` đã lưu Notes khi Tạo/Sửa → dữ liệu có trong DB nhưng **không hiển thị** trên UI.
- API: `GET /components/{id}` **đã trả** `notes` (ComponentsController L145) — nhưng `GET /components` (list) **KHÔNG trả** `notes`: projection query-level có `c.Notes` nhưng **projection thứ 2** (L103-109, `items.Select(...)` — chính là response trả ra) **thiếu** `c.Notes` → phải sửa cả backend.
- Đối chiếu pattern các trang khác: Asset (`span={2}`), Accessory/Consumable (`span={{ xs: 1, sm: 2 }}` + `detail.notes || '-'`) → áp dụng đúng pattern này cho Component Detail.
- Card-click-to-detail: **chưa có** ListPage nào (Asset/Accessory/Consumable/License) làm — chỉ có nút điều hướng → task yêu cầu thêm cho Component (toàn card click + `stopPropagation` cho nút).

### Thay đổi
- **Backend** `ComponentsController.cs`: thêm `c.Notes` vào **projection thứ 2** của list (L106) — đây mới là response thật (lần đầu sửa nhầm projection query-level L66, API vẫn thiếu notes → debug: response keys không có `notes`).
- **Frontend** `ComponentDetailPage.tsx`: thêm `<Descriptions.Item label="Ghi chú" span={{ xs: 1, sm: 2 }}>{component.notes || '-'}</Descriptions.Item>` cuối bảng Descriptions (sau "Giá mua"), đúng style Accessory/Consumable.
- **Frontend** `ComponentListPage.tsx`:
  - `ListItem` thêm `notes: string | null`.
  - Card: thêm dòng "Ghi chú" (icon `FileTextOutlined`) full-width trong dataGrid, **chỉ hiển thị khi có notes**; dùng `Typography.Paragraph ellipsis={{ rows: 2, tooltip: record.notes }}` → rút gọn 2 dòng + tooltip hover hiện đầy đủ (trang Chi tiết hiển thị đầy đủ).
  - Card: `onClick={() => navigate(\`/components/${record.id}\`)}` + `cursor: 'pointer'` (toàn card click được).
  - 3 nút Chi tiết/Sửa/Xóa: thêm `e.stopPropagation()` (click nút không kích hoạt điều hướng card).

### Verify (UI thật playwright-cli, admin, stack Aspire)
- **Detail:** bảng thông tin có dòng "Ghi chú" sau "Giá mua" hiển thị `Ngan 1 - RDP. ECC Registered, 4GB DDR3 1600MHz. Test OK 08/05/2024` (dữ liệu thật của RAM HP 4GB 1Rx4 PC3-12800R-R).
- **List Card:** card RAM HP hiển thị "Ghi chú" + nội dung; card không có notes (QCR-*) **không** hiển thị dòng này (chỉ 1 "Ghi chú" trên toàn trang). Long-note test: `ant-typography-ellipsis-multiple-line` + `-webkit-line-clamp: 2`; hover → tooltip hiện đầy đủ.
- **Click-to-detail:** click toàn card → điều hướng `/components/{id}` đúng. Nút "Sửa" → mở modal edit tại `/components/{id}/edit` (không điều hướng nhầm). Nút "Xóa" → mở Popconfirm, path vẫn `/components` (stopPropagation hoạt động). Nút "Chi tiết" → `/components/{id}`.
- `npm run build` **0 lỗi TS** · `dotnet build` 0 lỗi · `dotnet test` fast suite (`--filter "Category!=Concurrency"`) **332/332 PASS**.
- **API thật:** list trả `notes` cho RAM HP; detail trả notes đầy đủ.
- **Dọn test data:** QA-COMP-NOTES-TEST (tạo để test ellipsis) đã xóa + logs; QCR-* residue (5 components QCR-COMP-0..4 + QCR-CO + QCR-COMP category + 5 QCR-ACC + 5 QCR-CON + 5 QCR-AST + 5 component_assignments + 27 action_logs) do test suite concurrency (Category=Concurrency, chạy không filter khi stack đang chạy) sinh ra — **đã xóa hết** (pg_dump backup trước: `backups/db-backup-20260822-qcr-cleanup.sql`). Còn lại đúng baseline: 1 component RAM HP (có notes), 9 action_logs cũ của RDP Aircon (dữ liệu thật, giữ nguyên).

### ⚠️ Lưu ý cho người sau
- `dotnet test aspire-react.sln` không filter sẽ chạy cả `ConcurrencyRaceAuditTests` (Category=Concurrency) — các test này gọi API thật trên localhost:5428 và **tạo dữ liệu QCR-\*** không tự dọn → chỉ chạy khi stack đang lên và dọn sạch sau đó (fast suite chuẩn: `--filter "Category!=Concurrency"`).

## 58. HIỂN THỊ "GHI CHÚ" CHO LIST — ASSET / LICENSE / ACCESSORY / CONSUMABLE (2026-08-22)

### Audit Bước 0 (từng trang riêng biệt, không giả định giống nhau)
| Trang | UI thật | API List trả Notes? | Frontend DTO có notes? | Kết luận |
|---|---|---|---|---|
| Asset | Card (ProList grid) | ❌ thiếu ở **cả 2 projection** (query L70-83 + enriched L96-99) | ✅ AssetDto.notes | sửa backend + frontend |
| License | Card (ProList grid) | ❌ thiếu (1 projection L77-89) | ❌ LicenseListItem (notes chỉ ở LicenseDetailDto) | sửa backend + DTO + frontend |
| Accessory | Card (ProList grid) | ❌ thiếu (1 projection L57-66) | ❌ AccessoryDto (notes chỉ ở AccessoryDetail) | sửa backend + DTO + frontend |
| Consumable | Card (ProList grid) | ❌ thiếu (1 projection L54-62) | ❌ ConsumableDto (inline) | sửa backend + DTO + frontend |

→ Cả 4 trang đều **Card view** (không phải ProTable) → click-to-detail áp dụng ở mức **toàn card** (giống Component), không phải row click.

### Thay đổi
- **Backend** (4 controller, thêm `Notes` vào projection list — bài học Component: kiểm tra **số lượng projection** trong method, Asset có 2 nên sửa cả 2):
  - `AssetsController.cs`: `a.Notes` ở projection query-level **và** projection enriched (response thật).
  - `LicensesController.cs`: `l.Notes` · `AccessoriesController.cs`: `a.Notes` · `ConsumablesController.cs`: `c.Notes`.
- **Frontend** (4 ListPage — pattern giống Component §57: dòng "Ghi chú" full-width trong grid, chỉ hiện khi có notes, `Paragraph ellipsis={{rows:2, tooltip}}`):
  - `AssetListPage.tsx`: dòng "Ghi chú:" trong grid; Card thêm `onClick → /assets/{id}` + `cursor:pointer`; `stopPropagation` trên 8 nút (Xem/Sửa/Xác nhận/Cấp phát/Lưu trữ/Thu hồi/Mở lại/Xóa).
  - `LicenseListPage.tsx`: dòng "Ghi chú" (icon FileTextOutlined) trong dataGrid; Card `onClick → /licenses/{id}` (mở LicenseDetailModal); `stopPropagation` trên Cấp phát/Chi tiết/Sửa/Xóa.
  - `AccessoryListPage.tsx`: dòng "Ghi chú" trong dataGrid; Card `onClick → /accessories/{id}/view`; `stopPropagation` trên Sửa/Xem/Cấp phát/Thu hồi/Xóa.
  - `ConsumableListPage.tsx`: thêm `notes` vào ConsumableDto + dòng "Ghi chú"; Card `onClick → /consumables/{id}/view`; `stopPropagation` trên Xem/Sửa/Xác nhận/Xóa/Cấp phát.
  - DTO phụ: `licenses.service.ts` LicenseListItem + `accessories.service.ts` AccessoryDto thêm `notes`; `AccessoryDetailPage.tsx` (accessoryDtoForModal) + `ConsumableListPage.tsx` (?checkout= auto-open) bổ sung `notes` — lỗi TS2741 build lần đầu đã sửa.

### Verify (UI thật playwright-cli, admin — từng trang riêng biệt)
- **API thật:** cả 5 list endpoint (/assets /licenses /accessories /consumables /components) đều trả `notes`.
- **Asset:** card TE-AST-001 hiển thị "Ngăn 1. Local unit" (dữ liệu thật, 8/12 card có notes); click card → `/assets/{id}` (detail có Ghi chú); nút Sửa → mở modal, path giữ `/assets`.
- **License:** card QA-LIC-NOTES-TEST hiển thị Ghi chú; click card → `/licenses/{id}` + mở LicenseDetailModal (modal hiển thị notes); nút Chi tiết hoạt động.
- **Accessory:** card QA-ACC-NOTES-TEST hiển thị Ghi chú; click card → `/accessories/{id}/view`; nút Sửa → mở modal "Sửa phụ kiện", path giữ `/accessories`.
- **Consumable:** card QA-CON-NOTES-TEST hiển thị Ghi chú; click card → `/consumables/{id}/view`; nút Sửa → mở modal, path giữ `/consumables`.
- Ellipsis: `ant-typography-ellipsis-multiple-line` + `-webkit-line-clamp: 2` xác nhận trên card.
- Ảnh trước/sau mỗi trang (stash tạm 4 ListPage → chụp "before" → pop): `asset-list-before/after.png`, `license-list-before/after.png`, `accessory-list-before/after.png`, `consumable-list-before/after.png`.
- `npm run build` **0 lỗi TS** · `dotnet build` 0 lỗi · `dotnet test` fast suite (`--filter "Category!=Concurrency"`) **332/332 PASS**.
- **Dọn test data:** QA-LIC-NOTES-TEST (+2 seats) + QA-LIC-CAT + QA-ACC-NOTES-TEST + QA-CON-NOTES-TEST + 7 action_logs đã xóa (licenses/accessories/consumables = 0). Còn lại dữ liệu thật: 14 assets (nhiều cái có notes), 1 component RAM HP, 1 system RDP.

### ⚠️ Lưu ý
- Cả 4 ListPage giờ đều **click-to-detail toàn card** (giống Component); nút hành động bên trong đều `stopPropagation`.
- Khi thêm field vào list projection backend: kiểm tra **số lượng projection** trong method (Asset có 2 — Component từng dính lỗi sửa nhầm projection đầu, response vẫn thiếu field).

## 59. CONSUMABLE: ẨN NÚT "CẤP PHÁT" KHI CHỜ XÁC NHẬN + MỞ KHÓA VỊ TRÍ/GHI CHÚ SAU KHI CẤP PHÁT (2026-08-22)

### Audit Bước 0
- **ConsumableStatus enum** (`Domain/Enums/ConsumableStatus.cs`): chỉ 2 trạng thái `Pending = 1` (Chờ xác nhận) / `Confirmed = 2` (Đã xác nhận). Vòng đời: Tạo → **Pending** (trạng thái NGAY SAU KHI TẠO, chưa confirm) → `Confirm` → **Confirmed** → `Checkout` (cấp phát).
- **Nút "Cấp phát"** hiện tại hiển thị BẤT KỂ trạng thái ở cả List (ConsumableListPage L441, chỉ gate `canCheckout` + `remaining > 0`) và Detail (ConsumableDetailPage L322) — cần thêm điều kiện trạng thái. Permission `consumables.checkout` giữ nguyên.
- **Backend Update** (`ConsumablesController.Update` L139): `Status == Confirmed → chặn TẤT CẢ field` — chặn cả Vị trí/Ghi chú → **vấn đề nằm ở backend**, không chỉ frontend. Form frontend vốn KHÔNG khóa Location/Notes (chỉ khóa Company khi đã từng cấp phát).
- **Detail API thiếu `status`** trong response → Detail page và Form không biết trạng thái để gate.
- ⚠️ Phát hiện thêm: `CheckoutAsync` (ConsumableAllocationService) KHÔNG kiểm tra Status — backend hiện vẫn cho phép checkout vật tư Pending qua API (task chỉ yêu cầu ẩn nút UI, không đổi backend validate → ghi nhận để cân nhắc sau).

### Thay đổi
- **Backend** `ConsumablesController.cs`:
  - `GetConsumable`: thêm `Status = c.Status.ToString()` vào response.
  - `Update`: thay block chặn cứng `Confirmed` bằng **khóa field có điều kiện** (mirror Task F Asset): khi Confirmed, chỉ Vị trí (`LocationId`) + Ghi chú (`Notes`) được sửa; các field khác (name/itemNo/qty/minAmt/categoryId/manufacturerId/supplierId/companyId/modelNumber/orderNumber/purchaseCost/purchaseDate/image) bị từ chối với `error_code = CONFIRMED_CONSUMABLE_LOCKED` nếu payload gửi GIÁ TRỊ KHÁC giá trị hiện tại (**patch-aware**: gửi giá trị giống hiện tại thì không bị chặn — form sửa submit đủ field vẫn OK). Thêm ActionLog cho nhánh Confirmed (changes: locationId, notes).
- **Frontend**:
  - `ConsumableListPage`: nút Cấp phát thêm điều kiện `isConfirmed(record.status)`.
  - `ConsumableDetailPage`: thêm `status` vào `ConsumableDetail`; nút Cấp phát chỉ hiện khi `detail.status === 'Confirmed'`.
  - `ConsumableFormModal`: thêm state `confirmedLocked` (đọc từ `d.status === 'Confirmed'`); Alert cảnh báo "Vật tư đã xác nhận — chỉ Vị trí và Ghi chú được sửa."; disable 12 field (name/itemNo/categoryId/companyId/qty/minAmt/supplierId/manufacturerId/modelNumber/orderNumber/purchaseDate/purchaseCost), GIỮ editable Vị trí + Ghi chú. (Alert dùng prop `title` — tránh deprecation `message`.)

### Verify (UI thật playwright-cli, admin; stack Aspire)
- **Kịch bản 1 — ẩn nút:** List: QA-CON-PENDING (tag "Chờ xác nhận") → **KHÔNG có** nút Cấp phát; QA-CON-DONE (tag "Đã xác nhận") → **có** nút. Detail: Pending → không có; Confirmed → có.
- **Kịch bản 2 — sửa sau khi cấp phát:** QA-CON-EDIT (Confirmed + đã checkout 3) → mở Sửa: Alert hiện, Tên/Số lượng/Danh mục **disabled**, **Vị trí + Ghi chú editable** → đổi Vị trí → "Tủ #2 - Phòng T&E" + Ghi chú mới → Lưu → **PUT 200 OK**, modal đóng, Detail hiển thị Vị trí/Ghi chú mới (verify API: status=Confirmed, loc=Tủ #2, notes mới).
- **Backend từ chối đúng:** PUT qty=99 → 400 `CONFIRMED_CONSUMABLE_LOCKED` ("qty"); PUT name khác → 400; PUT qty=10 (cùng giá trị) → 200 (patch-aware).
- Ảnh trước/sau (stash tạm 3 file frontend → chụp → pop): `cons-list-before.png` (Pending card CÓ nút) ↔ `cons-cap-btn-list.png` (không có), `cons-edit-before.png` (modal không Alert, mọi field enabled) ↔ `cons-edit-confirmed.png` (Alert + field khóa), `cons-edit-after-save.png` (detail sau lưu).
- `npm run build` **0 lỗi TS** · `dotnet build` 0 lỗi · `dotnet test` fast suite (`--filter "Category!=Concurrency"`) **332/332 PASS**.
- **Dọn test data:** QA-CON-PENDING + QA-CON-DONE + QA-CON-EDIT (+2 checkout +9 action_logs) đã xóa; consumables = 0.

### ⚠️ Lưu ý
- Khóa field Confirmed dùng cơ chế **patch-aware so giá trị**: form sửa gửi toàn bộ field (giá trị không đổi) vẫn 200; chỉ field gửi giá trị KHÁC mới bị 400.

## 60. BẮT BUỘC: CHẶN CHECKOUT VẬT TƯ PENDING Ở TẦNG API (CONSUMABLE_NOT_CONFIRMED) (2026-08-22)

### Lỗ hổng
- Trước fix này, `ConsumableAllocationService.CheckoutAsync` **không kiểm tra Status** — dù UI đã ẩn nút "Cấp phát" cho vật tư Pending (§59), gọi thẳng `POST /consumables/{id}/checkout` qua API vẫn checkout được vật tư chưa xác nhận → bỏ qua ràng buộc nghiệp vụ.

### Fix
- `ConsumableAllocationService.CheckoutAsync`: thêm gate ngay sau check NOT_FOUND:
  ```csharp
  if (consumable.Status != ConsumableStatus.Confirmed)
      return new ConsumableCheckoutResult(false,
          "Vật tư chưa được xác nhận — không thể cấp phát. Hãy xác nhận vật tư trước.", "CONSUMABLE_NOT_CONFIRMED");
  ```
  Controller `RunTransactional` vốn map `!Success → 400 { message, error_code }` → API trả 400 có mã rõ ràng.
- **Tests** `ConsumableTests.cs`: `SeedConsumableAsync` thêm param `status` (mặc định Pending); 5 test checkout cũ seed `Confirmed` (để chạm đúng các nhánh lỗi cũ); thêm test mới `Checkout_Pending_Blocked_WithConfirmErrorCode` (expect `CONSUMABLE_NOT_CONFIRMED`, không có checkout/log).

### Verify (API thật — bỏ qua UI)
- Tạo QA-CON-NOTCONFIRMED (Pending, qty 5) → `POST /checkout` → **400** `{"error_code":"CONSUMABLE_NOT_CONFIRMED","message":"Vật tư chưa được xác nhận — không thể cấp phát..."}` ✅
- `PUT /confirm` → 200 → `POST /checkout` lại → **200** "1 consumable(s) checked out." ✅ (Confirmed vẫn checkout bình thường)
- `dotnet test` fast suite (`--filter "Category!=Concurrency"`) **333/333 PASS** (332 + 1 test mới).
- **Dọn test data:** QA-CON-NOTCONFIRMED (+1 checkout +3 action_logs) đã xóa; consumables = 0.

### ⚠️ Lưu ý
- Lỗ hổng này cùng bài học: **mọi ràng buộc nghiệp vụ phải enforce ở backend**, UI ẩn nút chỉ là lớp trải nghiệm. (Đã audit: Accessory/License/Component **không có** khái niệm Pending/Confirmed như Consumable — không cần gate tương tự; chỉ Consumable có workflow Confirm.)

## 61. T-DEP1 — DỌN SẠCH DEPRECATION ANT D 6 (2026-08-22)

### Phạm vi (audit console thật + typings `node_modules/antd/es` 6.5.3)
Thay thế toàn bộ deprecated API còn sót — nhiều chỗ hơn báo cáo audit ban đầu (audit đếm 11× destroyOnClose, thực tế quét grep ra 12 prop + phát hiện thêm khi verify console):
- **12× `destroyOnClose` → `destroyOnHidden`** (Modal): AssetRecallModal, AssetMaintenanceSection, AssetArchiveModal, AssetAllocationModal, AccessoryListPage, AssetListPage, AccessoryCheckoutModal, ComponentDetailPage ×3, AccessoryCheckinModal, ComponentFormModal (+1 comment sửa theo).
- **5× `dropdownRender` → `popupRender`** (Select/TreeSelect, cùng signature `(menu) => ReactNode`): CompanyTreeSelect, AssetMaintenanceSection, ComponentFormModal ×3.
- **1× `Statistic valueStyle` → `styles={{ content: ... }}`**: DashboardPage ("Sắp hết" #fa8c16).
- **Phát hiện thêm khi verify console thật** (báo cáo audit bỏ sót):
  - 1× `maskClosable={false}` → `mask={{ closable: false }}` (ComponentFormModal).
  - 1× `Drawer width={720}` → `size={720}` (ComponentDetailPage — Drawer lịch sử serial; App.tsx đã dùng size từ trước).
  - 4× `Alert message=` → `title=` (ComponentFormModal ×2, AssetListPage review-alert, AssetDetailPage archived-alert).
  - 3× `InputNumber addonAfter="VND"` → `Space.Compact block` bọc InputNumber + Button "VND" (AssetListPage Giá mua, ConsumableFormModal Unit Cost, AccessoryFormModal Đơn giá).
  - 1× lint error pre-existing `prefer-const` trong AccessoryCheckoutModal (let→const) — giảm 1 error cho `npm run lint`.

### Xác nhận API (typings antd 6.5.3, không đoán)
- Modal `destroyOnHidden?: boolean`; `maskClosable` @deprecated → `mask.closable`.
- Select/TreeSelect `popupRender?: (menu) => ReactElement` — cùng signature dropdownRender.
- Statistic `styles.content` (semantic styles); `valueStyle` @deprecated.
- Drawer `size?: 'default' | 'large' | number | string`; InputNumber `addonAfter` @deprecated → Space.Compact.

### Verify
- `npm run build` **0 lỗi TS** (chạy lại sau mỗi nhóm fix).
- `npm run lint`: các file T-DEP1 chạm đều sạch; lỗi duy nhất còn lại của file đã chạm là warning `exhaustive-deps` pre-existing (đã xác nhận bằng stash-compare). Full-repo lint: HEAD trước đó 33 errors → sau T-DEP1 32 (giảm 1 nhờ prefer-const).
- **Console thật (playwright, admin)**: đi qua Dashboard + Component List/Detail + mở cả 3 modal con (Nhập kho/Cấp phát/Thu hồi) + Drawer lịch sử serial + modal Sửa component + dropdown quick-add NSX (`popupRender` render đúng menu + input quick-add) + Asset create modal (Space.Compact VND render đúng): **0 errors / 0 warnings deprecation**.
- `maskClosable`/`Alert message`/`InputNumber addonAfter` không còn emit bất kỳ warning nào.

### ⚠️ Lưu ý
- `maskClosable` KHÔNG phải "hợp lệ ở v6" như báo cáo audit nói — antd 6.5.3 đánh dấu @deprecated → đã sửa.
- Các task UI/UX tiếp theo (T-RESP1...) làm trên nền đã sạch deprecation; khi thêm Modal/Select mới dùng ngay `destroyOnHidden`/`popupRender`.

## 62. T-RESP1 — SHARED HOOK `useIsMobile()` + BASELINE SCROLL CHO ProTable (2026-08-22)

### Thay đổi
- **Hook mới** `src/hooks/useIsMobile.ts`: `Grid.useBreakpoint()` → `screens.md === false` (desktop-first an toàn khi `screens` rỗng render đầu). Thay thế pattern lặp `const screens = useBreakpoint(); const isMobile = !screens.md;` ở **9 file**:
  - ConsumableFormModal, ComponentFormModal, AccessoryFormModal, AssetEditModal,
    UserFormModal, LicenseFormModal, GroupFormModal, MaintenanceCompleteModal, MaintenanceListPage
  (App.tsx giữ nguyên `Grid.useBreakpoint()` trực tiếp — layout shell cần nhiều breakpoint).
- **Scroll baseline cho ProTable/Table** (4 chỗ thiếu/sai):
  - `SystemInfoListPage`: bỏ comment — bật lại `scroll={{ x: 'max-content' }}` (trước đây bị tắt).
  - `UserListPage`: `x: 900` → `'max-content'`.
  - `GroupListPage`: `x: 900` → `'max-content'`.
  - `PermissionMatrixPage`: `x: 800` → `'max-content'`.

### Verify
- `npm run build` **0 lỗi TS** · eslint 10 file T-RESP1: 0 error (1 warning exhaustive-deps pre-existing).
- **375×812 thật (playwright)**: Users table scroll-x OK; Groups `.ant-table-content` scrollWidth 1164 > client 285 + overflow auto; SystemInfo scrollable (915px — trước đây bị cắt cột); PermissionMatrix scrollable. Component edit modal mobile: `width: 95%` qua hook mới, fit viewport. Console **0 errors / 0 warnings**.
- Ảnh bằng chứng: `resp1-users-375.png`, `resp1-groups-375.png`, `resp1-systeminfo-375.png`, `resp1-component-edit-375.png`.

### ⚠️ Lưu ý cho người sau
- Modal/page mới cần biết mobile → dùng `useIsMobile()` từ `src/hooks/useIsMobile.ts`, KHÔNG viết lại `useBreakpoint` thủ công.
- Table/ProTable mới luôn khai báo `scroll={{ x: 'max-content' }}` làm baseline responsive.
- T-RESP2/3/4 (mobile Card cho từng nhóm trang) sẽ dùng chính hook này — pattern mẫu `MaintenanceTable.tsx` ST7b.
- **Bổ sung verify desktop SystemInfoListPage (1440px)**: `scroll.x` bật lại KHÔNG gây lỗi desktop — scrollWidth 1076 = clientWidth 1076 (vừa khít), 1 row render đúng, console sạch. Ảnh `resp1-systeminfo-desktop-1440.png`.

## 63. T-A11Y1 — CONTRAST DASHBOARD #888 → TOKEN AA + FIX TIMELINE `items.children` DEPRECATED (2026-08-22)

### Audit Bước 0
- `DashboardPage.tsx` L144-145: timestamp activity feed + dòng "bởi …" dùng `color: '#888', fontSize: 12/11` — đo WCAG (relative luminance, nền card trắng): **#888 = 3.54:1 FAIL AA** (cần ≥4.5 với chữ thường).
- Fix đã nằm trong working tree từ phiên trước: thay bằng `textColors.secondary` (= `palette.mutedForeground` = **#475569**) — đo lại **7.58:1 PASS AA (AAA)**. Token export mới trong `designTokens.ts`: `textColors { primary: #020617, secondary: #475569, tertiary: #64748B }`.
- Grep toàn source: **0** chỗ `#888` còn lại.
- 🐛 **Phát hiện mới khi verify runtime**: console báo `[antd: Timeline] items.children is deprecated. Please use items.content instead` từ chính DashboardPage — typings antd 6.5.3 (`Timeline.d.ts` L24-25) xác nhận `children` @deprecated → `content`; useItems.js render qua fallback `content ?? children`. Báo cáo audit UI trước nói "Timeline children OK" là SAI ở runtime.

### Thay đổi (2 file)
- `DashboardPage.tsx`: `children:` → `content:` trong items của Timeline (kèm comment lý do).
- `designTokens.ts`: export `textColors` (đã có từ working tree, giữ nguyên).

### Verify (playwright-cli, admin, stack thật)
- **DOM computed color**: BEFORE = `rgb(136,136,136)` (#888) → AFTER = `rgb(71,85,105)` (**#475569**) trên cả 2 phần tử; timeline render đủ 10 items sau khi đổi sang `content`.
- **Console dashboard: 0 errors / 0 warnings** (Timeline deprecation biến mất).
- Contrast đo lại: #475569 vs trắng = **7.58:1** (AA cần 4.5, AAA cần 7 — đạt cả hai).
- `npx tsc -b` exit 0 · eslint 2 file chạm exit 0.
- Ảnh: `a11y1-dashboard-before-1440.png`, `a11y1-activity-card-before-1440.png`, `a11y1-dashboard-after-1440.png`, `a11y1-activity-card-after-1440.png`, `a11y1-dashboard-after-375.png`.

### ⚠️ Ghi nhận cho T-TOKEN1
- Label gray `#8c8c8c` còn ~14 chỗ / 10 files (icon creator, statusColors.closed…) — đo được **3.36:1**, fail AA nếu dùng cho chữ thường trên nền trắng. Icon lớn có thể chấp nhận theo AA large-text/graphics (3:1) nhưng nên chuẩn hóa token.

## 64. T-CLEAN1 — DEPRECIATIONLISTPAGE: ĐIỀU TRA NGUỒN GỐC + REFACTOR + VÁ POLICY BACKEND (2026-08-22)

### Nguồn gốc file (điều tra đầy đủ)
- Sinh ra cùng **Initial commit `6cd6ba0`**, không từng bị sửa (`git log --follow` chỉ 1 commit). Toàn bộ là **1 dòng minified 834 byte** — scaffold tự sinh/quick-port từ giai đoạn port Snipe-IT, không phải bị hỏng về sau.
- **KHÔNG phải dead code**: route `/admin/depreciations` (App.tsx L543) + sidebar "Khấu hao" nhóm QUẢN_TRỊ (L177), gate menu `depreciations.view` (permMap L129). Nhưng `ProtectedRoute` chỉ check auth → gõ thẳng URL vẫn vào page.
- Backend `AdminController.GetDepreciations` dùng `[Authorize]` **trần không policy** — đúng review #33 `BACKEND_ARCHITECTURE_REVIEW_2026-08-15` (key `depreciations.view` có sẵn trong PermissionCatalog L110 mà chưa gắn). → **lỗ hổng thật: mọi user đăng nhập đọc được dữ liệu khấu hao**.
- Read-only hoàn toàn: chỉ 1 endpoint GET, không CRUD (workflow doc §210); bảng `depreciations` không có CompanyId (reference data toàn cục, không cần company-scoping). Endpoint phải giữ: `AssetModelListPage` đổ Select khấu hao + ReportsPage báo cáo khấu hao.
- Audit cũ flag 2 lần (FRONTEND_AUDIT L210 + task T12 "chuyển ProTable + bổ sung gate") nhưng chưa từng xử lý.

### Quyết định (user chọn qua tool question)
Refactor page theo chuẩn admin chị em + gắn `[Authorize(Policy = "depreciations.view")]`. Không xóa (AssetModelListPage phụ thuộc data), không để nguyên (lỗ hổng).

### Thay đổi (2 file)
- **Backend** `AdminController.cs` L360: `[HttpGet("depreciations"), Authorize(Policy = "depreciations.view")]` (+ comment nguồn gốc). Superuser bypass sẵn qua PermissionHandler (realm role hoặc IsSuperUser local).
- **Frontend** `DepreciationListPage.tsx`: viết lại 1 dòng minified → 64 dòng chuẩn CompanyListPage pattern: ProTable (`headerTitle="Cấu hình khấu hao"`, request, options reload/density/setting), `usePermission('depreciations.view')` gate toolbar, typed columns, `scroll={{ x: 'max-content' }}`. Trang CHỈ XEM (backend read-only) nên không có nút CRUD — đúng thực tế endpoint.

### Verify bắt buộc (user yêu cầu test 2 chiều)
Tạo QA user riêng `qa-tclean1-20260822-212801` (Keycloak REST, profile đủ + requiredActions=[] để lấy JWT được):

| Test | Điều kiện | Kết quả |
|---|---|---|
| A | JWT user KHÔNG quyền nào | **HTTP 403** ✅ |
| B | JWT user CÓ `depreciations.view` (grant trực tiếp DB `user_permissions Value=1`) | **HTTP 200** `{"status":"success","data":[]}` ✅ |

- Lưu ý: JWT lifespan 5 phút — lần đầu TEST B dính 401 do hết hạn, refresh token → 200.
- **UI thật (admin superuser)**: `/admin/depreciations` render ProTable cột Tên/Số tháng, nút toolbar "Tải lại", request thật từ UI trả **200**, empty state "Trống" (bảng depreciations không seed data — đúng thiết kế), console **0 errors / 0 warnings**. Ảnh `tclean1-depreciations-after-1440.png` + `-375.png`.
- Build: `dotnet build aspire-react.Server` **0 error** · `tsc -b` exit 0 · eslint file mới exit 0.
- **Dọn sạch QA user**: Keycloak DELETE 204 · DB DELETE `users` + `user_permissions` = 1+1, còn lại 0. Không đụng tài khoản thật.

### ⚠️ Lưu ý cho người sau
- `GET /depreciations` giờ YÊU CẦU `depreciations.view` — nếu có client/script ngoài gọi endpoint này bằng token user thường không có quyền sẽ bắt đầu nhận 403 (đây chính là mục đích).
- Nếu sau này muốn CRUD khấu hao: catalog đã có sẵn `depreciations.create/edit/delete` — làm theo pattern Category/Manufacturer và bỏ ghi chú "trang chỉ xem".
- StatusLabelsListPage vẫn chưa tồn tại (menu "Trạng thái" đã bị xóa từ phase trước) — `statuslabels.view` policy trên GET /statuslabels vẫn hoạt động độc lập.

## 65. T-RESP2 (BATCH 1/2) — MOBILE CARD CHO 4 TRANG ADMIN MASTER-DATA THEO ST7b (2026-08-22)

### Pattern áp dụng (user chốt: ST7b MaintenanceTable + useIsMobile từ T-RESP1)
Mỗi trang branch theo `useIsMobile()`: **desktop giữ nguyên ProTable**, **mobile render ProList Card** — tái sử dụng:
1. **1 fetch dùng chung** (`fetchList`) cho `request` của cả ProTable lẫn ProList — không trùng code gọi API.
2. **1 `renderActions(record)` dùng chung** cho cột Thao tác (desktop) và cuối Card (mobile) — permission-gating (`usePermission`) + handler nằm MỘT chỗ.
3. **1 `formModal`** (biến JSX Modal tạo/sửa) định nghĩa một lần, render ở cả 2 branch.
4. Kèm dọn lint: `Modal.confirm` xóa → **Popconfirm trong renderActions** (confirm đúng 1 lần, đúng pattern CLAUDE.md), `catch (err: any)` → `catch (unknown)` + cast kiểu.

### 4 trang chuyển đổi
- `CategoryListPage`: card có chấm màu tagColor + tên + Tag loại + grid Màu/Chính sách; toolbar mobile có Select lọc loại + nút Thêm.
- `ManufacturerListPage`: card tên + code Tag + Website (link break-all) + Support Email.
- `SupplierListPage`: card tên + code Tag + grid Địa chỉ gộp/Người liên hệ/Điện thoại (tel:)/Email (mailto:).
- `LocationListPage`: card tên + Tag "Địa điểm con" (nếu có parentId) + địa chỉ gộp; 3 action icon (thêm con/sửa/xóa); mobile fetch flat + buildTree như desktop (ProList hiển thị phẳng, đủ thông tin qua card).
- Fix phụ LocationListPage (đã chạm file): `u: any` / `toTreeSelect: any[]` → typed interface (hếtlint `no-explicit-any`).

### ⚠️ Sự cố quy trình đã xử lý: mojibake UTF-8
Dùng PowerShell 5.1 `Get-Content -Raw` + `Set-Content -Encoding utf8` để replace text hàng loạt đã làm **hỏng ký tự tiếng Việt** (đọc ANSI ghi utf8-no-BOM) trong 4 file. Phát hiện ngay qua grep `KhÃ´|Lá»—`, xử lý bằng cách **viết lại toàn bộ 4 file bằng write tool (UTF-8 chuẩn)** — không mất thay đổi nào. Bài học: KHÔNG dùng pipeline Get-Content/Set-Content cho file chứa tiếng Việt; sửa text bằng edit tool hoặc script .NET với encoding tường minh.

### Verify (playwright-cli, admin, stack thật)
| Trang | Desktop 1440 | Mobile 375×812 |
|---|---|---|
| Categories | ProTable, 20 rows ✅ | 20 Cards; modal "Sửa danh mục" mở từ card ✅; Popconfirm "Xóa danh mục này?" hiện ✅ |
| Manufacturers | ProTable, 20 rows ✅ | 20 Cards (tên + ALLIE code + website + email) ✅ |
| Suppliers | ProTable, empty "Trống" ✅ (DB 0 NCC) | Card list empty state đúng ✅ |
| Locations | ProTable tree, 1 row ✅ | 1 Card + 3 action icons (plus/edit/delete) ✅ |

- Console **0 errors / 0 warnings** trên mọi trang cả 2 breakpoint. Request `/categories` thật 200 từ UI.
- `tsc --noEmit` exit 0 · eslint 4 file exit 0 (đã hết lỗi `no-explicit-any` cũ).
- Ảnh: `resp2-categories-375.png`, `resp2-manufacturers-375.png`, `resp2-suppliers-375.png`, `resp2-locations-375.png`, `resp2-categories-desktop-1440.png`.

### ⚠️ Lưu ý cho batch 2
- Batch 2 đề xuất: **AssetModelListPage, CompanyListPage, DepartmentListPage** (+ cân nhắc DepreciationListPage vừa refactor — thêm mobile card đơn giản vì chỉ 2 cột). SystemInfoListPage có bảng lồng expandedRowRender — phức tạp, nên tách riêng nếu làm.
- Ghi nhớ pattern: `.ant-pro-table` class cũng nằm trên root của ProList (pro-components kế thừa) — khi verify phải đếm `.ant-pro-table .ant-table` (table thật) chứ không đếm class trơn, tránh kết quả giả.

### BATCH 2/2 — AssetModel + Company + Department (+ Depreciation) — ĐÃ XONG (2026-08-22)
- `AssetModelListPage`: card tên + Tag "Yêu cầu cấp phát" + grid Số Model/Hãng SX/Danh mục/Khấu hao/EOL; giữ nguyên logic auto-requestable theo category.requireAcceptance trong modal dùng chung. Dọn `any` → typed (`CategoryLite`, `OptionItem`).
- `CompanyListPage`: mobile **flatten cây thành card phẳng** (`flattenCompanies`), công ty con có Tag "Công ty con"; giữ BUSINESS RULE chỉ root được thêm con (`parentId == null` mới hiện nút plus) + TreeSelect disable child làm cha + validate NOCO nguyên vẹn trong modal dùng chung.
- `DepartmentListPage`: card tên + Tag công ty + grid Người quản lý/Điện thoại (tel:).
- `DepreciationListPage` (vừa refactor ở T-CLEAN1): thêm mobile Card chỉ xem (tên + số tháng + ghi chú "Chỉ xem") — không có action vì endpoint read-only.
- Verify thật (playwright-cli, admin):
  - **Mobile 375**: Companies card "Công ty Quản lý bay miền Trung" + code MIRA + icons plus/edit/delete ✅; Departments empty "Trống" (DB 0 phòng ban) ✅; AssetModels 6+ cards đủ thông tin + Sửa/Xóa ✅; Depreciations empty view ✅.
  - **Desktop 1440**: cả 4 trang đều ProTable thật (`.ant-pro-table .ant-table` = 1): asset-models 9 rows, companies 1 row (tree expandable), departments/depreciations empty ✅.
  - Console desktop sạch; console asset-models mobile/desktop có **2 errors 404 `/custom-fieldsets` PRE-EXISTING** (code HEAD đã gọi với `.catch()` bảo vệ, backend chưa implement fieldsets) — KHÔNG do T-RESP2, ghi nhận backlog.
- `tsc --noEmit` exit 0 · eslint 4 file exit 0.
- Ảnh: `resp2-companies-375.png`, `resp2-departments-375.png`, `resp2-assetmodels-375.png`, `resp2-depreciations-375.png`.

### Kết luận T-RESP2
8/8 trang admin master-data giờ responsive: desktop ProTable (bảng đầy đủ tính năng) ↔ mobile ProList Card (đọc thoải mái, action touch-friendly), chung 1 fetch + 1 renderActions + 1 formModal per page. SystemInfoListPage chưa làm mobile card (có nested table phức tạp) — vẫn scroll-x ổn định từ T-RESP1; xem là backlog nếu cần.

## 66. T-RESP3 — USERS + GROUPS MOBILE CARD (2026-08-22)

### Đặc thù riêng của 2 trang này (khác 8 trang admin)
- **UserListPage** là trang DUY NHẤT dùng **ProTable search form** (`search={{...}}` — 5 field: Tìm kiếm/Email/Công ty/Trạng thái/Vai trò) — chính là phần "bẹp" ở 375px theo audit. Mobile KHÔNG chỉ đổi bảng mà phải **thay search form bằng filter bar riêng**: `Input.Search` full-width (flex 1 1 100%) + 2 Select (Trạng thái/Vai trò) xếp hàng riêng — không chèn nhau ở 375px.
- Filter mobile map thẳng sang query backend: `search` / `isActive` / `isSuperUser` (cùng param names ProTable desktop gửi).
- **GroupListPage**: cột "Quyền" desktop cắt 4 tags + "+n"; mobile card hiện **ĐỦ permission tags (wrap)** — thông tin quan trọng của trang phân quyền không bị ẩn trên mobile.

### Áp dụng ST7b như T-RESP2
- 1 `fetchUsers(query)` dùng chung (desktop truyền params ProTable, mobile truyền từ filter bar) · 1 `renderActions` (Chi tiết/Sửa/Vô hiệu hóa — Popconfirm giữ okText tiếng Việt) · form modal (`UserFormModal`/`GroupFormModal`) dùng chung ngoài branch.
- Group: tách `renderPermissionTags` dùng chung desktop (cột Quyền) + mobile (block "Quyền đã cấp").
- Fix nhỏ: `render: renderPermissionTags` thiếu signature `(_, record)` → TS2322, sửa thành arrow wrapper.

### Verify (playwright-cli, admin, stack thật)
- **Users desktop 1440**: ProTable + search form 5 fields + 1 row (System Admin) ✅. Ảnh `resp3-users-desktop-1440.png`.
- **Users mobile 375**: search form ProTable BIẾN MẤT (`searchFormVisible=false`); filter bar: Input.Search placeholder "Tên, tài khoản, email..." + 2 Select ✅; 1 card đủ Badge/Họ tên/Tag vai trò/Tài khoản/Email(mailto:)/Công ty/Chức danh/Trạng thái + 3 actions (eye/edit/delete) ✅.
  - **Filter hoạt động THẬT**: gõ "admin" Enter → `GET /users?search=admin` 200; chọn "Hoạt động" → `GET /users?search=admin&isActive=true` 200 ✅.
- **Groups mobile 375**: 2 cards (Admin + Nhóm thường); card Admin có Tag "Hệ thống" + "0 thành viên" + mô tả + **ĐỦ permission tags wrap** (accessories.checkout… assets.check…) + actions edit/delete (delete disabled cho nhóm hệ thống) ✅. Ảnh `resp3-groups-375.png`.
- **Groups desktop 1440**: ProTable 2 rows, cột Quyền render tags ✅. Ảnh `resp3-groups-desktop-1440.png`.
- Console 0 errors / 0 warnings mọi bước. `tsc` exit 0 · eslint 2 file exit 0.

### ⚠️ AGENTS.md đã bổ sung quy tắc cứng (lần mojibake thứ 3)
Mục mới "⛔ CẤM sửa file chứa tiếng Việt qua PowerShell" — liệt kê RÕ lệnh/API bị cấm (Get-Content/Set-Content mọi biến thể, -replace, [regex]::Replace, ReadAllText không tường minh Encoding), chỉ cho phép edit/write tool hoặc .NET API với `Encoding.UTF8` tường minh + bắt buộc grep mojibake-pattern sau mọi thao tác hàng loạt.

### ⚠️ Lưu ý cho người sau
- ProList root mang class `.ant-pro-table` (pro-components kế thừa) — verify phải đếm `.ant-pro-table .ant-table` (table thật).
- Khi thêm trang list mới có search: mobile phải có filter bar tương đương (không được để mất chức năng tìm kiếm khi ẩn search form ProTable).
- Cột "Quyền" GroupListPage desktop đổi `ellipsis: true` → `false` (tags wrap trong cell, đọc được đầy đủ hơn mà không phá layout vì đã scroll-x).

## 67. T-RESP4 — ACTIONLOGTABLE + LICENSEUSAGETABLE RESPONSIVE TẠI COMPONENT DÙNG CHUNG (2026-08-22)

### Nguyên tắc (user chỉ thị rõ)
useIsMobile áp dụng **ĐÚNG 1 CHỖ trong shared component** — KHÔNG sửa lặp lại từng Detail page:
- `shared/components/ActionLogTable.tsx`: thêm branch `isMobile` → **ProList Card** (Tag hành động + thời gian vi-VN + itemName + grid Người thực hiện/target + Chi tiết gộp từ formatLogDetail). Desktop giữ nguyên ProTable (tableLayout fixed + numeric scroll-x như cũ). `request`/`params`/`pagination`/`emptyText` truyền thẳng từ caller → mọi trang nhúng cùng hưởng.
- `shared/components/LicenseUsageTable.tsx`: mobile Card (license + seat# + Ngày cấp/Hết hạn tag màu/Ghi chú); desktop Table nguyên vẹn. Dùng bởi AssetDetail/UserDetail/SystemDetail.

### 2 trang Detail có ProTable lịch sử RIÊNG (trùng logic ActionLogTable) → chuyển sang dùng chung
- `ConsumableDetailPage` tab "Lịch sử hoạt động": ProTable local (`actionLogColumns`) + request `/action-logs?itemType=2&itemId=` → thay bằng `<ActionLogTable targetColumnTitle="Nội dung">`. Xóa actionLogColumns + interface ActionLogItem + import dư.
- `AccessoryDetailPage` tab "Lịch sử hoạt động": tương tự (có block parse logMeta riêng ~30 dòng) → thay bằng `<ActionLogTable>`; format logMeta giờ dùng `formatLogDetail` shared (hỗ trợ cả changes lẫn legacy top-level).
- ⚠️ Hành vi desktop: cột "Chi tiết" của ActionLogTable tổng hợp Vị trí/Hệ thống/logMeta/note — giàu hơn bản local cũ (chỉ note+logMeta); chấp nhận vì đây là chuẩn hiển thị chung của Asset/System history từ trước.
- Fix nhỏ khi chuyển: lần đầu đặt `targetColumnTitle="Người thực hiện"` gây header trùng cột → đổi thành "Nội dung" đúng tên cột cũ.

### Verify (playwright-cli, admin, stack thật)
- **Consumable "Ke góc chữ L 6 lỗ" (3 logs)**: mobile 375 tab "Lịch sử hoạt động" → **3 Cards** (Cập nhật/Xác nhận/Tạo mới + thời gian vi-VN + Người thực hiện), 0 realTable ✅. Desktop 1440 → ProTable trở lại: headers [Thời gian, Hành động, Người thực hiện, Nội dung, Chi tiết], 3 rows ✅.
- **Consumable thứ 2 (Epson LQ-300, 2 logs)**: desktop ProTable 2 rows ✅ → resize 375 reload → 2 Cards ✅ (cùng 1 component shared, KHÔNG đụng thêm code trang).
- Asset detail (asset chưa có log): section "Lịch sử" empty state render OK.
- SystemHistoryPage: chọn hệ thống RDP-2026-001 → `/action-logs/by-system` 200; filter Hành động=Cấp phát gửi đúng `actionType=4` (0 kết quả do dữ liệu thật của hệ thống này thuộc loại khác — không phải bug UI).
- Console 0 errors/0 warnings. `tsc` exit 0 · eslint exit 0 (2 warning react-refresh PRE-EXISTING từ HEAD — file export constants + default component cùng lúc).
- Ảnh: `resp4-consumable-logs-375.png`, `resp4-consumable-logs-desktop-1440.png`, `resp4-consumable2-logs-375.png`.

### ⚠️ Lưu ý cho người sau
- Detail page mới cần bảng lịch sử audit → NHÉT vào `ActionLogTable` (props: request/targetColumnTitle/pagination/emptyText) — KHÔNG viết ProTable riêng nữa; mobile được miễn phí.
- Bảng phụ khác trong Detail (checkout history Consumable/Accessory, units Component, positions SystemInfo, bảng Bảo trì section) vẫn là Table thường scroll-x — nếu card hóa tiếp thì theo pattern LicenseUsageTable; ngoài phạm vi task này.

## 68. FIX BUG TIÊU ĐỀ RENDER DỌC TRÊN MOBILE (375px) — 4 TRANG DETAIL (2026-08-22)

### Bước 0 — Điều tra (đo DOM thật, không đoán)
Triệu chứng: "Chi tiết vật tư" (ConsumableDetail) hiển thị mỗi ký tự 1 dòng, cao ~308px, đẩy nút Quay lại/Cấp phát/Sửa méo.

**Kết luận 1 — KHÔNG phải bug mới từ T-RESP4:** git diff HEAD..working của ConsumableDetailPage chỉ đụng tab "Lịch sử hoạt động" + imports — header giữ NGUYÊN từ HEAD (pre-existing). Bằng chứng phụ: AccessoryDetailPage (header cũng không bị T-RESP4 sửa) lỗi y hệt; Component/System/User (cấu trúc header khác) thì không.

**Kết luận 2 — 2 root cause CSS độc lập:**

| Root cause | Cơ chế | Trang bị |
|---|---|---|
| **A — header flex `nowrap`** | `<div style="display:flex;alignItems:center;gap:16">` (không flexWrap) chứa Quay lại + Title + Tag + spacer `flex:1` + Space(2 nút). Tổng > 375px → flex-shrink ép item; Title antd có `word-break: break-word` (đo: `titleW=15.5px, titleH=308px, headerH=1649px`) → min-content = 1 glyph → render dọc | ConsumableDetailPage, AccessoryDetailPage |
| **B — Descriptions column cứng** | `column={2}`/`column={4}` số nguyên trên mobile 335px → cell content dài bị nén (`Component cell "Tủ #1 - Phòng T&E" 44×303px dọc`; `Asset Model "Firewall FortiGate 40F" 12×370px dọc`) | AssetDetailPage (column=2), ComponentDetailPage (column=4) |

Không bị: SystemDetailPage (`column={{xs:1,sm:2,md:3}}` ✓), UserDetailPage (đo 0 squashed cell dù column={3} — nội dung ngắn), Consumable/Accessory Descriptions (đã `{xs:1,sm:2}` ✓).

### Sửa (4 file)
- `ConsumableDetailPage` + `AccessoryDetailPage` header: thêm `flexWrap: 'wrap'` (+ comment root cause).
- `ComponentDetailPage` L261: `column={4}` → `column={{ xs: 1, sm: 2, md: 4 }}`.
- `AssetDetailPage` L125: `column={2}` → `column={{ xs: 1, sm: 2 }}` + Ghi chú `span={2}` → `span={{ xs: 1, sm: 2 }}`.
- Bonus (cùng file, console thật phát hiện): `Steps items.description` deprecated → `content` (AssetDetailPage Vòng đời) — 0 lỗi console.

### Verify (playwright-cli, admin, 375px — đo lại sau sửa)
| Trang | TRƯỚC | SAU |
|---|---|---|
| Consumable | title 15×308, headerH 1649px | title **130×28 ngang**, headerH **80px**, 3 nút nguyên vẹn (108/116/80×32) ✅ |
| Accessory | title 15×364 | title **155×28 ngang**, headerH 80px ✅ |
| Component | cell "Tủ #1 - Phòng T&E" 44×303 dọc | **0 squashed cell** ✅ |
| Asset | Model 12×370 dọc | **0 squashed cell** ✅ |

- Desktop 1440 (no-regression): Descriptions Asset vẫn 2 cột (390/275px) ✅; console asset 0 errors/0 warnings (Steps fix).
- Ảnh before/after: `titlebug-{consumable,accessory,component,asset}-{before,after}-375.png` (8 ảnh).
- `tsc` exit 0 · eslint 4 file exit 0.

### ⚠️ Lưu ý cho người sau (anti-pattern)
- Header detail page: LUÔN có `flexWrap: 'wrap'` khi là flex ngang chứa title + nút (title Typography có word-break → bị bóp 1 glyph nếu nowrap).
- Descriptions ở trang (không phải modal cố định rộng): dùng `column={{ xs: 1, sm: 2, md: n }}` — KHÔNG bao giờ `column={n}` cứng; item span kèm responsive `span={{ xs: 1, sm: 2 }}`.
- Backlog: LicenseDetailModal `column={3}` + MaintenanceTable modal `column={2}` — modal antd mobile ~343px cũng có thể dọc cell nếu text dài; chưa verify (data license đang rỗng).

## 69. T-TOKEN1 — EXPORT PALETTE DÙNG CHUNG TỪ designTokens.ts (2026-08-22)

### Audit trước (27 hex literal UI rải rác / 14 file)
4 nhóm màu lặp lại: `#8c8c8c` (icon label gray — 13 chỗ code), `#fa8c16` (warning — 5 chỗ), `#f6ffed/#b7eb8f` (stock card — 2 chỗ), 3 biến thể `linear-gradient` badge card (Component/License/Maintenance: `#f0f5ff→#adc6ff`; Consumable: `#e6f4ff→#bae0ff`; Accessory: `#f0e6ff→#d4baff`).

### Thay đổi
- **`designTokens.ts`**: mở rộng `palette` (labelGray/warningAmber/stockSuccessBg/stockSuccessBorder) + 2 export semantic mới:
  - `uiColors = { labelGray, warningAmber, stockSuccessBg, stockSuccessBorder }` — giá trị NGUYÊN BẢN (không đổi tông; đổi màu cho AA là quyết định thiết kế riêng).
  - `cardBadgeGradients = { blue, lightBlue, purple }` — gradient badge card (1 nguồn duy nhất, hết lệch tông).
- **14 file** thay hex literal → token (ActionLogTable, AccessoryDetail/List/CheckinModal, ComponentList, ConsumableList/Detail, LicenseList, MaintenanceTable, AssetRecallModal, AssetArchiveModal, AssetEditModal, DashboardPage). Gradient động `hexToRgba(...)` (License/Maintenance badge theo màu dữ liệu) giữ nguyên — không token hóa được.

### Verify
- `tsc` exit 0 · eslint 0 errors (9 warning react-refresh pre-existing — chỉ 2 file export constants+component từ HEAD).
- **UI thật (playwright, admin)**: Dashboard statistic "Sắp hết" = `rgb(250,140,22)` (#fa8c16) ✅; Component card badge = `linear-gradient(135deg, rgb(240,245,255), rgb(173,198,255))` ✅; Accessory = `rgb(240,230,255)→rgb(212,186,255)` ✅; label icon gray = `rgb(140,140,140)` ✅. Console 0 errors/0 warnings. → **Màu render y hệt trước khi token hóa (0 lệch tông).**
- Mojibake scan 14 file: 0.

### ⚠️ Lưu ý
- AssetListPage L129 nền icon badge ĐỘNG theo status (#e6f4ff/#f6ffed/#f5f5f5) — khác ngữ nghĩa card gradient, để nguyên.
- MaintenanceTable L76 comment `// #8c8c8c` (chú thích giá trị statusColors.closed) — giữ.
- Mọi màu UI mới → thêm vào `uiColors`/`cardBadgeGradients`/`palette`, KHÔNG khai báo hex literal trong page.

## 70. T-UX1 — CLICK-TO-DETAIL MAINTENANCETABLE CARD + STOPPROPAGATION (2026-08-22) — KẾT THÚC CHUỖI UI/UX

### Thay đổi (`MaintenanceTable.tsx`, pattern ComponentListPage đã verify)
- **Card toàn phần**: `onClick={() => void handleDetail(record)}` + `cursor: 'pointer'` → click bất kỳ đâu trên card mở modal "Chi tiết bảo trì" (maintenance không có route detail riêng — modal là detail chuẩn của bản ghi).
- **Mọi nút trong `renderActions`** thêm `e.stopPropagation()`: Chi tiết, Mở tài sản, Hoàn thành, Đánh dấu đã kiểm tra, Xác nhận đóng (Popconfirm trigger + bản disabled), Mở lại, Xóa (Popconfirm trigger).
- **Link asset trong header card** thêm `onClick={(e) => e.stopPropagation()}` — click tên asset navigate sang asset, KHÔNG mở nhầm detail modal.

### Verify (playwright-cli, admin — tạo bản ghi QA thật qua API, xóa sau)
| Thao tác | Kết quả |
|---|---|
| Click toàn card (375px + 1440px) | Modal "Chi tiết bảo trì" + Descriptions mở ✅ |
| Nút "Hoàn thành" | Chỉ mở modal "Hoàn thành bảo trì", KHÔNG detail, path giữ /maintenances ✅ |
| Nút "Mở tài sản" | Navigate `/assets/{id}`, 0 modal ✅ |
| Nút "Xóa" (Popconfirm) | Mở confirm "Xóa bản ghi bảo trì này?", KHÔNG detail, không xóa nhầm ✅ |
| Link tên asset trong card | Navigate asset, 0 modal ✅ |
| cursor | `pointer` trên card ✅ |

- Console ổn định 0 errors/0 warnings (1 error thoáng qua giữa chuỗi click nhanh — không tái hiện khi reload).
- `tsc` exit 0 · eslint 0 errors (7 warning react-refresh pre-existing) · mojibake scan 0.
- Ảnh: `ux1-maintenance-detail-modal-1440.png`, `ux1-maintenance-card-375.png`.
- **Dọn QA**: DELETE bảo trì 200 + xóa 2 action_logs QA-UX1 (Tạo/Xóa) — DB về 0 bảo trì, 0 log QA.

### Kết thúc chuỗi UI/UX
Toàn bộ 7 task (T-RESP1→4, T-A11Y1, T-CLEAN1, T-TOKEN1, T-UX1) + fix bug title dọc (4 trang detail) đã xong, verify thật từng phần. Sẵn sàng commit GỘP DUY NHẤT theo quy trình an toàn (git status → secret grep → .gitignore → push → verify SHA).

## 63. SEC-FIX KHẨN CẤP: CS-3 LEO THANG ĐẶC QUYỀN + CS-7 LỘ LOG CHÉO CÔNG TY + CI-1 CI LUÔN ĐỎ (2026-08-23)

### Nguồn gốc phát hiện
- **2 vòng review độc lập** (company-scoping 22 controller; patch-safety/CI) + **1 vòng hợp nhất** + **1 vòng xác minh thực nghiệm** — mọi lỗ hổng đều được tái hiện bằng API thật trước khi vá (HTTP thật, không suy luận từ code tĩnh).
- CS-3 từng tấn công thành công: user Công ty A gán nhóm **Admin** cho user Công ty B → **200 + persist** (trong khi GET /users của A không thấy B — read scoped, write không).
- CS-7 từng tấn công thành công: user A đọc `GET /action-logs/by-system` của hệ thống Công ty B → **200 kèm toàn bộ log + tên asset/vị trí/địa điểm** (`AST-QCRC-2026-001 - ...`).
- CI-1: **8/9 lần chạy CI liên tiếp đỏ** đúng step "Run unit tests" (GitHub API) vì ConcurrencyRaceAuditTests cần live stack mà runner không có.

### Fix 1 — CS-3 (`UsersController.UpdateUserGroups`)
- Thêm company-scoping ngay sau khi load target user (trước mọi logic khác), **copy nguyên pattern UpdateUser/DeleteUser** (:444-450/:503-512): `actorCompanyScope = GetCurrentUserCompanyIdAsync()`; khác scope → **404 "User not found."** (hide existence). Superuser (null) bypass.
- File: `aspire-react.Server/Web/Controllers/UsersController.cs` (block `[SEC-FIX CS-3, 2026-08-23]`).

### Fix 2 — CS-7 (`ActionLogsController.GetBySystem`)
- Thay gate NO-OP dùng `GetUserCompanyIdsAsync()` (placeholder luôn trả `[]` → `Count == 0` luôn đúng) bằng `GetCurrentUserCompanyIdAsync()` — cùng pattern `IsItemVisibleAsync` đã đúng. Superuser (null) xem tất cả; user thường chỉ thấy hệ thống floater/cùng công ty; out-of-scope → 404.
- `GetUserCompanyIdsAsync` **không xóa** (còn được tham chiếu ở `AppDbContext.cs:533-534` global filter no-op + `SystemsController.cs:42`) — thay vào đó thêm ⚠️ cảnh báo XML-doc trên interface + implementation (`CompanyScopeService.cs`) rằng đây là placeholder và cấm dùng cho gate isolation.
- File: `aspire-react.Server/Web/Controllers/ActionLogsController.cs`, `.../Infrastructure/Services/CompanyScopeService.cs`.

### Fix 3 — CI-1 (`.github/workflows/ci.yml`)
- Step "Run unit tests" thêm `--filter "Category!=Concurrency"` + comment giải thích lý do + hướng dẫn chạy tay khi stack sống. **Không đụng** 2 chỗ `|| true` (format/lint) — backlog riêng (B9/CI-2).

### Verify thực nghiệm (stack Aspire thật, binary mới sau restart)
| Kịch bản | Trước fix | Sau fix |
|---|---|---|
| qa-fix-a (Cty MIRA) PUT groups của qa-fix-b (Cty QCR-CO) | 200 + persist | **404 "User not found."** ✅ |
| qa-fix-a tự gán nhóm cho chính mình (same-company) | 200 | **200** ✅ (không phá chức năng hợp lệ) |
| Superuser gán nhóm cho bất kỳ ai | 200 | **200** ✅ (bypass đúng) |
| qa-fix-a GET by-system hệ thống Cty B | 200 + log | **404** ✅ (message y hệt case system không tồn tại — không lộ tồn tại) |
| qa-fix-b (chủ Cty B, đủ quyền) xem log hệ thống mình | 200 | **200** ✅ đầy đủ log |
| Superuser GET by-system | 200 | **200** ✅ |

- Build Release + Debug 0 lỗi · fast suite `--filter "Category!=Concurrency"`: **333/333 PASS** (0 regression).
- Dữ liệu QA (2 user + asset/location/system/position + assignments + action_logs) đã dọn sạch cả DB lẫn Keycloak (verify: users=1 admin; companies=2 nguyên vẹn).

### ⚠️ Phát hiện phụ trong lúc verify (chưa fix — backlog)
- `DELETE /system-infos/{id}` trả **500** khi xóa system còn ActionLog tham chiếu qua `TargetSystemInfoId` (đã dọn bằng SQL trực tiếp). Cần điều tra FK/handler delete-guard cho ActionLog references.
- Xác minh thêm (phiên trước): JIT provisioning không gán CompanyId cho user mới (floater) + secret Keycloak dev là literal placeholder `${KEYCLOAK_BACKEND_CLIENT_SECRET}` (AppHost/compose không truyền env vào container Keycloak) + 14 vị trí fallback claim (PermissionHandler có fallback fail-closed 🟡) — đã liệt kê trong báo cáo hợp nhất 2026-08-23, chờ backlog riêng.

## 64. SEC-FIX JIT-COMPANYLESS: Siết quyền user company-less (Hướng B) (2026-08-23)

### Vấn đề (đã xác minh thực nghiệm 2026-08-23)
- JIT provisioning (`JitUserProvisioningService.cs:69-78`) tạo user mới KHÔNG gán CompanyId → user đăng nhập lần đầu là **company-less**.
- Token Keycloak **KHÔNG có nguồn thông tin company nào** (decode thật: chỉ profile + realm_access.roles; realm JSON 0 protocolMappers, 0 groups) → JIT **không thể** tự gán company (Hướng A bất khả thi).
- Lỗ hổng thật: `GetCurrentUserCompanyIdAsync()` trả `null` cho CẢ superuser lẫn user company-less → pattern `userCompanyId == null || ...` (42 chỗ) biến company-less thành "thấy mọi dữ liệu mọi công ty". **Đã chứng minh bằng API thật**: user company-less + Admin quyền → `GET /assets` trả 19/19 assets mọi công ty + `GET /companies` trả cả 2 công ty.
- `IsCompanyIdInUserScopeAsync` (`CompanyScopeService.cs:104`) cũng cho company-less → true với mọi công ty (import vào bất kỳ đâu).

### Quyết định (user duyệt 2026-08-23)
1. **Hướng B**: giữ JIT floater (không đoán company — an toàn), siết quyền company-less.
2. **GET /companies**: user company-less **VẪN xem được cây công ty** (để UI chọn khi được gán company) — chỉ siết dữ liệu công ty cụ thể.
3. **Phạm vi HẸP**: sửa ở service layer, không sửa 42 pattern.

### Thay đổi
- **`CompanyScopeService.GetCurrentUserCompanyIdAsync`**: giờ trả **3 trạng thái phân biệt** — superuser → `null` (sees all); user có company → company id; **user thường company-less → `Guid.Empty`** (sentinel). Nhờ đó **42 pattern `userCompanyId == null || ...` tự động chuyển thành floater-only cho company-less** mà không cần sửa từng chỗ (floater-to-floater giữ nguyên).
- **`CompanyScopeService.IsCompanyIdInUserScopeAsync`**: company-less (không superuser) → **false** (không import vào công ty cụ thể nào cho tới khi được gán company).
- **`CompaniesController.GetAll`** + **`CompanyScopeCachePolicy.ResolveScopeKeyAsync`**: đồng bộ — company-less (Guid.Empty) → vẫn full tree + cache key `"all"` (giữ duyệt #2).
- **`TestHelpers.FakeScope`** (test): phản ánh service mới (company-less → Guid.Empty; IsCompanyIdInUserScopeAsync company-less → false).
- **Test mới (+2)**: `CompanyScopeTests.RegularUserWithoutCompany_ReturnsGuidEmpty`, `ImportCompanyScopeTests.CompanyLessUser_TargetingAnyCompany_OutOfScope`.

### Verify thực nghiệm (API thật, stack Aspire sau restart, user QA riêng)
| Kịch bản | Trước fix | Sau fix |
|---|---|---|
| User company-less + Admin quyền `GET /assets` | **19/19** assets (mọi công ty) | **0** asset công ty cụ thể (HTTP 200, totalItems 0) ✅ |
| User company-less `GET /companies` | 2 công ty | **2 công ty** ✅ (duyệt #2 giữ cây) |
| Superuser `GET /assets` | 19 | **19** ✅ (bypass đúng — phân biệt realm role trước CompanyId, không khóa admin) |
| User CÓ company (MIRA) + Admin quyền `GET /assets` | 14 | **14/14 assets MIRA** (AST-MIRA + TE-AST đều MIRA), **chặn 5/5 QCR** ✅ (floater-to-floater giữ nguyên) |
| `IsCompanyIdInUserScopeAsync` company-less → import | true (mọi công ty) | **false** ✅ (test xUnit) |

- Build Release + Debug 0 lỗi · fast suite **335/335 PASS** (333 + 2 test mới).
- Dữ liệu QA (qa-jitb, qa-hasco + groups + logs) đã dọn sạch DB + Keycloak (verify: users=1 admin, companies=2, assets=19 nguyên vẹn).

### Ghi chú backlog
- **Refactor 42 pattern sang scope-state rõ ràng** (bỏ hẳn "null = sees all" khỏi codebase) — task riêng, không làm trong fix này (fix đã đạt hiệu quả qua sentinel Guid.Empty).
- `GetUserCompanyIdsAsync` placeholder vẫn giữ (AppDbContext global filter + SystemsController tham chiếu) — đã có cảnh báo do-not-use.

## 65. SEC-FIX CLAIM-CLEANUP: Dọn 14 vị trí fallback claim (2026-08-23)

### Vấn đề
14 vị trí đọc identity user với fallback sang Keycloak `sub`/`preferred_username` (bug-class 1 — `sub` là Keycloak UUID ≠ local id, parse thành công → id SAI; lookup username → mất quyền khi rename/case). Đã xác minh là **latent** (JIT luôn stamp `local_user_id` trước khi PermissionHandler/controller chạy → fallback không bao giờ kích hoạt), nhưng là code nguy hiểm nếu sau này thêm luồng auth khác.

### Danh sách 14 vị trí đã sửa
- **Nhóm A (9, parse sub→Guid — nguy hiểm):** CompaniesController, AdminController, ComponentsController, DepartmentsController, SystemInfoController, SystemConfigController, ComponentUnitsController, CustomFieldsController, ImportExportController — private `GetCurrentUserId()` giờ **chỉ đọc `local_user_id`, trả `Guid.Empty` khi vắng** (mẫu CompanyScopeService).
- **Nhóm B (5, lookup username — an toàn hơn):**
  - `PermissionHandler.cs` — bỏ nhánh fallback username/sub; claim vắng → `context.Fail()` (fail-closed).
  - `PermissionsController.cs` (GET /permissions/check) — claim vắng → `Unauthorized()`.
  - `UsersController.cs` (GET /users/me) — claim vắng / user không tồn tại → `Unauthorized()`; bỏ luôn khối null-check trùng lặp.
  - `ConsumablesController.cs` (GetCurrentUserIdAsync) — bỏ lookup username → Guid.Empty.
  - `ActionLogService.cs` (LogAction resolve + GetCurrentUserIdAsync) — bỏ lookup username → Guid.Empty (log skip thay vì ghi sai người).

### Verify
- **Sweep:** grep `ClaimTypes.NameIdentifier|"sub"` toàn Server = **0 match**; `preferred_username` còn 17 match nhưng **đều là comment** + 1 hợp lệ (JIT — nguồn identity chính thức để tìm/tạo user local).
- **Fast suite: 335/335 PASS** (không regression — không test nào cover nhánh fallback, xác nhận code chết).
- **API thật (stack sau restart):**
  - Login admin thường → `/users/me` 200 đầy đủ + `/permissions/check` 200 đủ quyền ✅
  - Service-account token → `/permissions/check` 200 fail-closed ✅
  - **User JIT MỚI login lần đầu** (tạo qua Keycloak trực tiếp, không qua API) → `/users/me` 200 (local user vừa tạo) + `/permissions/check` 200 rỗng ✅ — chứng minh việc bỏ fallback không đổi hành vi thật (JIT stamp claim trước khi resolve).
- **Dọn dẹp:** user QA (qa-cl-*) + service-account-backend-service (JIT tạo nhân test service-account) đã xóa DB + Keycloak — users về 1 (admin).

## 66. SEC-FIX S1: Scope Maintenance Update (2026-08-23)

### Lỗ hổng (đã xác nhận ở review kiến trúc gốc + tái hiện thực nghiệm)
- `AssetMaintenancesController.Update` (:343) **không có company-scope check nào** — trong khi Create/Detail/Close/Inspect cùng controller đều có. User công ty A có `assets.edit` sửa được record của công ty B theo id (kể cả thay assignee).
- ⚠️ **Đối chiếu pattern thật:** Close/Inspect dùng `Forbid()` (403) chứ KHÔNG phải 404 như báo cáo review gốc ghi ("404 y hệt Close/Inspect"). Quyết định: copy **đúng điều kiện scope** (`userCompanyId.HasValue && m.CompanyId != userCompanyId.Value && m.CompanyId != Guid.Empty`) + trả **404** theo đúng yêu cầu verify (hide existence, nhất quán phần còn lại dự án). Ghi nhận bất nhất nội bộ module (Update 404 vs Close/Inspect 403) → backlog thống nhất CS-9.

### Thay đổi
- `AssetMaintenancesController.Update` — thêm block scope ngay sau khi load `m` (trước `IsClosed` check): user thường chỉ sửa record công ty mình hoặc floater (`CompanyId == Guid.Empty`); out-of-scope → `404 "Maintenance not found."` (hide existence — không phân biệt với not-found). Superuser bypass.

### Verify thực nghiệm (API thật, user QA qa-s1-a/b)
| Kịch bản | Trước fix | Sau fix |
|---|---|---|
| qa-s1-a (Cty MIRA) PUT maintenance Cty QCR — *tấn công gốc* | sửa được | **404** (y hệt case maintenance không tồn tại) ✅ |
| qa-s1-b (chủ Cty QCR) PUT maintenance QCR | 200 | **200** ✅ |
| qa-s1-b PUT maintenance MIRA (khác công ty) | sửa được | **404** ✅ |
| Superuser PUT bất kỳ | 200 | **200** ✅ |

- Build Release + Debug 0 lỗi · fast suite **335/335 PASS** · dọn QA: 2 maintenance + 2 user (DB + Keycloak) đã xóa — users về 1 (admin), companies/assets nguyên vẹn.
- ⚠️ **Phát hiện phụ:** còn 1 maintenance `QA-UX1 click-to-detail test` (Cty MIRA) trong DB — dữ liệu test cũ từ task T-UX1 (§62 ghi "DB về 0 bảo trì" nhưng thực tế còn sót). Không tự ý xóa (không thuộc task này) — ghi backlog dọn.

## 67. SEC-FIX S2/S4-S6: Actor-scope cho allocation services (Consumable/Component/Accessory) (2026-08-23)

### Lỗ hổng (cùng lớp lỗi — sweep 1 lần)
Các action endpoint checkout/checkin/allocate chỉ validate **target↔record** company, KHÔNG validate **actor↔record**: user công ty A thao tác được vật tư/linh kiện/phụ kiện công ty B nếu biết id (đã tái hiện thực nghiệm từng domain).

### Thay đổi (copy pattern có sẵn trong từng domain)
- **Component** (`ComponentAllocationService`): pattern mẫu ĐÃ CÓ sẵn ở `SetUnitStatusAsync`/`DeleteUnitAsync` cùng service (`_companyScope.GetCurrentUserCompanyIdAsync()` + khác company → `NOT_FOUND`) → áp cho `AllocateAsync`/`ReturnAsync`/`StockInAsync` (3 hàm thiếu).
- **Accessory** (`CheckoutAccessoryCommand`/`CheckinAccessoryCommand`): pattern mẫu ĐÃ CÓ ở `DeleteAccessoryCommand` cùng domain → inject `ICompanyScopeService` + check actor↔accessory (checkin dùng `co.Accessory?.CompanyId`).
- **Consumable** (`ConsumableAllocationService.CheckoutAsync`): chưa có mẫu trong domain → dùng helper chung `ICompanyScopeService` (inject mới) + check actor↔consumable.
- **Test constructors** cập nhật arg scope mới: ConsumableTests (9), AccessoryTests (14), TaskK/TaskL2/TaskM1 (ConsumableAllocationService).

### Verify thực nghiệm (API thật, user QA qa-s2-a MIRA / qa-s2-b QCR, dữ liệu QCR tạo mới)
| Domain | Thao tác | Tấn công MIRA→QCR | Chủ QCR |
|---|---|---|---|
| Consumable | checkout | **400 NOT_FOUND** (trước: 200) ✅ | **200** ✅ |
| Component | assign (allocate) | **400 NOT_FOUND** ✅ | **200** ✅ |
| Component | stock-in serial | **400 NOT_FOUND** ✅ | **200** ✅ |
| Component | checkout serial | (tấn công qua assign đã chặn) | **200** ✅ |
| Component | checkin | **400 NOT_FOUND** ✅ | **200** ✅ |
| Accessory | checkout | **400 NOT_FOUND** ✅ | **200** ✅ |
| Accessory | checkin | (tấn công cần checkout trước — bị chặn ở checkout) | **200** ✅ |

- ⚠️ **Lưu ý status code:** các service trả `NOT_FOUND` nhưng controller map thành **400** (RunTransactional/controller switch) thay vì 404 — hide-existence vẫn đúng (message/error_code giống hệt case không tồn tại) nhưng lệch convention 404. Đây là vấn đề riêng đã có trong backlog (Y2/Y3/CS-10/CS-11 — thống nhất 404-vs-400), KHÔNG thuộc phạm vi S2/S4-S6.
- Build Release + Debug 0 lỗi · fast suite **335/335 PASS** · dọn QA sạch (consumable/accessory/component QCR mới + 2 user DB + Keycloak) — users về 1 (admin).














