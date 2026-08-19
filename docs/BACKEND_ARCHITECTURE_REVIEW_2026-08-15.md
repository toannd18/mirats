# Backend Architecture Review — aspire-react.Server (2026-08-15)

> **Phạm vi:** Review kiến trúc backend .NET 10 Web API toàn diện theo 10 mục yêu cầu (company-scoping, patch semantics, DateTime Kind, ActionLog/TargetType, Permission/Lockout, Concurrency/raw SQL, CQRS/MediatR, Redis, Clean Architecture, DI Extension Pattern).
> **Phương pháp:** 6 agent đọc code song song, độc lập — mỗi agent tự đọc code thật (không chỉ tin docs/handoff cũ), trace từng write-site/read-site, đối chiếu với frontend call-site khi cần xác định "latent" hay "reachable".
> **KHÔNG có sửa code nào trong lượt này** — chỉ audit + báo cáo theo đúng yêu cầu.

---

## ⚠️ Giới hạn của đợt audit này — đọc trước khi hành động

Đây là **audit tĩnh (static code read)**, không phải verify bằng thao tác thật trên DB Postgres/API thật như quy trình chuẩn của dự án yêu cầu (`docs/DEVELOPMENT_WORKFLOW.md` mục 1.5) — lượt này không khởi động Aspire stack. Hai nhóm mục đã tự đối chiếu đủ chắc chắn qua đọc code (không cần verify thêm):

- **DateTime Kind (mục 3)**: xác nhận qua đọc trực tiếp migration (ground truth kiểu cột) + toàn bộ write-site — không phụ thuộc runtime behavior, nên kết luận "FIXED" đáng tin cậy.
- **Company-scoping / Permission (mục 1, 5)**: là câu hỏi "code có gọi hàm scoping hay không" — đọc code là đủ bằng chứng, không cần chạy thật.

Hai nhóm sau **cần verify thêm bằng thao tác thật** trước khi coi là kết luận cuối:
- **Concurrency (mục 6)**: các race condition mô tả dưới đây là suy luận đúng từ đọc SQL/transaction scope, nhưng "race thật sự xảy ra và gây hậu quả gì" nên được xác nhận bằng test tải đồng thời thật (2 request `Task.WhenAll` gọi API thật) — khuyến nghị làm trước khi ưu tiên fix.
- **ActionLog TargetId=null cho Component Return (Serial)**: nên gọi API checkin bằng `serialNo` thật (không kèm `assetId`) và xem DB thật để xác nhận log ghi `TargetId=null`.

---

## Phần I — Rủi ro & lỗi dữ liệu (mục 1, 2, 3, 4, 5, 6 + phần lỗi cụ thể phát hiện trong mục 7)

Xếp theo mức độ nghiêm trọng giảm dần, không theo file/module.

### 🔴 CAO — 13 vấn đề

**Nhóm A — Company-scoping bị bỏ sót ở endpoint GHI (khác hẳn endpoint ĐỌC đã được ST1 audit trước đây)**

1. **Asset Update/Delete không company-scoped** — `Application/Assets/Commands/UpdateAssetCommand.cs:45`, `DeleteAssetCommand.cs:30`. Handler fetch asset chỉ bằng ID, không so `CompanyId` với user hiện tại. Bất kỳ user có policy `assets.edit`/`assets.delete` (không cần thuộc công ty đó) đều sửa/xóa được asset của công ty khác qua API, dù `GetAsset` (đọc) đã scoped đúng. Fix: inject `ICompanyScopeService` vào handler, reject/404 khi mismatch (theo mẫu `LicensesController.IsLicenseVisible`).
2. **Accessory Update/Delete không company-scoped** — `Web/Controllers/AccessoriesController.cs:141`, `DeleteAccessoryCommand.cs:33`. Cùng lỗi.
3. **Consumable Update/Delete không company-scoped** — `Web/Controllers/ConsumablesController.cs:125,164`. Cùng lỗi.
4. **Component Update/Delete không company-scoped** — `Web/Controllers/ComponentsController.cs:228,268`. Cùng lỗi.
5. **ComponentUnit UpdateStatus/Delete không company-scoped** — `ComponentUnitsController.cs:33` (`SetUnitStatusAsync` tại `ComponentAllocationService.cs:294-309`) và `:46`. User có `components.edit` có thể đổi trạng thái (Damaged/Disposed) hoặc xóa serial unit của công ty khác, tự động gỡ khỏi asset đang gán.

**Nhóm B — Rò rỉ dữ liệu người dùng cross-company**

6. **UsersController.GetUsers/GetUser hoàn toàn không company-scoped** — `UsersController.cs:54,205`. `User` có `CompanyId` nhưng list/detail không lọc gì — bất kỳ ai có `users.view` thấy TOÀN BỘ user mọi công ty (email, chức danh, phòng ban, group membership).

**Nhóm C — Bypass PermissionLockoutGuard (self-lockout admin)**

7. **GroupsController.DeleteGroup không gọi lockout guard** — `GroupsController.cs:167-189` chỉ check `group.IsSystem`, không check guard. Kịch bản thật: group không phải system group nhưng là nguồn cấp `admin` duy nhất cho user cuối cùng → `DELETE /groups/{id}` cascade xóa `GroupPermission`/`UserGroup` (cả hai `OnDelete(Cascade)`) → tước quyền admin của toàn hệ thống ngay lập tức, không cảnh báo. Fix: chạy `WouldGroupPermissionEditLockoutAsync` (permission set rỗng) trước khi cho xóa group đang cấp quyền quản trị.
8. **UsersController.UpdateUser (toggle IsSuperUser) bypass guard hoàn toàn** — `UpdateUserCommand.cs:84`, chỉ gate bằng policy `users.edit` — **yếu hơn** `admin` mà 2 endpoint có guard yêu cầu. Vì `IsSuperUser` flag short-circuit `PermissionHandler` (bước 3, trước khi xét group/permission — `PermissionHandler.cs:81-85`), user chỉ có `users.edit` có thể demote superuser cuối cùng qua `PUT /users/{id}` với `isSuperUser:false`, hoàn toàn độc lập với guard theo group. Đây là đường bypass nghiêm trọng nhất trong nhóm lockout.

**Nhóm D — Race condition không có lock (overcommit tồn kho / double-assign)**

9. **License seat checkout không có lock/transaction** — `LicensesController.cs:370-458`. Không `BeginTransactionAsync`, không `FOR UPDATE`, không concurrency token trên `LicenseSeat`. 2 request checkout cùng seat/cùng license 1-seat-còn-lại đồng thời → 1 request bị ghi đè âm thầm (không lỗi), hoặc auto-pick chọn trùng seat.
10. **Accessory checkout không lock hàng khi tính tồn** — `Application/Accessories/Commands/CheckoutAccessoryCommand.cs:42-51`. Có transaction nhưng đọc `remaining` bằng `FirstOrDefaultAsync` thường (không `FOR UPDATE`); vì mỗi request INSERT child row mới (không UPDATE parent) nên Postgres không tự serialize — 2 request checkout đồng thời cùng lúc còn 1 unit đều pass check → tồn kho âm.
11. **Component allocation (Bulk) không lock hàng khi tính tồn** — `ComponentAllocationService.cs:112-114` (Allocate) và `:198-212` (Return). Cùng pattern overcommit như trên.
12. **Consumable checkout không lock hàng khi tính tồn** — `ConsumableAllocationService.cs:40-52`. Cùng pattern overcommit.

**Nhóm E — ActionLog sai TargetId (audit trail sai, cùng lớp lỗi ST4/Task E)**

13. **Component Return (Serial) log `TargetId=null` thay vì asset thật** — `ComponentAllocationService.cs:178-189` (`ReturnAsync`, nhánh serial). Khi checkin qua `serialNo` (request `assetId` là null — path hoàn toàn reachable vì `CheckinComponentRequest.AssetId` optional, `ComponentsController.cs:464`), code log `TargetId` lấy từ tham số `assetId` (null) thay vì `unit.CurrentAssetId` (giá trị thật, bị null hóa ngay sau đó ở dòng 175). Kết quả: `TargetType=Asset` nhưng `TargetId=null` — lịch sử checkin mất dấu vết đã trả về asset nào.

---

### 🟠 TRUNG BÌNH — 16 vấn đề

**Company-scoping / Permission**

14. `AssetsController.GetHistory` không company-scoped — `AssetsController.cs:270-282`. Trả action-log/notes lịch sử của asset bất kỳ công ty nào (khác `GetAsset` cùng controller).
15. `ComponentsController.RemoveAssignment` không company-scoped — `ComponentsController.cs:298`.
16. `ConsumablesController.Confirm` không company-scoped — `ConsumablesController.cs:190`.
17. `DepartmentsController.GetAll/Get` không company-scoped — `DepartmentsController.cs:26,51`. Có `CompanyId` nhưng filter chỉ áp khi client tự truyền query param, không ép theo company của user.
18. `UsersController.DeleteUser` (soft-deactivate) không qua lockout guard — `DeleteUserCommand.cs:49`. Tài khoản superuser cuối cùng có thể bị vô hiệu hóa bởi bất kỳ ai có `users.delete`.
19. `LicensesController.CheckinSeat` không log `TargetType`/`TargetId` — `LicensesController.cs:481-482`. Khác Checkout (log đủ), Checkin null hóa field trước rồi log rỗng → mất dấu vết đã trả seat cho ai/hệ thống nào.

**Update/Patch semantics (latent — chưa có caller thật kích hoạt, nhưng handler sai)**

20. `AssetMaintenance` Update KHÔNG patch-safe — `AssetMaintenancesController.cs:363-369` (`SupplierId`/`CompletionDate`/`Cost` full-replace). **Xác nhận lại: vẫn y nguyên hiện trạng đã biết trước** — cả 2 frontend call-site (`AssetMaintenanceSection.tsx:161-175`, `MaintenanceCompleteModal.tsx:56-68`) vẫn luôn gửi đủ field, chưa có caller mới.
21. `Asset.Name` thiếu guard rỗng — `UpdateAssetCommand.cs:110-114`. `AssetTag` đã guard `!string.IsNullOrEmpty`, nhưng `Name` thì không — payload thiếu `name` sẽ deserialize `""`, luôn khác giá trị cũ → xóa nhầm tên asset. Latent (form hiện tại luôn gửi `name`).
22. `Component` Update — 7 field ghi đè vô điều kiện (`SupplierId`, `ManufacturerId`, `ModelNumber`, `LocationId`, `OrderNumber`, `PurchaseCost`, `PurchaseDate`) — `ComponentsController.cs:248-257`. Latent.
23. `License` Update — 7 field ghi đè vô điều kiện (`ExpirationDate`, `TerminationDate`, `PurchaseCost`, `PurchaseDate`, `OrderNumber`, `SupplierId`, `ManufacturerId`) — `LicensesController.cs:324-332`. Latent.
24. `User` Update — `IsSuperUser`/`IsActive` là `bool` không nullable, thiếu field trong payload sẽ default `false` → âm thầm tước quyền admin/vô hiệu hóa tài khoản — `UpdateUserCommand.cs:86-88`. Latent nhưng blast radius cao nếu có caller thứ 2.
25. `Consumable` Update tái dùng `CreateConsumableRequest` (full-replace) — `Qty`/`MinAmt` không nullable, thiếu field → set về 0 — `ConsumablesController.cs:136-141`. Latent.
26. Admin reference-data (Category/Manufacturer/Supplier/Location/AssetModel) bind thẳng vào entity, không guard field nào — `AdminController.cs` (5 action: `:38-46,80-90,135-146,187-200,241-246`). Cùng nguyên nhân gốc với bug Task F (Asset) trước khi fix, nhưng CHƯA từng được sửa ở nhóm này. Latent (mỗi trang admin có đúng 1 form gửi đủ field).

**Kiến trúc/CQRS gây lỗi thật (kéo ra từ mục 7 vì đây là bug cụ thể, không chỉ nhận xét kiến trúc)**

27. **Validator của Asset KHÔNG được thực thi trong request path thật** — `Program.cs` đăng ký MediatR + `AddValidatorsFromAssemblyContaining<CheckoutAssetCommandValidator>()` nhưng **không có `IPipelineBehavior` nào được đăng ký** (0 kết quả grep `IPipelineBehavior`/`AddOpenBehavior` toàn project). `AssetsController.CreateAsset`/`.Checkout` gọi thẳng `_mediator.Send` — validator chỉ chạy trong unit test (gọi tay), không bao giờ chạy khi gọi API thật. Hậu quả cụ thể: `CreateAssetCommandValidator` có rule chặn trùng `AssetTag` nhưng **không bao giờ thực thi**, và **không có unique index** trên `AssetTag` ở DB (đã kiểm tra `InitialBaseline` migration) → **API cho phép tạo trùng AssetTag ngay bây giờ**. Fix: đăng ký `ValidationBehavior<TRequest,TResponse> : IPipelineBehavior<TRequest,TResponse>` trong `Program.cs` MediatR config.

**Concurrency test coverage**

28. `FOR UPDATE` lock của Asset checkout/checkin (đã implement đúng) **không được test dưới tải đồng thời thật trong CI** — `AssetTests.cs:16-19,400-403` tự ghi chú EF InMemory không chạy được `FromSqlRaw`; test chỉ gọi `CheckoutAssetCommandValidator` trực tiếp (bỏ qua handler). Không có `Task.WhenAll`/`Parallel` nào trong toàn bộ test project. → lock chỉ được bảo vệ bởi suy luận thủ công, không có gì trong CI xác nhận nó hoạt động đúng.

**ActionLog CompanyId thiếu (master data có CompanyId thật)**

29. `Department` Create/Update/Delete không log `CompanyId` dù entity có field này (dùng ở chỗ khác cùng file) — `DepartmentsController.cs:67,85-86,103`.
30. `Location` (Admin) Create/Update/Delete không log `CompanyId` — `AdminController.cs:236,248-249,267`.
31. `SystemInfo` Create/Update/Delete không log `CompanyId` — `SystemInfoController.cs:108,126-127,140`.

---

### 🟡 THẤP — 12 vấn đề

32. `ImportExportController.ImportAssets` — asset import không gán `CompanyId` (null/floater) bất kể công ty người import, và log `companyId` bằng công ty người thực hiện chứ không phải từng record — `ImportExportController.cs:57-71`. Cần xác nhận đây có phải hành vi cố ý (aggregate log 1 dòng/lần import) hay thiếu sót.
33. `AdminController.GetDepreciations` dùng `[Authorize]` trần thay vì `[Authorize(Policy="depreciations.view")]` dù key đã có sẵn trong catalog — `AdminController.cs:278`. Rủi ro thấp (chỉ đọc config).
34. `AssetMaintenancesController.Reopen` dùng `[Authorize]` trần, dựa hoàn toàn vào check `IsSuperUser()` inline thay vì policy khai báo — `AssetMaintenancesController.cs:541,545`. Hoạt động đúng hiện tại nhưng dễ vỡ nếu refactor sau này gỡ nhầm inline check.
35. Catalog có 4 policy không ai dùng (`export`, `depreciations.create/edit/delete`) — chỉ để tham khảo, không phải lỗi.
36. `Accessory` Update — cùng cấu trúc full-replace như Consumable (`Qty`/`MinAmt` có thể set về 0) nhưng **hạ mức vì đây là dead code** — `accessoriesApi.update` không có caller nào trong frontend hiện tại (`AccessoriesController.cs:148-153`). Ngoài ra thiếu luôn CompanyId-lock-sau-khi-có-lịch-sử-checkout (Consumable đã có, Accessory chưa) — nên bổ sung nếu tương lai có UI gọi update.
37. Department/Company/CustomField/SystemInfo/SystemPosition/Group Update — cùng pattern full-entity-bind nhưng field count nhỏ, field quan trọng required nên rủi ro thấp — code smell, không phải bug đang hoạt động sai.
38. `action_logs.DeletedAt` là cột `without time zone` nhưng chưa từng có write-site nào ghi vào — an toàn hiện tại, chỉ trở thành rủi ro nếu tương lai thêm tính năng soft-delete cho ActionLog mà quên `SpecifyKind`.
39. Tài liệu `docs/HANDOFF_DATETIME_KIND_AUDIT.md` liệt kê `TerminationDate`/`StartDate`/`CompletionDate` là "chưa xử lý" — audit mới xác nhận **thực ra đã được fix đầy đủ** (`LicensesController.cs:268,325`, `AssetMaintenancesController.cs:293-294,367` đều đã `SpecifyKind`). Đề xuất cập nhật lại tài liệu để tránh phiên sau làm lại việc đã xong.
40. `Infrastructure/Persistence/DbInitializer.cs` — code chết (`EnsureCreated()` + `ALTER TABLE categories ADD COLUMN IF NOT EXISTS` self-heal, bọc try/catch nuốt lỗi) — xác nhận **không được gọi ở đâu cả** (`Program.cs:222` dùng `db.Database.Migrate()`). Không gây rủi ro drift ngay bây giờ, nhưng là "bẫy" nếu sau này có người vô tình gọi lại `DbInitializer.Initialize(...)` tưởng đây là dev-seed hợp lệ — nên xóa hẳn file, không để mồ côi.
41. `CreateUserCommand`/`UpdateUserCommand` trả về `UserDto` đầy đủ (giống Query) thay vì kết quả tối thiểu — vi phạm nhẹ tinh thần tách CQRS, không gây lỗi chức năng.
42. `UsersController` validate bằng cách tự inject `IValidator<T>` và gọi tay trong controller (`UsersController.cs:347-349,405-410`) thay vì qua pipeline — khác `Application/Assets` (dùng validator nhưng pipeline chưa wire — mục 27) và khác các module hoàn toàn không có validator (License/Maintenance/Consumable/Department...). 3 cách làm khác nhau cho cùng 1 việc trong cùng 1 codebase.
43. `Application/Users/Queries/GetUserByIdQuery.cs` và `Application/Assets/Queries/GetDueAuditAssetsQuery.cs` là Query+Handler hoàn chỉnh nhưng **không được gọi ở đâu cả** — `UsersController.GetUser` viết lại logic tương tự trực tiếp trên `_context.Users` thay vì gửi `GetUserByIdQuery`. Nhánh Query của CQRS bị bỏ dở ngay trong module đã có nó.

---

## Xác nhận lại các vấn đề đã biết trước (bằng bằng chứng mới, không copy kết luận cũ)

| Vấn đề đã biết trước (từ handoff cũ) | Kết luận audit mới | Bằng chứng |
|---|---|---|
| DateTime Kind — 4 nhóm A-D đã fix (`HANDOFF_DATETIME_KIND_AUDIT.md`) | **Xác nhận ĐÚNG, còn nguyên vẹn** — cả 4 nhóm vẫn dùng `SpecifyKind` tại mọi write-site đã liệt kê | Đọc lại từng file:line trong báo cáo cũ, đối chiếu migration làm ground truth |
| `TerminationDate`/`StartDate`/`CompletionDate` — tài liệu cũ ghi "chưa xử lý" | **SAI — đã được fix rồi**, tài liệu cũ lạc hậu so với code | `LicensesController.cs:268,325`; `AssetMaintenancesController.cs:293-294,367` |
| `UpdateMaintenanceCommand` patch-unsafe, latent vì 2 call-site đều gửi đủ field | **Xác nhận ĐÚNG, còn nguyên hiện trạng** — chưa có call-site thứ 3 | Đọc trực tiếp `AssetMaintenanceSection.tsx`, `MaintenanceCompleteModal.tsx` |
| ST1 "đã áp dụng company-scoping tường minh cho 10 Controller" | **Đúng nhưng KHÔNG đầy đủ như cảm giác ban đầu** — ST1 tự nhận là áp dụng cho "Read Endpoints"; audit mới cho thấy **endpoint GHI (PUT/DELETE)** của chính 5 trong 10 module đó (Asset, Accessory, Consumable, Component, ComponentUnit) **chưa từng được scope** — đây là khoảng trống thật, không phải hiểu nhầm | Mục 1-5 trong Phần I 🔴 CAO |
| Asset FOR UPDATE concurrency — "chỉ test ở validator-level" | **Xác nhận ĐÚNG** | `AssetTests.cs:16-19,400-403` |
| `scripts/audit-sweeps.ps1` Sweep 3 — "đã sweep xác nhận companyId đủ" | **Có blind spot thật** — Sweep 3 chỉ quét literal `LogAction(`, hoàn toàn bỏ qua pattern `_context.ActionLogs.Add(new ActionLog{...})` (~50% số file ghi log dùng pattern này) → không phát hiện được 3 lỗ hổng CompanyId ở Department/Location/SystemInfo (mục 29-31) | Đọc `scripts/audit-sweeps.ps1:104-139` |

---

## Phần II — Đánh giá kiến trúc (mục 7-10 — KHÔNG xếp mức độ nghiêm trọng, đây là đánh giá thiết kế/tùy chọn cải tiến, không phải lỗi đang gây hại)

### Mục 7 — CQRS qua MediatR: có tồn tại, nhưng chỉ phủ ~15% hệ thống

**Có/Không, ở đâu:** MediatR + CQRS **có tồn tại thật**, nhưng chỉ ở `Application/{Accessories,Assets,Users,Common}` — 4 trong khoảng 21 domain có controller. Đếm theo action: 138 action method trên toàn bộ 20 controller, chỉ **16 action (~12%)** thật sự gọi `_mediator.Send` (9 ở Assets, 4 ở Accessories, 3 ở Users). Ngay cả 3 controller "có dùng MediatR" vẫn tự query `_context` trực tiếp cho mọi thao tác GET. 17 controller còn lại (Consumable, Component, License, Maintenance, Department, Company, Admin, Group, System*, CustomField, ImportExport, Label, Report, Dashboard, Permission, ActionLog) làm **100%** qua `AppDbContext` trực tiếp trong controller — không có Command/Query nào.

**Command có trả tối thiểu không?** Không nhất quán — Asset/Accessory/User-Delete trả tối thiểu (`AssetResult(Success,Message,AssetId,ErrorCode)`), nhưng `CreateUserCommand`/`UpdateUserCommand` trả nguyên `UserDto` giống hệt Query — ngay trong cùng 1 module (Users) đã có 2 kiểu khác nhau.

**So sánh cấu trúc giữa module hay-sửa (Asset/License/Maintenance/Consumable) và module ít đụng (Department/Supplier/Company):** Asset là module "chuẩn nhất" — mỗi command 1 file, có FluentValidation validator riêng, handler cùng file. Accessory dùng cùng khung nhưng validate bằng `if` thủ công trong handler (không FluentValidation). Users tách validator ra thư mục riêng nhưng lại gọi tay trong controller thay vì qua pipeline. License/Maintenance/Consumable/Department/Company/Supplier/Manufacturer/Category **hoàn toàn không có Application layer** — mọi logic (kể cả tính seat còn trống, guard xóa, uniqueness check) nằm thẳng trong controller action dạng `if...return BadRequest`. Đây không phải "1 kiểu đúng, nhiều kiểu sai" mà là 4-5 cách làm khác nhau phản ánh việc viết ở các thời điểm/phiên khác nhau, chưa từng được thống nhất lại.

**Phát hiện quan trọng nhất của mục này đã đưa vào Phần I (mục 27, 🟠):** validator của Asset đăng ký trong DI nhưng chưa từng chạy trong request path thật vì thiếu `IPipelineBehavior` — đây không phải nhận xét thẩm mỹ, mà là hậu quả cụ thể của việc CQRS "có khung nhưng thiếu 1 mắt xích" (pipeline behavior).

**Migration history:** sạch, xác nhận chỉ có `InitialBaseline` + `LicenseSeatSystemInfoTarget`, không còn self-heal SQL nào chạy thật (dù `DbInitializer.cs` vẫn còn tồn tại dưới dạng code chết — mục 40).

**Rủi ro/lợi ích nếu hoàn thiện CQRS cho toàn bộ:** không có gì "hỏng" vì thiếu CQRS ở 17 controller còn lại — pattern `if...BadRequest` trực tiếp trong controller vẫn hoạt động đúng chức năng. Cái mất thực tế nằm ở chỗ khác: đoạn filter company-visibility ~30 dòng bị copy-paste y hệt giữa `ReportsController.cs:147-183` và `DashboardController.cs:179-215`, và đoạn ghi ActionLog thủ công lặp lại ~40+ lần across các controller không dùng MediatR — đây là những chỗ dễ lệch/dễ quên khi thêm endpoint mới hơn là bản thân việc "thiếu CQRS". Lợi ích nếu hoàn thiện: test được handler độc lập không cần HTTP, và có 1 nơi enforce audit-log/company-scoping xuyên suốt thay vì copy tay từng chỗ.

### Mục 8 — Redis: có hạ tầng, KHÔNG có consumer nào

**Có/Không, ở đâu:** Redis được khai báo trong `AppHost.cs:9,24-25` (`AddRedis("cache")`, wire vào Server), và Server có package `Aspire.StackExchange.Redis.OutputCaching` + `Program.cs:26` gọi `AddRedisClient("cache")` (đăng ký DI) + `Program.cs:206-214` dùng Redis cho health check readiness. **Nhưng grep toàn bộ Domain/Application/Infrastructure/Web không tìm thấy bất kỳ consumer nào** — không `IDistributedCache`, không `IConnectionMultiplexer`, không `[OutputCache]`, không `app.UseOutputCache()`. `CompanyScopeService` có cache nhưng dùng `IMemoryCache` (in-process), không phải Redis.

**Kết luận rõ ràng:** Redis đang là **năng lực hạ tầng đã cấp phát nhưng chưa được dùng** — không phải cache đang hoạt động, cũng không phải bug (không có gì để "stale"). Không cần thêm code dùng Redis trong lượt này.

**Đánh giá rủi ro/lợi ích:** Rủi ro giữ nguyên: thêm 1 dependency khởi động bắt buộc (`WaitFor(cache)`) cho 1 năng lực chưa dùng tới — làm chậm/phức tạp hóa dev startup không cần thiết. Lợi ích nếu kích hoạt: các endpoint đọc nhiều, ít đổi (danh mục categories/manufacturers/suppliers, `GET /permissions`) là ứng viên tốt cho output caching mà không cần hạ tầng mới.

### Mục 9 — Clean Architecture: khung đúng, chỉ 3/20 controller thực sự tuân thủ

**Layering:** cấu trúc thư mục Domain/Application/Infrastructure/Web tồn tại đúng như mô tả, nhưng chỉ áp dụng đầy đủ cho 3 feature (Asset, Accessory, User).

**Domain purity:** đa số entity là POCO sạch. Có rò rỉ nhỏ: `Accessory.cs`, `Component.cs`, `Consumable.cs` dùng `[NotMapped]` (namespace `System.ComponentModel.DataAnnotations.Schema` — thuộc EF Core) cho computed property — đây là rò rỉ framework thật, dù ở mức thấp (không có `[Table]`/`[ForeignKey]`/`DbContext` nào lọt vào Domain). `IApplicationDbContext`/`IAuditable`/`ICompanyable` sạch, không leak `DbSet`/`DbContext`.

**Controller purity (7 controller mẫu):** `AssetsController` — mọi hành động ghi đều qua MediatR, chỉ GET query thẳng DbContext. `LicensesController` (524 dòng, KHÔNG dùng MediatR dù là module hay sửa) — chứa logic nghiệp vụ thật ngay trong controller: tính lại seat count khi update, switch 3 nhánh validate company-mismatch khi checkout, invariant check "đúng 1 target". `Departments/Companies/Admin/Permissions/Reports/Dashboard/Labels` — không dùng MediatR, chứa uniqueness check, delete-guard nhiều bảng, cây đệ quy + BFS cycle-check viết tay ngay trong `CompaniesController`, và đoạn filter company-visibility ~30 dòng bị copy y hệt giữa Reports/Dashboard.

**Kết luận hướng phụ thuộc:** đúng cho 3 feature có Application layer; 17 controller còn lại đi thẳng Web → Infrastructure (bỏ qua Application vì tầng đó không tồn tại cho chúng) — là layering đơn giản hơn có chủ đích hoặc do chưa migrate hết, không phải vi phạm ngẫu nhiên.

**Rủi ro/lợi ích:** phần tốn kém thực tế không phải "thiếu tầng" mà là đoạn filter company-visibility bị copy-paste và ~40 lần viết tay ActionLog rải rác — đúng những chỗ dễ quên khi thêm endpoint mới (khớp với phát hiện mục 29-31 ở Phần I). Hoàn thiện Clean Architecture cho 17 controller còn lại sẽ giúp test độc lập và có 1 nơi enforce các cross-cutting concern này, nhưng đây là lựa chọn đầu tư, không phải sửa lỗi cấp bách.

### Mục 10 — DI Extension Pattern: chưa tồn tại, toàn bộ đăng ký dồn trong Program.cs

**Có/Không:** Hoàn toàn KHÔNG tồn tại — grep toàn solution không thấy `*ServiceCollectionExtensions.cs`, `*DependencyInjection.cs`, `AddApplicationServices`, `AddInfrastructureServices`, `AddPersistence` ở bất kỳ đâu.

**Hiện trạng `Program.cs`:** 331 dòng, ~24 lời gọi `Add...()` trực tiếp + ~80 dòng logic khởi động imperative (migrate DB, seed default permission group, migrate legacy superuser — dòng 219-298) cũng nằm thẳng trong file. Nhóm lớn nhất là JWT/JIT-provisioning (~95 dòng, dòng 51-145) — trộn lẫn wiring framework với logic nghiệp vụ thật (tạo user, đồng bộ field, gắn cờ superuser) ngay trong event handler `OnTokenValidated`.

**Đề xuất cấu trúc (chỉ đề xuất, không tự làm):**
- `Infrastructure/Persistence/PersistenceServiceCollectionExtensions.cs` → `AddPersistence(...)`: EF Core + health check Postgres.
- `Infrastructure/InfrastructureServiceCollectionExtensions.cs` → `AddInfrastructureServices(...)`: Keycloak, `ICurrentUserService`, `IActionLogService`, `IComponentAllocationService`, `IConsumableAllocationService`, `ICompanyScopeService`, `PermissionLockoutGuard`, MemoryCache, HttpContextAccessor.
- `Infrastructure/Authorization/AuthorizationServiceCollectionExtensions.cs` → `AddPermissionAuthorization(...)`: policy loop từ `PermissionCatalog` + `PermissionHandler`.
- `Infrastructure/Authentication/AuthenticationServiceCollectionExtensions.cs` → `AddKeycloakAuthentication(...)`: JWT bearer setup; nên tách riêng logic JIT-provisioning ra 1 service (`IJitUserProvisioningService`) thay vì viết trực tiếp trong `OnTokenValidated`.
- `Application/ApplicationServiceCollectionExtensions.cs` → `AddApplicationServices(...)`: MediatR + FluentValidation (và đây cũng là chỗ đăng ký `IPipelineBehavior` còn thiếu ở mục 27).
- Khối seed/migration (80 dòng) nên chuyển vào `DbInitializer.cs` đã có sẵn (sau khi dọn code chết ở mục 40) hoặc 1 `IHostedService` riêng, gọi 1 dòng từ `Program.cs`.
- Kết quả: `Program.cs` còn lại ~10-15 dòng gọi extension, dễ đọc hơn nhiều.

**Rủi ro/lợi ích:** không có gì "hỏng" hiện tại — đây thuần là chi phí bảo trì khi dự án phát triển thêm module. Lợi ích rõ nhất: tách JIT-provisioning ra khỏi JWT event handler sẽ giúp unit-test được logic đó độc lập, và tiện thể là nơi tự nhiên để vá lỗ hổng `IPipelineBehavior` (mục 27).

---

## Phụ lục — Bảng inventory chi tiết (tham chiếu nhanh)

### A. Company-scoping theo endpoint (từ agent mục 1)

| Endpoint | Trạng thái | File:Line |
|---|---|---|
| Assets.GetAssets/GetAsset (đọc) | SCOPED | AssetsController.cs:65-66,120 |
| Assets.GetHistory | **NOT-SCOPED** | AssetsController.cs:270-282 |
| Assets.UpdateAsset/DeleteAsset (ghi) | **NOT-SCOPED** | UpdateAssetCommand.cs:45; DeleteAssetCommand.cs:30 |
| Assets.Checkout (ghi) | SCOPED | CheckoutAssetCommand.cs:120 |
| Accessories.Get* (đọc) | SCOPED | AccessoriesController.cs:52,81 |
| Accessories.Update/Delete (ghi) | **NOT-SCOPED** | AccessoriesController.cs:141; DeleteAccessoryCommand.cs:33 |
| Consumables.Get* (đọc) | SCOPED | ConsumablesController.cs:49,76-78 |
| Consumables.Update/Delete (ghi) | **NOT-SCOPED** | ConsumablesController.cs:125,164 |
| Components.Get* (đọc) | SCOPED | ComponentsController.cs:57,122 |
| Components.Update/Delete (ghi) | **NOT-SCOPED** | ComponentsController.cs:228,268 |
| ComponentUnits.UpdateStatus/Delete (ghi) | **NOT-SCOPED** | ComponentUnitsController.cs:33,46 |
| Licenses.* (đọc+ghi) | SCOPED | LicensesController.cs (IsLicenseVisible, xuyên suốt) |
| AssetMaintenances.* (đọc+ghi) | SCOPED (403) | AssetMaintenancesController.cs (xuyên suốt) |
| Users.GetUsers/GetUser | **NOT-SCOPED** | UsersController.cs:54,205 |
| Departments.GetAll/Get | **NOT-SCOPED** | DepartmentsController.cs:26,51 |
| Reports/Dashboard/Systems/SystemInfo/ActionLogs/Labels/ImportExport(export) | SCOPED | (đã audit, xem chi tiết trong log agent) |

Quy ước 404-vs-403: nhất quán, không resource nào trộn 2 kiểu (SystemInfo/System/ActionLog = 404; AssetMaintenance = 403).

### B. Patch-safety theo entity (từ agent mục 2)

| Entity | Vị trí | Patch-safe? | Field khóa được enforce? |
|---|---|---|---|
| Asset | UpdateAssetCommand.cs:105-179 | PARTIAL (Name thiếu guard) | YES (IsConfirmed gate) |
| AssetMaintenance | AssetMaintenancesController.cs:363-369 | NO (3 field) | YES (StartDate, IsClosed) |
| Component | ComponentsController.cs:248-257 | PARTIAL (7 field) | YES (CategoryId/CompanyId/TrackingType) |
| License | LicensesController.cs:321-332 | PARTIAL (7 field) | YES (CategoryId/CompanyId) |
| User | UpdateUserCommand.cs:79-88 | PARTIAL (bool default false) | YES (Username immutable) |
| Consumable | ConsumablesController.cs:136-141 | NO (full PUT) | YES (Status/CompanyId-after-checkout) |
| Accessory | AccessoriesController.cs:148-153 | NO (full PUT, nhưng dead code) | PARTIAL (thiếu CompanyId-lock) |
| Admin ref-data (5 entity) | AdminController.cs | NO (full entity bind) | PARTIAL (CategoryType khóa bằng cách bỏ qua) |
| ComponentUnit | ComponentUnitsController.cs:33-38 | YES (single-field PATCH) | N/A |

### C. DateTime Kind theo cột (từ agent mục 3) — TẤT CẢ ĐÃ FIX, không còn 🔴/🟠

| Bảng.Cột | Kiểu Postgres | Trạng thái |
|---|---|---|
| asset_maintenances (7 cột) | without tz | FIXED |
| asset_maintenance_assignees.AssignedAt | without tz | FIXED |
| component_units (3 cột) | without tz | FIXED |
| license_seats.CreatedAt/UpdatedAt | without tz | FIXED |
| license_seats.AssignedAt | **with tz** | SAFE (đúng, không phải bug) |
| licenses.DeletedAt/TerminationDate | without tz | FIXED |
| action_logs.DeletedAt | without tz | SAFE — chưa có write-site |

---

## Tổng kết theo số lượng

| Mức độ | Số lượng | 
|---|---|
| 🔴 CAO | 13 |
| 🟠 TRUNG BÌNH | 16 |
| 🟡 THẤP | 12 |

**3 vấn đề nên ưu tiên xử lý trước nhất** (theo đánh giá tổng hợp — mức độ + khả năng bị khai thác thật):
1. Company-scoping thiếu ở toàn bộ endpoint GHI của Asset/Accessory/Consumable/Component/ComponentUnit (mục 1-5) — cross-tenant write, không cần lỗi logic gì thêm, chỉ cần biết ID.
2. Bypass `PermissionLockoutGuard` qua `UsersController.UpdateUser` (mục 8) — 1 request `users.edit` là đủ tước quyền admin toàn hệ thống.
3. `UsersController.GetUsers/GetUser` không company-scoped (mục 6) — rò rỉ PII cross-tenant quy mô toàn bộ user base.

Việc sửa các mục trên nên tách thành các subtask riêng theo đúng quy trình chuẩn của dự án (`docs/DEVELOPMENT_WORKFLOW.md` mục 1.2) — không gộp company-scoping (bảo mật) với concurrency (dữ liệu) hay DateTime Kind (đã xong, không cần làm gì) vào cùng 1 đợt phê duyệt.
