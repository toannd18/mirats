# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project overview

AspireReact — a Snipe-IT-style enterprise IT asset management system, migrated to .NET 9/10 + React 19 + Ant Design 6, orchestrated with .NET Aspire 13.4. Multi-tenant (company-scoped), asset lifecycle (checkout/checkin/audit), consumables/components/accessories, software licenses/seats, reporting, CSV import/export. Repo root is not a git repository (no `.git`) — treat any git operations as out of scope unless the user sets one up.

The actual solution lives under `aspire-react/` (root has only docs, screenshots, and scratch QA output files).

## MANDATORY: read the workflow doc first

**Before writing or modifying any code, read [docs/DEVELOPMENT_WORKFLOW.md](docs/DEVELOPMENT_WORKFLOW.md) in full.** It is the authoritative, continuously-updated source of truth for this project's conventions — distilled from real bugs that have happened in this codebase, not theory. `.clinerules/*.md` (below) summarize/route to it but explicitly defer to it on conflict. If `docs/DEVELOPMENT_WORKFLOW.md` is missing or unreadable, stop and tell the user before proceeding — do not guess at conventions.

Key non-negotiables from that doc (see it for full detail and rationale):

- **Audit before coding.** Grep/read the actual code and report current state before proposing changes — never assume from memory, old reports, or documentation (docs go stale; code is the only source of truth).
- **Current user identity**: always use claim `local_user_id` (JIT-provisioned by `IJitUserProvisioningService` — invoked from the JWT `OnTokenValidated` handler in `Infrastructure/Authentication/AuthenticationServiceCollectionExtensions.cs`, which stamps `local_user_id`). Never use Keycloak `sub` or `preferred_username` as a user FK. Use `ICurrentUserService.GetLocalUserId()` / `IActionLogService.GetCurrentUserIdAsync()`.
- **ActionLog is mandatory** for every Create/Update/Delete/Checkout/Checkin/Confirm/Close/Reopen/Inspect/Dispose/Import, with `TargetType`, `TargetId`, `CompanyId` of the affected record, and `LogMeta = { changes: { field: { old, new } } }` for updates. Must be persisted in the same transaction as the data change.
- **Company-scoping is explicit on BOTH read AND write endpoints** (List/Detail/Create/Update/Delete — Task I/J/K/L2) — the global EF query filter in `AppDbContext` is currently a no-op (`GetUserCompanyIdsAsync()` returns `[]`). Use `ICompanyScopeService.GetCurrentUserCompanyIdAsync()`; don't rely on the filter. (Read out-of-scope → 404; Create out-of-scope → `400 COMPANY_MISMATCH`.)
- **Permissions**: backend needs `[Authorize(Policy = "<resource>.<action>")]` (key must exist in `PermissionCatalog`) on every write endpoint; frontend gates every sensitive action button with `usePermission('<code>')`. Don't use `isSuperUser()` as the primary gate — reserve it for genuinely superuser-only actions (e.g. Reopen maintenance). Anti self-lockout via `PermissionLockoutGuard` on Group/User admin ops (Task J); `UsersController.UpdateUser` requires policy `admin` (not `users.edit`) — mirror `usePermission('admin')` in `UserListPage`.
- **Delete-guard by usage history**: records with transaction history (checkout/allocation) must not be hard-deleted — block or soft-delete (`DeletedAt`). Master data deletes must check *all* referencing tables, not just the current module.
- **Patch semantics on Update** (Task F→M1→M2, 11 entities: Asset, Component, License, Consumable, User, Category, Manufacturer, Supplier, Location, AssetModel, Accessory): request DTO fields must be nullable; the handler assigns ONLY when the field was actually sent (`is not null`/`HasValue`). An absent field must NEVER be treated as "changed" or overwritten with default/null — this previously wiped real Serial/AssetTag data. Whitelist fields; fields locked post-creation reject with `FIELD_LOCKED` (not silently ignore), except intentionally-ignored fields like Qty.
- **Enums are serialized as strings** by the API (global `JsonStringEnumConverter`). Never compare frontend enum values to numbers (`status === 2`) — use string comparison or the `normalize*` helpers in `frontend/src/types/asset.ts`. (Known recurring bug class — see Appendix A in the workflow doc.)
- **DateTime Kind**: Postgres `timestamp without time zone` columns MUST be written with `DateTime.SpecifyKind(value, DateTimeKind.Unspecified)`; `with time zone` columns use `DateTime.UtcNow` (Kind=UTC). Wrong Kind → Npgsql throws 500. See `docs/HANDOFF_DATETIME_KIND_AUDIT.md`.
- **Concurrency**: checkout/allocate must lock rows `FOR UPDATE` inside a transaction (Asset `FromSqlRaw ... FOR UPDATE`, License seat/Accessory/Component/Consumable — Task O-FIX). No lock → stock overcommit / lost update (reproduced empirically).
- **EF InMemory does NOT enforce real Postgres constraints** (DateTime Kind, transactions/locks, raw SQL, unique indexes) — `dotnet test` PASS is NOT sufficient evidence. Any change touching DateTime, transaction/lock, raw SQL, or constraints MUST be verified by calling the real API on the Aspire stack.
- **DI registration lives in layer extensions, not Program.cs** (Task Q): add services to the matching `Infrastructure/*/...ServiceCollectionExtensions.cs` or `Application/ApplicationServiceCollectionExtensions.cs`. Program.cs is a thin composition root (~93 lines) that only calls the `Add*` extension methods + `StartupDataSeeder.Seed(...)`.
- **MediatR `ValidationBehavior` is wired** (Task L): FluentValidation validators now actually run in the request pipeline (mapped to 400 by `ValidationExceptionHandler`). Writing a new validator takes effect immediately — no need to call validators manually.
- **Redis OutputCache** (Task P): 5 reference-data endpoints (categories/manufacturers/suppliers/permissions/companies) are cached via `ReferenceDataCachePolicy` (TTL 300s, Redis-backed via `AddRedisCaching`). When adding/editing an endpoint that writes these groups, invalidate the corresponding cache entry. Do NOT cache business/inventory data (checkout/Qty/stock) with output caching.
- **Schema changes**: EF Core Migrations are the current mechanism (baseline `InitialBaseline` applied 2026-08-14; `StartupDataSeeder.Seed(app.Services)` calls `db.Database.Migrate()` at startup). Use `dotnet ef migrations add <Name>` → review the generated file → `dotnet ef database update`. Do **not** write raw SQL self-heal blocks anymore (that pattern is retired).
- **After any file edit, re-read it from disk** to confirm the change actually persisted — do not trust tool "success" output or diff output alone.
- **UI changes need real verification**: actual clicks/console/network, plus screenshots of the exact affected screen (3 breakpoints — ~375px/~768px/~1440px — for responsive changes). `tsc --noEmit` / build passing is not sufficient evidence.

## Frontend Verification & Testing Rules

- ƯU TIÊN dùng playwright-cli (session-based) cho mọi verify cần đăng nhập, click chuỗi hành động, hoặc so sánh 2 user/session — đây là công cụ duy nhất trong dự án làm được việc này, KHÔNG bị cấm sử dụng.
- BẮT BUỘC mỗi lệnh playwright-cli phải có giới hạn thời gian chờ rõ ràng. Nếu 1 lệnh không phản hồi quá 60 giây, hủy và thử lại tối đa 1 lần — không để treo vô thời hạn.
- BẮT BUỘC đóng session (`playwright-cli -s=<tên> close`) ngay sau khi dùng xong, kể cả khi verify thất bại giữa chừng — không để session mở sót lại gây xung đột cho lệnh tiếp theo trong cùng phiên hoặc phiên sau.
- TRÁNH mở nhiều session song song trong 1 lệnh gộp (VD mở 3 session cùng lúc để verify 3 breakpoint hoặc 3 user) — verify TUẦN TỰ từng session, đóng session này trước khi mở session tiếp theo, giảm rủi ro treo do tranh chấp tài nguyên.
- Với các trường hợp CHỈ cần xem giao diện tĩnh (không cần đăng nhập, không cần tương tác) — dùng `npx playwright screenshot <url> <path>` cho nhanh, thay vì mở session đầy đủ không cần thiết.
- Nếu playwright-cli treo quá 60 giây dù đã áp dụng các quy tắc trên: dừng lại, kiểm tra process Chromium còn sót (kill nếu cần), báo cáo rõ bước nào không verify được bằng UI thật — không cố lặp lại vô hạn, không bỏ qua bước verify mà không báo cáo.
- KHÔNG dùng lệnh tương tác chặn terminal theo kiểu mở GUI rồi chờ người dùng thao tác tay (VD `playwright codegen` mở trình duyệt cho người dùng tự click) — mọi verify phải chạy tự động, có thể lặp lại được bởi agent.
- **Sweep the whole codebase** when a bug represents a known error class (see Appendix A of the workflow doc: claim misuse, enum-as-number comparisons, missing ActionLog, missing company-scoping, missing `usePermission`, invalid Postgres `ADD CONSTRAINT IF NOT EXISTS`, cascade-delete of historical records). Run `scripts/audit-sweeps.ps1` to check sweeps 1–4 automatically.
- Break large tasks into approval-gated subtasks; don't bundle unrelated risk categories (e.g. security fix + schema migration) into one approval.

## Commands

All backend/solution commands run from `aspire-react/` (where `aspire-react.sln` lives); frontend commands run from `aspire-react/frontend/`.

### Run the full stack (Aspire orchestrates Postgres, Redis, Keycloak, backend, frontend)
```bash
cd aspire-react/aspire-react.AppHost
dotnet run
```
Frontend: http://localhost:5173 · API: http://localhost:5428 (HTTP) / https://localhost:7314 (HTTPS) · Keycloak admin: https://localhost:8080/admin · dev login: user `admin`, password đọc từ file gitignored `.mirats-test-admin-password` ở repo root (rotated 2026-08-29 [SECRET-ROTATE] — giá trị cũ trong git history đã bị vô hiệu; Keycloak master admin password lưu ở AppHost user-secrets `Parameters:kcBootstrapAdminPassword`).

### Setup secrets lần đầu (người clone mới — làm MỘT lần, KHÔNG commit giá trị thật)
```bash
cd aspire-react/aspire-react.AppHost
dotnet user-secrets init
dotnet user-secrets set "Parameters:dbPassword" "<postgres-password-của-bạn>"
dotnet user-secrets set "Parameters:kcBootstrapAdminPassword" "<keycloak-master-admin-password-của-bạn>"
# kcClientSecret: sinh bằng cơ chế chính thức của Keycloak (KHÔNG tự bịa) — Keycloak Admin Console
#   > realm aspire-react > Clients > backend-service > Credentials > Regenerate, hoặc qua Admin API:
#   POST /admin/realms/aspire-react/clients/{clientId}/client-secret
dotnet user-secrets set "Parameters:kcClientSecret" "<secret-vừa-sinh>"
echo "<app-admin-password-của-bạn>" > ../../.mirats-test-admin-password   # gitignored; code đọc có .Trim() nên newline cuối vô hại
```
Lưu ý:
- `kcBootstrapAdminPassword` chỉ seed Keycloak master admin **lần đầu khi volume `keycloak-data` rỗng**; nếu volume cũ còn, đổi nó không đổi password đang chạy — reset qua Keycloak Admin API (`PUT /admin/realms/master/users/{id}/reset-password`).
- `kcClientSecret` được AppHost inject vào Server (`Keycloak__ClientSecret`) và vào container Keycloak (`KEYCLOAK_BACKEND_CLIENT_SECRET`) — bản import realm mới sẽ resolve placeholder `${KEYCLOAK_BACKEND_CLIENT_SECRET}`; với volume cũ, secret active phải rotate qua Admin API (đã làm 2026-08-29 [SECRET-ROTATE]).
- Postgres password phải giữ nguyên giá trị qua các lần restart (volume `postgres-data` gắn với password lúc tạo).

Prereqs: .NET 10 SDK, Node.js 20+, Docker Desktop (containers for Postgres/Redis/Keycloak).

### Backend build/test
```bash
dotnet restore aspire-react.sln
dotnet build aspire-react.sln --configuration Release --no-restore
dotnet test aspire-react.sln --configuration Release --no-build
dotnet format aspire-react.sln --verify-no-changes --no-restore
```
Run a single test class/method (xUnit, from `aspire-react/`):
```bash
dotnet test aspire-react.Tests --filter "FullyQualifiedName~AssetTests"
dotnet test aspire-react.Tests --filter "FullyQualifiedName~AssetTests.Checkout_Sets_AssignedTo"
```
Tests use EF Core **InMemory** provider with handlers/services invoked directly (no HTTP layer) — see `aspire-react.Tests/TestHelpers.cs` for the fixture pattern. ⚠️ InMemory does NOT enforce real Postgres constraints (DateTime Kind, `FOR UPDATE` locks, raw SQL, unique indexes) — `dotnet test` PASS is not sufficient; verify DateTime/transaction/raw-SQL/constraint changes against the real API on the Aspire stack.

### EF Core migrations (schema changes)
```bash
dotnet ef migrations add <DescriptiveName> --project aspire-react.Server
dotnet ef database update --project aspire-react.Server
```
Review the generated migration file before running `database update`. Never edit applied migrations after the fact; never write raw SQL self-heal blocks in `Program.cs` (retired convention, see §3.9/§5 of the workflow doc).

### Frontend (from `aspire-react/frontend/`)
```bash
npm run dev        # vite dev server
npm run build       # tsc -b && vite build
npm run lint         # eslint .
npm run preview
```
No test runner is configured for the frontend; verification is manual (real UI interaction + screenshots), per workflow doc §1.3/§1.5.

### Known error-class sweep (run before commits/releases)
```bash
pwsh -File scripts/audit-sweeps.ps1
```
Checks: claim misuse (sub/preferred_username without local_user_id), frontend enum-vs-number comparisons, ActionLog missing companyId — scans BOTH `LogAction(` (needs `companyId:`) and `_context.ActionLogs.Add(new ActionLog {...})` (needs `CompanyId =`), plus tables missing `scroll=` (only for pages still on Table/ProTable). Exit 0 = clean, 1 = violations (prints file:line).

## Architecture

Backend follows Clean Architecture inside `aspire-react/aspire-react.Server/`:

- **`Domain/`** — Entities (`Domain/Entities/`), Enums (`Domain/Enums/`), interfaces (`ICurrentUserService`, `ICompanyScopeService`-style contracts, `IActionLogService`, `IApplicationDbContext`, `IAuditable`, `ICompanyable`, `IKeycloakService`). No framework dependencies.
- **`Application/`** — Commands/queries/handlers/DTOs/validators, organized per feature (`Accessories/`, `Assets/`, `Users/`, `Common/`). Uses MediatR (CQRS) + FluentValidation. `ApplicationServiceCollectionExtensions.cs` = `AddApplicationServices` (MediatR + FluentValidation + `ValidationBehavior` pipeline).
- **`Infrastructure/`** — `Persistence/` (EF Core `AppDbContext`, `AppDbContextFactory`, `PermissionMigration`, `StartupDataSeeder`, `PersistenceServiceCollectionExtensions.AddPersistence`), `Authorization/` (`PermissionCatalog` — single source of truth for policy keys, `PermissionHandler`, `PermissionLockoutGuard`, `PermissionRequirement`, `AuthorizationServiceCollectionExtensions.AddPermissionAuthorization`), `Authentication/` (`AuthenticationServiceCollectionExtensions.AddKeycloakAuthentication` — JWT bearer + JIT hookup), `Caching/` (`CachingServiceCollectionExtensions.AddRedisCaching` — Redis output-cache store + `ReferenceDataCachePolicy` for reference-data endpoints), `Services/` (`ActionLogService`, `CompanyScopeService`, `ComponentAllocationService`, `ConsumableAllocationService`, `CurrentUserService`, `JitUserProvisioningService`, `KeycloakService`, `RealmAccessHelper`). `InfrastructureServiceCollectionExtensions.cs` = `AddInfrastructureServices` (Keycloak admin API, JIT, app services, lockout guard).
- **`Web/Controllers/`** — one controller per resource (Assets, Accessories, Components, ComponentUnits, Consumables, Licenses, AssetMaintenances, Systems/SystemInfo, Users, Groups, Companies, Departments, CustomFields, ImportExport, Labels, Dashboard, Reports, Permissions, ActionLogs, Admin).
- **`Program.cs`** — thin composition root only: calls the `Add*` extension methods (Persistence/Application/Infrastructure/Authentication/Authorization), `StartupDataSeeder.Seed(app.Services)` (migrate + seed + legacy-superuser migration), and wires the HTTP pipeline (CORS, auth, controllers, health, file server). All DI registration and JIT logic live in the extension files.
- **`Migrations/`** — EF Core migrations; `InitialBaseline` (2026-08-14) is the schema baseline, applied without re-running against the live DB (see workflow doc §5 for how that was done — don't repeat that dance for normal schema changes, just `migrations add` + `database update`).

Request flow: Browser → Keycloak (OIDC login, JWT) → Vite dev server (5173) → ASP.NET Core API → `JwtBearerHandler` validates token → `PermissionHandler` checks policy → Controller → MediatR Command/Query → Handler → EF Core → PostgreSQL. Redis backs output caching.

Solution structure (`aspire-react/aspire-react.sln`):
- `aspire-react.AppHost` — Aspire orchestration (`AppHost.cs`): registers Postgres (+pgAdmin, data volume), Redis, Keycloak (realm import from `aspire-react-realm.json`, bootstrap admin creds), the Server project (with health check + service refs), and the Vite frontend (pinned to port 5173 for stable Keycloak redirect URIs). `server.PublishWithContainerFiles(webfrontend, "wwwroot")` bakes the built frontend into the server's published container.
- `aspire-react.ServiceDefaults` — shared OpenTelemetry/health-check/resilience wiring (`Extensions.cs`).
- `aspire-react.Server` — the Web API described above.
- `aspire-react.Tests` — xUnit + EF InMemory tests, one file per feature area.
- `frontend/` — React app, referenced by AppHost as a Vite app (not a .NET project, but has a stub `.esproj` for VS tooling).

Frontend (`aspire-react/frontend/src/`):
- `pages/` — one page component per route/feature (list + detail + form pages per resource).
- `components/<feature>/` — feature-scoped shared components; `components/common/` for cross-cutting ones (e.g. `CompanyTreeSelect.tsx` — the shared company dropdown supporting child-company selection, used by every form; don't write a flat company Select by hand).
- `services/` — one Axios-based service module per resource, plus `api-client.ts` (singleton Axios instance; request interceptor attaches Keycloak Bearer token + auto-refresh; response interceptor handles 401 and normalizes error shape to `{status, message, error_code}`) and `keycloak.ts`.
- `hooks/usePermission.ts` — `usePermission(code)` / `usePermissionMap()`, backed by a module-level cache of `GET /permissions/check` (fetched once per session; superuser always passes; fail-closed on error).
- `types/asset.ts` — canonical enum-normalization helpers; check here before writing a new enum comparison.

State management: no React Query/Zustand — service layer (Axios) + `useState`/props is the actual pattern in use. Don't introduce those libraries without confirming they're actually installed (`package.json`) first; `.clinerules/02-react-ant.md` calls this out explicitly as a past source of drift.

UI conventions (enforced project-wide, see workflow doc §3.8): **Main inventory list pages (Asset/Accessory/Component/Consumable/License) use the Card List pattern** (`<ProList>` grid + `<Card>`, reference `AccessoryListPage`/`LicenseListPage` as the model) — NOT `<ProTable>`. Admin/master-data pages and Detail pages still use `<ProTable>` from `@ant-design/pro-components` (fetch via `request`, `valueType`, `toolBarRender`, trailing options column with `Popconfirm`); wide tables there need `scroll={{ x: true }}` (only applies to pages still on Table/ProTable). Forms use `Modal` opened **in place via local state** (no navigate-then-open). Card icon color: only `LicenseListPage` derives it dynamically from `category.tagColor` (field is `tagColor`, not `color`; fallback when absent); Accessory/Component/Consumable use fixed per-resource colors; Maintenance colors by STATUS (in_progress/completed/closed), not category. Loading uses `Spin`/`Skeleton`, empty state uses `<Empty />`, status uses semantic `Tag`/`Badge`. Company column/filter is superuser-only across all list pages. Ant Design v6 breaking changes to remember: `destroyOnClose` → `destroyOnHidden`, `popupRender` for custom popup content, `Drawer width` → `size`.

## Repo-specific docs worth knowing about

- [docs/DEVELOPMENT_WORKFLOW.md](docs/DEVELOPMENT_WORKFLOW.md) — read this first (see above).
- [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) — architecture diagrams and permission resolution chain.
- [docs/API.md](docs/API.md) — endpoint reference.
- [docs/DEPLOYMENT.md](docs/DEPLOYMENT.md) — deployment guide.
- `docs/Handoff *.md` / `docs/HANDOFF_LATEST.md` / `PHASE*_COMPLETION_REPORT.md` — historical session handoff notes per module/subtask; useful for "why is this built this way" context but may be stale (code wins on conflict).
- `docs/sql/` — historical migration SQL scripts (UP/DOWN), kept as documentation only; not auto-run.

## CI (`.github/workflows/ci.yml`)

Three jobs: backend build+test+format-check (`dotnet build`/`test`/`format --verify-no-changes`, working dir `aspire-react`), frontend lint+build (`npm ci && npm run lint && npm run build`, working dir `aspire-react/frontend`), and a Docker image build for the server (depends on both).
