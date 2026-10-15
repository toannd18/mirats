# Glossary — Code → Nhãn UI

> Bảng thuật ngữ ánh xạ **mã trong code** (enum/type/key) → **nhãn hiển thị trên UI**.
> Mọi nhãn dưới đây được **trích từ `aspire-react/frontend/src/` thật** (không tự đặt).
> Khi viết tài liệu, dùng đúng nhãn ở đây; nếu thiếu → grep `frontend/src` rồi bổ sung.

## 1. Trạng thái (status)

| Code (enum/type) | Giá trị | Nhãn UI | Nguồn |
|---|---|---|---|
| `AssetStatus` | `Pending` | Chờ cấp phát | `features/asset/types/asset.ts:19` |
| `AssetStatus` | `Deployed` | Đã cấp phát (list) / Đang sử dụng (lifecycle detail) | `asset.ts:19` + `features/asset/pages/AssetDetailPage.tsx:34` |
| `AssetStatus` | `Archived` | Đã thu hồi (list) / Đã lưu trữ (lifecycle) | `asset.ts:19` + `AssetDetailPage.tsx:35` |
| `ComponentUnitStatus` | `InStock` | Trong kho | `features/component/pages/ComponentDetailPage.tsx:21` |
| `ComponentUnitStatus` | `Allocated` | Đã cấp phát | `ComponentDetailPage.tsx:22` |
| `ComponentUnitStatus` | `Damaged` | Hư hỏng | `ComponentDetailPage.tsx:23` |
| `ComponentUnitStatus` | `Disposed` | Đã loại bỏ | `ComponentDetailPage.tsx:24` |

## 2. Đối tượng nhận / cấp phát (allocation target)

| Code | Giá trị | Nhãn UI | Nguồn |
|---|---|---|---|
| `AllocationTargetType` | `User` | Người dùng | `features/asset/types/asset.ts:31` |
| `AllocationTargetType` | `Department` | Phòng ban | `asset.ts:31` |
| `AllocationTargetType` | `SystemPosition` | Hệ thống | `asset.ts:31` |
| `checkoutType` (Accessory) | `User` = 1 | Người dùng | `features/accessory/services/accessories.service.ts:70` |
| `checkoutType` | `Department` = 2 | Phòng ban | `accessories.service.ts:70` |
| `checkoutType` | `Location` = 3 | Vị trí | `accessories.service.ts:70` |
| `checkoutType` | `SystemPosition` = 4 | Hệ thống | `accessories.service.ts:70` |

## 3. Loại hành động ActionLog (`actionTypeValue`)

| Code (số) | Nhãn UI | Nguồn |
|---|---|---|
| 1 | Tạo mới | `shared/components/ActionLogTable.tsx:34` |
| 2 | Cập nhật | `ActionLogTable.tsx:35` |
| 3 | Xóa | `ActionLogTable.tsx:36` |
| 4 | Cấp phát | `ActionLogTable.tsx:37` |
| 5 | Thu hồi | `ActionLogTable.tsx:38` |
| 6 | Kiểm kê | `ActionLogTable.tsx:39` |
| 7 | Import | `ActionLogTable.tsx:40` |
| 8 | Export | `ActionLogTable.tsx:41` |
| 9 | Chấp nhận | `ActionLogTable.tsx:42` |
| 10 | Từ chối | `ActionLogTable.tsx:43` |
| 11 | Xác nhận | `ActionLogTable.tsx:44` |
| 12 | Lưu trữ | `ActionLogTable.tsx:45` |
| 13 | Mở lại | `ActionLogTable.tsx:46` |
| 14 | Cập nhật bị từ chối | `ActionLogTable.tsx:47` |
| 15 | Nhập kho | `ActionLogTable.tsx:48` |
| 16 | Đánh dấu hỏng | `ActionLogTable.tsx:49` |
| 17 | Loại bỏ | `ActionLogTable.tsx:50` |

## 4. Route → nhãn menu (sidebar)

| Route (key) | Nhãn UI | Nguồn |
|---|---|---|
| `/assets` | Tài sản | `App.tsx:239` |
| `/consumables` | Vật tư tiêu hao | `App.tsx:186` |
| `/components` | Linh kiện | `App.tsx:187` |
| `/accessories` | Phụ kiện | `App.tsx:188` |
| `/licenses` | Bản quyền | `App.tsx:238` |
| `/maintenances` | Bảo trì | `App.tsx:240` |
| `/users` | Người dùng | `App.tsx:248` |
| `/groups` | Nhóm | `App.tsx:249` |
| `/permissions` | Phân quyền | `App.tsx:250` |
| `/admin/categories` | Danh mục | `App.tsx:192` |
| `/admin/manufacturers` | Nhà SX | `App.tsx:193` |
| `/admin/suppliers` | Nhà cung cấp | `App.tsx:194` |
| `/admin/asset-models` | Asset Models | `App.tsx:195` |
| `/admin/locations` | Địa điểm | `App.tsx:196` |
| `/admin/companies` | Công ty | `App.tsx:199` |
| `/admin/departments` | Phòng ban | `App.tsx:200` |
| `/maintenance/templates` | Template bảo dưỡng | `App.tsx:241` |
| `/maintenance/campaigns` | Đợt bảo dưỡng | `App.tsx:242` |

## 5. Cột bảng (một số ví dụ phổ biến)

| `dataIndex`/key | Nhãn UI | Nguồn |
|---|---|---|
| `code` (SystemInfo/Position) | Mã hệ thống / Mã vị trí | `features/admin/pages/SystemInfoListPage.tsx:122,157` |
| `name` (Department) | Tên phòng ban | `features/admin/pages/DepartmentListPage.tsx:132` |
| `name` (Supplier) | Tên NCC | `features/admin/pages/SupplierListPage.tsx:179` |
| `name` (Location) | Tên địa điểm | `features/admin/pages/LocationListPage.tsx:281` |
| `checkedOutAt` (Accessory) | Ngày cấp | `features/accessory/pages/AccessoryDetailPage.tsx:76` |
| `assignedQty` | Đã cấp | `AccessoryDetailPage.tsx:82` |
| `returnedQty` | Đã thu | `AccessoryDetailPage.tsx:84` |

## Ghi chú

- Đường dẫn ở cột "Nguồn" là **tương đối so với `aspire-react/frontend/src/`**.
- Bảng này là **đại diện** (không đầy đủ). Khi cần nhãn mới → grep nhãn thật trong
  `aspire-react/frontend/src` rồi bổ sung vào đây, kèm file + dòng.
- Một số khái niệm có **2 nhãn** tùy ngữ cảnh (VD `Deployed` = "Đã cấp phát" ở list nhưng
  "Đang sử dụng" ở lifecycle detail) — ghi rõ ngữ cảnh, không gộp chung.
