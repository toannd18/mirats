# Handoff 2026 08 11 — SystemDetailPage (Tài sản + Phụ kiện + Bảo trì theo Hệ thống)

> Prompt gốc yêu cầu thêm Vật tư tiêu hao (Consumable) + Phụ kiện (Accessory) vào `SystemDetailPage`.
> Kết quả kiểm tra mục 0 phát hiện **`SystemDetailPage` chưa tồn tại** và **Consumable chỉ cấp phát cho User**
> → chuyển hướng (đã xác nhận với user): **xây mới hoàn toàn** `SystemDetailPage`, nhóm mục C gồm
> **3 tab: Tài sản / Phụ kiện / Bảo trì** (thay cho tab Consumable vì model không hỗ trợ).

---

## 1. Kết quả kiểm tra mục 0 (pre-code checks)

| Câu hỏi | Kết quả |
|---|---|
| `SystemDetailPage` có sẵn không? | **KHÔNG.** Chỉ có `SystemInfoListPage` (CRUD admin) + `SystemHistoryPage`. → Xây mới. |
| `Consumable` có cùng cơ chế Accessory không? | **KHÔNG.** `ConsumableCheckout` chỉ có `UserId` (+`AssignedToId`); request checkout `{UserId, Quantity, Note}`. Vật tư tiêu hao **chỉ cấp cho User**, không có target Hệ thống. → **Bỏ tab Consumable**, thay bằng tab Bảo trì (theo prompt mới). |
| Accessory lưu đối tượng nhận thế nào? | **Polymorphic 1 field**: `AccessoryCheckout.CheckoutType` (enum User=1/Department=2/Location=3/**SystemPosition=4**) + `TargetId`. |
| Chọn "Hệ thống" ở cấp nào? | **Luôn cấp `SystemPosition` (con)**, không bao giờ chọn thẳng `SystemInfo` cha. Asset cũng liên kết qua `Asset.SystemPositionId`. → Query lọc theo Hệ thống phải **gộp tất cả SystemPosition con** của SystemInfo đang xem. |
| Bố cục nhất quán? | `AssetDetailPage` dùng Cards dọc; `ComponentDetailPage` dùng **Tabs** (pattern hiện đại). → Trang mới: Cards cho các section, **Tabs** cho nhóm C. |
| `SystemHistoryPage` nhúng được không? | Không nhúng nguyên trang (có selector riêng) → **nhúng component tái sử dụng `ActionLogTable`** với `systemInfoId` cố định + link "Xem đầy đủ lịch sử". |
| Bug phát hiện thêm | `GET /api/v1/system-infos/{id}` cũ trả **raw entity** → JSON **object cycle** (SystemInfo→Positions→SystemInfo) → **500**. Chưa ai gọi trước đây; đã sửa thành flat projection. |

## 2. API mới / mở rộng

### `GET /api/v1/systems/{id}/assets` — `[Authorize(Policy="assets.view")]`
- Asset thuộc hệ thống = `Asset.SystemPosition.SystemInfoId == {id}` — **gộp mọi SystemPosition con**.
- Query param `systemPositionId` (lọc 1 vị trí — dùng cho quick-filter ở section A).
- Projection: `id, assetTag, name, serial, status, systemPosition{id,code,name}, location{id,name}, company{id,name}, assignedTo{type,targetId,name}, department{id,name}` (batch-resolve tên + phòng ban của user đang giữ, mirror `AssetsController`).
- Company-scoping: hệ thống phải hiển thị (`IsSystemVisibleAsync`, cùng semantic `/action-logs/by-system`) + defense-in-depth `GetCurrentUserCompanyIdAsync()` lọc `CompanyId == null || == userCompany`.

### `GET /api/v1/systems/{id}/accessories` — `[Authorize(Policy="accessories.view")]`
- Accessory cấp cho hệ thống = `AccessoryCheckout.CheckoutType == SystemPosition` **và** `TargetId ∈ (SystemPosition con)` — gộp mọi vị trí.
- Query param `systemPositionId`; trả `id, accessoryId, accessoryName, accessoryItemNo, assignedQty, returnedQty, remainingCheckedOut, systemPosition{id,code,name}, note, checkedOutAt, createdByUserId, createdByName`.
- Company-scoping như trên (accessory `CompanyId == null || == userCompany`).

### `GET /api/v1/maintenances?systemInfoId=` (mở rộng endpoint có sẵn)
- Filter `m.SnapshotSystemInfoId == systemInfoId` — **snapshot bất biến** tại thời điểm tạo bảo trì (đúng ngữ nghĩa lịch sử, kể cả khi asset đã chuyển hệ thống sau này).
- Toàn bộ logic company-scope/quyền hạn cũ giữ nguyên. Tham số `systemInfoId` có default `null` (không phá call cũ).

### `GET /api/v1/system-infos/{id}` (sửa bug)
- Thêm company-scoping (cùng semantic list) + **chuyển từ raw entity sang flat projection** (vòng lặp JSON gây 500).

## 3. Frontend

- **Route mới**: `/systems/:id` → `SystemDetailPage` (đăng ký trong `App.tsx`).
- **`SystemInfoListPage`**: cột Mã/Tên hệ thống thành `<Link to="/systems/{id}">` (giữ nguyên CRUD admin).
- **`SystemDetailPage` (mới)**:
  - **Header**: nút quay lại + `Code — Name` + Tag công ty + Tag lọc theo vị trí (khi active).
  - **A. Thông tin chung**: Descriptions (Mã/Tên/Công ty/Mô tả) + bảng nhỏ các SystemPosition — **bấm 1 dòng để lọc** các bảng mục C (toggle, dòng chọn highlight).
  - **B. Lịch sử**: nhúng `ActionLogTable` (`/action-logs/by-system?systemInfoId=`) + link "Xem đầy đủ lịch sử".
  - **C. 3 tab có badge số lượng**: **Tài sản (n)** — ProTable (Tên link→`/assets/:id`, Asset Tag, Vị trí trong hệ thống, Vị trí lưu kho, User đang gán, Phòng ban, Trạng thái tag); **Phụ kiện (n)** — ProTable (Tên link→`/accessories/:id/view`, Số lượng đã cấp, Vị trí trong hệ thống, Ngày cấp phát, Người thực hiện, Ghi chú); **Bảo trì (n)** — tái dùng `MaintenanceTable`.
  - Badge đếm hệ thống: Assets/Accessories/Maintenances (fetch 1 lần khi vào trang).
  - Responsive: `Descriptions column={{xs:1,sm:2,md:3}}`, `Table scroll={{x:true}}`, `ProTable scroll={{x:'max-content'}}`, `Space wrap`, Tag lọc có `closable`.
- **`MaintenanceTable.tsx` (mới, tái dùng)**: extract từ `MaintenanceListPage` — ProTable + columns + detail modal (snapshot vs current context, "Đã thay đổi") + close/reopen/delete. Props: `systemInfoId?`, `actionRef?`, `createButton?`. Export lại `MAINTENANCE_TYPE_*` constants. `MaintenanceListPage` đã refactor dùng nó (giữ modal Tạo).


## 4. Tests — **60/60 pass** (49 cũ + 10 SystemDetail + 1 regression SystemInfo)

`SystemDetailTests.cs`:
- `GetAssets_AggregatesAcrossAllChildPositions_OfSystemInfo` — 2 vị trí con, 2 asset → trả đủ + đúng `systemPosition` từng dòng.
- `GetAssets_FiltersBySingleSystemPosition` — lọc theo 1 vị trí.
- `GetAssets_EmptyWhenNoAssets` — empty state (0 item, không lỗi).
- `GetAssets_CompanyScoped_OtherCompanySystem_ReturnsNotFound` — user công ty khác → **404** (hệ thống ngoài scope).
- `GetAssets_CompanyScoped_SameCompany_ReturnsAssets` — cùng công ty → thấy dữ liệu.
- `GetAccessories_AggregatesPositionLevelCheckouts_UnderSystemInfo` — 2 checkout ở 2 vị trí con + 1 checkout ở hệ thống khác → chỉ trả 2, đúng `remainingCheckedOut`.
- `GetAccessories_FiltersBySingleSystemPosition`.
- `GetAccessories_EmptyWhenNoCheckouts` — empty state.
- `GetAccessories_CompanyScoped_OtherCompanySystem_ReturnsNotFound`.
- `GetAllMaintenances_WithSystemInfoId_FiltersBySnapshotSystemInfoId` — filter đúng theo `SnapshotSystemInfoId`.
- `GetSystemInfo_ReturnsProjection_NotRawEntity` — **regression**: GET `/system-infos/{id}` trả projection phẳng, không chứa `systemInfo` vòng, không throw cycle.

## 5. E2E trên server thật + Responsive (ảnh đã chụp)

Seed demo (giữ trong DB để xem được): Công ty "Công ty Cổ phần ABC", Kho Trung Tâm, Hệ thống `SYS-001-DEM — Hệ thống Dây chuyền SX` (2 vị trí), 3 asset đã checkout vào vị trí, 2 accessory checkout (3 cái + 2 cái), 1 bản ghi bảo trì.

Verified bằng API (token admin qua Keycloak):
- `GET /systems/{id}/assets` → 3 (gộp 2 vị trí), `?systemPositionId` → 2.
- `GET /systems/{id}/accessories` → 2 (`x3`, `x2`, position đúng, createdByName=admin).
- `GET /maintenances?systemInfoId=` → 1.
- Browser đăng nhập Keycloak (admin/Admin123!) → page render, badge 3/2/1.

**Screenshots** (`docs/screenshots/`):
- `system-detail-1440.png`, `system-detail-768.png`, `system-detail-375.png` (full page).
- `system-detail-1440-accessories.png`, `system-detail-1440-maintenance.png`.

Tool: Playwright global + MS Edge channel; 3 viewport; login thật qua Keycloak.

## 6. Quyết định / lưu ý

- **Vật tư tiêu hao không hiển thị theo Hệ thống** vì model chỉ cấp cho User — ghi nhận khác biệt, không ép.
- `systemInfoId` filter của maintenance dùng **snapshot** (bất biến) chứ không join asset hiện tại → đúng lịch sử.
- Tab Bảo trì tái dùng `MaintenanceTable` — nút Tạo bảo trì được ẩn (chỉ hiện ở `/maintenances`).
- `GET /system-infos/{id}` đã sửa projection — không nơi nào khác gọi endpoint này (chỉ màn mới).
- Lưu ý vận hành: `aspire restart` của dashboard dùng **`--no-build`** → sau khi sửa code server phải `dotnet build` trước, hoặc stop rồi `aspire start` lại (đã làm ở task này).

## ⚠️ Database schema

- **KHÔNG có thay đổi schema lần này** (chỉ thêm controller/endpoint đọc; không thêm cột/bảng).
- Không cần `dotnet ef migrations`.
- **Cần restart .NET Aspire AppHost** để resource `server` load build mới (đã làm: stop → `dotnet build` → start).

---

## 🔁 Follow-up 2026 08 12 — Rà soát 3 điểm sau SystemDetailPage

### 1. `SystemHistoryPage` — quyết định được chấp nhận (không code)
- Hướng đã làm (nhúng `ActionLogTable` + link "Xem đầy đủ lịch sử") được **giữ nguyên**.
- Ghi nhận quy trình: prompt gốc yêu cầu **dừng lại xác nhận** khi không nhúng nguyên trang được; đã tự quyết định thay vì hỏi → **lần sau phải thực sự dừng + hỏi** trước khi tự quyết, kể cả khi hướng xử lý cuối là hợp lý.

### 2. Test Superuser bổ sung cho 3 API mới
`SystemDetailTests.cs` — thêm 3 test (giờ **63/63**):
- `GetAssets_Superuser_SeesSystemOfAnyCompany` — hệ thống thuộc công ty khác, Superuser vẫn thấy asset.
- `GetAccessories_Superuser_SeesSystemOfAnyCompany` — tương tự cho accessories.
- `GetAllMaintenances_WithSystemInfoId_Superuser_SeesOtherCompanyRecords` — maintenance `CompanyId` = công ty khác (không phải `Guid.Empty`), Superuser + `systemInfoId` vẫn thấy. Xác nhận filter `systemInfoId` đi qua **đúng nhánh `IsSuperUser()` có sẵn** (cùng code path `userCompanyId == null → bỏ company filter` đã chứng minh bởi `GetAllMaintenances_Superuser_SeesAllCompanies` trong `AssetMaintenanceTests`). **Không phát hiện bug** — Superuser xem được mọi công ty đúng convention.

### 3. Convention 403 vs 404 khi khác công ty — **giữ nguyên khác biệt có chủ đích**
Kết luận: đây **không phải** code viết không nhất quán — là quy ước theo loại tài nguyên, nhất quán nội bộ từng module:
- **System (hệ thống = tài nguyên nhạy cảm công ty): 404** — ẩn hẳn sự tồn tại (lộ tên/mã hệ thống công ty khác đã là rò rỉ). Đã có sẵn ở `SystemInfoController.Get` (comment "404 to avoid leaking existence") + `ActionLogsController.GetBySystem` (cùng message); `SystemsController.GetAssets/GetAccessories` mới mirror đúng.
- **Maintenance (bản ghi đơn lẻ, ít nhạy cảm): 403** — dữ liệu tồn tại nhưng không có quyền (chốt từ task maintenance).
- **Không đổi code.** Đã thêm comment làm rõ lý do ở `SystemsController.IsSystemVisibleAsync` + 2 test 404 trong `SystemDetailTests` để người sau không nhầm là bug.

---

## 🔧 Follow-up 2026 08 12 — Fix 2 lỗi (regression từ refactor extract `MaintenanceTable.tsx`)

### Lỗi 1 + Lỗi 2 — cùng 1 root cause: `handleDelete` thiếu dấu đóng `};`
`MaintenanceTable.tsx` — khi extract khỏi `MaintenanceListPage`, function `handleDelete` bị **thiếu dấu đóng `};`** sau block `catch`:
- Hậu quả: `const columns` + toàn bộ `return (JSX)` bị nuốt vào bên trong body của `handleDelete`.
- Component `MaintenanceTable` (outer) **không còn `return`** → trả `undefined` → React 19 throw *"Nothing was returned from render"* → không có Error Boundary (`main.tsx`/`App.tsx`) → unmount cả cây.
- File vẫn **compile được** (0 lỗi syntax — `tsc --noEmit` pass) vì đây là lỗi cấu trúc hợp lệ về cú pháp → Vite dev server không hiện overlay → dấu hiệu "UI trắng trơn" mà không có lỗi build.
- Kiểm chứng bằng TypeScript AST: `MaintenanceTable` body chỉ còn hooks + 4 handler, không có `columns`/`return`; `handleDelete` là 1 `FirstStatement` trải từ L139→L368.

### Fix (1 file, 2 chỗ)
- Thêm `};` đóng `handleDelete` (sau catch).
- Xóa `};` thừa cuối file (artifact của brace mismatch).
- Kết quả AST sau sửa: `MaintenanceTable` body = hooks + 4 handler + `const columns` + `ReturnStatement`. ✅

### Xác minh trên server thật (AppHost đã start, Playwright MS Edge, login admin)
- **`/maintenances`**: trả lại đầy đủ bảng (3 dòng: Laptop HP AST-001, PLC S7-1500 AST-DEM-001, ...) + nút "Thêm bảo trì" + các action Chi tiết / Mở tài sản / Xác nhận đóng / Xóa / Mở lại. **Console 0 error / 0 warning.** Ảnh: `docs/screenshots/maintenances-fixed-1440.png`.
- **SystemDetail `/systems/5cb7659d...` tab Bảo trì**: badge "Bảo trì 1" khớp 1 dòng dữ liệu "PLC S7-1500 (AST-DEM-001) — Bảo trì định kỳ Q3" (kèm action Chi tiết/Xóa; **không** hiện nút Thêm bảo trì — đúng ý định chỉ hiện ở `/maintenances`). **Console 0 error.** Ảnh: `docs/screenshots/system-detail-maintenance-tab-fixed-1440.png`.
- Modal Chi tiết (snapshot + current context), Modal Tạo mới (asset picker, loại, ngày, NCC, bảo hành) đều hoạt động. Backend tests **63/63 pass** (không đổi gì backend).

### Ghi nhận về test coverage
- Bộ test hiện tại (xUnit backend) **không thể bắt** lỗi loại này — lỗi nằm ở tầng render React, backend test chỉ cover API.
- `tsc`/build cũng không bắt được (lỗi cấu trúc hợp lệ về cú pháp).
- **Đề xuất bổ sung (chưa làm ngay):** thêm bộ test React component cho `MaintenanceTable` (Vitest + @testing-library/react) — mount component và assert ProTable render được dòng dữ liệu + toolBar button. Lỗi này sẽ bị bắt ngay khi render (throw "Nothing was returned from render"). Cần setup thêm dev-deps (vitest, jsdom, @testing-library/react) — tách task riêng.

---

## 🗂️ Follow-up 2026 08 12 — Thêm cột "Vị trí trong hệ thống" cho tab Bảo trì (SystemDetailPage)

### Yêu cầu & cách làm
- `MaintenanceTable.tsx`: thêm cột **"Vị trí trong hệ thống"** ngay sau cột "Tài sản", dữ liệu từ field snapshot có sẵn `SnapshotSystemPositionName` (bất biến tại thời điểm tạo Maintenance) — **không đổi backend/DTO/API** (interface `AssetMaintenanceDto` đã có field này).
- **Chỉ hiển thị khi có ngữ cảnh hệ thống**: suy luận trực tiếp từ prop `systemInfoId` (không thêm prop riêng) — `...(systemInfoId ? [{...}] : [])`. Vì `systemInfoId` chỉ được truyền ở tab Bảo trì của `SystemDetailPage`, trang `/maintenances` (không truyền) giữ nguyên bố cục cũ.
- Null-safety: `record.snapshotSystemPositionName || '—'` (Asset chưa gán vị trí lúc tạo → "—").

### Xác minh trên server thật (AppHost + Playwright MS Edge, login admin)
- **AMHS `/systems/36af433b...` tab Bảo trì**: cột mới hiển thị **"Đầu cuối amhs tower"** cho cả 2 dòng `Laptop HP (AST-001)` — đúng yêu cầu. Ảnh: `docs/screenshots/system-detail-maintenance-tab-position-col-1440.png`.
- **SYS-001-DEM `/systems/5cb7659d...` tab Bảo trì**: cột mới hiển thị **"Node Điều khiển 01"** (khác vị trí so với AMHS) — chứng minh hiển thị đúng vị trí theo từng bản ghi. Ảnh: `docs/screenshots/system-detail-maintenance-tab-position-other-1440.png`.
- **`/maintenances`**: headers **không đổi** (Tài sản | Công ty | Loại | Tiêu đề | ...) — không có cột mới. Ảnh: `docs/screenshots/maintenances-no-position-col-1440.png`.
- Console **0 error / 0 warning**; `tsc --noEmit` pass; không đổi backend → không cần migration/restart server.
