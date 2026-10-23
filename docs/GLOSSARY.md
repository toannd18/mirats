# GLOSSARY — Thuật ngữ UI thống nhất

> [UI-1 GĐ2] Một thuật ngữ cho mỗi khái niệm, dùng xuyên suốt sidebar, breadcrumb, tiêu đề, cột bảng,
> nút, tooltip và thông báo. Không dịch tên riêng của thực thể kỹ thuật khi đã là chuẩn ngành, nhưng
> **không để tiếng Anh lẻ** trong câu tiếng Việt.

## Thuật ngữ chính

| Khái niệm | Dùng thống nhất | Không dùng |
|---|---|---|
| Màn hình tổng quan | **Tổng quan** | Dashboard |
| Bản ghi phần mềm có số ghế | **Bản quyền** | License |
| Thu hồi tài sản/ghế/vật tư | **Thu hồi** | Checkin |
| Cấp phát tài sản/ghế/vật tư | **Cấp phát** | Checkout |
| Phiên bản checklist/mẫu | **Phiên bản** | Version |
| Công bố phiên bản | **Công bố** | Publish |
| Mẫu checklist bảo dưỡng | **Mẫu bảo dưỡng** | Template bảo dưỡng |
| Danh mục kiểm tra | **Danh mục kiểm tra** / **Hạng mục kiểm tra** | Checklist / ChecklistItems |
| Model của tài sản | **Model tài sản** | Asset Models |
| Số lượng | **Số lượng** | Qty |
| Số model | **Số model** | Model No. |
| Số đơn hàng | **Số đơn hàng** | Order Number / Order No. |
| Mã hàng | **Mã hàng** | Item No. |
| Ngày mua | **Ngày mua** | Purchase Date |
| Đơn giá | **Đơn giá** | Unit Cost |
| Khóa bản quyền / Serial | **Khóa bản quyền / Serial** | Product Key / Serial, Product Key |
| Hết vòng đời | **Hết vòng đời (EOL)** | EOL (khi đứng một mình) |
| Trang web | **Trang web** | Website |
| Email hỗ trợ | **Email hỗ trợ** | Support Email |
| Hành động chỉnh sửa | **Sửa** | Cập nhật (trong nhãn hành động quyền) |
| Serial | **Serial** | Số serial (trong UI mới) |
| Model | **Model** | Mẫu (khi là tên model) |
| Mã tài sản | **Mã tài sản** | Asset Tag |
| Vị trí trong hệ thống | **Vị trí hệ thống** | SystemPosition |
| Loại quản lý theo số lượng | **Theo số lượng** | Bulk |
| Loại quản lý theo từng cái | **Theo serial** | Serial (khi là loại quản lý) |
| Trạng thái tài sản | **Chờ cấp phát / Đã cấp phát / Đã lưu trữ** | Pending / Deployed / Archived |
| Có thể cấp lại | **Có thể cấp lại** | Reassignable |
| Ngưỡng cảnh báo số ghế | **Ngưỡng cảnh báo số ghế** | MinSeats |
| Nhập dữ liệu Excel | **Nhập Excel** | Import Excel |
| Xuất dữ liệu CSV | **Xuất CSV** | Export CSV / Assets CSV |
| Không thuộc công ty | **Không thuộc công ty** | Floater |

## Quy ước hiển thị

- **Ngày**: `dd/MM/yyyy` — chỉ dùng cho trường chỉ có ngày (ngày mua, ngày bắt đầu, ngày hết hạn…).
- **Ngày + giờ**: `dd/MM/yyyy HH:mm` — dùng cho mốc thời gian (audit, checkin/checkout, công bố…).
- Dùng duy nhất `frontend/src/utils/format.ts` (`formatDate`, `formatDateTime`, `formatNumber`).
  Sweep 7 của `scripts/audit-sweeps.ps1` chặn `toLocale*` và định dạng ngày tự viết ngoài helper.
- **Trạng thái**: luôn là nhãn tiếng Việt; giá trị enum kỹ thuật chỉ dùng trong code/API.
- **Nút**: hành động chính là động từ ngắn (`Cấp phát`, `Thu hồi`, `Công bố`); xem chi tiết dùng
  **`Xem`** hoặc **`Chi tiết`** — thống nhất theo từng ngữ cảnh, không trộn trong cùng một danh sách.
- **Empty state**: dùng câu hướng dẫn cụ thể (“Chưa có…”, “Chưa có bản quyền nào đang gán”), không chỉ “Trống”.
- Không hiển thị raw key JSON/camelCase (`templateVersionId`, `versionNumber`…) — dùng nhãn ở bảng trên.
- **Serial** và **Model** được giữ nguyên như thuật ngữ kỹ thuật/ngành; không đổi thành “Số serial” hoặc “Mẫu” trong UI mới.
