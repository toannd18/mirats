# 01 — Tổng quan Mirats

> Mirats (AspireReact) là hệ thống quản lý tài sản CNTT kiểu Snipe-IT, đa công ty
> (multi-tenant): tài sản, vật tư tiêu hao, linh kiện, phụ kiện, bản quyền phần mềm,
> bảo trì (phiếu lẻ + đợt checklist), người dùng và phân quyền chi tiết.

## 1. Mục đích / đối tượng đọc

- **Người dùng cuối** (nhân viên, thủ kho, kỹ thuật bảo trì): đọc cụm B để thao tác.
- **Admin hệ thống**: đọc cụm C để cấu hình công ty, người dùng, nhóm quyền, import.
- **Dev / vận hành**: trang này + cụm A còn lại (kiến trúc, ERD, sequence, triển khai).

Đọc xong cụm A, dev mới hiểu được các khối xây dựng hệ thống và đường đi của
một request; admin hiểu được mô hình công ty — nhóm quyền trước khi cấu hình.

## 2. Phạm vi chức năng

| Phân hệ (nhãn UI) | Route | Tóm tắt |
|---|---|---|
| Tài sản | `/assets` | CRUD, xác nhận (confirm), cấp phát/thu hồi, kiểm kê, lưu trữ, tem QR |
| Vật tư tiêu hao | `/consumables` | CRUD, xác nhận, cấp phát theo số lượng |
| Linh kiện | `/components` | CRUD theo 2 kiểu theo dõi (Bulk/Serial), cấp phát serial |
| Phụ kiện | `/accessories` | CRUD, cấp phát/thu hồi theo số lượng |
| Bản quyền | `/licenses` | CRUD, cấp phát/thu hồi seat |
| Bảo trì (phiếu lẻ) | `/maintenances` | Tạo/sửa/đóng/kiểm tra/mở lại phiếu |
| Template bảo dưỡng | `/maintenance/templates` | Template + version + hạng mục + tiêu chuẩn |
| Đợt bảo dưỡng | `/maintenance/campaigns` | Tạo đợt, ghi kết quả checklist, hoàn thành |
| Lịch sử hệ thống | `/system-history` | Tra cứu ActionLog |
| Báo cáo | `/reports` | Khấu hao, kiểm kê, xuất Excel |
| Người dùng / Nhóm / Phân quyền | `/users`, `/groups`, `/permissions` | Quản trị truy cập |
| Quản trị (9 trang) | `/admin/*` | Danh mục, NSX, NCC, model, địa điểm, khấu hao, công ty, phòng ban, hệ thống–vị trí, import Excel, cấu hình hệ thống |

Xác thực: đăng nhập password (JWT tự ký) + passkey tùy chọn; đổi mật khẩu bắt buộc
khi được cấp mới (`mustChangePassword`).

## 3. Công nghệ

| Lớp | Công nghệ (bản đang dùng) |
|---|---|
| Backend | .NET 10 (`net10.0`), ASP.NET Core Web API, EF Core + PostgreSQL, Redis (output cache), MediatR (CQRS + 3 behavior), FluentValidation |
| Frontend | React 19.2, Ant Design 6.5, Axios (service layer thuần, không React Query/Zustand), Vite 8, React Router |
| Điều phối dev | .NET Aspire (Postgres + Redis + API + Vite), HTTPS dev bắt buộc |
| Triển khai | Docker Compose (Postgres, Redis, API, frontend+Nginx, pgAdmin-debug) sau Nginx Proxy Manager; Cloudflare Tunnel cho public (xem `14-trien-khai.md`) |

## 4. Quy ước đọc tài liệu này

- Mỗi trang kết thúc bằng mục **Nguồn tham chiếu** (file code + dòng) — code là
  chuẩn khi tài liệu lệch.
- Nhãn UI trong tài liệu lấy đúng chuỗi frontend hiển thị (xem
  `.claude/skills/mirats-docs/references/glossary.md`), không tự đặt.
- Chỗ chưa kiểm chứng ghi `[CẦN XÁC NHẬN]` kèm lý do.
- Endpoint/mã lỗi tra cứu nhanh ở `docs/API.md` và `docs/ERROR_CODES.md`.

## Nguồn tham chiếu

- `aspire-react/frontend/src/App.tsx:459-633` (toàn bộ route)
- `aspire-react/frontend/package.json:18-20,36` (antd 6.5, axios, React 19.2, Vite 8)
- `aspire-react/aspire-react.Server/aspire-react.Server.csproj:4` (`net10.0`)
- `aspire-react/aspire-react.AppHost/AppHost.cs` (4 resources dev)
