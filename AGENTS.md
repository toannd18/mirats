# AGENTS.md

AspireReact: Snipe-IT-style multi-tenant IT asset management. .NET 9/10 + React 19 + Ant Design 6, orchestrated by .NET Aspire 13.4 (Postgres, Redis, Keycloak).

## Mandatory reads first
- **Read [docs/DEVELOPMENT_WORKFLOW.md](docs/DEVELOPMENT_WORKFLOW.md) in full before writing/editing any code.** It is the authoritative, continuously-updated convention source (audit-first, ActionLog, company-scoping, etc.). Code is the source of truth; docs go stale — grep before assuming.
- [CLAUDE.md](CLAUDE.md) is a fuller copy of the same conventions with commands/architecture.
- Note: `.clinerules/` (01-dotnet.md, 02-react-ant.md, 03-database.md) exists but is a summary — defer to `docs/DEVELOPMENT_WORKFLOW.md` on conflict.

## Critical paths
- **Repo root is NOT a git repo** and holds only docs/screenshots/scratch. The solution lives under `aspire-react/` (aspire-react.sln). Treat git ops as out of scope.
- Backend/solution commands run from `aspire-react/`; frontend commands run from `aspire-react/frontend/`.

## Run / verify
```bash
# Full stack (Aspire orchestrates Postgres, Redis, Keycloak, API, frontend)
cd aspire-react/aspire-react.AppHost && dotnet run
# Frontend http://localhost:5173 · API http://localhost:5428 · Keycloak admin https://localhost:8080/admin
# Login: admin / Admin123!
```
```bash
cd aspire-react
dotnet restore aspire-react.sln
dotnet build aspire-react.sln --configuration Release --no-restore
dotnet test aspire-react.sln --configuration Release --no-build   # xUnit + EF Core InMemory, no HTTP layer
dotnet test aspire-react.Tests --filter "FullyQualifiedName~AssetTests.Checkout_Sets_AssignedTo"  # single test
dotnet format aspire-react.sln --verify-no-changes --no-restore
cd frontend
npm run dev / build / lint   # no frontend test runner — verify manually (see workflow §1.3)
```
```bash
# Known error-class sweep (run before commits/releases) — script is at REPO ROOT, not aspire-react/
pwsh -File scripts/audit-sweeps.ps1
```

## Non-negotiable conventions (see workflow doc for rationale)
- **User identity**: use claim `local_user_id` only (JIT-provisioned by `IJitUserProvisioningService` in `Infrastructure/Services/JitUserProvisioningService.cs`, invoked from the JWT `OnTokenValidated` handler in `Infrastructure/Authentication/AuthenticationServiceCollectionExtensions.cs` which stamps `local_user_id`; extracted from Program.cs in Task Q). Never use Keycloak `sub`/`preferred_username` as a user FK. Use `ICurrentUserService.GetLocalUserId()` / `IActionLogService.GetCurrentUserIdAsync()`.
- **ActionLog is mandatory** for every Create/Update/Delete/Checkout/Checkin/Confirm/Close/Reopen/Inspect/Dispose/Import, with `TargetType`, `TargetId`, `CompanyId` (+ `LogMeta.changes` for updates), persisted in the same transaction as the change.
- **Company-scoping is explicit on BOTH read AND write endpoints** (List/Detail/Create/Update/Delete — Task I/J/K/L2) — the global EF query filter in `AppDbContext` is a no-op. Use `ICompanyScopeService.GetCurrentUserCompanyIdAsync()`; don't rely on the filter.
- **Permissions**: backend `[Authorize(Policy = "<resource>.<action>")]` (key must exist in `PermissionCatalog`) on every write endpoint; frontend gates every sensitive button with `usePermission('<code>')`. Don't gate primarily on `isSuperUser()`.
- **Delete-guard by usage history**: records with checkout/allocation history must not be hard-deleted (block or soft-delete `DeletedAt`). Master-data deletes must check ALL referencing tables.
- **Whitelist fields on Update**; fields locked post-creation must reject with `FIELD_LOCKED`, not silently ignore.
- **Enums serialize as strings** (global `JsonStringEnumConverter`). NEVER compare frontend enums to numbers — use string comparison or `normalize*` helpers in `frontend/src/types/asset.ts`.
- **Schema changes = EF Core migrations only**: `dotnet ef migrations add <Name> --project aspire-react.Server` → review file → `dotnet ef database update --project aspire-react.Server`. No raw SQL self-heal blocks in `Program.cs`.
- **After every file edit, re-read it from disk** to confirm it persisted — don't trust tool success output.
- **UI changes need real verification** (clicks/console/network + screenshots at ~375/768/1440px for responsive). Passing `tsc --noEmit`/build is not sufficient evidence.

## Frontend Verification & Testing Rules

- ƯU TIÊN dùng playwright-cli (session-based) cho mọi verify cần đăng nhập, click chuỗi hành động, hoặc so sánh 2 user/session — đây là công cụ duy nhất trong dự án làm được việc này, KHÔNG bị cấm sử dụng.
- BẮT BUỘC mỗi lệnh playwright-cli phải có giới hạn thời gian chờ rõ ràng. Nếu 1 lệnh không phản hồi quá 60 giây, hủy và thử lại tối đa 1 lần — không để treo vô thời hạn.
- BẮT BUỘC đóng session (`playwright-cli -s=<tên> close`) ngay sau khi dùng xong, kể cả khi verify thất bại giữa chừng — không để session mở sót lại gây xung đột cho lệnh tiếp theo trong cùng phiên hoặc phiên sau.
- TRÁNH mở nhiều session song song trong 1 lệnh gộp (VD mở 3 session cùng lúc để verify 3 breakpoint hoặc 3 user) — verify TUẦN TỰ từng session, đóng session này trước khi mở session tiếp theo, giảm rủi ro treo do tranh chấp tài nguyên.
- Với các trường hợp CHỈ cần xem giao diện tĩnh (không cần đăng nhập, không cần tương tác) — dùng `npx playwright screenshot <url> <path>` cho nhanh, thay vì mở session đầy đủ không cần thiết.
- Nếu playwright-cli treo quá 60 giây dù đã áp dụng các quy tắc trên: dừng lại, kiểm tra process Chromium còn sót (kill nếu cần), báo cáo rõ bước nào không verify được bằng UI thật — không cố lặp lại vô hạn, không bỏ qua bước verify mà không báo cáo.
- KHÔNG dùng lệnh tương tác chặn terminal theo kiểu mở GUI rồi chờ người dùng thao tác tay (VD `playwright codegen` mở trình duyệt cho người dùng tự click) — mọi verify phải chạy tự động, có thể lặp lại được bởi agent.
- State management: plain Axios service layer + `useState`/props — no React Query/Zustand. Don't introduce them unless confirmed in `package.json`.
- **Patch semantics on Update** (Task F→M1→M2, 11 entities: Asset, Component, License, Consumable, User, Category, Manufacturer, Supplier, Location, AssetModel, Accessory): request DTO fields must be nullable; handler assigns ONLY when the field was actually sent (`is not null`/`HasValue`). An absent field must NEVER be treated as "changed" or overwritten with default/null — this previously wiped real Serial/AssetTag data. Whitelist fields; reject `FIELD_LOCKED` for locked fields.
- **DateTime Kind**: Postgres `timestamp without time zone` columns MUST be written with `DateTime.SpecifyKind(value, DateTimeKind.Unspecified)`; `with time zone` columns use `DateTime.UtcNow` (Kind=UTC). Wrong Kind → Npgsql throws 500. See `docs/HANDOFF_DATETIME_KIND_AUDIT.md`.
- **Concurrency**: checkout/allocate must lock rows `FOR UPDATE` inside a transaction (Asset `FromSqlRaw ... FOR UPDATE`, License seat/Accessory/Component/Consumable — Task O-FIX). No lock → stock overcommit / lost update (reproduced empirically).
- **EF InMemory does NOT enforce real Postgres constraints** (DateTime Kind, transactions/locks, raw SQL, unique indexes) — `dotnet test` PASS is NOT sufficient evidence. Any change touching DateTime, transaction/lock, raw SQL, or constraints MUST be verified by calling the real API on the Aspire stack.
- **Never reset/modify existing accounts to test** (`admin`/`ndkien`/`st1verify` are real users — changing passwords breaks their sessions, even with a note). When a non-superuser is needed for UI/API verification, create a NEW dedicated test user named clearly TEST/QA (e.g. `qa-<task>-<ts>`), then delete it after (Keycloak + DB). Mandatory since 2026-08-16 (st1verify reset incident in LAYOUT-2).
- **DI registration lives in layer extensions, not Program.cs** (Task Q): add services to the matching `Infrastructure/*/...ServiceCollectionExtensions.cs` or `Application/ApplicationServiceCollectionExtensions.cs`. Program.cs is a thin composition root (~93 lines) that only calls the `Add*` extension methods + `StartupDataSeeder.Seed(...)`.
- **MediatR `ValidationBehavior` is wired** (Task L): FluentValidation validators now actually run in the request pipeline (mapped to 400 by `ValidationExceptionHandler`). Writing a new validator takes effect immediately — no need to call validators manually.
- **Redis OutputCache** (Task P): 5 reference-data endpoints (categories/manufacturers/suppliers/permissions/companies) are cached via `ReferenceDataCachePolicy` (TTL 300s, Redis-backed via `AddRedisCaching`). When adding/editing an endpoint that writes these groups, invalidate the corresponding cache entry. Do NOT cache business/inventory data (checkout/Qty/stock) with output caching.

## Key references
- [docs/DEVELOPMENT_WORKFLOW.md](docs/DEVELOPMENT_WORKFLOW.md) — conventions + Appendix A (recurring bug classes: claim misuse, enum-as-number, missing ActionLog, missing company-scoping, missing usePermission, invalid PG `ADD CONSTRAINT IF NOT EXISTS`, cascade-delete of history).
- [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) · [docs/API.md](docs/API.md) · [docs/DEPLOYMENT.md](docs/DEPLOYMENT.md).
- Ant Design 6 breaking changes: `destroyOnClose` → `destroyOnHidden`, `popupRender` for custom popup content, `Drawer width` → `size`. **Main inventory list pages (Asset/Accessory/Component/Consumable/License) use the Card List pattern** (`<ProList>` grid + `<Card>`, reference `AccessoryListPage`/`LicenseListPage` as the model) — NOT `<ProTable>`. Admin/master-data pages and Detail pages still use `<ProTable>` from `@ant-design/pro-components`; wide tables there need `scroll={{ x: true }}` (only applies to pages still on Table/ProTable). Forms use `Modal`.
