RULE: DATABASE SCHEMA SYNC

Dự án này đang trong giai đoạn CHUYỂN ĐỔI cơ chế quản lý schema — LUÔN kiểm tra trạng thái hiện tại trước khi hành động, không giả định theo trí nhớ/session trước.

Cách kiểm tra trạng thái hiện tại (làm trước, mỗi lần):

Xem Program.cs còn khối raw SQL self-heal (ADD COLUMN IF NOT EXISTS, DROP CONSTRAINT IF EXISTS...) hay không.
Xem thư mục Migrations/ đã có InitialBaseline (hoặc migration đầu tiên hợp lệ, đã áp dụng thật) hay chưa.
Đọc docs/DEVELOPMENT_WORKFLOW.md mục 3.9/§5 để biết trạng thái chính thức mới nhất đã ghi nhận.
Nhánh A — NẾU CHƯA hoàn tất baseline EF Migration (còn self-heal, chưa có InitialBaseline đã áp dụng):

Khi sửa/thêm/xóa property trong Entity class:

CẤM chạy hoặc đề xuất dotnet ef migrations add / dotnet ef database update. DB đang dùng EnsureCreated() — không có __EFMigrationsHistory; chạy EF migrations lúc này có thể phá schema/dữ liệu thật.
PHẢI cảnh báo rõ: "Database Schema đã thay đổi".
PHẢI thêm khối raw SQL self-heal idempotent vào Program.cs, dùng đúng cú pháp Postgres hợp lệ:
ALTER TABLE ... ADD COLUMN IF NOT EXISTS ...
CREATE TABLE IF NOT EXISTS ... / CREATE INDEX IF NOT EXISTS ...
DROP CONSTRAINT IF EXISTS ... rồi ADD CONSTRAINT ... (Postgres KHÔNG hỗ trợ ADD CONSTRAINT IF NOT EXISTS — dùng sai cú pháp này đã từng gây lỗi thật, bị try/catch nuốt âm thầm).
PHẢI viết kèm script docs/sql/migration_<tên>.sql có đủ UP và DOWN.
Nhắc áp dụng đúng quy trình stop → dotnet build → start cho .NET Aspire AppHost — KHÔNG chỉ "restart" qua dashboard (đã từng gây lỗi chạy nhầm binary cũ do file bị khóa, khiến thay đổi tưởng đã áp dụng nhưng thực tế chưa).
Nhánh B — NẾU ĐÃ hoàn tất baseline EF Migration:

Khi sửa/thêm/xóa property trong Entity class:

Cảnh báo rõ: "Database Schema đã thay đổi".
Xuất lệnh chính xác ở cuối phản hồi:
dotnet ef migrations add <DescriptiveName>
dotnet ef database update (Nhắc người dùng REVIEW nội dung file migration vừa sinh ra trước khi chạy database update — không chạy mù.)
Nhắc restart .NET Aspire AppHost theo quy trình stop → dotnet build → start.
Bắt buộc trong mọi trường hợp:

Nếu không chắc chắn đang ở Nhánh A hay Nhánh B (VD docs/DEVELOPMENT_WORKFLOW.md không có ghi chú rõ, hoặc dấu hiệu trong code mâu thuẫn nhau), DỪNG LẠI hỏi người dùng — tuyệt đối không tự đoán rồi hành động, vì hậu quả chọn sai nhánh có thể ảnh hưởng dữ liệu thật.