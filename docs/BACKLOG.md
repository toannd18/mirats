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
