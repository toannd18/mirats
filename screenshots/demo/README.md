# Ảnh demo luồng nghiệp vụ

Ảnh được chụp trên DB tạm (`mirats_tmp_df`) seed bằng `scripts/seed-demo.mjs` — không phải dữ liệu thật.
Tài khoản dùng cho từng ảnh:

| Ảnh | Tài khoản | Nội dung |
|---|---|---|
| `01-intake.png` | `admin` | Nhập tài sản mới, tem/asset tag và trạng thái chờ cấp phát |
| `02-install-transfer.png` | `admin` | Lắp đặt vào vị trí hệ thống, thu hồi và điều chuyển |
| `03-maintenance.png` | desktop: `admin`; mobile: `demo-tech` | Danh sách/chi tiết đợt bảo dưỡng và checklist trên mobile |
| `03-maintenance-ktv-390.png` / `03-maintenance-ktv-390-full.png` | `demo-tech` | Bằng chứng DF-6: KTV nhập kết quả checklist trên mobile 390px (đã lưu) |
| `04-repair.png` | `admin` | Phiếu sửa chữa: tạo, hoàn thành, đóng/kiểm tra và mở lại |
| `05-timeline-workflow.png` | `admin` | Timeline lịch sử tài sản sau các thao tác |
| `06-search-qr.png` | desktop: `admin`; mobile: `demo-tech` | Tìm kiếm và quét QR mở nhanh tài sản |
| `07-leader.png` | `demo-leader` | Dashboard và báo cáo sửa chữa của Lãnh đạo |
| `07-leader-reports-repairs.png` | `demo-leader` | Bằng chứng DF-3: Dashboard → Báo cáo → Sửa chữa, không có request 403 |
| `df2-403-campaign-alert.png` | `demo-leader` | Bằng chứng DF-2: 403 thật hiển thị Alert “không có quyền”, không phải empty state |
| `df4-group-permission-labels.png` | `admin` | Bằng chứng DF-4: nhãn quyền tiếng Việt dùng chung, key lạ fallback |
| `df2b-ktv-campaign-list.png` | `demo-tech` | Bằng chứng DF-2b: KTV thấy 3 đợt (khớp DB), API 200 |
| `df2b-leader-no-permission.png` | `demo-leader` | Bằng chứng DF-2b: direct route hiển thị không có quyền, không phát sinh request danh sách/403 |
| `df6c-step1-form-no-company.png` | `demo-tech` | DF-6c.3: form tạo tài sản không có trường Công ty |
| `df6c-step2-review.png` | `demo-tech` | DF-6c.3: màn hình xác nhận không có trường Công ty |
| `df6c-step3-created.png` | `demo-tech` | DF-6c.3: tạo thành công, danh sách tăng lên 61 tài sản |
| `df8-location-form-no-company.png` | `demo-tech` | DF-8.3: form Địa điểm (non-Asset) đã ẩn trường Công ty |
| `08-admin.png` | `admin` | Quản trị danh mục, nhóm quyền và cấu hình |

> **Lưu ý ảnh 03:** đợt `DOT-DEMO-Q1` ở trạng thái **Completed** do **seed script dùng token admin**
> gọi API hoàn thành — **không phải KTV thao tác**. Ảnh KTV tự nhập kết quả trên mobile 390px đã được
> bổ sung tại `03-maintenance-ktv-390.png` / `03-maintenance-ktv-390-full.png` (DF-6).
