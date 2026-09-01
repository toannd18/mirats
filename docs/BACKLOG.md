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
