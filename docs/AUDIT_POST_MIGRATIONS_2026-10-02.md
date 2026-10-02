# AUDIT SAU MEDIATR MIGRATION + AUTH MIGRATION — 2026-10-02

> Phạm vi: 8 mục theo yêu cầu (Clean Architecture, CQRS/MediatR, Company-scoping, Patch-safety,
> Auth system, Frontend FDA, Backlog, Docs). READ-ONLY — không sửa code nào ngoài việc tạo file
> báo cáo này. Mọi kết luận có bằng chứng file:line / grep / live API. Fixture QA tạo+xóa qua API
> đúng §8 (INCIDENT-1 rule), KHÔNG dùng SQL.

## Trạng thái môi trường trong phiên

- Stack: AppHost chạy lại giữa phiên vì **Docker engine chết lần 5 (INFRA-1 tái diễn)** —
  `docker version` mất Server section giữa lúc chạy live test; restart Docker Desktop ~5s lên lại,
  volume `postgres-data` nguyên vẹn (fixtures sống sót).
- CI evidence: `dotnet test --filter "Category!=Concurrency"` → **455/455 PASS** (4
  ConcurrencyRaceAuditTests cần live stack — đã bị dừng lúc build, không phải regression);
  `npm run lint` → **0 error** (11 warning pre-existing); `npm run build` → OK 7.61s;
  `scripts/audit-sweeps.ps1` → **0 violations**.
- Live test fixture: 2 companies (QA-AUD8 A/B) + group + 5 users (userA/userB/gate/super/lockuser)
  + location + asset — tạo qua API, dọn qua API. Tồn dư có chủ đích (xem §Cleanup).

---

## MỤC 1 — CLEAN ARCHITECTURE: PASS (2 ghi nhận)

### 1a. Domain purity — PASS TUYỆT ĐỐI
- `aspire-react.Domain.csproj`: 0 ProjectReference + 0 PackageReference (chỉ PropertyGroup net10.0).
- Grep 82 file .cs: `using Microsoft.EntityFrameworkCore` / `using Microsoft.AspNetCore` / `Npgsql` /
  `MediatR` → **0 match**. 23 match chữ "Infrastructure" đều là XML doc comments (grep
  `using.*Infrastructure` → 0 code).

### 1b. Application dependencies — PASS, 1 điểm cần cập nhật ghi chú duyệt
- csproj ref duy nhất Domain; packages đúng ngoại lệ đã duyệt (EF Core provider-independent,
  Npgsql, QRCoder, FluentValidation, MediatR).
- `\.Infrastructure\.` → 0 match; `Microsoft.AspNetCore` → 1 match là comment
  (Labels/Queries/GenerateLabelsQuery.cs:17).
- Npgsql 2 match code: CampaignResultCommands.cs:160 exception-catching thuần (đúng ngoại lệ
  BUG-D); **CreateCampaignCommand.cs:163** `new Npgsql.NpgsqlParameter(...)` cho
  `FromSqlRaw(... FOR UPDATE)` — KHÔNG mở ADO connection (đi qua EF DbSet, 0 NpgsqlConnection
  trong toàn Application) → không vi phạm dependency direction, nhưng **vượt phạm vi ghi chú
  "Npgsql CHỈ cho exception-types"** trong csproj — cần cập nhật ghi chú duyệt.
- IPasswordHasherService: 3 file đều qua contract `Domain.Interfaces` ✓.

### 1c. Controller thinness — 22 THIN / 5 hybrid chủ đích / 3 FAT
- **22 THIN** (chỉ IMediator; Auth thêm IAuthCookieService = HTTP concern hợp lệ).
- **5 hybrid** (hành vi 100% thin, ctor giữ dead deps theo comment chủ đích "tests + DI, xóa là
  churn"): Assets, AssetMaintenances, ImportExport, MaintenanceTemplates, MaintenanceCampaigns.
- **3 FAT thật**:
  1. **UsersController** — hybrid IMediator + AppDbContext + IActionLogService +
     PermissionLockoutGuard + ICompanyScopeService, TẤT CẢ đang dùng (BUG-M, xem mục 7).
  2. **AccessoriesController** — List/GetById EF trực tiếp (:41-42, :84-86), Update EF trực tiếp
     (:165-199) — **chưa migrate** dù chiến dịch "24 controller" ghi nhận hoàn tất.
  3. **SystemConfigController** — KHÔNG có IMediator; EF + LogAction trực tiếp (:65-101, :112-159).
- Ngoại lệ cũ "Templates GET tĩnh": ĐÃ GIẢI QUYẾT — cả 16 action đều `_mediator.Send`.
- UsersController: **0 tham chiếu Keycloak chức năng** (22 match toàn solution đều comment lịch sử);
  lý do cũ "Keycloak liên quan" trong header comment giờ stale nhưng code giữ đúng cam kết BUG-M.
- AuthController: THIN 100% (11 action, mỗi action 1 Send; chỉ cookie/IP/claim-parse).
- Program.cs: 103 dòng (docs nói ~93 — minor), composition root mỏng đúng Task Q (0
  AddSingleton/Scoped/Transient inline).

## MỤC 2 — CQRS/MEDIATR: PASS

- **ILoggableCommand**: 46/111 command implement (behavior atomic tx + log). 65 còn lại: **0
  violation** — 61 log thủ công/ủy quyền hợp lệ (Assets ×10, Accessories ×3, Components ×8,
  Consumables ×5, Licenses ×5, Users ×3, MaintTemplates ×13, Campaigns ×4, Imports ×8 — service
  tự log từng row), 4 auth/session (Login/Refresh/Logout/VerifyPasskeyAssertion — audit nằm trong
  `auth_login_attempts`, đúng quyết định đã duyệt).
- **Auth-era commands**: ChangePassword + AdminResetPassword + CompletePasskeyRegistration +
  DeletePasskey đều ILoggableCommand ✓ (log WHO, không log password). CreateUser/UpdateUser/
  DeleteUser log thủ công trong handler (hợp lệ) — nhưng KÈM controller log lần 2 = BUG-M còn
  nguyên (mục 7).
- **CacheInvalidationBehavior**: 12 command (Category/Company/Manufacturer/Supplier × C/U/D) khớp
  1:1 với 4 nhóm GET có `[OutputCache]` — **0 gap**; Import path tự evict trong ExcelImportService
  (:154, :194); /permissions là catalog tĩnh.
- **Thứ tự behaviors** đúng chuẩn: Cache (outermost) → Validation → ActionLog(tx) → Handler →
  log+commit → evict (ApplicationServiceCollectionExtensions.cs:26-36, comment giải thích
  stale-race).
- **Validation**: 8 AbstractValidator đều chạy qua ValidationBehavior pipeline; **0 handler nào
  tự gọi validator tay** trong Application (2 match `ValidateAsync` là helper domain-check
  MaintenanceAssignees — không phải FluentValidation). UsersController gọi tay :225/:295 —
  verbatim parity, ghi nhận là điểm lệch chuẩn còn sót.

## MỤC 3 — COMPANY-SCOPING: PASS (1 gap minor mới)

### 3a. Live test (fixture QA-AUD8, user A công ty A vs user B công ty B)
| Test | Kết quả |
|---|---|
| userB GET asset công ty A | **404** hide-existence ✓ |
| userB PUT/DELETE asset công ty A | 403 (policy assets.edit không có) — chặn trước scope ✓ |
| userB GET location công ty A | **404** ✓ |
| userA POST /users companyId=B | **400 COMPANY_MISMATCH** "Bạn chỉ được tạo người dùng cho công ty của mình." ✓ |
| userB POST /locations companyId=A | **400 COMPANY_MISMATCH** ✓ |
| userA POST /locations companyId=A (công ty mình) | 200 ✓ |
| userB reset-password userA (khác công ty) | **404** "User not found." ✓ |
| List scope | userA thấy 18 floater + 1 QA-AUD8, **0 asset MIRAT** (31 assets thật ẩn hết) ✓ |
| Lưu ý | GET /assets KHÔNG nhận param `companyId` (bỏ qua, không filter) — không phải leak; FE không dùng param này cho asset list |

### 3b. Static sweep 18 feature có CompanyId
Đầy đủ List/GetById/Create/Update/Delete (bảng chi tiết trong subagent report; pattern chuẩn
`userCompanyId == null || CompanyId == null || match`). Nhóm global (Category/Manufacturer/
Supplier/Model/CustomField/Depreciation/Group) xác nhận không có CompanyId — không cần scope.

**GAP mới (🟡 MINOR, BUG-G variant)**: `UpdateUserCommand.cs:85` gán `user.CompanyId =
request.CompanyId` vô điều kiện — **CompanyId MỚI không được validate theo scope actor**;
UsersController.cs:316-318 chỉ check company HIỆN TẠI của target. Policy `admin` là grantable →
admin công ty A chuyển user sang công ty B được. (Create có check :246-248.)

### 3c. JWT claims consistency — PASS
- TokenService issue: sub, preferred_username, email, given_name, family_name, **local_user_id**,
  jti (+ pwd_change khi must-change; + permission="superuser" + realm_access JSON khi IsSuperUser).
  Live decode token admin xác nhận đúng shape golden strategy.
- Đọc ở: CurrentUserService (`local_user_id`), CompanyScopeService (:41 — resolve company qua DB
  theo localUserId, KHÔNG dùng claim company — không có claim company nào cả), PermissionHandler
  (:47 + SuperuserClaims), ActionLogService (:46, :120), PasswordChangeGate (`pwd_change`).
- **0 stale claim kiểu Keycloak cũ** — mọi claim được đọc đều được issue; `sub`/`preferred_username`
  KHÔNG còn được dùng làm user id ở bất kỳ đâu (chỉ display).
- Ghi nhận: `SuperuserClaims.cs` nằm ở `Infrastructure/Services/` (CLAUDE.md ghi `Authentication/`)
  — doc drift nhỏ.

## MỤC 4 — PATCH-SAFETY: FAIL (3 violation, 1 live-confirmed)

### 🔴 VI PHẠM 1 (HIGH, live-confirmed): UpdateUserCommand — Auth Phase 4 làm MẤT patch semantics Task M2
`UpdateUserCommand.cs:80-81, 85-87`:
```csharp
user.EmployeeNumber = request.EmployeeNumber?.Trim();  // absent → null → CLEAR
user.JobTitle = request.JobTitle?.Trim();              // absent → null → CLEAR
user.CompanyId = request.CompanyId;                    // absent → null → CLEAR (mất company-scope!)
user.DepartmentId = request.DepartmentId;             // absent → null → CLEAR
user.LocationId = request.LocationId;                  // absent → null → CLEAR
```
(IsSuperUser/IsActive :83-84 vẫn đúng `HasValue` guard — chỉ 5 field này regress.)
**Live proof (T4)**: PUT `/users/{userA}` chỉ `{id, firstName, lastName, email}` (không companyId)
→ 200, sau đó GET trả `companyId=null` — user bị floater-hóa âm thầm. Đây đúng lớp bug từng xóa
Serial/AssetTag thật. UsersController bind `[FromBody] UpdateUserCommand` trực tiếp (:287-290) —
không lớp DTO trung gian.

### 🟡 VI PHẠM 2: UpdateCompanyCommand.cs:68-69 full-PUT
`c.Name = request.Name` (absent → DB NOT NULL violation 500 — BUG-I class) +
`c.ParentId = request.ParentId` (absent → re-root công ty về root). Code là field duy nhất
patch-aware (:71-76). Doc comment "verbatim full-put" — nhưng là bug-class BUG-E.

### 🟡 VI PHẠM 3: UpdateGroupCommand.cs:70
`group.Description = request.Description;` trực tiếp → absent → clear Description. (Name có guard
:57-58.)

### Regression check 5 bug đã fix — 5/5 PASS
BUG-E (Department nullable + blank-khi-gửi 400 + dup exclude self :73-86) · BUG-F (Category dup
Name+Type chỉ khi đổi :69-75) · BUG-H (ModelValidation helper đủ empty/dup/FK→400
RESOURCE_NOT_FOUND) · BUG-I (CustomField nullable ×8 + dup-slug exclude self :73-88) · BUG-N
(Maintenance 3 field HasValue guard :112-114, Notes `is not null` :110).

### Ghi nhận thêm
- `??` pattern (chấp nhận được, empty-string semantics): UpdateComponentCommand.Notes:62,
  UpdateLicenseCommand.Serial:75/Notes:82 — Serial là field từng bị wipe lịch sử.
- **AccessoriesController.Update KHÔNG ghi ActionLog** (AccessoriesController.cs:163-201) — vi
  phạm ActionLog mandatory (comment :159 nói có nhưng không có LogAction nào).
- UpdateGroupPermissionsCommand: `request.Permissions` không null-guard (:68, :85) → absent → NRE
  500 thay vì 400.
- UpdateUserGroupsCommand: GroupIds absent = xóa hết groups (full-replace by design — ghi nhận).

## MỤC 5 — AUTH SYSTEM: PASS (sau 5 live test; 2 ghi nhận)

### 5a. grep -ri keycloak
- **Backend code: SẠCH** — 0 match `KEYCLOAK_[A-Z_]+` trong *.cs; 1 scheme "App" duy nhất;
  AppHost không còn AddKeycloak. ~77 match comment lịch sử (vô hại).
- **Match "sống" duy nhất**: `frontend/Dockerfile:17-23` — ARG/ENV `VITE_KEYCLOAK_*` (0 code đọc —
  dead build-args nhưng vẫn là instruction build).
- **Deployment artifacts stale** (mục 8): docker-compose.yml (service keycloak + env
  Keycloak__ClientSecret bắt buộc `:?`), .env.example (block KEYCLOAK_* L41-68),
  scripts/seed-initial-admin.ps1 (100% Keycloak Admin API).

### 5b. Live tests (tất cả qua API thật)
| Test | Kết quả |
|---|---|
| **T6 Brute-force** (user thật qa-aud8-lockuser) | 5 fail → INVALID_CREDENTIALS; **lần 6 → 400 ACCOUNT_LOCKED** ✓; mật khẩu ĐÚNG trong lúc lock vẫn bị chặn (lock check trước verify) ✓ |
| **T9 MustChangePassword gate** | login mới → mustChange=True; GET /users/me **200**; GET /assets **403 MUST_CHANGE_PASSWORD** (body JSON đúng); POST passkey options **403** (gate chặn cả passkey); đổi pass xong → re-login mustChange=False ✓ |
| **T7 Passkey flag** | GET passkeys-enabled=false → PUT true 200 → GET true ✓; POST /auth/passkeys/login/options **200** khi bật / **403 PASSKEYS_DISABLED** khi tắt ✓; restore về giá trị cũ ✓ |
| **T5 Refresh race** (6 refresh đồng thời cùng cookie, curl, 2 lần chạy) | **1×200 + 5×401** cả 2 runs ✓; cookie cũ sau race → 401 (rotated/reuse-detected) ✓; login mới sau race → 200 (user không bị khóa) ✓ |
| **T8 ChangePassword** | wrong current → 400 INVALID_CREDENTIALS; đúng → 200; pass mới login OK, pass cũ rejected ✓ |
| Claims decode | token admin: sub=local_user_id, preferred_username, email, given/family_name, jti, permission+realm_access (superuser), exp 15 phút ✓ |

**Kết luận T5 (khoảng trống đã lấp)**: server-side rotation + reuse-detection AN TOÀN ở 6-way
concurrency — đúng 1 request thắng rotation, 5 thua nhận 401 (reuse-detection revoke-all chạy
như containment). KHÔNG có multi-rotation. Lưu ý: handler không dùng FOR UPDATE/tx nhưng unique
index TokenHash + check RevokedAt đủ chặn thực nghiệm (window lý thuyết cực hẹp giữa SELECT và
UPDATE — nếu 2 request cùng đọc RevokedAt=null thì CẢ HAI có thể rotate; thực nghiệm 6-way không
bắt được; mọi thua đều thấy RevokedAt đã set). Đề xuất (optional): `FOR UPDATE` trên credential
row để đóng cửa sổ lý thuyết — không phải bug đã reproduce.

### 5c. Secrets/signing key — PASS
- `Auth:SigningKey` + `Auth:BootstrapAdminPassword` + `Parameters:dbPassword` đều trong
  **user-secrets** (dotnet user-secrets list xác nhận, nằm ngoài repo); `.mirats-test-admin-password`
  gitignored (git check-ignore exit 0, 0 file secret nào tracked).
- Rotation plan: đổi `authSigningKey` trong user-secrets + restart → mọi access token cũ vô hiệu
  (HS256 verify fail), refresh token rows vẫn hash-matched (TokenHash không phụ thuộc signing key)
  — rotation không logout toàn bộ refresh; nếu muốn revoke hết phải UPDATE user_credentials (qua
  app) — ghi chú cho lần rotation thật.

### 5d. Ghi nhận auth nhỏ
- Lockout chỉ áp cho username TỒN TẠI (unknown user → generic failure ngay, không đếm) — hợp lý,
  generic message chống enumeration ✓.
- ChangePasswordCommand comment/code mismatch: comment nói "revoke all OTHER sessions (this device
  stays logged in)" nhưng code revoke TẤT CẢ (kể cả session hiện tại — command không nhận refresh
  token) → thiết bị đang đổi pass cũng logout khi access token hết hạn. Comment sai, code có thể
  là chủ ý — cần chốt.

## MỤC 6 — FRONTEND FDA: PASS cấu trúc (3 finding)

- **12 feature** nhất quán pages/components/services; auth = pages+services (shape chuẩn);
  KHÔNG file sai chỗ; src/pages/ đã xóa sạch; src/services/ chỉ còn api-client.ts (dùng bởi 40+
  file); **0 file mồ côi**; 33 lazy route + 3 auth import — 100% trỏ đúng file tồn tại.
- Lệch nhẹ: admin/user/maintenance thiếu services/ (gọi apiClient inline / import chéo
  asset.service); license thiếu types/; auth service 2/3 file không theo convention `*.service.ts`;
  shared components nằm 3 chỗ (components/, components/common/, shared/components/).
- **Interceptor 401-retry** (api-client.ts): single-flight + queue đúng; exclude list đúng
  (/auth/refresh, /auth/login, /auth/passkeys/login); `_retry` chặn loop; queue clear cả 2 nhánh.
  Edge cases: (a) refresh hết hạn → AN TOÀN; (b) token revoke → AN TOÀN (parked request có thể
  2 chu kỳ refresh — bounded); (c) refresh tự 401 → AN TOÀN (raw axios); **(d) 2 TAB refresh
  đồng thời → LỖI THẬT**: `isRefreshing` per-tab, backend reuse-detection revoke ALL → cả 2 tab
  mất session (không phải security hole — là availability bug). Cần `navigator.locks` (Web Locks).
- **clearPermissionCache() dead code** (usePermission.ts:43, 0 caller): logout → login user khác
  không F5 → menu/permission gate theo user CŨ (backend vẫn chặn 403 — UI bug).
- **AssetMaintenanceSection.tsx:90** đọc `companyId` scalar từ asset detail — field KHÔNG tồn tại
  trong AssetDetailDto (chỉ có object `company`) → filter assignee theo công ty âm thầm vô hiệu.
- **dayjs phantom dependency**: import ở 14 file, không có trong package.json (transitive qua
  antd) — fragile cho pnpm/upgrade.
- Refresh call không timeout (auth.ts:105) — server hang → isRefreshing kẹt vĩnh viễn.
- Vite chunk >500kB (build warning) — code-splitting backlog T8 cũ vẫn mở (App.tsx đã lazy 33
  routes nhưng chunk chính vẫn lớn).

## MỤC 7 — BACKLOG ĐỐI CHIẾU: 9/10 RESOLVED đúng, BUG-M còn, INFRA-1 tái diễn

| Entry | Trạng thái ghi nhận | Đối chiếu code hiện tại |
|---|---|---|
| BUG-E Department | RESOLVED 09-05 | **PASS** — nullable + blank-khi-gửi 400 + dup exclude self (UpdateDepartmentCommand.cs:73-86) |
| BUG-F Category | RESOLVED | **PASS** — dup Name+Type chỉ khi đổi, exclude self (:69-75) |
| BUG-G Location Create | RESOLVED | **PASS** — check COMPANY_MISMATCH còn nguyên ở handler (CreateLocationCommand.cs:64-69) + live 400 ✓. ⚠️ Comment controller stale "TODO SECURITY BUG-G ... NO scoping" (LocationsController.cs:54-55) — gây hiểu lầm, fix nằm ở handler |
| BUG-H Models | RESOLVED | **PASS** — ModelValidation đầy đủ empty/dup/FK→400 |
| BUG-I CustomFields | RESOLVED | **PASS** — nullable ×8 + dup-slug exclude self |
| BUG-J Dashboard trend | RESOLVED | **PASS** — superuser branch bọc `if (userCompanyId != null)`; GroupBy SQL; :D2 client-side (GetMonthlyCheckoutTrendQuery.cs:50-73) |
| BUG-K Groups | RESOLVED | **PASS** — nhưng Description patch regression mới (mục 4) |
| BUG-L Reports date | RESOLVED | **PASS** — SpecifyKind Utc (CheckoutHistoryReportQuery.cs:58-59) |
| BUG-N Maintenance | RESOLVED | **PASS** — 3 field HasValue + Notes is-not-null (:110-114) |
| **BUG-M Users double-log** | OPEN | **VẪN CÒN NGUYÊN** — cả 3 write: handler log + controller LogAction/SaveChanges lần 2 sau Send (UsersController.cs:263-271, :345-353, :432-440). Keycloak đã xóa nhưng code giữ đúng cam kết verbatim |
| INFRA-1 Docker/WSL2 | OPEN (4 lần) | **TÁI DIỄN LẦN 5 — TRONG PHIÊN NÀY**: engine chết giữa audit (npipe biến mất, AppHost chết theo), Docker Desktop restart ~5s, volume nguyên vẹn. Cùng lớp WSL2-instability như 4 lần trước |
| INFRA-2 6 PNG mất | REOPENED (2 lần) | **KHÔNG tái diễn lần 3** — cả 6 file còn đủ trong working tree, git status sạch |

## MỤC 8 — DOCS ĐỒNG BỘ: FAIL (stale rộng)

| File | Vấn đề |
|---|---|
| **ERROR_CODES.md** | Quét 09-05 (trước Auth migration). **Bucket A — 7 code chết còn trong doc**: KEYCLOAK_USERNAME_EXISTS/EMAIL_EXISTS (409), KEYCLOAK_CREATE_FAILED/ID_RETRIEVAL_FAILED/SYNC_FAILED/UPDATE_FAILED/ERROR (502) — CreateUser trùng username/email giờ là 400 "Validation failed." shape (validator). **Bucket B — thiếu ~10 code auth mới**: INVALID_CREDENTIALS, ACCOUNT_LOCKED, MUST_CHANGE_PASSWORD (403), PASSKEYS_DISABLED (403), PASSKEY_REGISTRATION_EXPIRED, PASSKEY_ALREADY_REGISTERED, PASSKEY_MALFORMED_RESPONSE, PASSKEY_VERIFICATION_FAILED, PASSKEY_ASSERTION_EXPIRED, USER_NOT_FOUND (auth 401). VALIDATION_ERROR điều kiện doc sai. Header L4-5 nguồn quét sai (KeycloakService đã xóa) |
| **HANDOFF_LATEST.md** | Dừng ở 2026-08-28 (MC-9/10 + reset Keycloak) — KHÔNG có gì về MediatR migration (TODO L574 còn ghi "chỉ 3/20 controller") lẫn Auth migration (0 match local auth/passkey) |
| **ARCHITECTURE.md** | L30/L39/L49/L56 request flow còn vẽ Keycloak OIDC |
| **API.md** | L4 "JWT từ Keycloak"; **0 endpoint /auth/* documented** (login/refresh/logout/password/passkeys ×7) |
| **DEPLOYMENT.md** | Toàn flow Keycloak (L84, §7 L302, L381 KC_BOOTSTRAP bắt buộc, §8.2-8.9 troubleshooting) |
| **DEVELOPMENT_WORKFLOW.md** | Gần sạch; Phụ lục B **L298** vẫn liệt kê JIT provisioning như cơ chế hiện hành (service đã xóa Phase 5) |
| AGENTS.md / CLAUDE.md | SẠCH (đã rewrite local auth) |
| Root artifacts | docker-compose.yml (service keycloak + env bắt buộc — `docker compose up` sẽ fail/không cần), .env.example (KEYCLOAK_* L41-68), scripts/seed-initial-admin.ps1 (100% Keycloak Admin API), frontend/Dockerfile (ARG/ENV VITE_KEYCLOAK_*), docker-reset.ps1 (filter keycloak-data no-op) |

---

## TỔNG HỢP PHÁT HIỆN MỚI (phân loại, chờ duyệt — KHÔNG fix trong audit)

### 🔴 HIGH — cần fix sớm
| # | Phát hiện | Bằng chứng |
|---|---|---|
| N1 | **UpdateUserCommand patch-safety regression** (Auth Phase 4): 5 field bị clear khi absent — EmployeeNumber/JobTitle/CompanyId/DepartmentId/LocationId; live-confirmed companyId WIPED | UpdateUserCommand.cs:80-87 + live T4 |
| N2 | **AccessoriesController.Update thiếu ActionLog** (vi phạm mandatory) | AccessoriesController.cs:163-201 (0 LogAction) |

### 🟡 MEDIUM
| # | Phát hiện | Bằng chứng |
|---|---|---|
| N3 | UpdateCompanyCommand full-PUT Name (→500 NOT NULL) + ParentId (→re-root) | UpdateCompanyCommand.cs:68-69 |
| N4 | UpdateGroupCommand clear Description khi absent | UpdateGroupCommand.cs:70 |
| N5 | Users.Update không validate CompanyId MỚI theo scope (admin chuyển user sang công ty khác) | UpdateUserCommand.cs:85 + UsersController.cs:316-318 |
| N6 | FE multi-tab refresh race: 2 tab cùng refresh → reuse-detection revoke ALL → mất session cả 2 tab (cần Web Locks) | api-client.ts:23-27 per-tab + AuthCommands.cs:146-156 |
| N7 | clearPermissionCache dead code — logout/login user khác không F5 → permission gate theo user cũ | usePermission.ts:43 (0 caller) |
| N8 | AssetMaintenanceSection đọc `companyId` scalar không tồn tại trong asset detail → filter assignee vô hiệu | AssetMaintenanceSection.tsx:90 vs AssetDetailDto |
| N9 | Docs/artifacts stale sau Auth migration (chi tiết mục 8) — nghiêm trọng nhất: docker-compose + .env.example + seed script hướng dẫn Keycloak sống | mục 8 |
| N10 | UpdateGroupPermissionsCommand `request.Permissions` không null-guard → NRE 500 | UpdateGroupPermissionsCommand.cs:68, :85 |

### 🟢 LOW / ghi nhận
| # | Phát hiện |
|---|---|
| N11 | Refresh call FE không timeout (auth.ts:105) — isRefreshing kẹt nếu server hang |
| N12 | dayjs phantom dependency (14 file, thiếu package.json) |
| N13 | NpgsqlParameter usage vượt ghi chú duyệt "chỉ exception-types" (CreateCampaignCommand.cs:163 — qua EF, không ADO) |
| N14 | ChangePassword comment/code mismatch (revoke ALL sessions vs "other sessions") |
| N15 | Comment stale: LocationsController.cs:54 "TODO SECURITY BUG-G" (đã fix), AuthController.cs:14 "Dual-auth ... legacy Keycloak Bearer", UsersController.cs:24 lý do cũ |
| N16 | SuperuserClaims.cs ở Infrastructure/Services (CLAUDE.md ghi Authentication) |
| N17 | Build warnings: 12× CS8xxx nullable + CS0618 HasCheckConstraint obsolete + NU1903 Microsoft.OpenApi CVE |
| N18 | Vite chunk >500kB (code-splitting backlog cũ vẫn mở) |
| N19 | RefreshTokenCommandHandler không FOR UPDATE (window lý thuyết hẹp — 6-way live không reproduce; optional hardening) |
| N20 | Program.cs 103 dòng (docs ~93) |

### Backlog task đề xuất (approval-gated, thứ tự ưu tiên)
1. **FIX-N1** UpdateUserCommand patch-safe 5 field (is-not-null/HasValue) + test bug-repro + live
   verify (kèm quyết định N5: validate CompanyId mới theo scope).
2. **FIX-N2** AccessoriesController.Update ActionLog (hoặc gộp với việc migrate nốt controller
   này sang MediatR — nó là FAT controller duy nhất còn EF trực tiếp ngoài Users/SystemConfig).
3. **FIX-N3+N4+N10** đợt patch-safety nhỏ (Company Name/ParentId, Group Description, Group perms
   null-guard).
4. **FIX-N6+N7+N11** đợt FE auth nhỏ (Web Locks cross-tab, clearPermissionCache call, refresh
   timeout).
5. **DOCS-SYNC** ERROR_CODES.md (2 bucket) + ARCHITECTURE/API/DEPLOYMENT/HANDOFF +
   DEVELOPMENT_WORKFLOW L298 + docker-compose/.env.example/seed-initial-admin.ps1/frontend
   Dockerfile.
6. **INFRA-1 update**: lần 5 (2026-10-02) — giữ entry OPEN, ghi nhận timeline.

### Cleanup fixture (minh bạch)
- Đã xóa qua API: 5 users (soft-delete, groups detached, company unset), group, 2 locations,
  company B.
- **Tồn dư có chủ đích** (không xóa được qua API, §8 cấm SQL tay):
  - Asset `QA-AUD8 FIXTURE LEFTOVER (confirmed asset - delete-guard)` — IsConfirmed=true (auto
    khi Physical=true, CreateAssetCommand.cs:98-100) → delete-guard chặn; đã rename nhận diện rõ.
  - Company `QA-AUD8 Company A (FIXTURE LEFTOVER)` (code QA8A) — bị asset trên tham chiếu
    (COMPANY_IN_USE); đã rename nhận diện rõ.
  - 2 bản ghi này vô hại (asset floater trống, company trống) — xóa bằng tay qua UI nếu muốn.

## VERDICT TỔNG
| Mục | Verdict |
|---|---|
| 1. Clean Architecture | **PASS** (2 ghi nhận nhỏ) |
| 2. CQRS/MediatR | **PASS** |
| 3. Company-scoping | **PASS** (1 gap minor N5; live test sạch) |
| 4. Patch-safety | **FAIL** — N1 regression live-confirmed (Auth Phase 4), N3, N4 |
| 5. Auth system | **PASS** — brute-force/gate/passkey-flag/race/secrets đều live PASS; race-safe ở 6-way |
| 6. Frontend FDA | **PASS cấu trúc** — interceptor 1-tab an toàn; multi-tab N6 |
| 7. Backlog | 9/10 RESOLVED đúng code; BUG-M còn; INFRA-1 tái diễn lần 5; INFRA-2 không tái diễn |
| 8. Docs | **FAIL** — stale rộng sau Auth migration (ERROR_CODES 2 bucket + 4 docs + 4 artifacts) |
