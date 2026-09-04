# BACKLOG — các phát hiện đang chờ xử lý

> Các phát hiện từ audit/migration được đăng ký tại đây để xử lý riêng, KHÔNG trộn vào task
> tái cấu trúc đang chạy (nguyên tắc: parity trước, cải thiện sau).

---

## BUG-E — DepartmentsController.UpdateDepartment: full-PUT, field không gửi bị clear (vi phạm patch-safety)

- **Trạng thái:** OPEN — phát hiện 2026-09-01 trong Giai đoạn 1 (pilot MediatR, parity verification)
- **Phân loại:** patch-safety (cùng lớp lỗi Task M1/M2 đã fix cho 11 entity khác — xem workflow §Patch semantics)
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

- **Trạng thái:** OPEN — phát hiện 2026-09-01 trong Giai đoạn 2 (parity verification trên binary cũ)
- **Phân loại:** business-rule inconsistency (Create có dup-check, Update không)
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

- **Trạng thái:** OPEN — phát hiện 2026-09-01 trong Giai đoạn 2 (audit Models trước khi migrate)
- **Mức độ: MEDIUM** (nghiệp vụ — reference data, KHÔNG có company-isolation risk như BUG-G)
- **Vị trí:** hành vi CÓ TỪ TRƯỚC trong `AdminController.CreateModel` / `UpdateModel` (HEAD trước
  migrate Models); di chuyển verbatim vào `aspire-react.Application/Models/Commands/` kèm comment
  `// TODO BUG-H` in-code.
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

- **Trạng thái:** OPEN — phát hiện 2026-09-01/02 trong Giai đoạn 3 (audit + parity verification
  trên binary cũ); di chuyển verbatim vào
  `aspire-react.Application/CustomFields/Commands/` kèm comment `// TODO BUG-I` in-code.
- **Mức độ: MEDIUM** (nghiệp vụ — CustomField không có CompanyId, không isolation risk như BUG-G)
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

## BUG-G — Location.Create KHÔNG có company-scoping và không có validation nào (SECURITY/HIGH)

- **Trạng thái:** OPEN — phát hiện 2026-09-01 trong Giai đoạn 2 (audit Location trước khi migrate)
- **Mức độ: SECURITY/HIGH** (khác MEDIUM của BUG-E/F): regular user (không phải superuser) có thể
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

- **Trạng thái:** OPEN — phát hiện 2026-09-03 trong Giai đoạn 3 (audit Groups + parity verification
  trên binary cũ); di chuyển verbatim vào `aspire-react.Application/Groups/Commands/` kèm comment
  `// TODO BUG-K` in-code.
- **Mức độ: MEDIUM** (user-facing — GroupListPage quản trị group là UI thật, khác BUG-J zero-impact;
  không phải SECURITY — PermissionGroup không có CompanyId, không isolation risk)
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

- **Trạng thái:** OPEN — phát hiện 2026-09-03 trong Giai đoạn 3 (baseline Reports trên binary cũ);
  di chuyển verbatim vào `aspire-react.Application/Reports/Queries/CheckoutHistoryReportQuery.cs`
  kèm comment `// TODO BUG-L` in-code.
- **Mức độ: MEDIUM** — user-facing 500 khi có filter, NHƯNG **zero frontend impact**: grep toàn
  frontend xác nhận KHÔNG có caller của `/reports/checkout-history` (ReportsPage chỉ gọi
  depreciation + audit) → latent API defect, không user nào gặp (khác BUG-J cùng class).
- **Thành phần — độ tin cậy:** **GET /reports/checkout-history?startDate=…&endDate=… → 500** ✅
  CONFIRMED VIA REPRODUCTION (baseline + post đều 500 — parity verbatim). Nguyên nhân: query param
  DateTime bind Kind=Unspecified, so sánh trực tiếp với cột `timestamptz` → Npgsql throw
  (error-class DateTime Kind đã tài liệu hóa trong workflow doc). Unfiltered call hoạt động bình
  thường (17 rows). Fix = `DateTime.SpecifyKind(value, DateTimeKind.Utc)` cho filter trước khi so
  sánh — THAY ĐỔI HÀNH VI (500→200), cần duyệt riêng.
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

## INFRA-2 — 6 file PNG evidence (root) phát hiện mất khỏi working tree, không rõ thời điểm (ghi riêng, KHÔNG gộp vào INFRA-1)

- **Trạng thái:** RESOLVED (file) / OPEN (nguyên nhân) — phát hiện trong subtask A (Giai đoạn 3,
  nhóm Rất nặng), khôi phục 100% từ git blob ngay trong phiên (`git checkout HEAD --`,
  verify size + `git status` sạch)
- **Hiện tượng:** 6 file PNG ở repo root (`mc8_template_builder_nested.png`,
  `mc8b_after_expand.png`, `mc8b_after_form.png`, `qa7d_campaign_3of3.png`,
  `qa7d_campaign_detail.png`, `qa7d_template_builder.png` — evidence QA đợt MC-7d/MC-8/MC-9,
  commit `4c08d9b` ngày 2026-08-29) ở trạng thái `" D"` (mất khỏi working tree, chưa stage),
  `Test-Path` xác nhận vật lý không còn, `git diff HEAD` = N bytes → 0.
- **Vì sao TÁCH RIÊNG khỏi INFRA-1:** 2 vụ INFRA-1 trước (31-file loss, AdminController revert)
  có timeline rõ ràng — xảy ra GIỮA lúc agent đang thao tác, có thể liên hệ với Docker sập
  cùng thời điểm. Vụ này KHÔNG xác định được thời điểm xóa (đã tồn tại ở `git status` đầu
  tiên của phiên, trước mọi sửa code — có thể tiền-phiên: dọn tay, session agent khác, hoặc
  tác nhân ngoài). Gộp chung khi thiếu bằng chứng nhân quả sẽ làm loãng độ tin cậy của
  chính record INFRA-1.
- **Loại trừ nguyên nhân agent hiện tại:** toàn bộ lệnh destructive-capable trong phiên chỉ gồm
  `Remove-Item` target `apphost.log` trong thư mục temp + `git add/commit` với path liệt kê
  tường minh (.cs + BACKLOG.md); không `git clean/checkout/restore/rm`, không script quét ảnh.

---

## BUG-N — AssetMaintenances Update: SupplierId/CompletionDate/Cost gán trực tiếp, field absent bị clear (cùng lớp BUG-E)

- **Trạng thái:** OPEN — phát hiện trong subtask C (Giai đoạn 3, nhóm Rất nặng); migrate
  verbatim vào `aspire-react.Application/AssetMaintenances/Commands/UpdateMaintenanceCommand.cs`
  kèm comment `// TODO BUG-N` in-code (KHÔNG fix trong migration — parity trước).
- **Mức độ: MEDIUM** (nghiệp vụ — cùng lớp patch-safety BUG-E/BUG-I#1: không isolation risk)
- **Hành vi:** PUT /api/v1/maintenances/{id} — 3 field `SupplierId`, `CompletionDate`, `Cost`
  gán TRỰC TIẾP từ DTO nullable (`m.SupplierId = r.SupplierId` …); payload thiếu field →
  bị clear/null (VD: PUT chỉ `{title}` → CompletionDate/Cost/SupplierId mất). Các field còn
  lại patch-aware (Title/Notes/Type/IsWarranty conditional; StartDate FIELD_LOCKED).
  *Độ tin cậy: suy luận từ đọc code (payload realistic của frontend Maintenance form gửi đủ
  field nên chưa trigger — cùng tình trạng với BUG-I#1).*
- **Fix sketch (THAY ĐỔI HÀNH VI, cần duyệt riêng):** reserve `null` làm "clear có chủ đích"
  qua field kèm theo (VD `ClearCompletionDate: bool`) hoặc chuyển 3 field sang conditional
  assign như Title; quyết định cùng lúc với BUG-E (Department) nếu dọn patch-safety toàn diện.
