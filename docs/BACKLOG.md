# BACKLOG — các phát hiện đang chờ xử lý

> Các phát hiện từ audit/migration được đăng ký tại đây để xử lý riêng, KHÔNG trộn vào task
> tái cấu trúc đang chạy (nguyên tắc: parity trước, cải thiện sau).

---

## 🆕 ĐỢT 2 — 2026-10-02 (10 mục I → A → B → C → D → E → F → G → H → J)

> **Trạng thái: xong 10/10 mục, TẤT CẢ commit LOCAL — CHƯA PUSH** (chờ duyệt). Luật mới áp dụng
> xuyên đợt: mỗi commit phải chạy ĐÚNG 6 gate CI ở local (`scripts/precommit-gates.ps1`) và mọi thay
> đổi hành vi phải verify LIVE trên stack thật (§1.9 DEVELOPMENT_WORKFLOW).

| Mục | Nội dung | Commit | Bằng chứng verify |
|---|---|---|---|
| **I** | `.editorconfig` + `.gitattributes` (chốt **LF**, giải thích: index đã 100% LF, 32 file có BOM nên KHÔNG đặt `charset`) + `scripts/precommit-gates.ps1` + §1.9 quy tắc gate/CI | `b6a9add` | `git ls-files --eol` 585/585 `i/lf w/lf`; `git diff --ignore-all-space` rỗng (không cần commit chuẩn hoá riêng) |
| **A (BUG-M)** | `UsersController` thin 100% (chỉ `IMediator`); Create/Update/Delete/AdminReset thành `ILoggableCommand`, guard+company-scope chuyển vào handler → **hết log kép** | `84ba53f` | LIVE trước/sau trên cùng luồng: delta ActionLog **2/2/2 → 1/1/1**; 474 test PASS |
| **B** | `SystemConfigController` (controller FAT cuối cùng) → MediatR; contract format tách sang Domain (`AssetTagFormat`), 2 write command = `ILoggableCommand` | `7a6a9dc` | Parity 2-run **11/11 call giống hệt** (status+body); log vẫn 1 row/change, 0 row cho invalid/no-op |
| **C** | `AccessoriesController.GetCheckouts` → `GetAccessoryCheckoutsQuery` (controller hết EF trực tiếp) | `42814c0` | Parity 2-run **3/3** (kể cả 404 hide-existence) |
| **D** | `ConcurrencyRaceAuditTests` tự dọn fixture qua API (return/checkin trước, rồi báo cáo BLOCKED kèm error_code của delete-guard) + sửa 2 lỗi tiềm ẩn: `AdminUserId` hardcode **stale sau reset DB** (test âm thầm không test gì) và tên fixture trùng khi chạy lại; base URL cấu hình qua `MIRATS_TEST_BASE_URL` | `1842dc0` | Chạy thật: race cho đúng 1 winner/lần; cleanup return 5/5 accessory+component+license-seat; xoá bị chặn **đúng thiết kế** (`ACCESSORY_HAS_CHECKOUTS`, `COMPONENT_HAS_ALLOCATION_HISTORY`, `LICENSE_IN_USE`, `ASSET_CONFIRMED_CANNOT_DELETE`, `CATEGORY_IN_USE`, `COMPANY_IN_USE`) |
| **E** | N3+N4+N10 patch-safety: `UpdateCompanyCommand` Name/ParentId (absent → giữ, `Guid.Empty` → re-root), `UpdateGroupCommand` Description, null-guard `permissions` (handler + controller) | `ca75791` | LIVE: PUT thiếu `parentId` **giữ nguyên cha** (trước: re-root), sentinel re-root OK, tên trùng/blank → 400 (trước: 500), rename-only giữ description; 10 test mới |
| **F** | N6 Web Locks cross-tab refresh + N7 cache quyền **khoá theo danh tính** + `clearPermissionCache()` khi logout + N11 timeout refresh/login/logout | `db439c2` | Browser thật **2 tab**: `navigator.locks` chặn chéo tab (`otherTabLock: BLOCKED-timeout` khi tab kia giữ lock, `storageShared` xác nhận cùng origin); tab 2 boot refresh 200, cả 2 tab vẫn đăng nhập |
| **G** | Docs: ERROR_CODES xoá **7 mã Keycloak chết** + thêm **10 mã auth mới** (kèm HTTP status), ARCHITECTURE/HANDOFF_LATEST/DEVELOPMENT_WORKFLOW/.clinerules/skills bỏ Keycloak "hiện hành" | `8b9e6da` | grep `KEYCLOAK_[A-Z_]+` trong `*.cs` = **0**; 5 mã auth spot-check đều tồn tại trong code |
| **H** | N8 `AssetMaintenanceSection` đọc `company.id` (DTO không có `companyId` phẳng) + dọn mojibake **11 file / 62 dòng** (gồm 2 message API bị ký tự thay thế U+FFFD) | `09bd65e` | UI thật: dropdown "Người phụ trách" của asset thuộc công ty X hiện **đúng 2/37 user**; build 0 warning |
| **J** | N13 tách row-lock ra `IRowLockService` (Application hết ref Npgsql) · N14 đổi mật khẩu **giữ session hiện tại** · N15 2 comment stale · N16 `SuperuserClaims` → `Authentication/` · N17 hết warning (CVE OpenApi → 2.12.0, CS0618, CS8xxx) · N18 chunk threshold có ghi số đo · N19 **`FOR UPDATE`** cho refresh rotation · N20 docs 103 dòng | `c07b7c7` | N19 LIVE **6-way cùng cookie: 1×200 + 5×401**, chỉ 1 access token, family bị revoke; N14 LIVE: session A refresh 200 / session B 401; build **0 warning**; `dotnet ef migrations has-pending-model-changes` = không lệch model |

### 📌 Phát hiện MỚI trong đợt 2 (chưa xử lý — đăng ký để làm sau)

1. **`AppDbContext` có 2 block `modelBuilder.Entity<LicenseSeat>` trùng nhau** (L585 và L648 — phát
   hiện khi sửa N17). Model nhận hợp của cả hai (index + column type ở block 2, navigation License ở
   block 1) nên **hiện không sai**, nhưng rất dễ sửa nhầm một nửa. Đề xuất: gộp về 1 block (phải soát
   `dotnet ef migrations has-pending-model-changes` = rỗng sau khi gộp).
2. **`Invoke-WebRequest`/HttpWebRequest của PowerShell 5.1 ÂM THẦM BỎ header `Cookie` set tay** — chỉ
   dùng `System.Net.Http.HttpClient` (hoặc `-WebSession`) khi cần test cookie; nếu không sẽ kết luận
   sai về hành vi server (đã xảy ra với N14: tưởng fix lỗi, thực ra test không gửi cookie).
3. **`docs/HANDOFF_LATEST.md` (và `docs/sql/`, `backups/*.sql`)** còn mojibake lịch sử/dump — cố ý
   không sửa (là ghi chép sự cố); `backups/*.sql` là artifact backup, không phải code.

### 🤖 Đề xuất đưa `ConcurrencyRaceAuditTests` vào CI (mục D — CHƯA sửa `ci.yml`)

- **Hiện trạng:** test cần stack thật + quyền admin, tag `Category=Concurrency`, mặc định chỉ chạy tay.
- **Khả thi:** thêm 1 job chạy **trên DB dùng-một-lần** — `docker compose up -d --build` (hoặc service
  container Postgres + chạy API trực tiếp) → chờ `/health` → `MIRATS_TEST_BASE_URL=http://localhost:5000`
  + `MIRATS_TEST_ADMIN_PASSWORD` (từ CI secret) → `dotnet test --filter "Category=Concurrency"` →
  `docker compose down -v`.
- **2 điều kiện bắt buộc đã rõ:** (a) admin bootstrap mới bị `MustChangePassword=true` → job phải đổi
  mật khẩu 1 lần (`/auth/password`) trước khi test; (b) **KHÔNG bao giờ chạy trên DB dev/dùng chung** —
  các fixture có lịch sử cấp phát bị delete-guard giữ lại vĩnh viễn (census hiện tại: ~838 row `QCR-*`
  tích luỹ từ nhiều phiên, không xoá được qua API và **không được xoá bằng SQL** theo §8).
- **Chi phí:** ~5–10 phút/job (build image). Đề xuất: chạy ở nhánh `main`/nightly, không chặn PR.

---

## 🆕 AUDIT 2026-10-02 (sau MediatR + Auth migration) — phân loại N1–N20

- **Nguồn:** `docs/AUDIT_POST_MIGRATIONS_2026-10-02.md` (audit 8 mục: Clean Architecture,
  CQRS/MediatR, company-scoping, patch-safety, auth system, frontend FDA, backlog, docs).
- **Đợt 1 (các mục chặn push/triển khai)** — mỗi mục 1 commit LOCAL (chưa push):
  `863b3a9` (N1+N5) · `0ad2099` (N9-artifact) · `7501200` (N2).

### ✅ ĐỢT 1 — RESOLVED

| # | Mức | Phát hiện | Xử lý |
|---|---|---|---|
| **N1** | HIGH | `UpdateUserCommand` (Auth Phase 4 rewrite) gán vô điều kiện 5 field → payload thiếu field **xoá** `CompanyId`/`DepartmentId`/`LocationId`/`EmployeeNumber`/`JobTitle` (regression Task M2; live-confirmed qua API) | Patch-safe theo pattern BUG-E/N. Quy ước 3 giá trị cho Guid? (đã có sẵn trong dự án): **absent → giữ**, **`Guid.Empty` → clear (floater)**, **Guid thật → set**. FE edit-mode gửi sentinel khi admin xoá công ty. 9 test mới. Commit `863b3a9` |
| **N5** | HIGH | `UpdateUserCommand` không validate `CompanyId` **mới** theo scope actor (admin công ty A chuyển user sang công ty B) | Check `COMPANY_MISMATCH` (pattern Task L2/CreateUser) TRƯỚC mọi mutation; body `error_code` snake như CreateUser; superuser bypass; floater (Guid.Empty) cho phép. Live PASS + test. Commit `863b3a9` |
| **N9-artifact** | HIGH | Artifact triển khai còn Keycloak (compose service + `Keycloak__*`/`KC_BOOTSTRAP_*`, `.env.example`, `frontend/Dockerfile` VITE_KEYCLOAK_*, 2 script seed Keycloak, docker-reset filter) | Dọn sạch + **verify bằng `docker compose up -d --build` thật** trên DB trắng: 4 service healthy, 0 container keycloak, login `/api/v1/auth/login` 200, các endpoint bảo vệ 200. **3 blocker thật phát hiện khi verify** (xem dưới). Commit `0ad2099` |
| **N2** | HIGH | `AccessoriesController.Update` sửa bằng EF trực tiếp và **KHÔNG ghi ActionLog** (vi phạm ActionLog mandatory) | Migrate `List`/`GetById`/`Update` sang MediatR; `UpdateAccessoryCommand` = `ILoggableCommand` (log cùng transaction, có LogMeta changes). Parity 2-run baseline↔post: 4/4 call giống hệt field-for-field, log 1→2 entry. Commit `7501200` |

### 🚧 3 BLOCKER TRIỂN KHAI phát hiện khi verify compose (đã fix trong đợt 1)

1. **Không có admin trên DB trắng** → deploy xong không ai đăng nhập được (JIT provisioning đã xóa ở
   AUTH Phase 5; script seed Keycloak bị xóa cùng Keycloak). Fix: `StartupDataSeeder` **tự tạo** admin
   đầu tiên từ `AUTH_BOOTSTRAP_ADMIN_PASSWORD` + `INITIAL_ADMIN_USERNAME/EMAIL` (idempotent, không ghi
   đè hash đã có; bỏ qua + log warning nếu email đã dùng).
2. **`aspire-react.Server/Dockerfile` stale sau tách 4-layer (Giai đoạn 0.1)** — chỉ copy
   `Server` + `ServiceDefaults`, thiếu `Domain`/`Application`/`Infrastructure` → image **không build được**
   (CS0246). Fix: copy + restore đủ 5 project. (CI job build Docker image cũng hỏng vì lý do này.)
3. **`vite.config.ts` bắt buộc cert HTTPS dev cho MỌI command** → `npm run build` trong image production
   throw → `docker compose up --build` fail. Fix: yêu cầu cert chỉ còn ở `vite serve` (chính sách
   HTTPS-dev giữ nguyên; đã verify vẫn fail-loud khi thiếu cert).

### ⏳ ĐỢT 1 — còn OPEN (chờ duyệt đợt sau)

| # | Mức | Phát hiện | Ghi chú |
|---|---|---|---|
| N3 | MEDIUM | `UpdateCompanyCommand.cs:68-69` full-PUT: `Name` absent → DB NOT NULL violation (500); `ParentId` absent → re-root công ty | Cùng lớp BUG-E |
| N4 | MEDIUM | `UpdateGroupCommand.cs:70` `Description` gán trực tiếp → absent clear | Cùng lớp BUG-E |
| N6 | MEDIUM | FE multi-tab refresh race: 2 tab cùng refresh → reuse-detection revoke ALL → mất session cả 2 tab | Cần Web Locks (`navigator.locks`); server-side race 6-way đã PASS |
| N7 | MEDIUM | `clearPermissionCache()` dead code (0 caller) → logout/login user khác không F5 → gate theo user cũ | UI bug (backend vẫn chặn 403) |
| N8 | MEDIUM | `AssetMaintenanceSection.tsx:90` đọc `companyId` scalar không tồn tại trong asset detail → filter assignee vô hiệu | `AssetDetailDto` chỉ có object `company` |
| N9-docs | MEDIUM | Docs stale sau Auth migration: `ERROR_CODES.md` (7 code KEYCLOAK_* chết + thiếu ~10 code auth mới), `ARCHITECTURE.md`, `API.md` (0 endpoint `/auth/*`), `HANDOFF_LATEST.md` (dừng 2026-08-28) | `DEPLOYMENT.md` + artifacts đã xử lý ở đợt 1 |
| N10 | MEDIUM | `UpdateGroupPermissionsCommand` `request.Permissions` không null-guard → NRE 500 thay vì 400 | |
| N11 | LOW | Refresh call FE không timeout (`auth.ts:105`) → `isRefreshing` kẹt nếu server treo | |
| N12 | LOW | `dayjs` phantom dependency (14 file import, không có trong `package.json`) | |
| N13 | LOW | `CreateCampaignCommand.cs:163` dùng `NpgsqlParameter` cho `FromSqlRaw` — ngoài ghi chú ngoại lệ "Npgsql chỉ cho exception-types" | Không phá dependency direction (qua EF DbSet) |
| N14 | LOW | `ChangePasswordCommand` comment nói "revoke OTHER sessions" nhưng code revoke TẤT CẢ | Chốt lại comment hoặc hành vi |
| N15 | LOW | Comment stale: `LocationsController.cs:54` "TODO SECURITY BUG-G" (đã fix ở handler), `AuthController.cs:14` "Dual-auth legacy Keycloak", `UsersController.cs:24` lý do Keycloak | |
| N16 | LOW | `SuperuserClaims.cs` ở `Infrastructure/Services/` nhưng CLAUDE.md ghi `Authentication/` | Doc drift |
| N17 | LOW | Build warnings: 12× CS8xxx nullable, CS0618 `HasCheckConstraint` obsolete, NU1903 `Microsoft.OpenApi` CVE | |
| N18 | LOW | Vite chunk >500kB — code-splitting (backlog T8 cũ) vẫn mở | |
| N19 | LOW | `RefreshTokenCommandHandler` không `FOR UPDATE` (cửa sổ race lý thuyết; 6-way live không reproduce) | Optional hardening |
| N20 | LOW | `Program.cs` 103 dòng (docs ghi ~93) | |

### 📌 Ghi nhận lịch sử (minh bạch)

- **Báo cáo tổng kết MediatR trước đây SAI về Accessories/SystemConfig.** Tài liệu/handoff các phiên
  trước mô tả chiến dịch "24 controller đã migrate sang MediatR" như đã hoàn tất, nhưng audit
  2026-10-02 xác nhận: `AccessoriesController` vẫn chạy EF trực tiếp ở List/GetById/Update (và
  **Update không ghi ActionLog** = N2), `SystemConfigController` chưa hề có `IMediator`. Đợt 1 đã
  migrate 3 action của Accessories; **`GetCheckouts` (accessories) + toàn bộ `SystemConfigController`
  vẫn còn EF trực tiếp** → coi là phần còn lại của chiến dịch CQRS, KHÔNG phải "đã xong".
- **BUG-M (Users log 2 lần) vẫn OPEN**, không nằm trong đợt 1.
- File test cũ còn **mojibake sẵn** (do sửa qua PowerShell từ trước — đúng lỗi mà AGENTS.md cảnh báo):
  `TaskM2PatchSafetyTests.cs`, `TaskL2CreateCompanyScopeTests.cs`. Đợt 1 không sửa (ngoài phạm vi) —
  nên có một lượt dọn mojibake riêng.
- **`AUTH_SIGNING_KEY` rotation**: đổi khóa = vô hiệu mọi access token đã cấp (refresh token vẫn dùng
  được vì lưu dạng hash). Ghi vào `docs/DEPLOYMENT.md` §4.1/§7 đợt 1.
- **CI đã đỏ SUỐT từ trước đợt 1 — nguyên nhân thật KHÔNG phải Dockerfile (đã sửa xong):** gate
  `Format check` (`dotnet format --verify-no-changes`) fail vì (a) 2 file test còn wrap dòng sai chuẩn
  (`ActionLogNameResolutionTests.cs`, `MaintenanceCampaignTests.cs`) + (b) 1 phát hiện analyzer
  `xUnit2013` (`AssetMaintenanceTests.cs:329` — `Assert.Equal(1, x.Count)` → `Assert.Single`), cộng
  thêm tình trạng **line-ending trộn LF/CRLF** trong repo (không có `.editorconfig`). Vì gate backend
  fail, job `Docker — Build Images` (`needs: [backend, frontend]`) **bị skip** ở MỌI run → lỗi
  Dockerfile (thiếu 3 project sau tách 4-layer) chưa bao giờ được CI phát hiện. Đã sửa cả 2 nhóm:
  commit `c5c97b5` → **CI xanh cả 3 job lần đầu tiên** (run 37017686138). Ghi nhận: ghi chú cũ trong
  `ci.yml` ("CI-2: the whole solution was formatted ... passes clean locally") **không còn đúng** tại
  thời điểm đó. Khuyến nghị cho đợt sau: thêm `.editorconfig` (`end_of_line`, `indent_size`,
  `insert_final_newline`) + chuẩn hoá line endings để tránh tái diễn.

---

## BUG-E — DepartmentsController.UpdateDepartment: full-PUT, field không gửi bị clear (vi phạm patch-safety)

- **Trạng thái: RESOLVED 2026-09-05** (patch-safety fix — behavior change theo sketch đã dự kiến).
  Đổi `UpdateDepartmentCommand`/`UpdateDepartmentRequest` sang nullable-only + handler gán theo
  `is not null` (Task M1/M2 pattern): field không gửi → GIỮ NGUYÊN (không còn bị clear). Name chỉ
  bắt buộc KHI GỬI (blank gửi → 400 như cũ); dup-check chỉ chạy khi name thực sự thay đổi (đồng bộ
  rule Create). LogMeta old→new tự phản ánh đúng field thực sự đổi (unchanged → old==new).
  Tests: `DepartmentPatchSafetyTests` ×4 — bug-repro (PUT chỉ {name} → CompanyId/Phone/Fax GIỮ
  nguyên), full-payload vẫn update đủ (positive, frontend full-form không vỡ), blank-name 400,
  dup-name 400. Full suite 404/404.
- **Phát hiện lúc audit (2026-09-01, Giai đoạn 1 pilot):** patch-safety (cùng lớp lỗi Task M1/M2
  đã fix cho 11 entity khác — xem workflow §Patch semantics). Lưu ý verify cũ: parity old==new đã
  được xác nhận (full-PUT cả 2 phía) — bug KHÔNG được tạo ra bởi migration; frontend Department
  form gửi full payload nên chưa trigger.
- **Vị trí:** `aspire-react.Application/Departments/Commands/UpdateDepartmentCommand.cs` (hành vi di chuyển
  verbatim từ `DepartmentsController.Update` — bug CÓ TỪ TRƯỚC, KHÔNG phải do migrate tạo ra;
  dẫn chứng: HEAD `e060ffa` — `[FromBody] Department updated` + gán vô điều kiện
  `d.Name = updated.Name; d.CompanyId = updated.CompanyId; d.ManagerId = updated.ManagerId;
  d.Phone = updated.Phone; d.Fax = updated.Fax;`)
- **Hành vi hiện tại:** PUT /api/v1/departments/{id} — mọi field không có trong payload bị set
  null/clear (VD: PUT chỉ `{name}` → CompanyId=null (trở thành floater), ManagerId/Phone/Fax mất).
  Update gán Name + CompanyId vô điều kiện; chỉ nhận diện được 2 field bắt buộc (Name) — các field
  còn lại không có cơ chế "absent ≠ changed".
- **Impact:** client gửi payload thiếu field sẽ âm thầm mất dữ liệu department (đúng dạng bug
  "wiped real data" từng xảy ra với Serial/AssetTag — workflow doc Patch semantics).
- **Fix sketch (khi thực hiện):** đổi `UpdateDepartmentRequest` sang nullable-only fields; handler
  gán theo `is not null` (Task M2 pattern); giữ nguyên LogMeta changes (old/new sẽ phản ánh đúng
  chỉ field thực sự đổi); thêm/điều chỉnh test patch-safety (PUT 1 field → còn lại giữ nguyên —
  đổi kỳ vọng từ "bị clear" sang "giữ nguyên" LÀ THAY ĐỔI HÀNH VI — cần duyệt riêng lúc fix).
- **Lưu ý verify:** ở Giai đoạn 1, parity old==new đã được xác nhận (full-PUT cả 2 phía) — bug này
  KHÔNG được tạo ra bởi migration; frontend Department form hiện gửi full payload nên chưa trigger.

---

## BUG-F — Category Update không kiểm tra trùng Name+CategoryType như Create (cho phép rename thành tên trùng lặp)

- **Trạng thái: RESOLVED 2026-09-05** (behavior change theo sketch: rename trùng từ 2xx → 400).
  Dup-check `Name+CategoryType` thêm vào `UpdateCategoryCommandHandler` — chỉ khi name THAY ĐỔI
  THẬT (re-send current name vẫn no-op), exclude self, message đồng bộ Create ("Tên danh mục đã
  tồn tại." — 400 không error_code). **Không hồi tố**: dup tạo trước fix vẫn tồn tại (audit dữ liệu
  thật trước fix: 25 categories có duy nhất 1 cặp trùng = fixture parity G2-era
  "G2-baseline-DUP-20260901091044", đã cleanup cả 2 — 0 references; dev data sạch dup). Tests:
  `CategoryPatchSafetyTests` ×4 — bug-repro (rename trùng bị chặn + bản gốc giữ nguyên), rename
  tên tự do ✔, same-name khác-type vẫn cho phép (rule per-type như Create), re-send current name
  không bị chặn. Full suite 408/408.
- **Phát hiện lúc audit (2026-09-01, Giai đoạn 2, parity verification trên binary cũ):**
  business-rule inconsistency (Create có dup-check, Update không). Backend không có code path nào
  tra cứu Category theo Name — ComponentsController/LicensesController đều lookup theo Id; rủi ro
  chính = UI hiển thị 2 danh mục trùng tên trong cùng loại (user confusion).
- **Vị trí:** hành vi CÓ TỪ TRƯỚC trong `AdminController.UpdateCategory` (HEAD `425be5c` trước
  migrate — không có `AnyAsync(x => x.Name == updated.Name ...)`); hành vi được di chuyển verbatim
  vào `aspire-react.Application/Categories/Commands/UpdateCategoryCommand.cs` (đúng nguyên tắc
  parity — KHÔNG phải do migrate tạo ra).
- **Hành vi hiện tại:** `POST /categories` chặn trùng cặp Name+CategoryType (400 "Tên danh mục đã
  tồn tại."); `PUT /categories/{id}` CHO PHÉP rename thành một Name+CategoryType đã tồn tại
  (2xx, verify thực tế: baseline binary cũ trả 2xx khi rename trùng).
- **Impact assessment (quick, 2026-09-01):** backend không có code path nào tra cứu Category theo
  Name — ComponentsController/LicensesController đều lookup theo Id; **rủi ro chính = UI hiển thị
  2 danh mục trùng tên trong cùng loại (user confusion)**. Frontend có nơi nào select Category
  theo name (thay vì id) hay không: **cần điều tra thêm** (chưa điều tra sâu).
- **Fix sketch (khi thực hiện):** thêm dup-check `Name+CategoryType` vào
  `UpdateCategoryCommandHandler` (đối chiếu `x.Id != request.Id`), message/error_code đồng bộ với
  Create; là THAY ĐỔI HÀNH VI (rename trùng từ 2xx → 400) — cần duyệt riêng lúc fix.
- **Lưu ý verify:** parity old==new đã xác nhận ở Giai đoạn 2 (cả 2 phía cho phép rename trùng).

---

## BUG-H — AssetModel Create/Update thiếu toàn bộ validation + FK không kiểm tra tồn tại + client tự set Id (MEDIUM)

- **Trạng thái: RESOLVED 2026-09-05** (behavior change theo sketch). Implement qua
  `ModelValidation` shared helper dùng chung Create/Update:
  (1) empty-name — "Tên model không được để trống." (400, Update chỉ khi name ĐƯỢC GỬI và blank);
  (2) dup-name — "Tên model đã tồn tại." (Create exact; Update chỉ khi name thực sự thay đổi,
  exclude self; re-send current name = no-op);
  (3) FK-existence ManufacturerId/CategoryId/DepreciationId/FieldsetId — GUID không tồn tại →
  **400 `RESOURCE_NOT_FOUND`** + message "Trường tham chiếu không tồn tại: {field}." (trước đây
  raw FK-violation 500 tại SaveChanges) — check trước mọi mutation.
  Thành phần #1 (client-set-Id) đã loại từ lúc migrate (DTO không có field Id). Audit dữ liệu thật
  trước fix: 14 models, 0 empty-name, 0 dup → không hồi tố vấn đề. Tests: `ModelValidationTests`
  ×7 phủ cả negative/positive/patch-safe. Full suite 415/415.
- **Phát hiện lúc audit (2026-09-01, Giai đoạn 2):** MEDIUM (nghiệp vụ — reference data, KHÔNG có
  company-isolation risk như BUG-G). Vị trí gốc: `AdminController.CreateModel` / `UpdateModel`;
  di chuyển verbatim vào `aspire-react.Application/Models/Commands/` (parity trước, fix riêng).
- **Các thành phần riêng biệt của bug (ghi tách bạch để đánh giá lại khi fix):**
  1. **Client có thể tự set Id qua entity binding** — `CreateModel([FromBody] AssetModel m)` cho
     phép JSON chứa `"id": "<guid>"` → tạo model với PK do client chọn → trùng PK đã tồn tại =
     **PK violation → 500** (cùng bug-class với BUG-C/D: race/constraint chưa được xử lý sạch).
     ⚠️ Khác bản chất với "thiếu dup-check" (BUG-F): đây là lỗi chấp nhận PK từ client.
     *Cập nhật sau migrate (Giai đoạn 2): DTO hóa `CreateModelRequest` (không có field Id) đã TỰ
     LOẠI BỎ quirk này ở endpoint API — client gửi id sẽ bị bỏ qua (server tự sinh). Phần còn lại
     của BUG-H dưới đây vẫn OPEN.*
  2. **Không có bất kỳ validation nào**: không empty-name check, không dup-check Name (Create lẫn
     Update đều không có — khác Manufacturer/Supplier có dup cả 2 chiều), cho phép tạo vô số model
     trùng tên.
  3. **4 field FK không kiểm tra tồn tại**: ManufacturerId / CategoryId / DepreciationId /
     FieldsetId nhận GUID tùy ý từ client — GUID sai → FK violation → **500** tại SaveChanges
     (thay vì 400 sạch).
- **Impact:** dữ liệu models bẩn (name rỗng/trùng), 500 thay vì 400 cho input sai, trải nghiệm
  người dùng + tính nhất quán dữ liệu tham chiếu.
- **Fix sketch (khi thực hiện — THAY ĐỔI HÀNH VI, cần duyệt riêng):** FluentValidation hoặc
  soft-fail cho empty-name + dup-check Name; kiểm tra tồn tại 4 FK trước khi save (400
  RESOURCE_NOT_FOUND); quyết định message/error_code đồng bộ phong cách section.

---

## BUG-I — CustomFields Create/Update: thiếu dup-Slug ở Update (→ 500 DB index) + FULL-PUT ×8 + không empty-Name check (MEDIUM)

- **Trạng thái: RESOLVED 2026-09-05** (cả 3 thành phần, behavior change theo sketch — thành phần
  (b) dup-Slug confirmed-500 ưu tiên trước đúng kế hoạch):
  (a) **FULL-PUT ×8 → patch-safe** (Task M1/M2): `UpdateCustomFieldCommand` + request DTO nullable
  toàn bộ, gán theo `is not null` — field không gửi GIỮ NGUYÊN (Name=null trước đây → DB NOT NULL
  violation → 500).
  (b) **Dup-Slug check thêm vào Update** — chỉ khi slug thực sự thay đổi, exclude self, message
  đồng bộ Create ("A field with this slug already exists." — 400 không error_code); thay raw 500
  đã CONFIRMED. Re-send current slug = no-op.
  (c) **Empty-Name/Slug check** cả Create lẫn Update (blank khi gửi → 400 "Field name is
  required." / "Field slug is required.").
  Audit dữ liệu thật trước fix: 0 custom fields trong dev DB → không hồi tố. **Frontend impact:
  0** — scan toàn frontend: KHÔNG có UI nào gọi /custom-fields CRUD (chỉ /custom-fieldsets GET ở
  trang khác). Tests: `CustomFieldValidationTests` ×5 (bug-repro dup-slug-500 + patch-safe +
  guards + positive). Full suite 420/420.
- **Phát hiện lúc audit (2026-09-01/02, Giai đoạn 3):** MEDIUM (CustomField không có CompanyId,
  không isolation risk như BUG-G). Vị trí gốc: `CustomFieldsController.Update/Create`; di chuyển
  verbatim vào `aspire-react.Application/CustomFields/Commands/` (parity trước, fix riêng).
- **3 thành phần — độ tin cậy KHÁC NHAU (ghi rõ để đánh giá lại khi fix):**
  1. **Update FULL-PUT ×8** — tất cả field gán vô điều kiện; payload thiếu field → bị clear/null
     (Name=null → DB NOT NULL violation → 500). *Độ tin cậy: suy luận từ đọc code (chưa reproduce
     chủ động — payload realistic luôn gửi đủ field nên chưa trigger).*
  2. **Update KHÔNG có dup-Slug check** (Create có) → rename sang slug đã tồn tại → **DB unique
     index violation → raw 500 body rỗng**. ✅ **CONFIRMED VIA REPRODUCTION** — xảy ra chắc chắn
     với BẤT KỲ user nào rename field trùng slug, không cần điều kiện đặc biệt (khác BUG-D cần
     concurrency); reproduce bằng parity script trên binary cũ (baseline step-5 → 500 body rỗng,
     2 lần độc lập).
  3. **Create/Update không empty-Name/Slug check** — name/slug rỗng tạo được. *Độ tin cậy: suy
     luận từ đọc code (chưa chủ động reproduce).*
- **Vị trí gốc:** `CustomFieldsController.Update/Create` (HEAD trước migrate); di chuyển verbatim
  vào `aspire-react.Application/CustomFields/Commands/UpdateCustomFieldCommand.cs` +
  `CreateCustomFieldCommand.cs` kèm comment `// TODO BUG-I` in-code.
- **Impact:** user rename field trùng slug nhận 500 thay vì 400 sạch; payload thiếu field âm thầm
  mất dữ liệu; slug trùng phá tính duy nhất mà Create đã cam kết.
- **Fix sketch (khi thực hiện — THAY ĐỔI HÀNH VI, cần duyệt riêng):** thêm dup-Slug check vào
  Update (đối chiếu `x.Id != request.Id` → 400 "A field with this slug already exists." — thay 500);
  empty-Name/Slug soft-fail; FULL-PUT → nullable patch hoặc giữ nguyên tùy quyết định (BUG-E
  precedent). Ưu tiên #2 trước (confirmed, user-facing 500).

---

## BUG-J — Dashboard monthly-checkout-trend: 500 cho MỌI superuser (visibleAssetIds null → Contains trong EF expression) (MEDIUM)

- **Trạng thái: RESOLVED 2026-09-05** (behavior change 500→200; **root cause ĐÃ RE-DIAGNOSE
  đúng lúc fix** — ghi nhận minh bạch):
  - **Chẩn đoán ban đầu (sai một phần):** visibleAssetIds null → `Contains()` throw → 500 chỉ
    superuser. **Sai lệch:** InMemory unit test pass cả 2 phiên bản → không phát hiện được; live
    test sau branch-fix (superuser branch, không Contains) VẪN 500 → chứng tỏ nguyên nhân khác.
  - **Root cause thật (verified live):** inline projection `$"{g.Key.Year}-{g.Key.Month:D2}"`
    KHÔNG translate được bởi Npgsql (format specifier `:D2` trong composite format) → EF throw
    lúc translate → 500 cho MỌI caller (cả superuser lẫn regular; baseline chỉ test superuser
    nên ghi nhầm "chỉ superuser"). Fix: GroupBy (Year, Month) + count aggregate trong SQL, build
    month-string client-side (D2 in memory). Regular-user path giữ contains-filter; superuser
    branch không filter (giữ hành vi "sees all" đúng thiết kế).
  - **Verified:** Release binary + live Postgres → 200 + data chuẩn (month "2026-08"/"2026-09",
    counts khớp ActionLogs thật). Tests: `DashboardTests` +2 (superuser 200 với data, regular
    user scope không leak company B) — InMemory pass vì client-eval projection (bài học: bug này
    KHÔNG test được bằng InMemory, phải live-verify). Full suite 428/428.
- **Phát hiện lúc audit (2026-09-03):** MEDIUM (API defect confirmed-reproduce NHƯNG zero
  frontend impact — DashboardPage không gọi endpoint này). Lưu ý process-lesson: `dotnet run`
  mặc định Debug — các lần verify trước đây dùng `--no-build` sau khi build Release có thể đã
  chạy Debug binary cũ; lần này dùng `--configuration Release` tường minh.
- **Mức độ: MEDIUM** (API defect thật, confirmed reproduce — NHƯNG **zero frontend impact**: grep
  toàn frontend, endpoint monthly-checkout-trend KHÔNG được gọi — DashboardPage chỉ gọi 5 endpoint
  còn lại; khác BUG-K user-facing thật).
- **Hành vi:** GET /api/v1/dashboard/monthly-checkout-trend với superuser → visibleAssetIds null
  (superuser không bị filter company) → `Contains()` trên collection null trong EF expression →
  ArgumentNullException lúc translate → 500. Regular user KHÔNG bị (visibleAssetIds có giá trị).
- **Fix sketch (THAY ĐỔI HÀNH VI, cần duyệt riêng):** guard visibleAssetIds null → skip Contains
  filter (200 + full data) — quyết định cùng đợt dọn patch-safety/bug sau migration.

---

## BUG-G — Location.Create KHÔNG có company-scoping và không có validation nào (SECURITY/HIGH)

- **Trạng thái: RESOLVED 2026-09-05** (SECURITY/HIGH — fix hành vi thật, thiết kế duyệt riêng
  trước khi code theo quy trình behavior-change). Re-audit dữ liệu thật trước fix: 8 locations
  (1 thật "QA AUD Loc" do superuser tạo + 7 PRT fixture floater) — **0 cross-company sign** → fix
  không ảnh hưởng dữ liệu hiện có.
  - **Thay đổi hành vi (thiết kế Bước 2 đã duyệt):** `CreateLocationCommandHandler` thêm check
    ĐẦU TIÊN theo đúng pattern Task L2 (`CreateDepartmentCommand`): regular user chỉ tạo được
    Location cho company của chính họ (hoặc floater CompanyId=null); superuser bỏ qua; mismatch →
    **400 + `error_code: COMPANY_MISMATCH`** + message "Bạn chỉ được tạo địa điểm cho công ty của
    mình." Blocked request không tạo row và không tạo ActionLog. Positive path verified trên
    binary thật (admin tạo location company + floater 200/200, không regression).
  - **Phạm vi fix — CHỈ company-scoping** (tránh gộp): empty-name check + dup-name check vẫn còn
    thiếu → thuộc nhóm patch-safety dọn dẹp sau. **Limitation đã biết, chấp nhận (nhất quán với
    Department — KHÔNG phải thiếu sót riêng của Location):** không có company-existence check
    (CompanyId không tồn tại vẫn pass — chỉ data-hygiene, không security-risk).
  - **Tests:** `LocationCommandTests` ×4 (FakeScope, unit-test là đủ theo duyệt — không tạo user
    Keycloak thật): negative A→B = COMPANY_MISMATCH + 0 row + 0 log; positive A→A ✔; floater ✔;
    superuser → company B ✔. Full suite 400/400 (396 + 4 mới).
- **Phát hiện lúc audit (2026-09-01, Giai đoạn 2):** Mức độ SECURITY/HIGH (khác MEDIUM của
  BUG-E/F): regular user (không phải superuser) có thể
  tạo Location cho **company BẤT KỲ** (cross-company creation) — vi phạm trực tiếp company-isolation
  (nguyên tắc cứng nhất của dự án, convention Task L2: "Create out-of-scope → 400 COMPANY_MISMATCH");
  ngoài ra không có empty-name/dup-name check (location name rỗng cũng tạo được).
- **Vị trí:** hành vi CÓ TỪ TRƯỚC trong `AdminController.CreateLocation` (bind cả entity
  `Location l` + `Add + Save` ngay, không check gì); hành vi di chuyển verbatim vào
  `aspire-react.Application/Locations/Commands/CreateLocationCommand.cs` (đúng nguyên tắc parity —
  KHÔNG phải do migrate tạo ra) kèm comment `// TODO SECURITY BUG-G` ngay trong handler.
- **Scoping hiện tại của section:** GetAll/Update/Delete CÓ scope (filtered/404); CHỈ Create
  thiếu hoàn toàn (3/4 path đã đúng, 1 path sai). GetById MỚI áp dụng scoped-404 theo quyết định
  đã duyệt (không lấy Create sai làm chuẩn).
- **Impact assessment (2026-09-01, read-only SQL):** tổng 1 location trong DB (QA fixture
  "QA AUD Loc"), 0 user/asset tham chiếu, **0 cross-company sign** → chưa có dữ liệu thật bị ảnh
  hưởng → giữ ở backlog chờ; nếu sau này phát hiện dữ liệu thật bị tạo sai company → chuyển xử lý
  ưu tiên riêng.
- **Fix sketch (khi thực hiện — là THAY ĐỔI HÀNH VI, cần duyệt riêng):** thêm
  `ICompanyScopeService.GetCurrentUserCompanyIdAsync()` check vào `CreateLocationCommandHandler`
  (mismatch → 400 COMPANY_MISMATCH, message đồng bộ Department/Asset pattern), quyết định có thêm
  empty-name check hay không.

---

## INCIDENT-1 (LOW, RESOLVED): Xóa nhầm company MIRAT (dev-seed) trong lúc audit Companies guard do dùng entity có sẵn làm fixture thay vì tạo mới

- **Trạng thái:** RESOLVED — xảy ra 2026-09-03 trong Giai đoạn 3 (audit Companies trước khi migrate),
  recovery hoàn tất cùng phiên; không phải BUG (không phải lỗi code — guard 10-blockers chạy ĐÚNG,
  lỗi là quy trình testing của agent)
- **Chuỗi sự kiện:** baseline run-1 dùng company MIRAT có sẵn làm fixture cho DELETE guard với giả
  định "MIRAT có users/assets" KHÔNG kiểm chứng (mọi user seed đều company-less; assets thuộc
  company QCR/QA khác) → guard 10-blockers verified 0 references (đúng logic) → cho qua → DELETE
  thành công thật. Phát hiện ở run-2 (POST dup-code với code=MIRAT → 2xx bất thường; guard → 405
  do id rỗng).
- **Recovery:** recreate qua API ngay trong phiên — ID mới `aefe9209-1890-449d-b4a2-1db18df6f033`
  (name "Công ty Quản lý bay miền Trung" UTF-8 chuẩn, code MIRAT, root) — 0 FK references tại thời
  điểm xóa (chính là lý do guard cho qua) → không orphan dữ liệu nghiệp vụ nào.
- **Hệ quả còn lại:** 14 ActionLog rows mang CompanyId/ItemId cũ (`088d28b7-8268-44f0-892b-c111ea53a9bd`)
  không resolve được — 12 rows = 6 cặp Create+Delete log của component fixture đã xóa từ 2026-09-01
  (history đã đóng) + 2 rows = Create (2026-08-28, seed-era) và Delete log của chính MIRAT cũ.
  **Giữ nguyên, KHÔNG sửa** (audit log là append-only history — sửa log = vi phạm tính toàn vẹn
  audit). Xác minh bằng dynamic scan toàn bộ cột `CompanyId` trong DB qua `information_schema`:
  12 references, 0 ở bảng nào khác.
- **Phạm vi:** dev-only (Aspire local stack, seed data) — không ảnh hưởng production/demo.
- **Quy trình sửa từ đây về sau (áp dụng ngay, xem playbook §8):** fixture test guard BẮT BUỘC
  tạo mới hoàn toàn qua API trong chính lượt test (create → link reference → test guard → cleanup
  ngược thứ tự) — TUYỆT ĐỐI không dùng entity có sẵn trong DB (dù trông giống fixture, dù tên
  nghe như QA/test) làm đối tượng test guard.

---

## BUG-K — Groups Create/Update: không có dup-Name check, không empty-Name check (MEDIUM)

- **Trạng thái: RESOLVED 2026-09-05** (behavior change theo sketch; case-insensitivity đã quyết
  tại thời điểm fix). Audit dữ liệu thật trước fix: 2 groups (Admin, Superuser — đều IsSystem,
  seed) → 0 dup, 0 empty → không hồi tố. Fix:
  1. **Create dup-Name check (CONFIRMED bug)** — case-INSENSITIVE ("Admin"/"admin" = trùng —
     group name là role-like identifier), exclude-none, 400 "A group with this name already
     exists." (không errorCode — soft-fail style section);
  2. **Create empty-Name check** — 400 "Group name is required.";
  3. **Update dup-Name on rename** — chỉ khi name THAY ĐỔI (re-send current name = no-op), case-
     insensitive, exclude self; empty-name cũng chặn; GUARD ORDER: SYSTEM_GROUP_LOCKED vẫn check
     TRƯỚC validation (đúng hàng rào cũ).
  Frontend impact: 0 vỡ — `GroupFormModal` trims name trước khi gửi + hiển thị `message` từ error
  response → dup/empty message hiện đúng chỗ. (Convention `errorCode` camelCase + `Permissions[]
  .Value` int GIỮ NGUYÊN verbatim — ghi nhận riêng, không thuộc BUG-K.) Tests: `GroupValidation
  Tests` ×5. Full suite 426/426.
- **Phát hiện lúc audit (2026-09-03, Giai đoạn 3):** MEDIUM user-facing — GroupListPage quản trị
  group là UI thật (khác BUG-J zero-impact; không phải SECURITY — PermissionGroup không có
  CompanyId, không isolation risk). Vị trí gốc: `GroupsController.CreateGroup/UpdateGroup`;
  di chuyển verbatim vào `aspire-react.Application/Groups/Commands/`.
- **Các thành phần — độ tin cậy:**
  1. **Create không dup-Name check** ✅ **CONFIRMED VIA REPRODUCTION** — POST 2 group cùng tên →
     cả 2 đều 201 (baseline bắt được trên binary cũ; post parity 2xx). Hệ quả: danh sách group trùng
     tên, phân quyền theo tên gây hiểu nhầm.
  2. **Create không empty-Name check** — *suy luận từ đọc code (Create bind Name trực tiếp, không
     check gì — cùng code path với #1 nên xác suất rất cao)*; nhóm tên rỗng tạo được.
  3. **Update rename không dup-Name check** — *suy luận từ đọc code* (Update chỉ check
     SYSTEM_GROUP_LOCKED, không check trùng tên nhóm khác).
- **Vị trí gốc:** `GroupsController.CreateGroup/UpdateGroup` (HEAD trước migrate); di chuyển verbatim
  vào `aspire-react.Application/Groups/Commands/CreateGroupCommand.cs` + `UpdateGroupCommand.cs`.
- **Fix sketch (khi thực hiện — THAY ĐỔI HÀNH VI, cần duyệt riêng):** dup-Name check
  (case-insensitive? quyết định khi fix) → 400 "A group with this name already exists."; empty-Name
  soft-fail. Ưu tiên #1 (confirmed, user-facing).
- **Convention inconsistency (ghi nhận kèm — LOW priority, KHÔNG fix trong migration):** controller
  này dùng `errorCode` (camelCase) trong error bodies thay vì `error_code` (snake_case) như mọi
  controller khác; `GET /groups` trả `Permissions[].Value` là số int thay vì string enum. Cả 2 giữ
  nguyên verbatim vì parity — nếu sau này thống nhất convention phải sửa đồng bộ frontend.

---

## BUG-L — Reports checkout-history: date filters → raw 500 (DateTime Kind=Unspecified vs timestamptz) (MEDIUM)

- **Trạng thái: RESOLVED 2026-09-05** (behavior change 500→200 theo sketch). Fix đúng DateTime
  Kind convention: 2 filter params (bind Kind=Unspecified) được `DateTime.SpecifyKind(value,
  DateTimeKind.Utc)` trước khi so sánh với `timestamptz` ActionDate (reinterpreted AS UTC — khớp
  contract vì ActionDate luôn ghi bằng UtcNow). Verify: **live Postgres + Release binary →
  filtered 200 + unfiltered 200** (trước fix filtered = 500 confirmed). Zero frontend impact giữ
  nguyên (không caller); fix chỉ mở đường cho caller tương lai. Tests: `CheckoutHistoryReport
  Tests` ×2 pin filter-logic (InMemory không reproduce được Npgsql Kind-throw — live-verify là
  bằng chứng chính, ghi rõ). Full suite 430/430.
- **Phát hiện lúc audit (2026-09-03, Giai đoạn 3):** MEDIUM — user-facing 500 khi có filter,
  NHƯNG zero frontend impact (ReportsPage chỉ gọi depreciation + audit) → latent API defect.
  Điều kiện kích hoạt: BẤT KỲ caller nào truyền startDate/endDate (policy reports.view).
- **Điều kiện kích hoạt:** BẤT KỲ caller nào truyền startDate hoặc endDate (chỉ có superuser/
  admin token gọi được — policy reports.view; không user frontend nào bị ảnh hưởng).
- **Fix sketch:** SpecifyKind UTC cho 2 filter params; nếu build UI filter cho report này sau
  này thì PHẢI fix BUG-L trước (ghi dependency).

---

## INFRA-1 — Điều tra hạ tầng dev: Docker Desktop/WSL2 mất engine ×3 + file-loss/revert anomalies
- **Trạng thái:** OPEN — điều tra lần 1 hoàn tất 2026-09-02 (kết quả dưới); reopen khi tái diễn
- **Triệu chứng đã xảy ra (3 lần Docker + 2 lần file):**
  1-3. Docker Desktop engine/daemon mất đột ngột (containers Exited 255, npipe biến mất,
  `docker exec` connection reset) — lần 3 trong phiên Giai đoạn 2; volumes + dữ liệu NGUYÊN VẸN
  cả 3 lần (Aspire volumes persistent); engine lên lại <1 phút sau khi start Docker Desktop.
  a. 31 file (docs + PNG) mất khỏi working tree giữa phiên Giai đoạn 1 (git restore khôi phục
  100% từ index) — KHÔNG có thao tác git nào giải thích được.
  b. AdminController revert về HEAD giữa phiên Models (các edit Models-turn biến mất) — nguyên
  nhân không xác định, phát hiện qua state-audit; viết lại deterministic + verify.
- **Điều tra lần 1 (2026-09-02) — kết quả:**
  - Docker Desktop 29.6.2, backend = WSL2 distro `docker-desktop` (v2); AutoStart=False;
    Windows Event Logs (Application/System, 7 ngày) KHÔNG có bất kỳ error nào của
    Docker/WSL/vmcompute → daemon chết KHÔNG để lại trace ở mức Windows Event (đặc trưng
    WSL2 VM abort âm thầm).
  - Docker host logs (`%LOCALAPPDATA%\Docker\log\host\`) có init.log/monitor.log rotate dày
    (~3-5 phút/lần) → backend churn; cần đọc sâu trong điều tra lần 2 nếu tái diễn.
  - git reflog 25 entry: 100% là commit/amend/reset có chủ đích của agent → mất file/revert
    KHÔNG phải do git; khả năng cao tác nhân ngoài (process khác trên máy, sync/AV, sự cố
    NTFS) — chưa có smoking gun.
- **Đánh giá:** 3 lần Docker = CÙNG LỚP sự cố hệ thống (WSL2/Docker Desktop instability) — không
  phải ngẫu nhiên rời rạc; 2 vụ file anomaly chưa rõ nguyên nhân (không cùng cơ chế với Docker —
  file nằm trên NTFS working tree, không trong WSL).
- **Khuyến nghị:** (1) `wsl --update` + cập nhật Docker Desktop; (2) cân nhắc bật AutoStart và
  rà `.wslconfig` (memory/CPU); (3) giữ quy trình `git status` audit sau mỗi batch (đã áp dụng);
  (4) nếu tái diễn lần 4+ → đọc sâu Docker host logs + Windows Reliability Monitor + cân nhắc
  reinstall WSL2.
- **Tái diễn lần 4 (2026-09-04, subtask B post-verify):** engine mất đột ngột giữa phiên —
  `docker version` chỉ còn Client (không Server section), `docker ps` rỗng, AppHost + API chết
  theo; volumes/dữ liệu nguyên vẹn; `Docker Desktop.exe` start lại → engine lên trong ~1 phút
  (đúng pattern 3 lần trước). Docker Desktop vẫn 29.6.2 / 4.84.0 — khuyến nghị (1)(2) vẫn chưa
  thực hiện. Lần 4 cùng lớp WSL2-instability, KHÔNG kèm file anomaly (xác nhận tách bạch với
  INFRA-2: tree sạch sau restart).
- **Tái diễn lần 5 (2026-10-02, phiên fix đợt 1 — giữa lúc chạy live test audit):** engine mất đột
  ngột (`docker ps` → `failed to connect to the docker API at npipe:////./pipe/dockerDesktopLinuxEngine`),
  AppHost (job `dotnet run`) chết theo với exit code 1; `Docker Desktop.exe` start lại → engine lên
  trong ~5 giây (nhanh nhất trong 5 lần); **volume dev `postgres-data` nguyên vẹn** (fixture QA và
  dữ liệu PRT còn đủ sau restart) → cùng lớp WSL2-instability, KHÔNG kèm file anomaly.
  Bài học vận hành bổ sung: sau khi engine chết, process `aspire-react.AppHost` CŨ vẫn giữ port
  dashboard (VD 22073) → lần `dotnet run` kế tiếp fail `Failed to bind to address ... address already
  in use`; phải diệt theo **port** (không kill mù toàn bộ dotnet) rồi mới start lại.

---

## BUG-M — Users Create/Update/Delete: log 2 lần, log thứ 2 không atomic với data (LOW)

- **Trạng thái:** OPEN — phát hiện 2026-09-03 trong Giai đoạn 3 (audit Users trước khi migrate
  4 action inline; 3 action write giữ nguyên theo ranh giới đã duyệt)
- **Mức độ: LOW** (xác suất trigger thấp — chỉ khi `SaveChanges` của log thứ 2 fail sau khi
  command đã commit; không có isolation risk như BUG-G, không user-facing 500 như BUG-L)
- **Vị trí:** hành vi CÓ TỪ M1 trong `UsersController.CreateUser/UpdateUser/DeleteUser`
  (`aspire-react.Server/Web/Controllers/UsersController.cs` — handler M1 đã `LogAction +
  SaveChanges` 1 lần trong command, controller sau `_mediator.Send` lại `LogAction +
  SaveChanges` lần 2 với note khác: Create `"Tạo người dùng ..."` vs `"Created user: ..."`,
  Update `"Cập nhật người dùng ..."` vs `"Updated user: ..."`, Delete `"Vô hiệu hóa ..."`
  vs `"Deactivated user: ..."`). Migrate verbatim theo nguyên tắc parity — KHÔNG fix trong
  task Users (ranh giới đã duyệt: chỉ migrate 4 action inline, giữ nguyên 3 Command M1).
- **Vi phạm nguyên tắc:** "ActionLog phải atomic với data" (workflow §3.2 — log persist cùng
  transaction với thay đổi, rollback cùng nhau). Ở đây data đã commit trong handler trước khi
  log thứ 2 của controller được stage/persist ở transaction riêng → nếu `SaveChanges` thứ 2
  fail, data tồn tại mà log thứ 2 mất (dù log thứ 1 trong handler vẫn còn → hệ quả thực tế
  chỉ là thiếu 1 bản log trùng lặp, không mất audit hoàn toàn — chính vì vậy mức LOW).
- **Fix sketch (khi thực hiện — là THAY ĐỔI HÀNH VI ghi log, cần duyệt riêng, tốt nhất gộp
  vào đợt dọn dẹp ActionLog toàn diện):** bỏ `LogAction + SaveChanges` thứ 2 ở controller,
  chuyển 3 Command M1 sang `ILoggableCommand` (behavior mở 1 ambient transaction: handler
  `SaveChanges` join vào, behavior stage log + save + commit 1 lần → data+log atomic, đúng
  playbook §4); quyết định giữ note/LogMeta nào trong 2 bản hiện tại (controller bản tiếng
  Việt vs handler bản tiếng Anh + LogMeta chi tiết).

---

## INFRA-2 — 6 file PNG evidence (root) mất khỏi working tree — ⚠️ TÁI DIỄN LẦN 2 (CÙNG ĐÚNG 6 FILE) — nâng mức: nghi có nguyên nhân HỆ THỐNG, không phải ngẫu nhiên

- **Trạng thái: REOPENED — TÁI DIỄN LẦN 2 (2026-09-06, phiên AUTH Phase 1).** Cùng ĐÚNG 6 file bị
  mất LẦN NỮA sau khi đã khôi phục và đã push (`fc84f25` trở đi — file nằm an toàn trên remote),
  phát hiện qua `git status --porcelain` ngay sau commit Phase 1 (`3110436`). Khôi phục lần 2 bằng
  `git checkout HEAD --` (100% OK). **Kết luận đã nâng:** 2 lần cùng một bộ file chính xác = KHÔNG
  còn giải thích được là ngẫu nhiên/tiền-phiên — nghi có tác nhân hệ thống có CHỌN LỌC target đúng
  nhóm PNG này (thời điểm loss lần 2: trong phiên, giữa lúc agent chạy build/test/commit — KHÁC
  với lần 1 vốn là tiền-phiên). Điều tra chuyên sâu lần 2 đã chạy ngay:
  - **Loại trừ agent hiện tại (lần 2):** toàn bộ `Remove-Item` trong phiên nhắm `_parity_*.ps1`
    (root), `apphost.log`, thư mục TEMP — không một lệnh nào target `*.png`; mọi `git add` đều
    path-explicit; chỉ dùng `git checkout HEAD --` (khôi phục, không xóa).
  - **Loại trừ scripts của repo:** `audit-sweeps.ps1` (không đụng file), `docker-reset.ps1`
    (chỉ remove Docker volumes `mirats-*`), `seed-initial-admin.ps1` (chỉ Remove temp body file)
    — KHÔNG script nào quét/xóa PNG.
  - **Loại trừ cloud-sync/NTFS attribute:** file attributes thuần `Archive` (không OneDrive/
    Sparse/Recall flags), drive D:\ NTFS local, free 48.9GB.
  - **Nghi phạm còn lại (điều tra tiếp nếu tái diễn lần 3):** Windows Storage Sense/StorSvc
    (đang Running — cần kiểm tra cấu hình "clean temp files" có target thư mục này không),
    antivirus/EDR quét-xóa, tiến trình khác trên máy. **Hành động phòng ngừa đã áp dụng:** 6
    file đã push lên remote (an toàn vĩnh viễn trên git); khuyến nghị commit lịch theo dõi —
    nếu tái diễn lần 3, snapshot `Get-Process` + `Handle` list ngay khi phát hiện.
- **Lịch sử lần 1 (2026-09-04):** phát hiện trong subtask A (Giai đoạn 3) ở trạng thái `" D"`
  tiền-phiên, khôi phục từ git blob; khi đó đánh giá "không xác định được thời điểm xóa".
- **Hiện tượng (giống hệt 2 lần):** `mc8_template_builder_nested.png`, `mc8b_after_expand.png`,
  `mc8b_after_form.png`, `qa7d_campaign_3of3.png`, `qa7d_campaign_detail.png`,
  `qa7d_template_builder.png` (evidence QA đợt MC-7d/MC-8/MC-9, commit `4c08d9b` 2026-08-29) —
  mất vật lý khỏi working tree, `git diff HEAD` = N → 0 bytes.
- **Vì sao TÁCH RIÊNG khỏi INFRA-1 (giữ nguyên, thêm đánh giá lại):** lần 1 không timeline;
  **nhưng lần 2 CÓ timeline trong phiên → ranh giới với INFRA-1 mờ đi** — nếu lần 3 đồng thời
  với Docker/WSL2 sự cố thì GỘP vào INFRA-1 làm một hồ sơ hạ tầng; nếu lần 3 vẫn độc lập thì
  giữ riêng và điều tra như một tác nhân file-targeting độc lập.

---

## BUG-N — AssetMaintenances Update: SupplierId/CompletionDate/Cost gán trực tiếp, field absent bị clear (cùng lớp BUG-E)

- **Trạng thái: RESOLVED 2026-09-05** (behavior change theo sketch — phương án "chuyển 3 field
  sang conditional assign" đã chọn). `UpdateMaintenanceCommandHandler`: `SupplierId`,
  `CompletionDate`, `Cost` giờ gán CHỈ KHI GỬI (`is not null`/`HasValue` — Task M1/M2 pattern,
  đồng bộ Title/Notes/Type/IsWarranty vốn đã conditional); `Notes` đổi từ `?? m.Notes` sang
  `is not null` cho nhất quán (subtle: empty-string notes trước đây vẫn ghi đè qua `??` — giờ
  empty-string là giá trị gửi, vẫn ghi đè đúng; chỉ absent mới giữ). `CompletionDate` vẫn
  SpecifyKind Unspecified khi gởi. **Known limitation (same as all patch DTOs):** JSON explicit
  null indistinguishable from absent → không có "clear có chủ đích" (FE gửi prefill đủ field khi
  muốn đổi thành null — MaintenanceCompleteModal đã verify). Frontend impact: 0 — 2 caller
  (`AssetMaintenanceSection` full-form, `MaintenanceCompleteModal` gửi 5 field kèm đủ 3 field
  này prefill từ record). Tests: `Update_PatchSafe_PartialPayload_KeepsExistingSupplierCost
  Completion_BugRepro` (PUT chỉ {title} → SupplierId/CompletionDate/Cost GIỮ NGUYÊN). Full suite
  421/421.
- **Phát hiện (subtask C, Giai đoạn 3, nhóm Rất nặng):** MEDIUM — cùng lớp patch-safety
  BUG-E/BUG-I#1: không isolation risk. Hành vi gốc: 3 field gán TRỰC TIẾP từ DTO nullable;
  payload thiếu field → bị clear/null. *Độ tin cậy lúc audit: suy luận từ đọc code (payload
  realistic của frontend luôn gửi đủ field nên chưa trigger).* Migrate verbatim trước, fix riêng
  bây giờ.
