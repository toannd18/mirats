# DEVELOPMENT WORKFLOW — Aspire React (Asset Management)

> Tài liệu chính thức về quy trình phát triển của dự án. **Mọi phiên làm việc mới (kể cả agent khác hoặc phiên mới của cùng agent) PHẢI đọc và tuân thủ tài liệu này trước khi viết code.**
>
> Tài liệu được tổng hợp từ thực tế phát triển các module: Asset, Component, Accessory, Consumable, License, Asset Maintenance, System Detail, Category/Company/Location, Role–Permission — KHÔNG phải lý thuyết. Mỗi nguyên tắc dưới đây gắn với một lớp lỗi đã từng xảy ra thật.
>
> Cập nhật lần đầu: 2026-08-13 (sau đợt audit toàn diện).
> Cập nhật ST9: 2026-08-14 (triển khai `scripts/audit-sweeps.ps1` + mở rộng test Asset/Accessory/User-CRUD-ActionLog/Company-Scope).
> Cập nhật ST10: 2026-08-14 (khắc phục đăng nhập admin/Admin123!: reset password realm `aspire-react` qua Admin API + IsSuperUser local + JIT stamp từ realm role).
> Cập nhật 2026-08-16 (Task Q/R/P): JIT tách sang `IJitUserProvisioningService`; Program.cs → composition root (DI trong extension); seed/migration → `StartupDataSeeder`; Redis output-cache reference-data; bổ sung Patch-semantics/DateTime Kind/Concurrency/EF-InMemory/ValidationBehavior.

---

## 1. Nguyên tắc cốt lõi (bắt buộc)

### 1.1. Luôn audit code thực tế TRƯỚC khi code
- Không giả định tính năng "chưa tồn tại" hay "đã tồn tại" dựa theo tài liệu cũ, trí nhớ, hay báo cáo phiên trước.
- Grep/đọc code thật, báo cáo kết quả kiểm tra ("hiện trạng") trước khi đề xuất giải pháp.
- Tài liệu (kể cả file này) có thể lỗi thời — **code là nguồn sự thật duy nhất**.

### 1.2. Chia nhỏ task lớn thành subtask có mốc dừng rõ ràng
- Theo mẫu A→E đã dùng ở module License / Role-Permission: mỗi subtask có phạm vi hẹp, kết quả kiểm chứng được, và **dừng lại chờ người dùng duyệt** trước khi sang subtask tiếp theo.
- Không gộp tiến độ của các hạng mục rủi ro khác nhau (ví dụ: sửa lỗi bảo mật và chuyển đổi schema DB là 2 luồng phê duyệt riêng).

### 1.3. Mọi thay đổi UI phải có ảnh chụp đúng màn hình liên quan
- Không suy luận từ ảnh của màn hình khác; không chỉ mô tả bằng lời hoặc DOM snapshot.
- Nếu bảng/nội dung rộng hơn viewport: chủ động dùng viewport lớn hơn hoặc screenshot đúng element để phần cần chứng minh nằm trong khung hình.
- Thay đổi responsive: chụp đủ 3 mốc ~375px / ~768px / ~1440px.

### 1.4. Mọi sửa file phải được `view` lại sau khi sửa
- Đặc biệt với sửa multi-line bằng script tự động: đọc lại vùng đã sửa để xác nhận nội dung đổi thật.
- **Phân biệt 2 việc KHÁC NHAU (quan trọng, đã có lỗi thực tế bị chính 2 lần):**
  - **(a) Output của lệnh sửa file** (diff/patch output) — cái mà công cụ "báo thành công".
  - **(b) Đọc LẠI TỪ ĐĨA** (read file/`Get-Content`/grep) — nội dung thật đã ghi.
  - Chỉ tin **(b)**. NOT dùng output của chính lệnh sửa (hay lệnh cùng pipeline) làm bằng chứng đã lưu thành công vì **đôi khi công cụ báo success nhưng không ghi đúng xuống đĩa** (BVV đã xảy ra với F10: diff hiển thị có thêm `SaveChanges` nhưng file thật không có → log không persist; và lịch sử dự án từng gặp với UserListPage thiếu nút Chi tiết). Phải **mở lại file bằng thao tác đọc độc lập** để xác nhận.
- Không tin vào exit code / kết quả trả về của script hoặc lệnh sửa.

### 1.5. Test pass KHÔNG phải bằng chứng đủ cho UI hoạt động đúng
- Thay đổi frontend cần xác minh thêm bằng thao tác thật (click thật, xem console/network thật).
- Test backend (xUnit) và hành vi UI là 2 lớp xác minh độc lập.

### 1.6. Sweep toàn bộ codebase khi tìm thấy 1 lỗi thuộc "lớp lỗi" có thể lặp lại
- Khi phát hiện 1 bug, giả định ngay rằng các module khác cũng mắc bug cùng lớp, và quét hết trong CÙNG MỘT lần sửa (grep pattern liên quan trên toàn solution).
- Danh sách các lớp lỗi đã biết: xem **Phụ lục A**. Không vá từng cái khi tình cờ gặp qua nhiều lần riêng biệt.

### 1.7. Ghi chú thẳng thắn khi báo cáo trước đó sai
- Không âm thầm sửa rồi coi như chưa từng xảy ra. Ghi rõ trong báo cáo/changelog: sai ở đâu, vì sao, đã sửa thế nào — để rút kinh nghiệm.

### 1.8. Tính năng nhạy cảm (quyền / dữ liệu lịch sử): nghĩ trước về trường hợp biên
- Self-lockout: người quản trị cuối cùng không thể tự gỡ quyền quản trị của chính mình (đã có `PermissionLockoutGuard`).
- 2 người cùng mất quyền cùng lúc: guard phải tính "người cuối cùng còn giữ quyền" trên toàn hệ thống, không chỉ trên user hiện tại.
- Migrate dữ liệu không được thu hẹp quyền hiện có (chỉ THÊM membership — xem `PermissionMigration`).
- Audit trail: bản ghi đã phát sinh giao dịch thì không được hard-delete làm mất lịch sử (xem mục 3.5).

---

## 2. Quy trình chuẩn cho một task

```
(1) Audit hiện trạng  →  (2) Báo cáo + kế hoạch chia subtask  →  (3) Chờ duyệt
(4) Thực thi từng subtask (code + test + verify)  →  (5) Báo cáo kèm bằng chứng
(6) Chờ duyệt  →  (7) Sweep lớp lỗi liên quan trên toàn project  →  (8) Cập nhật docs
```

- Bước (1): đọc code thật, liệt kê những gì đã có / chưa có / đang sai, kèm đường dẫn file + số dòng làm bằng chứng.
- Bước (5): bằng chứng tối thiểu = kết quả build/test + ảnh chụp UI (nếu có thay đổi UI) + bảng kiểm checklist mục 4.
- Bước (7): bắt buộc với các lớp lỗi ở Phụ lục A.

---

## 3. Tiêu chuẩn kỹ thuật cho module mới / sửa module cũ

### 3.1. Định danh user hiện tại (claim)
- **LUÔN ưu tiên claim `local_user_id`** (do JIT provisioning gắn qua `IJitUserProvisioningService` trong `Infrastructure/Services/JitUserProvisioningService.cs`, được gọi từ `OnTokenValidated` trong `Infrastructure/Authentication/AuthenticationServiceCollectionExtensions.cs` — tách khỏi `Program.cs` từ Task Q). Keycloak `sub` ≠ id user local; dùng `sub` làm FK sẽ gây FK violation hoặc ghi sai người thực hiện.
- Fallback (chỉ cho luồng legacy): `preferred_username` → tra bảng `Users`. KHÔNG parse `sub` thành user id.
- Helper chuẩn: `ICurrentUserService.GetLocalUserId()` hoặc `IActionLogService.GetCurrentUserIdAsync()`. Không tự viết lại logic đọc claim trong controller mới.

### 3.2. ActionLog (audit trail)
- Mọi hành động Create / Update / Delete / Checkout / Checkin / Confirm / Close / Reopen / Inspect / Dispose / Import **phải ghi ActionLog**.
- Ghi qua `IActionLogService.LogAction(...)` (hoặc `_context.ActionLogs.Add` nếu cùng transaction) với đầy đủ:
  - `TargetType` + `TargetId` (cho checkout/checkin — phải đúng loại target thật, không map sai),
  - `CompanyId` của bản ghi bị tác động (để lọc lịch sử theo công ty),
  - `LogMeta` dạng `{ changes: { field: { old, new } } }` cho Update.
- Log phải được persist cùng transaction với thay đổi dữ liệu (rollback cùng nhau).
- KHÔNG hard-delete ActionLog (soft-delete qua `DeletedAt`).

### 3.2a. ActionLog typed-safe — `ActionLogEntry` builder (Task S2a/S2b, pattern toàn hệ thống)
- **DÙNG `ActionLogEntry` (object initializer + `required` properties) thay cho `_context.ActionLogs.Add(new ActionLog{...})`** khi viết log ở Controller. Object initializer tự do cho phép BỎ SÓT field bắt buộc mà compiler không báo (đã gây 3 lỗi thật: thiếu CompanyId Task N, sai TargetType ST4/E, TargetId=null Task N).
- `ActionLogEntry` (`Domain/Entities/ActionLogEntry.cs`) khai báo **`required`** (C# 11) cho 5 field LUÔN có ở mọi log: `ItemType`, `ItemId`, `ActionType`, `CreatedBy`, `CompanyId` → compiler TỪ CHỐI compile nếu thiếu. Field đặc thù action (TargetType/TargetId/TargetSystemInfoId/Name/LogMeta/Note/ActionDate) để optional.
- Gọi: `_actionLogService.Log(new ActionLogEntry { ItemType = ..., ItemId = ..., ActionType = ..., CreatedBy = GetCurrentUserId(), CompanyId = ..., Note = ... })` (gọi `Log(entry)` thay vì `ActionLogs.Add`). `Log` là wrapper THIN trong `ActionLogService` — chỉ `_context.ActionLogs.Add(entry.Build())`, KHÔNG enrichment (khác `LogAction`), giữ transaction của caller.
- **`CompanyId` là `required Guid?` và KHÔNG reject `Guid.Empty`** — floater Maintenance legitimately có `CompanyId == Guid.Empty` (server set `Asset.CompanyId ?? Guid.Empty`); `required` chỉ ép "phải truyền tường minh", không cấm sentinel floater hợp lệ.
- **`ActionDate` optional** — chỉ dùng khi log cần ghi đè timestamp (Asset Audit/Accept/Decline); khi null `Build()` để entity default UtcNow.
- **Sweep 3 (audit-sweeps.ps1) đã hiểu Pattern 3:** `_actionLogService.Log(new ActionLogEntry{...})` được coi là AN TOÀN (compiler-enforced), không quét sâu; chỉ flag `Log(new ActionLog{...})` (bỏ qua builder, mất lớp bảo vệ compile).
- **Đã áp dụng TOÀN BỘ vị trí free-form (57 vị trí):** License/Maintenance (S2a) + Admin/Component/ComponentAllocationService/Company/Department/SystemInfo/CustomField/Asset commands (S2b). Grep `_context.ActionLogs.Add(new ActionLog` → chỉ còn trong `ActionLogService.cs` (nội bộ service, hợp lệ).
- ⚠️ **Phân biệt 2 pattern (quan trọng):** các vị trí `_actionLogService.LogAction(...)` (Consumable/Group/User/ImportExport/Accessory/1 số Asset commands/ConsumableAllocation) dùng helper ENRICHED (set RemoteIp/UserAgent/ActionSource/LocationName/SystemInfo-name) — **GIỮ NGUYÊN**, KHÔNG chuyển sang `Log(entry)` thin (sẽ drop enrichment = đổi hành vi). Khi viết log mới, nếu cần enrichment dùng `LogAction`, nếu là log CRUD/checkout thường dùng `Log(new ActionLogEntry{...})`.

### 3.3. Company-scoping (multi-tenant)
- **Lưu ý kiến trúc hiện tại**: global query filter trong `AppDbContext` đang là NO-OP vì `GetUserCompanyIdsAsync()` là placeholder trả `[]`. Vì vậy scoping phải làm **tường minh từng endpoint** theo pattern `ICompanyScopeService.GetCurrentUserCompanyIdAsync()`:
  - Superuser → `null` → không giới hạn.
  - User thường → chỉ thấy bản ghi có `CompanyId == null` (floater) hoặc `== company của mình`.
  - Ngoài scope: trả `404` (ẩn sự tồn tại — dùng cho list/detail/system) hoặc `403` (bản ghi đơn lẻ đã scope — dùng cho maintenance). KHÔNG trộn lẫn 2 quy ước.
- **Áp cho CẢ read VÀ write**: List/Detail/Create/Update/Delete đều phải scope (Task I/J/K/L2) — không chỉ List/Detail.
  - Read out-of-scope → `404` (ẩn tồn tại). Create out-of-scope (CompanyId ≠ company user) → `400 COMPANY_MISMATCH`. Update/Delete → `404` (ẩn) kèm guard lockout nếu cần.
- Endpoint write (checkout/checkin/create con) phải validate target cùng công ty: `COMPANY_MISMATCH`.
- Danh sách endpoint chuẩn đã làm đúng (để tham chiếu): `AssetMaintenancesController`, `LicensesController`, `SystemsController`.
- ⚠️ **`GET /companies` đã được company-scoped (Task V)**: Superuser thấy toàn bộ cây; user thường CHỈ thấy subtree công ty mình. Cache endpoint này theo scope (`CompanyScopeCachePolicy`), KHÔNG global-shared — xem mục 3.15.

### 3.4. Phân quyền
- **Backend (bắt buộc)**: mọi endpoint phải có `[Authorize(Policy = "<resource>.<action>")]` với key tồn tại trong `PermissionCatalog`. Không dùng `[Authorize]` trần cho endpoint ghi dữ liệu.
- **Frontend (bắt buộc)**: mọi nút hành động nhạy cảm (Xóa / Sửa / Cấp phát / Thu hồi / Đóng / Mở lại / Kiểm tra / Duyệt...) phải gate bằng `usePermission('<permission-code>')`. Không dùng `isSuperUser()` của `keycloak.ts` làm gate chính (chỉ dùng cho logic đặc thù "chỉ Superuser" như Reopen).
- Thêm permission mới: sửa duy nhất `PermissionCatalog.cs` (policy tự đăng ký + frontend lấy catalog qua `GET /permissions`).
- **Chống self-lockout — `PermissionLockoutGuard`** (Task J) áp cho: `GroupsController.DeleteGroup`, `UsersController.UpdateUser`, `UsersController.DeleteUser` (guard `WouldDeleteGroupLockoutAsync` / `WouldDemoteSuperUserLockoutAsync` / `WouldDeactivateUserLockoutAsync` — chặn khi thao tác khiến toàn hệ thống không còn ai giữ khả năng quản lý phân quyền; trả `400 SELF_LOCKOUT`).
- **`UsersController.UpdateUser` yêu cầu policy `admin`** (KHÔNG phải `users.edit`) — đổi ở Task J để chặn privilege escalation (holder `users.edit` từng tự bật `IsSuperUser=true`). Đồng bộ frontend `UserListPage` dùng `usePermission('admin')` cho nút sửa user.

### 3.5. Delete-guard theo lịch sử sử dụng
- Bản ghi đã từng phát sinh giao dịch KHÔNG được hard-delete:
  - Có lịch sử checkout/allocation → chặn (`*_HAS_ALLOCATION_HISTORY` / `*_IN_USE`) hoặc soft-delete (`DeletedAt`).
  - Chưa từng phát sinh → được xóa (kèm ActionLog Delete ghi TRƯỚC khi xóa).
- Master data (Category/Manufacturer/Supplier/Location/Company/Department/Model): trước khi xóa phải quét TẤT CẢ bảng tham chiếu (kể cả License, Asset, Model, User — không chỉ module hiện tại), vì FK `SET NULL` sẽ âm thầm xóa tham chiếu lịch sử.

### 3.6. Whitelist field khi Update + field khóa sau khi tạo
- Update endpoint chỉ áp dụng whitelist field; field ngoài whitelist bị **bỏ qua có chủ đích** (Qty) hoặc **từ chối rõ ràng** (`FIELD_LOCKED`) nếu client gửi giá trị KHÁC giá trị hiện tại.
- **PATCH semantics bắt buộc (chuỗi Task F→M1→M2, 11 entity: Asset, Component, License, Consumable, User, Category, Manufacturer, Supplier, Location, AssetModel, Accessory):**
  - DTO request cho field optional phải **nullable** (`string?`, `Guid?`, `bool?`, `decimal?`, `DateTime?`).
  - Handler CHỈ gán khi field **thực sự được gửi** (`request.X is not null` / `HasValue`). Field ABSENT (thiếu trong payload) **KHÔNG được** coi là "đã đổi" hay ghi đè bằng default/null.
  - Đã từng gây mất dữ liệu THẬT: full-replace ghi đè `Serial`/`AssetTag`/`Qty` thành null/0 khi client chỉ gửi payload một phần.
  - Field khóa sau tạo: so sánh patch-aware (`request.X.HasValue && khác hiện tại → chặn`), không chặn nhầm khi field absent.
- Các field khóa chuẩn đã có: Component (`CategoryId`, `CompanyId`, `TrackingType`), License (`CategoryId`, `CompanyId`), Maintenance (`AssetId`, `CompanyId`, `StartDate`, toàn bộ `Snapshot*`, và mọi field sau khi `IsClosed`), Consumable (chặn Update/Delete khi `Status == Confirmed`, + `CompanyId` lock sau cấp phát).
- Frontend phải hiển thị field khóa ở dạng disabled/locked, không cho bấm.

### 3.7. Enum: backend serialize STRING
- `Program.cs` đăng ký `JsonStringEnumConverter` toàn cục → API trả enum dạng **string**.
- Frontend KHÔNG so sánh enum với số (`status === 2`). Dùng string hoặc helper `normalize*` chấp nhận cả 2 dạng (mẫu: `frontend/src/types/asset.ts`).
- Ngoại lệ có chủ đích: `PermissionValue` trong API groups/permissions được cast `(int)` cố ý — không đổi.

### 3.8. UI/UX
- Form dùng **Modal** (không dùng trang form full-page). Modal mở **TẠI CHỖ bằng state cục bộ** — KHÔNG navigate sang trang/list khác rồi mới mở modal (bug đã fix ở Task A; `ConsumableFormPage` đã bị xóa → thay bằng `ConsumableFormModal`).
- **List page chính (inventory): dùng Card List** (`<ProList>` grid + `<Card>`), KHÔNG dùng `<ProTable>`. Đã chuyển: Component, Accessory, Consumable, License, Asset. **Mẫu chuẩn: `AccessoryListPage` / `LicenseListPage`** (buildParams/fetchPage dùng chung, company filter + company row chỉ render khi superuser).
- **Admin/master-data + Detail pages vẫn dùng `<ProTable>`** từ `@ant-design/pro-components` (fetch qua `request`, `valueType`, `toolBarRender`, cột option cuối + Popconfirm). `scroll={{ x: true }}` hoặc `responsive` chỉ áp dụng cho các trang còn dùng Table/ProTable — KHÔNG cần trên Card List.
- Dropdown chọn công ty: dùng chung **`CompanyTreeSelect`** (`components/common/CompanyTreeSelect.tsx`) — hỗ trợ chọn cả công ty con. Không tự viết Select phẳng riêng.
- Màu icon/badge trên Card List: **chỉ `LicenseListPage` lấy ĐỘNG từ `category.tagColor`** (hex từ backend, có fallback) — lưu ý tên field là `tagColor`, KHÔNG phải `color`. Accessory/Component/Consumable dùng màu cố định theo resource (hardcode). **Maintenance màu theo TRẠNG THÁI** (in_progress/completed/closed → processing/success/default), không theo category.
- Modal phải responsive (mobile: width theo % viewport — mẫu `UserFormModal` dùng `Grid.useBreakpoint()`).
- Trạng thái: `Spin/Skeleton` khi loading, `<Empty />` khi không có dữ liệu, `Tag/Badge` màu semantic cho status.
- Cột/bộ lọc "Công ty": chỉ hiển thị cho Superuser (gate bằng quyền), ẩn với user thường — thống nhất mọi trang list.

### 3.9. Schema database (quy ước HIỆN HÀNH — EF Core Migrations, đã hoàn tất ST6d)
- App tạo/đồng bộ schema bằng `db.Database.Migrate()` trong **`StartupDataSeeder.Seed(app.Services)`** (chạy mỗi lần khởi động; Task Q đã tách khỏi `Program.cs`, Task R xóa `DbInitializer`). **Không còn `EnsureCreated()` và không còn khối raw SQL self-heal v2–v7** (đã gỡ ở ST6d Bước 3).
- Baseline: migration `20260814135409_InitialBaseline` đã **đánh dấu áp dụng** trên DB thật (chèn bản ghi vào `__EFMigrationsHistory`; Database Schema đã thay đổi).
- Mọi thay đổi schema từ đây: `dotnet ef migrations add <Tên>` → review file migration → `dotnet ef database update`. **KHÔNG viết tay SQL self-heal nữa.**
- Script `docs/sql/*.sql` giữ làm tài liệu lịch sử (không còn tự chạy tự động).

### 3.10. Test
- Module mới có logic nghiệp vụ (guard, allocation, company check, lockout...) phải có test xUnit trong `aspire-react.Tests` (đang dùng EF InMemory + handler/service trực tiếp).
- Không để test bị skip/comment-out không lý do.
- ⚠️ **EF InMemory KHÔNG enforce ràng buộc Postgres thật** (DateTime Kind, transaction/lock `FOR UPDATE`, raw SQL, unique index, FK constraint) — `dotnet test` PASS KHÔNG phải bằng chứng đủ. Mọi thay đổi đụng **DateTime Kind, transaction/lock, raw SQL, hoặc DB constraint PHẢI verify bằng gọi API THẬT trên Aspire stack**. Đây là lớp lỗi lặp lại nhiều lần nhất (DateTime Kind chặn Maintenance nhiều phiên; race condition chỉ lộ khi Postgres thật; validator chưa chạy) — xem Phụ lục A.
- ⚠️ **KHÔNG được reset password / thay đổi tài khoản có sẵn để test** (kể cả khi có ghi chú lại). `admin`/`ndkien`/`st1verify`... là account thật của người dùng/team — đổi mật khẩu sẽ phá phiên đăng nhập hiện có của họ. Khi cần 1 user thường (non-superuser) để verify, **PHẢI tạo user test MỚI riêng, đặt tên rõ TEST/QA** (VD `qa-<task>-<ts>`), dùng xong xóa sạch (Keycloak + DB). Quy tắc bắt buộc từ 2026-08-16 (sau sự cố reset `st1verify` trong LAYOUT-2).

### 3.11. DateTime Kind (Npgsql)
- Cột Postgres **`timestamp without time zone`** PHẢI ghi `DateTime.SpecifyKind(value, DateTimeKind.Unspecified)` (kể cả `UtcNow`).
- Cột **`with time zone`** dùng `DateTime.UtcNow` (Kind=Utc).
- Sai Kind → Npgsql throw `Cannot write DateTime with Kind=...`, HTTP **500**. Đã từng chặn toàn bộ tính năng Maintenance/License qua nhiều phiên mà không ai phát hiện (InMemory không bắt). Tham chiếu đầy đủ: `docs/HANDOFF_DATETIME_KIND_AUDIT.md`.

### 3.12. Concurrency / lock hàng (race condition)
- Mọi endpoint checkout/allocate phải khóa hàng **`FOR UPDATE` trong transaction**: Asset (`FromSqlRaw ... FOR UPDATE`), License seat, Accessory, Component, Consumable (Task O-FIX).
- Không lock → **overcommit tồn kho** (Qty/seat gán 2 lần) / **lost update** — đã tái hiện bằng thực nghiệm (4/5 và 5/5 lần race bị mất).
- Test `ConcurrencyRaceAuditTests` (`Category=Concurrency`) chạy trên Postgres thật, KHÔNG chạy trong suite InMemory.

### 3.13. MediatR ValidationBehavior (Task L)
- `ValidationBehavior<,>` đã được wire trong pipeline (`AddApplicationServices` → `AddOpenBehavior`) — validator FluentValidation giờ **THỰC SỰ chạy trong request path** (trước đây đăng ký nhưng không có `IPipelineBehavior` nên không bao giờ chạy; duplicate AssetTag từng trả 500 thay vì 400).
- Lỗi validator → `ValidationExceptionHandler` map **400** với `errors` theo field. Viết validator mới → có hiệu lực NGAY, không cần gọi tay.

### 3.14. DI Extension Pattern (Task Q)
- Mọi đăng ký service PHẢI vào đúng extension theo layer: `Infrastructure/*/...ServiceCollectionExtensions.cs`, `Application/ApplicationServiceCollectionExtensions.cs`, `Infrastructure/Caching/CachingServiceCollectionExtensions.cs`. **KHÔNG thêm thẳng vào `Program.cs`**.
- `Program.cs` chỉ là composition root (~93 dòng): gọi `Add*` extension + `StartupDataSeeder.Seed(...)` + pipeline HTTP.
- Lưu ý `AddRedisCaching` dùng `Configure<OutputCacheOptions>` (KHÔNG gọi lại `AddOutputCache`, kẻo override Redis store bằng in-memory).

### 3.15. Redis OutputCache (Task P)
- 5 endpoint reference-data được cache (TTL 300s, Redis-backed): `GET /categories`, `/manufacturers`, `/suppliers`, `/permissions` (catalog) qua `ReferenceDataCachePolicy`; `GET /companies` qua `CompanyScopeCachePolicy` (Task V). **KHÔNG cache dữ liệu nghiệp vụ** (checkout/Qty/tồn kho).
- ⚠️ **`/companies` là company-scoped (Task V)** — cache key PHẢI phân biệt theo scope: `CompanyScopeCachePolicy` thêm `VaryByValues["company_scope"]` (Superuser/user không company → `all`; user có công ty X → `c:<X>`). KHÔNG dùng `ReferenceDataCachePolicy` cho `/companies` (sẽ chia sẻ 1 entry cho mọi user → rò rỉ chéo công ty). Mọi scope variant vẫn mang tag `ref:companies` nên `InvalidateCompaniesAsync()` evict TẤT CẢ scope cùng lúc.
- Khi thêm/sửa endpoint GHI các nhóm này (Category/Manufacturer/Supplier/Company/permission catalog) phải **invalidate cache tương ứng** (tag hoặc chờ TTL).
- An toàn nhờ `UseOutputCache()` đặt SAU `UseAuthorization()` (unauthorized → 403 trước khi tới cache) + custom policy bật cache cho authenticated request (default policy KHÔNG cache authenticated).

---

## 4. Checklist hoàn thành task (đính kèm CUỐI mỗi báo cáo)

```
## Checklist hoàn thành
- [ ] Đã audit hiện trạng trước khi code (ghi trong báo cáo)
- [ ] dotnet build 0 lỗi · dotnet test pass · tsc --noEmit 0 lỗi (nếu đụng frontend)
- [ ] Ảnh chụp UI đúng màn hình thay đổi (3 mốc nếu là responsive)
- [ ] Xác minh bằng thao tác thật (click thật / console / network) cho thay đổi frontend
- [ ] Claim: chỉ dùng local_user_id (không sub/username cho user id)
- [ ] ActionLog: đủ cho mọi hành động Create/Update/Delete/Checkout/Checkin...
      (TargetType, TargetId, CompanyId, LogMeta)
- [ ] Company-scoping: mọi endpoint List/Detail/Write đã kiểm tra company
      (hoặc ghi rõ lý do miễn)
- [ ] Permission: backend [Authorize(Policy=...)] + frontend usePermission(...)
      cho mọi nút nhạy cảm
- [ ] Enum: không so sánh số với enum từ API
- [ ] Whitelist/FIELD_LOCKED cho Update; delete-guard theo lịch sử
- [ ] Sweep lớp lỗi liên quan trên TOÀN project (liệt kê các chỗ đã quét)
- [ ] Test mới cho logic nghiệp vụ (nếu có)
- [ ] Đã xem lại (view) mọi file đã sửa
- [ ] Cập nhật docs liên quan
```

### Ghi chú ST6a — Delete-guard CustomField (2026-08-14)

- **Audit dứt điểm Depreciation/StatusLabel**: cả 2 chỉ có `GET` read-only (`GET /depreciations`, `GET /statuslabels`), **không có** endpoint Create/Update/Delete → **không có rủi ro mất dữ liệu** → không cần guard. Không nằm trong phạm vi ST6a.
- **CustomField** (`DELETE /custom-fields/{id}`): thêm guard chặn field đang được `CustomFieldFieldset` tham chiếu (`CustomFieldFieldsets.AnyAsync(fieldId)` → `400 CUSTOM_FIELD_IN_USE`) — FK `FieldId → CustomField` là `OnDelete(Cascade)` nên xóa sẽ cascade mất pivot rows field↔fieldset (bug class F7). Verify 2 chiều thật: không link → 200 + xóa + Delete log; có link → 400 + field/pivot nguyên + không log.
- Test: `CustomFieldDeleteGuardTests` (2 test). Sửa pre-existing test break: constructor `ConsumablesController`/`ComponentsController` đã thêm `ICompanyScopeService` từ ST1 nhưng `ConsumableTests`/`CategoryAndComponentTests` chưa cập nhật → bổ sung `new SuperUserScope()` (10 site).

### Ghi chú ST6b — Frontend permission wiring (2026-08-14)

- **Nguyên tắc 2 nhóm nút**: (A) nút cần permission cụ thể `resource.action` → `usePermission('<code>')` khớp policy backend; (B) nút "chỉ Superuser" → giữ `isSuperUser()` (backend check `IsSuperUser()` trong body, không có policy tương ứng).
- **Nhóm A đã gate (~25 file)**: Asset List/Detail, Consumable List/Detail, Accessory List/Detail, Component List/Detail, License List, User List, Group List (`admin` policy), SystemInfo List, 8 trang admin (Category/Supplier/Manufacturer/Location/Department/Company/AssetModel + reports export = `assets.view`), form pages submit (Asset/Accessory/Consumable), Maintenance Table (Đóng/Kiểm tra) + List (Tạo bảo trì) + Section (Sửa/Kiểm tra/Đóng/Thêm).
- **🐛 Bug sửa**: nút "Mở lại" (Reopen) trong `AssetMaintenanceSection` đang dùng `canDeleteMaintenance` (`assets.edit`) — **sai**, backend Reopen = `[Authorize]` + check `IsSuperUser()` trong body → **chuyển sang `superUser`** (nhóm B). `MaintenanceTable` đã đúng từ trước.
- **Giữ `isSuperUser()` hợp lệ**: Reopen bảo trì (2 chỗ), `LicenseFormModal` (chỉ superuser gán companyId khi tạo), `MaintenanceListPage` (superuser chọn công ty) — không chuyển nhầm.
- **Verify thật**: `GET /permissions/check` — admin (superuser) → true; `st1verify` (Admin group) → mọi key=1; `noperm` (không group) → permissions rỗng (fail-closed). `tsc --noEmit` 0 lỗi.

### Ghi chú ST6c — Responsive / ProTable (2026-08-14)

- **Chuyển `<Table>` → `<ProTable>` (10 list page)**: Supplier, Manufacturer, Department, Category, AssetModel, Company, Location, SystemInfo (8 admin) + ComponentListPage, LicenseListPage. Pattern: `request` prop (tự quản fetch/loading/pagination), `actionRef.reload()` sau save/delete, `toolBarRender` cho nút tạo, `search={false}`, `options={{reload,density,setting}}`, `scroll={{x:true}}`.
- **Giữ nguyên custom logic (không mất tính năng)**: Company/Location **tree data** (`onLoad` đồng bộ state cho TreeSelect + inherit companyId, `expandable defaultExpandAllRows`); SystemInfo **expandedRowRender = positions sub-table** (Table thường trong expand, hợp lệ); Component/License **filter server-side** (search/category/company/location + UNCATEGORIZED/UNCOMPANIED + expiring/lowSeats qua closure trong `request`); mọi cell render (Tag/link/Popconfirm/ExpiryCell) giữ nguyên.
- **🐛 Bug enum-string phát hiện khi verify UI**: `CategoryListPage` dùng `Record<number,string>` cho `categoryType` nhưng API trả **string** ("Asset"/"Consumable"/...)" → bảng hiển thị `#Asset` sai. Fix: label map 2-chiều (string + số), filter/Modal Select dùng value string. Xác nhận các file khác (AccessoryFormPage/AssetModel/Consumable) đã dùng string so sánh đúng — sweep không còn chỗ sai.
- **Không chuyển (bảng con/widget — không phải list page)**: DashboardPage (3 Table trong Card Statistic), ReportsPage (báo cáo), LicenseDetailModal/AssetMaintenanceSection/LicenseUsageTable/ComponentDetailPage (sub-table trong detail). `ModelListPage.tsx` = dead code (không được route).
- **Verify thật**: `tsc --noEmit` 0 lỗi; browser Playwright đăng nhập admin — SystemInfo/Location/Component/License/Category render đúng (column + data + toolbar + Modal mở được), console 0 error, responsive 375px bảng scroll-x hoạt động. Ảnh 3 mốc: `C:\Users\Public\st6c-{locations,components,licenses,categories}-{375,768,1440}.png` + `st6c-systeminfos-1440.png`.
- **🐛 Fix Layout responsive (App.tsx — dùng chung toàn app)**: Sider cũ dùng `<Sider collapsible>` không có breakpoint → luôn chiếm ~220px kể cả 375px (nội dung bảng gần như không đọc được). Fix: `Grid.useBreakpoint()` (`isMobile = screens.md === false`, ≤768px); mobile → **không render Sider** + hamburger (MenuOutlined) trong Header mở **Drawer** chứa cùng `siderMenu` (click menu tự đóng Drawer); desktop → Sider collapsible (icon-only khi collapsed, `trigger={null}` + `onCollapse`). Content padding co giãn theo breakpoint.
- **Verify Layout dùng chung**: 375px — Sider ẩn hẳn, hamburger "Mở menu" mở Drawer chứa toàn bộ menu (Dashboard/Vật tư/Bản quyền/Tài sản/...); 4 trang ST6c (Category/Component/License/Location) + Dashboard đều hiển thị bảng đầy đủ cột/data trên toàn màn hình; 768px → Sider hiện lại (md boundary); 1440px → Sider bình thường, không hamburger. Console 0 error/warning. Ảnh: `st6c-{categories,components,licenses,locations,dashboard}-375-fixed.png`.
- **Chuẩn AntD v6**: sửa `Drawer width` → `size` (deprecated), `Modal destroyOnClose` → `destroyOnHidden` (sweep 5 file admin ST6c).

---

## 5. Chuyển đổi sang EF Core Migrations (quy ước MỚI — thay thế mục 3.9)

> Trạng thái: **ĐÃ THỰC HIỆN — HOÀN TẤT ST6d ngày 2026-08-14**. Mục 3.9 (bên trên) là quy ước HIỆN HÀNH (EF Core Migrations). Tóm tắt đã làm: backup `docs/sql/backups/st6d_b3_prestep3_20260814.dump`; audit drift → model & DB về 0; xóa 8 migration stale; `dotnet ef migrations add InitialBaseline` (20260814135409); đánh dấu applied trên DB thật qua `__EFMigrationsHistory` (không chạy schema); verify trên DB clone + DB fresh (`database update` no-op trên clone, apply sạch 33 bảng trên fresh, đều OK); gỡ toàn bộ self-heal v2–v7 + chuyển `EnsureCreated()` → `Migrate()`; restart server health 200.

Lý do chuyển đổi: dự án đã lớn (nhiều module, `Program.cs` phình to qua nhiều khối self-heal v2–v7), raw SQL tự viết khó versioning/rollback.

**Quy trình bắt buộc (không bỏ bước, không gộp với task sửa lỗi khác):**
0. **Backup**: `pg_dump` DB thật (hoặc snapshot volume container Postgres) trước mọi thao tác. Chưa có backup đáng tin cậy → dừng, không làm tiếp.
1. **Xác nhận model khớp schema thật 100%**: audit drift (cột có trong DB nhưng thiếu trong entity và ngược lại); sửa hết drift trước khi baseline. Xóa thư mục `Migrations/` cũ (8 migration stale 2026-08-05→08, chưa từng được apply — EnsureCreated không dùng migrations).
2. `dotnet ef migrations add InitialBaseline` — migration phải phản ánh ĐÚNG schema hiện tại, không thêm/bớt. Review file migration sinh ra trước khi chạy.
3. Đánh dấu baseline "đã áp dụng" mà KHÔNG chạy nó trên DB thật (DB đã có sẵn cấu trúc): insert bản ghi tương ứng vào `__EFMigrationsHistory` (hoặc cơ chế tương đương chuẩn của EF).
4. **Xác minh trên bản sao DB** (clone từ backup, KHÔNG làm trên DB thật): `dotnet ef database update` phải no-op, không lỗi, không chạy lại baseline.
5. Gỡ toàn bộ khối self-heal trong `Program.cs` + chuyển `EnsureCreated()` → `Migrate()` (hoặc bỏ hẳn init schema khỏi Program.cs) sau khi bước 4 ổn định.
6. Từ đây: mọi thay đổi schema = `dotnet ef migrations add <Tên>` + review file migration + `database update`. Không viết tay SQL self-heal nữa. Script `docs/sql/*.sql` giữ làm tài liệu lịch sử.
7. Cập nhật tài liệu này (mục 3.9 + mục 5) sau khi hoàn tất.

**Rủi ro**: làm sai bước 3–4 có thể phá schema/dữ liệu thật. Luôn thử trên DB clone trước.

---

## Phụ lục A — Các lớp lỗi đã biết (phải sweep mỗi khi đụng liên quan)

| # | Lớp lỗi | Ví dụ đã xảy ra | Pattern sweep |
|---|---------|-----------------|--------------|
| 1 | Dùng claim `sub`/`username` thay `local_user_id` | License, Maintenance, PermissionHandler, ImportExport, CompanyScopeService, UsersController, ConsumablesController, ActionLogService | grep `FindFirst\|ClaimTypes\|"sub"\|preferred_username` trong `*.cs` |
| 2 | Enum string (API) bị frontend so sánh theo số | Consumable/Accessory (`status === 2`), `categoryType === 1/2/3` ở AssetModel/Accessory/Consumable | grep `=== [0-9]` trong `frontend/src` gần field enum |
| 3 | Thiếu ActionLog cho checkout/checkin/CRUD | Consumable (từng không ghi log), Asset AcceptDecline/Audit/BulkUpdate, User CRUD, master data | grep `LogAction\|ActionLogs.Add` theo từng controller/command |
| 4 | Thiếu company-scoping | Consumable checkout khác công ty không bị chặn; list/detail nhiều module chưa lọc | grep `GetCurrentUserCompanyIdAsync` — module nào không gọi là nghi ngờ |
| 5 | `usePermission` chưa wire cho nút nhạy cảm | Mới chỉ wire nút Xóa Maintenance | grep `usePermission` trong `frontend/src` |
| 6 | Cú pháp self-heal sai với Postgres | `ADD CONSTRAINT IF NOT EXISTS` (component_units) | grep `ADD CONSTRAINT IF NOT EXISTS` trong `Program.cs` |
| 7 | Xóa bản ghi đã có lịch sử (cascade mất dữ liệu) | Phát hiện qua audit 2026-08-13 (Consumable/Accessory/Asset delete cascade) | soát FK `OnDelete(Cascade)` + delete endpoint |
| 8 | **Full-replace Update ghi đè field absent (patch-wipe)** | M1/M2 — `Serial`/`AssetTag`/`Qty` bị set null/0 khi client chỉ gửi payload một phần | grep `= r.X` gán vô điều kiện trong Update handler; DTO phải nullable + gán khi `is not null` |
| 9 | **DateTime Kind sai → Npgsql 500** | Maintenance/License bị chặn nhiều phiên (Kind=Utc vào cột `without time zone`) | grep `DateTime.UtcNow` gán vào property cột `without time zone` (phải `SpecifyKind(Unspecified)`) |
| 10 | **Race condition: checkout/allocate không lock** | O-FIX — overcommit tồn kho / lost update (tái hiện 4/5, 5/5) | endpoint checkout/allocate phải `FOR UPDATE` trong transaction; test `Category=Concurrency` trên Postgres thật |
| 11 | **EF InMemory pass nhưng Postgres thật fail** | DateTime Kind, lock, unique index không được InMemory enforce | thay đổi đụng DateTime/transaction/raw SQL/constraint PHẢI verify API thật trên Aspire stack |
| 12 | **Validator đăng ký nhưng không chạy** | L — duplicate AssetTag trả 500 (không có pipeline behavior) | validator phải chạy qua `ValidationBehavior` (đã wire); lỗi → 400 |

---

## Phụ lục B — Tham chiếu nhanh trong codebase

| Khái niệm | Vị trí |
|---|---|
| JIT provisioning (local user từ token) | `Infrastructure/Services/JitUserProvisioningService.cs` (`IJitUserProvisioningService`) — gọi từ `Infrastructure/Authentication/AuthenticationServiceCollectionExtensions.cs` (OnTokenValidated) |
| Seed + migrate khởi động | `Infrastructure/Persistence/StartupDataSeeder.cs` (KHÔNG còn `DbInitializer.cs` — đã xóa Task R) |
| Redis output-cache (reference-data) | `Infrastructure/Caching/CachingServiceCollectionExtensions.cs` + `ReferenceDataCachePolicy.cs` |
| Permission catalog (single source of truth) | `Infrastructure/Authorization/PermissionCatalog.cs` |
| Permission handler (local_user_id → deny/grant/group/admin-wildcard) | `Infrastructure/Authorization/PermissionHandler.cs` |
| Chống self-lockout | `Infrastructure/Authorization/PermissionLockoutGuard.cs` |
| Company scope service (+ placeholder `GetUserCompanyIdsAsync`) | `Infrastructure/Services/CompanyScopeService.cs` |
| ActionLog service chuẩn | `Infrastructure/Services/ActionLogService.cs` |
| Allocation services (company guard + log) | `Infrastructure/Services/ComponentAllocationService.cs`, `ConsumableAllocationService.cs` |
| DI composition root (gọi extension) | `aspire-react.Server/Program.cs` (~93 dòng) |
| Hook quyền frontend | `frontend/src/hooks/usePermission.ts` |
| Helper chuẩn hóa enum | `frontend/src/types/asset.ts` |
| Company dropdown (tree, kể cả công ty con) | `frontend/src/components/common/CompanyTreeSelect.tsx` |
| Script SQL UP/DOWN lịch sử | `docs/sql/` |
| Test | `aspire-react.Tests/` (xUnit + EF InMemory) |

---

## Phụ lục C — Đề xuất cải tiến workflow (chờ phê duyệt)

1. **Checklist tự động**: checklist mục 4 nên được copy vào cuối mỗi báo cáo hoàn thành task (có thể lưu thành template `.clinerules/task-completion-checklist.md` để agent tự đính kèm).
2. **Script sweep lớp lỗi đã biết** (`scripts/audit-sweeps.ps1`): tự động hóa grep các pattern ở Phụ lục A + thống kê theo file, chạy định kỳ trước mỗi đợt release hoặc khi bắt đầu module mới — giảm phụ thuộc vào audit thủ công. **ĐÃ TRIỂN KHAI (ST9, 2026-08-14)**: `powershell -File scripts/audit-sweeps.ps1` — Sweep 1 (Claims: sub/preferred_username thiếu local_user_id), Sweep 2 (Enum so sánh số trên frontend), Sweep 3 (ActionLog thiếu companyId), Sweep 4 (Table thiếu scroll). Exit code 0 = sạch, 1 = có vi phạm (kèm vị trí). Kiểm chứng negative-test bằng probe file tạm (S2/S3 phát hiện đúng).
   - **Sweep 3 đã MỞ RỘNG (Task N, 2026-08-16)**: giờ quét CẢ 2 pattern — `LogAction(` (kiểm `companyId:`) VÀ `_context.ActionLogs.Add(new ActionLog {...})` (kiểm `CompanyId =`), exempt master-data không có cột CompanyId. Trước đây chỉ quét `LogAction(` nên bỏ sót ~50% chỗ ghi log (Department/Location/SystemInfo/SystemPosition từng thiếu CompanyId lọt qua nhiều lần "exit 0").