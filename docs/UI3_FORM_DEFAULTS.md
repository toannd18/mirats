# UI-3 — Giá trị mặc định khi tạo mới

> Chuẩn hóa theo UI-3 đợt 2/3. Quy tắc: **không tự ý điền 0 cho tiền, ngày hoặc thời hạn**.
> Các ô này phải trống (`null` / không gửi) để người dùng chủ động nhập; chỉ khi người dùng
> thực sự nhập `0` thì giá trị mới là `0`.

## Bảng mặc định

| Form | Giá trị mặc định | Ghi chú |
|---|---|---|
| Tạo tài sản | `purchaseCost = null`, `warrantyMonths = null`, `purchaseDate = null` | Mã tài sản để trống để backend tự sinh |
| Vật tư tiêu hao | `qty = 1`, `minAmt = 0`, `purchaseCost = null`, `purchaseDate = null` | `minAmt = 0` là ngưỡng cảnh báo có nghĩa nghiệp vụ |
| Phụ kiện | `qty = 1`, `minAmt = 0`, `purchaseCost = null`, `purchaseDate = null` | |
| Linh kiện | `trackingType = Bulk`, `qty = 1`, `minAmt = 0`, `purchaseCost = null`, `purchaseDate = null` | Serial chỉ dùng khi chọn `Serial` |
| Bản quyền | `reassignable = true`, `seats = 1`, `minSeats = 0`, `purchaseCost = null`, `purchaseDate = null` | |
| Người dùng | `isActive = true`, `isSuperUser = false`, `employeeNumber = ""`, `jobTitle = ""` | Mật khẩu ban đầu bắt buộc khi tạo |
| Danh mục | `tagColor = #1890ff`, `categoryType = Asset`, `useDefaultEula = true` | |
| Model tài sản | `requestable = false` | Có thể tự đổi theo danh mục |
| Hoàn thành bảo trì | `completionDate = null`, `cost = null`, `isWarranty = null`, `notes = null` | Không nhập chi phí ⇒ gửi `null`, báo cáo vẫn tính vào **Thiếu chi phí** |
| Hạng mục mẫu | `cycleMonths = 12` | Chu kỳ mặc định có nghĩa nghiệp vụ |
| Các form còn lại | Không đặt default nghiệp vụ | Để trống cho người dùng nhập/chọn |

## Quy ước triển khai

- Dùng `initialValues` của `Form` (hoặc `initialValue` ở `Form.Item` cho trường đơn lẻ).
- Không dùng `0` làm mặc định cho `purchaseCost`, `warrantyMonths`, `cost`, `purchaseDate`
  hoặc `isWarranty`.
- API patch/update: trường không gửi = giữ nguyên; trường gửi `null` = xóa giá trị.
- Báo cáo sửa chữa: `MissingCostCount` đếm `Cost IS NULL` (không đếm `Cost = 0`).
