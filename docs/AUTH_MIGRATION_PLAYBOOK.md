# AUTH_MIGRATION_PLAYBOOK — phần Phase 4 (VIẾT LẠI THEO HIỆN TRẠNG CODE THẬT)

> **Ghi chú phiên bản:** File playbook gốc chỉ tồn tại ở dạng trình bày plan-mode, chưa từng được
> ghi ra disk. File này được tạo tại thời điểm chuẩn bị Phase 4 (2026-09-07) và **CHỈ chứa phần
> Phase 4 viết lại** dựa trên audit code thực tế Phase 1-3 (commit `ef94384`) + census dữ liệu
> thật. Các phần khác của playbook gốc (§1-§3, §5, §11) giữ nguyên giá trị phê duyệt gốc; khi
> cần tham chiếu, xem hội thoại phê duyệt ban đầu. Phần Phase 4 dưới đây **THAY THẾ** tóm tắt
> Phase 4 gốc ở mọi điểm khác biệt.
>
> **Điều kiện vào Phase 4:** Phase 1-3 hoàn tất và đã được user xác nhận (pwd_change gate đã vá
> `4e350ec`, interceptor retry đã fix, E2E passkey 15/15).

---

## Phase 4 — Cắt sang local auth hoàn toàn (User CRUD local-only)

### 4.0. Hiện trạng code ĐÃ XÁC NHẬN (audit 2026-09-07 — đây là cơ sở của mọi chi tiết dưới đây)

**4.0.1. Dual-auth hiện đang chạy thế nào**
- Default scheme = **"AuthPolicy"** (`AuthSchemes.Policy`) — một `PolicyScheme` forwarder đọc
  issuer của Bearer token (KHÔNG verify) rồi forward:
  issuer == `Auth:Issuer` ("aspire-react") → scheme **"App"**; còn lại → scheme **"Bearer"**
  (Keycloak, `JwtBearerDefaults.AuthenticationScheme`, Authority-based, ValidateIssuer/Audience
  = false, JIT provisioning stamp `local_user_id` trong `OnTokenValidated`).
- Scheme "App": self-signed HS256 (`Auth:SigningKey` user-secret), ValidateIssuer/Audience/
  SigningKey bật, `MapInboundClaims=false` giữ claim short-OIDC; `OnTokenValidated` chỉ check
  `IsActive` (pwd_change gate nằm ở middleware — xem 4.0.2).
- Nơi đăng ký: `AuthenticationServiceCollectionExtensions.AddKeycloakAuthentication()` (tên
  method giữ tên cũ — đổi tên là việc Phase 5, không phải Phase 4).
- **Hàm ý Phase 4:** cắt scheme "Bearer" = xóa nhánh else của `ForwardDefaultSelector` + block
  `.AddJwtBearer("Bearer", ...)` + JIT hook. Vì đã không còn Keycloak token, forwarder có thể
  trở thành no-op (default thẳng "App") — nhưng việc tháo PolicyScheme là việc Phase 5 (dọn
  sạch), Phase 4 chỉ cần **ngắt luồng Keycloak** an toàn.

**4.0.2. PasswordChangeGateMiddleware — KHÔNG cần thay đổi ở Phase 4**
- Vị trí hiện tại (Program.cs): `UseAuthentication()` → **`UseMiddleware<PasswordChangeGateMiddleware>()`**
  → `UseAuthorization()` → `UseOutputCache()`.
- Logic: session `pwd_change=1` (authenticat­ed) chỉ được `/api/v1/users/me` +
  `/api/v1/auth/password` (EXACT path match — không khớp `/api/v1/users/{id}`), còn lại 403
  `MUST_CHANGE_PASSWORD`. Phase 4 bổ sung admin reset UI không đổi middleware; luồng
  "admin reset → user bị ép đổi mật khẩu" đã chạy đầy đủ từ Phase 1-gap-fix (đã live-verify).

**4.0.3. Bản đồ phụ thuộc IKeycloakService (grep 2026-09-07 — cập nhật so bản đồ §2 gốc)**
| # | Nơi | Dùng gì | Phase 4 làm gì |
|---|---|---|---|
| 1 | `CreateUserCommand` | `CreateUserAsync` + `AddUserToSuperUserGroupAsync` (nếu superuser) | Thay bằng: set `PasswordHash` + `MustChangePassword=true` từ password admin nhập; bỏ sync |
| 2 | `UpdateUserCommand` | `UpdateUserAsync` + `AddUserToSuperUserGroupAsync`/`RemoveUserFromSuperUserGroupAsync` | Bỏ 3 call sync; giữ toàn bộ logic local (ActionLog, patch semantics) |
| 3 | `DeleteUserCommand` | `DisableUserAsync` | Bỏ call; giữ soft-delete (`IsActive=false`) |
| 4 | `InfrastructureServiceCollectionExtensions` | `AddSingleton<IKeycloakService, KeycloakService>` | Giữ đăng ký đến cuối Phase 4 (FakeKeycloakService của tests cần); Phase 5 xóa |
| 5 | `Tests/FakeKeycloakService`, `UserActionLogTests`, `JitProvisioningClaimMappingTests` | Mock/no-op | UserActionLogTests giữ (mock vẫn compile); Jit tests đánh dấuObsolete/delete khi cắt JIT (D-6) |
| 6 | `KeycloakApiException` | catch trong 3 commands | Khi bỏ call sync → catch rỗng bỏ theo; ERROR_CODES giữ KEYCLOAK_* đến Phase 5 |
- **KHÔNG phụ thuộc IKeycloakService** (kiểm chứng grep): PermissionHandler, CompanyScopeService,
  RealmAccessHelper, CheckPermissionsQuery — các nơi này chỉ đọc **claims** `permission`/
  `realm_access` mà scheme "App" đã mirror từ Phase 1 (golden strategy) → Phase 4 không đụng.

**4.0.4. RealmAccessHelper & realm-bypass — làm rõ so kế hoạch gốc**
- `RealmAccessHelper.IsSuperUser(principal)` đọc claim `permission`/`realm_access` — **KHÔNG gọi
  Keycloak API**. Nơi dùng: `PermissionHandler` (superuser bypass policy), `CompanyScopeService`,
  `GroupsController.IsRealmSuperUser`, `UsersController.IsRealmSuperUser`,
  `PermissionsController.CheckPermissionsQuery`.
- "Xóa realm-bypass" ở Phase 4 do đó **KHÔNG phải xóa RealmAccessHelper** — mà là: xác nhận mọi
  nơi listed trên hoạt động đúng với token "App" (đã đúng từ Phase 1 do mirror claims — có
  live-verify users/me + dashboard). Việc đổi tên/gộp RealmAccessHelper là cosmetic → Phase 5.

**4.0.5. Frontend 401-retry — Phase 4 KHÔNG đổi**
- Sau fix Phase 3 (`api-client.ts`): request gốc gặp 401 → refresh bằng httpOnly cookie → retry
  TRỰC TIẾP với token mới; request 401 song song trong lúc refresh đang chạy → park vào queue,
  được processQueue retry; auth endpoints (login/refresh/passkeys/login) loại trừ khỏi
  refresh-loop. Đã E2E-verified trong Phase 3 (15/15).
- Phase 4 FE chỉ THÊM: action "Đặt lại mật khẩu" (admin) trên Users UI + cột "Có mật khẩu?" —
  `adminResetPassword()` đã có sẵn trong `features/auth/services/auth.service.ts` từ Phase 2.

**4.0.6. Census dữ liệu THẬT (2026-09-07, read-only SQL qua container — không phải fixture)**
- Tổng active users: **9** → trong đó **1 user thật duy nhất: `admin`** (super, hash=YES từ
  bootstrap seeder Phase 1, must_change=false) và **8 orphan QA fixtures** (`qa-phase3-passkey-*`
  còn active do các run E2E fail trước khi kịp cleanup).
- **User thật chưa có PasswordHash: 0** — khối lượng "migrate user hàng loạt" thực tế = **0**.
- Điều kiện an toàn tối thiểu đã nêu ("KHÔNG cắt Keycloak sync cho đến khi ≥1 user thật đã login
  thành công qua auth mới") → **ĐÃ THỎA** từ Phase 1 (admin login/live-verify nhiều lần).
- Việc dọn 8 orphan fixtures là B0 (mở đầu Phase 4, DELETE qua API đúng §8).
- Cảnh báo census: active users = 9 nhưng 8 là fixture → số liệu "chưa có password" hiển thị cho
  admin sau Phase 4 sẽ thực tế chỉ phản ánh user THẬT (admin đã có) → cột này chủ yếu có giá trị
  cho user được tạo TRƯỚC Phase 1 trên hệ thống khác khi restore data cũ.

### 4.1. Mục tiêu (giữ nguyên mục tiêu gốc, chi tiết cập nhật)
1. **User CRUD local-only**: Create/Update/Delete không còn call Keycloak Admin API; CreateUser
   nhận thêm mật khẩu ban đầu (≥8 ký tự, validator như ChangePassword) → `PasswordHash` +
   `MustChangePassword=true` (bắt buộc đổi lần đầu — đúng policy đã duyệt).
2. **Admin reset UI**: action "Đặt lại mật khẩu" trên Users UI → `POST /users/{id}/reset-password`
   (endpoint Phase 1), set `MustChangePassword=true` + revoke sessions (đã có).
3. **Cột "Có mật khẩu?"**: thêm `hasPassword` vào UserDto list/detail + filter/badge trên
   UserListPage (với dữ liệu hiện tại: mọi user active thật đều có hash — cột phục vụ data cũ).
4. **MustChangePassword áp dụng đầy đủ**: đã chạy từ Phase 1-gap-fix — Phase 4 chỉ thêm E2E case
   "user mới tạo bởi admin → bắt đổi mật khẩu lần đầu" vào luồng verify.
5. **Realm-bypass**: xác nhận (không sửa code) — xem 4.0.4.
6. **ConcurrencyRaceAuditTests → /auth/login**: 4 test concurrency lấy token Keycloak
   (`https://localhost:8080/realms/...`) → đổi sang `POST /api/v1/auth/login` với
   admin + password từ `.mirats-test-admin-password` (file gitignored hiện có).
7. **KHÔNG xóa code Keycloak thật sự** (`KeycloakService`, `IKeycloakService`, realm.json, AppHost
   container, `VITE_KEYCLOAK_*`) — đó là Phase 5. Phase 4 chỉ ngắt WIRE-OUT (các call sync).

### 4.2. Thứ tự thực hiện đề xuất (mỗi bước verify riêng, commit riêng nếu lớn)
- **B0 — Cleanup fixtures orphan**: DELETE (soft) 8 `qa-phase3-passkey-*` active **QUA API
  (DeleteUser command) — TUYỆT ĐỐI KHÔNG dùng SQL trực tiếp để XÓA** (census SQL chỉ đọc là
  chuyện khác; mọi thao tác GHI phải qua đúng luồng ứng dụng — §8 xuyên suốt).
  Không đụng user thật.
- **B1 — CreateUser local-only** (+password ban đầu + MustChangePassword): unit test (hash set,
  flag set, ActionLog) + live verify bằng fixture `qa-phase4-create-*` (create → login với mật
  khẩu admin cấp → bị ép đổi mật khẩu → đổi → vào dashboard). KHÔNG test trên admin.
- **B2 — UpdateUser/DeleteUser local-only**: unit + live verify fixture `qa-phase4-*` (update
  không còn sync; delete soft). Xác nhận admin (user thật) KHÔNG bị đụng tới.
- **B3 — Admin reset UI + hasPassword**: FE (UserListPage action + cột) + E2E playwright
  (admin reset fixture → fixture login bị ép đổi).
- **B4 — ConcurrencyRaceAuditTests → /auth/login**: 4 test chuyển nguồn token; suite phải về
  458+/458+ (không còn phụ thuộc Keycloak runtime trong tests).
- **B5 — Full regression**: full suite + E2E Phase 2 (password flow) + Phase 3 (passkey flow,
  flag OFF/ON) chạy lại toàn bộ PASS.
- **B6 — Mở Phase 5** (xóa code Keycloak thật sự — riêng biệt).

### 4.3. Rủi ro riêng của Phase 4 (KHÁC Phase 1-3 — lần đầu thay đổi hành vi User CRUD thật)
- **INCIDENT-1 / §8 nhắc lại**: mọi test CRUD đều qua fixture `qa-phase4-*` tạo mới bằng API;
  TUYỆT ĐỐI không Create/Update/Delete/reset-password trên `admin` hay bất kỳ user thật; admin
  chỉ xuất hiện trong verify ở vai người GỌI API (actor), không phải đối tượng bị tác động.
- **Không xóa-nhầm dữ liệu thật khi bỏ sync**: DeleteUser giữ soft-delete nguyên trạng — Phase 4
  không đổi semantics xóa, chỉ bỏ call `DisableUserAsync`.
- **Validator mật khẩu CreateUser**: bắt buộc ≥8 ký tự (đồng bộ ChangePasswordCommand) — nếu
  admin tạo user với mật khẩu yếu phải 400, không silently chấp nhận.
- **ActionLog không thay đổi hình dạng**: note/logMeta giữ ngôn ngữ + cấu trúc hiện có (chỉ bỏ
  câu chữ "Keycloak" trong note nếu có).
- **Rollback**: vì Phase 4 không xóa code Keycloak (chỉ bỏ call), rollback = revert commit —
  scheme "Bearer" và Keycloak container vẫn nguyên trong suốt Phase 4.
- **Qa fixtures orphan**: nếu E2E fail giữa chừng lần nữa — cleanup B0 phải chạy lại trước khi
  kết thúc Phase 4 (bắt buộc, đưa vào checklist hoàn thành).

### 4.4. Verify checklist Phase 4 (đính kèm cuối báo cáo Phase 4)
- Unit: 458+ (mới thêm: CreateUser password/flag, Update/Delete no-sync, hasPassword).
- Live: B1/B2 fixture flows + admin reset UI E2E + full suite + E2E Phase 2/3 regression.
- Sweep: mojibake + secret-grep trước commit (không push — dồn theo quyết định hiện hành).
- Census sau Phase 4: user thật vẫn = admin duy nhất active + 0 orphan fixture active.
- **[Duyệt 2026-09-07] JIT regression check**: sau khi cắt sync CRUD, login bằng token
  Keycloak CŨ (Keycloak runtime còn sống đến Phase 5) → JIT provisioning vẫn stamp
  `local_user_id` đúng + endpoint bảo vệ trả 200 — chứng minh việc đổi CreateUser/UpdateUser/
  DeleteUser sang local-only KHÔNG ảnh hưởng luồng Keycloak login path.

### 4.5. Quyết định CẦN USER CHỐT trước khi code (D-1…D-7)
- **D-1. CreateUser — mật khẩu ban đầu do admin nhập ngay trong form tạo?** (đề xuất: CÓ —
  field "Mật khẩu ban đầu" bắt buộc, user bị ép đổi lần đầu; phương án khác: không có mật khẩu
  lúc tạo, admin reset sau — gây thêm 1 bước thủ công cho mỗi user mới).
- **D-2. Thứ tự cắt**: cắt cả 3 command trong 1 commit hay từng command (Create trước, Update/
  Delete sau)? (đề xuất: 1 commit cho B1, 1 cho B2 — tách để rollback dễ, như 4.2).
- **D-3. Cơ chế cắt sync**: xóa call sync thẳng (revert = git revert) hay để sau feature-flag
  `auth.keycloakSync.enabled`? (đề xuất: XÓA THẲNG — flag thêm phức tạp mà điều kiện an toàn đã
  thỏa, rollback bằng revert là đủ; Phase 5 sẽ xóa nốt code).
- **D-4. User mới tạo có cần email/đặt mật khẩu tự thân?** Không có hệ thống email — mật khẩu
  ban đầu do admin cấp và giaooffline (đề xuất: giữ như D-1; không làm flow "gửi link").
- **D-5. IsRealmSuperUser (Users/Groups controller)**: giữ nguyên (claims-mirror, đổi tên ở
  Phase 5) hay gộp ngay vào PermissionHandler? (đề xuất: giữ nguyên — Phase 4 không đụng).
- **D-6. 8 orphan fixtures active**: cleanup ngay B0 (đề xuất: CÓ) hay giữ để tái sử dụng?
  (khuyến nghị CÓ — chúng là rác từ run E2E fail, tái tạo fixture mới rẻ hơn).
- **D-7. ConcurrencyRaceAuditTests**: giữ admin làm user login cho các test này (đề xuất: CÓ —
  mật khẩu đọc từ `.mirats-test-admin-password`) hay tạo dedicated test user? (đề xuất: admin —
  tests chỉ LOGIN, không sửa user).
