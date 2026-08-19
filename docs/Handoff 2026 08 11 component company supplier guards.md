# Handoff: Component — Company/Location/Supplier/Delete guard + UI Modal refactor
_Ngày: 2026-08-11 — Trạng thái: backend test 29/29 pass · frontend tsc sạch (chỉ còn TS6133 pre-existing) · validate end-to-end trên server thật._

---

## 1️⃣ Sửa lỗi ưu tiên: FK `component_units` bị thiếu (pre-existing bug)

**Nguyên nhân:** dòng 355-356 `Program.cs` dùng `ALTER TABLE component_units ADD CONSTRAINT IF NOT EXISTS ...` — PostgreSQL **không hỗ trợ** `IF NOT EXISTS` cho `ADD CONSTRAINT` → syntax error bị try/catch nuốt âm thầm → FK không bao giờ được tạo.

**Sửa:** đổi sang pattern `DROP CONSTRAINT IF EXISTS` + `ADD CONSTRAINT` (idempotent, giống đoạn Category). **Đã rà soát toàn bộ Program.cs** — không còn `ADD CONSTRAINT IF NOT EXISTS` nào khác (chỉ còn `ADD COLUMN/CREATE TABLE/CREATE INDEX IF NOT EXISTS`, đều hợp lệ trong Postgres).

**Bằng chứng xác minh trực tiếp DB** (query `pg_constraint` sau restart):
```
component_units:  FK_component_units_components_ComponentId  → confdeltype = 'c' (CASCADE) ✅
                  FK_component_units_assets_CurrentAssetId   → confdeltype = 'n' (SET NULL) ✅
```
**Dữ liệu mồ côi:** 0 orphan units, 0 orphan asset refs — không cần xử lý xóa/gán lại.

---

## 2️⃣ Company & Location cho Component

- **Đã tồn tại sẵn** (tái sử dụng 100%): entity `Company`, `Location`; `Component` đã có `CompanyId`/`LocationId` + FK (SET NULL). Không tạo entity mới.
- **Thay đổi:**
  - `CompanyId` **bắt buộc khi tạo** → `400 COMPANY_REQUIRED`; company không tồn tại → `400 INVALID_COMPANY`.
  - FK `components.CompanyId` / `components.LocationId`: **SET NULL → RESTRICT** (DB level, đã xác minh `confdeltype='r'`).
  - Delete-guard API: `CompaniesController.Delete` → `COMPANY_IN_USE`; `AdminController.DeleteLocation` → `LOCATION_IN_USE` (nếu Component tham chiếu).
  - Filter `GET /components`: thêm `companyId`, `locationId`, `uncompanied=true`.
- **Ràng buộc cấp phát cùng công ty** (`ComponentAllocationService.AllocateAsync`, trước khi tìm/trừ tồn kho, cả Bulk + Serial):
  - `Component.CompanyId == null` → `400 COMPONENT_COMPANY_REQUIRED` (chặn lách rule qua dữ liệu cũ).
  - `Component.CompanyId != Asset.CompanyId` → `400 COMPANY_MISMATCH` kèm tên 2 công ty.

## 3️⃣ Giới hạn field sửa (Update endpoint)

- **Khóa cứng** (payload gửi giá trị KHÁC DB → `400 FIELD_LOCKED` liệt kê field): `trackingType`, `categoryId`, `companyId`.
- **Whitelist được phép sửa**: `name`, `notes`, `supplierId`, `manufacturerId`, `modelNumber`, `minAmt`, `locationId` — **+ `orderNumber`, `purchaseCost`, `purchaseDate`** (⚠️ quyết định mở rộng: spec mục 2 không liệt kê 3 field này nhưng mục 3 yêu cầu nhập được trong form; tôi đã thêm vào whitelist — nếu bạn muốn khóa lại hãy báo).
- **Luôn bỏ qua**: `qty`, `serial`, `itemNo` (không lỗi, client cũ vẫn gửi được).
- Frontend edit mode: `Danh mục`, `Công ty`, `TrackingType` hiển thị dạng **Tag tĩnh + tooltip** "Không thể thay đổi sau khi tạo".

## 4️⃣ Supplier / Manufacturer / các field tương tự Accessory

- **Đã tồn tại sẵn**: entity `Supplier` (Code/Name/Url/Address/Phone/Contact...) và `Manufacturer` (Code/Name/Url/Support...) — **tái sử dụng, không tạo mới**.
- **Thêm vào Component**: `SupplierId` (FK SET NULL), `ManufacturerId` (FK SET NULL), `ModelNumber` (text). `MinAmt` = `MinQty` đã có sẵn; `PurchaseCost/PurchaseDate/OrderNumber` đã có sẵn từ trước.
- **Delete-guard**: `DeleteSupplier` → `SUPPLIER_IN_USE`, `DeleteManufacturer` → `MANUFACTURER_IN_USE` (nếu Component/Accessory/Consumable tham chiếu).
- `ComponentFormPage` có quick-add cho Supplier/Manufacturer (dropdownRender, giống Category).
- `ComponentDetailPage`/`ComponentListPage` hiển thị đầy đủ: Công ty, Vị trí, NSX, NCC, ModelNumber, Số đơn hàng, Ngày mua, Giá mua.


## 5️⃣ Delete guard theo lịch sử cấp phát

- `DELETE /components/{id}`: chặn nếu Component (hoặc bất kỳ `ComponentUnit` con) **từng có `ActionType.Checkout`** → `400 COMPONENT_HAS_ALLOCATION_HISTORY` (kể cả đã checkin hết). Không có lịch sử → hard delete + ghi ActionLog Delete.
- `DELETE /component-units/{unitId}` **(thêm mới)**: unit chưa từng Checkout → soft-delete (`DeletedAt`) + giảm `Qty` của Component; đã từng Checkout → `400 COMPONENT_UNIT_HAS_ALLOCATION_HISTORY` (gợi ý dùng Dispose).
- API trả `canDelete` (list + detail + units) → UI **disable nút Xóa + tooltip** giải thích lý do.

## 6️⃣ Schema / self-heal (Program.cs)

Đã thêm khối raw SQL (idempotent):
1. `component_units` FK → DROP + ADD (fix bug mục 1).
2. `components` CompanyId/LocationId FK → DROP + ADD **ON DELETE RESTRICT**.
3. `components` + `SupplierId`/`ManufacturerId`/`ModelNumber` cột + index + FK SET NULL.
4. Index mới: `IX_components_CompanyId`, `IX_components_LocationId`, `IX_components_SupplierId`, `IX_components_ManufacturerId`.

Script thủ công: `docs/sql/migration_component_company_supplier_guards.sql` (UP/DOWN).

## 7️⃣ Tests — 29/29 PASS (19 cũ + 10 mới)

`CategoryAndComponentTests.cs` (+7): `COMPANY_REQUIRED`, `INVALID_COMPANY`, `FIELD_LOCKED`, whitelist+Qty-ignored, `COMPONENT_HAS_ALLOCATION_HISTORY` (chặn/cho phép), `uncompanied` filter.
`ComponentAllocationServiceTests.cs` (+3): `COMPONENT_COMPANY_REQUIRED`, `COMPANY_MISMATCH`, same-company success. (SeedAsync đã gán CompanyId cho component+asset.)

## 8️⃣ Validation end-to-end (server thật)

- `dotnet build` 0 lỗi · `dotnet test` **29/29** · frontend `tsc --noEmit` **0 lỗi**.
- API thật: COMPANY_REQUIRED ✅ · INVALID_COMPANY ✅ · create Bulk có company ✅ · FIELD_LOCKED (đổi companyId) ✅ · update whitelist (qty bị bỏ qua) ✅ · uncompanied filter ✅ · delete không lịch sử ✅ · delete có lịch sử → COMPONENT_HAS_ALLOCATION_HISTORY ✅ · checkout cùng công ty ✅ · COMPANY_MISMATCH ✅ · COMPONENT_COMPANY_REQUIRED ✅.
- DB verify: FK component_units tồn tại (CASCADE/SET NULL) ✅ · components CompanyId/LocationId = RESTRICT ✅ · SupplierId/ManufacturerId/ModelNumber columns ✅ · 0 orphans ✅.

---

## ⚠️ RULE: EF CORE DATABASE SYNC
Schema đã thay đổi (components + SupplierId/ManufacturerId/ModelNumber + FK RESTRICT Company/Location + component_units FK). Project **không dùng `dotnet ef`** — áp dụng bằng raw SQL self-heal trong `Program.cs` (tự chạy khi server start, **đã restart**) + script `docs/sql/migration_component_company_supplier_guards.sql`.

### File tạo mới
- `docs/sql/migration_component_company_supplier_guards.sql`
- `docs/Handoff 2026 08 11 component company supplier guards.md`

### File sửa

---

## 🔟 UI Refactor: ComponentFormPage → ComponentFormModal (2026-08-11)

- **`src/components/ComponentFormModal.tsx` (mới)**: thay trang riêng bằng Modal `width={720}`, `destroyOnClose`, `maskClosable={false}`, footer chứa nút Hủy/Lưu (loading khi submit). Layout `layout="vertical"` + `Row/Col gutter=[16,8]` (`xs=24 sm=12`), nhóm field bằng `Divider titlePlacement="start"` (lưu ý **antd v6 đổi `orientation` thành `titlePlacement`**):
  - Thông tin cơ bản: Tên, Hình thức quản lý (Radio khi tạo; `LockedFieldTag` khi sửa), Danh mục (quick-add; locked tag khi sửa), Serial mẫu.
  - Số lượng / Tồn kho: Bulk → Qty + MinAmt; Serial tạo mới → khối nhập serial trong `Card` (counter + duplicate check); Serial edit → MinAmt + Alert.
  - Vị trí & Công ty: Công ty (TreeSelect + **quick-add company mới** `POST /companies`; locked tag khi sửa), Vị trí.
  - Nhà cung cấp & mua hàng: NSX/NCC (quick-add), ModelNumber, Số đơn hàng, Ngày mua, Giá mua.
  - Ghi chú (full width).
  - **Dirty-check**: đóng modal khi form đã sửa (`isFieldsTouched`) → `modal.confirm` cảnh báo mất dữ liệu.
- **`ComponentListPage.tsx`**: modal điều khiển bằng route (`/components/new` tạo mới, `/components/:id/edit` sửa — deep-link/reload được). Nút Tạo mới/Sửa vẫn `navigate` tới route; đóng/sau khi lưu → `navigate('/components')` + refresh list.
- **`ComponentDetailPage.tsx`**: nút Sửa mở modal inline (`editOpen`); lưu xong → đóng + `refreshAll()` (không điều hướng).
- **`App.tsx`**: `/components/new` và `/components/:id/edit` render `ComponentListPage` (modal đè lên). Xóa import `ComponentFormPage`.
- **Xóa**: `src/pages/ComponentFormPage.tsx` (không còn nơi nào import).

---

## 1️⃣1️⃣ Asset Maintenance (Snipe-IT style) — snapshot ngữ cảnh 2 cấp System (2026-08-11)

**Entity/Enum/migration:**
- `AssetMaintenanceType` (mới): Maintenance/Repair/Upgrade/HardwareSupport/SoftwareSupport/PatTest/Calibration/**IncidentReport** (8 loại).
- `AssetMaintenance` (mới): Type, Title, Notes, SupplierId (tái dùng Supplier), StartDate, CompletionDate (null = đang thực hiện), Cost, IsWarranty + **10 field `Snapshot*`** (SystemInfoId/Name, SystemPositionId/Name — **cả 2 cấp riêng biệt**, LocationId/Name, AssignedUserId/Name, DepartmentId/Name).
- `ItemType.AssetMaintenance = 8` (ghi ActionLog).
- Self-heal `Program.cs` (DROP+ADD, đúng cú pháp) + `docs/sql/migration_asset_maintenances.sql`.

**API** (`AssetMaintenancesController`, route `api/v1`):
- `GET /assets/{assetId}/maintenances` (phân trang, sort StartDate DESC) · `POST /assets/{assetId}/maintenances` (server tự chốt snapshot từ trạng thái asset hiện tại, client không gửi).
- `PUT /maintenances/{id}` — whitelist (Title, Notes, Type, SupplierId, CompletionDate, Cost, IsWarranty); `StartDate` khác → `FIELD_LOCKED`; snapshot fields không nhận (bỏ qua).
- `DELETE /maintenances/{id}` — **chỉ Superuser** (`ICompanyScopeService.IsSuperUser()` → 403 `Forbid()` cho người khác); soft-delete + **bắt buộc ActionLog** kèm toàn bộ nội dung bản ghi đã xóa (cả snapshot) trong `LogMeta` (serialize với `UnsafeRelaxedJsonEscaping` để tiếng Việt đọc được).
- Validate: Title bắt buộc, CompletionDate >= StartDate, Cost >= 0.

**Snapshot** (đúng nguyên tắc ActionLog — ghi cả id + tên hiển thị): SystemPosition + SystemInfo cha (join SystemPosition.SystemInfo), Location, User (từ CurrentAssignment TargetType=User + department của user), Department (từ assignment TargetType=Department). Chốt 1 lần lúc tạo, **bất biến**.

**Frontend:**
- `AssetMaintenanceSection.tsx` (mới) — Card "Bảo trì" trong `AssetDetailPage`: bảng (Loại/Tiêu đề/Bắt đầu/Hoàn thành-tag Đang thực hiện/Chi phí/NCC/Bảo hành) + Badge đếm đang thực hiện + Modal form (create/edit, whitelist) + Modal chi tiết với khối **"Ngữ cảnh tại thời điểm bảo trì"** hiển thị riêng Hệ thống + Vị trí trong hệ thống.
- Nút Xóa **chỉ hiển thị** khi `hasRealmRole('superuser') || hasRealmRole('admin')` (ẩn hẳn cho người khác).
- Supplier quick-add trong form.

**Testing — 37/37 (29 cũ + 8 mới)**: snapshot đủ 2 cấp SystemInfo+SystemPosition ✅ · snapshot bất biến sau khi đổi asset ✅ · FIELD_LOCKED StartDate ✅ · CompletionDate<StartDate bị chặn ✅ · non-superuser DELETE → 403 ✅ · superuser DELETE → thành công + ActionLog kèm nội dung ✅ · IncidentReport hoạt động bình thường ✅.
**E2E server thật** (admin): create → list trả đủ snapshot 2 cấp ✅ · update whitelist ✅ · delete → 3 ActionLog (Create/Update/Delete) + LogMeta đầy đủ nội dung đã xóa ✅.

- **Giữ nguyên toàn bộ logic**: validate bắt buộc, whitelist update, FIELD_LOCKED tránh bằng cách không gửi field khóa, quick-add Category/Company/Supplier/Manufacturer, fix "Đang tải..." (read-only lấy từ `loadedComponent`).
- **Xác minh**: `tsc -b` — các file mới/sửa **0 lỗi** (chỉ còn TS6133 unused pre-existing ở file khác); Vite transform serve đúng module; backend không đổi (test 29/29).
- ⚠️ Chưa chụp được screenshot browser (Playwright install bị treo network) — đã xác minh bằng tsc + Vite transform; nên mở `/components` → Tạo mới để kiểm tra mắt thường.

- `aspire-react.Server/Domain/Entities/Component.cs` (SupplierId, ManufacturerId, ModelNumber + nav)
- `aspire-react.Server/Infrastructure/Persistence/AppDbContext.cs` (Component: FK Company/Location → Restrict, + Supplier/Manufacturer FK, + indexes)
- `aspire-react.Server/Program.cs` (self-heal: fix FK component_units, FK RESTRICT Company/Location, cột + FK mới)
- `aspire-react.Server/Web/Controllers/ComponentsController.cs` (Create: COMPANY_REQUIRED/INVALID_COMPANY + field mới; Update: whitelist + FIELD_LOCKED; GetComponents: filters + canDelete + projection mới; GetComponent: + canDelete; GetUnits: + canDelete; Delete: history guard; records mới)
- `aspire-react.Server/Infrastructure/Services/ComponentAllocationService.cs` (AllocateAsync: COMPONENT_COMPANY_REQUIRED + COMPANY_MISMATCH)
- `aspire-react.Server/Web/Controllers/ComponentUnitsController.cs` (thêm DELETE unit guard)
- `aspire-react.Server/Web/Controllers/AdminController.cs` (delete-guard Supplier/Manufacturer/Location)
- `aspire-react.Server/Web/Controllers/CompaniesController.cs` (delete-guard COMPANY_IN_USE)
- `aspire-react.Tests/CategoryAndComponentTests.cs` (+7 test, request có field mới)
- `aspire-react.Tests/ComponentAllocationServiceTests.cs` (+3 test, SeedAsync có CompanyId)
- `frontend/src/services/components.service.ts` (types mới + deleteUnit)
- `frontend/src/pages/ComponentFormPage.tsx` (Company TreeSelect bắt buộc, Location/Supplier/Manufacturer + quick-add, ModelNumber/OrderNumber/PurchaseDate/PurchaseCost, lock field khi edit)
- `frontend/src/pages/ComponentListPage.tsx` (cột + filter Company/Location, canDelete)
- `frontend/src/pages/ComponentDetailPage.tsx` (hiển thị field mới, canDelete, Xóa unit, checkout lọc cùng công ty)

> Lưu ý: 2 Component cũ (RAM 16GB, SSD 512GB) có `CompanyId = null` — hiển thị tag "Chưa xác định" + filter "Chưa xác định công ty" (`uncompanied=true`); chúng **chưa thể cấp phát** cho tới khi admin gán công ty (API chặn `COMPONENT_COMPANY_REQUIRED`).

---

## 9️⃣ Bugfix sau review: "Danh mục"/"Công ty" treo "Đang tải..." ở form Sửa (2026-08-11)

**Nguyên nhân gốc (frontend):** 2 field read-only (Category/Company) resolve tên bằng cách tìm trong `categoryOptions`/`companyTree` thay vì dùng dữ liệu Component đã load:
1. `companyTree.find(...)` chỉ tìm **top-level** — công ty con nằm lồng trong `children` nên không bao giờ khớp → treo mãi.
2. Khi value null (dữ liệu cũ) → fallback "Đang tải..." thay vì "Chưa phân loại"/"Chưa xác định công ty".
3. Backend **vốn đã trả đủ** `category`/`company` (object `{id, name}`) trong `GET /components/{id}` — không cần sửa backend.

**Sửa (`ComponentFormPage.tsx`):** lưu `loadedComponent` (dữ liệu `GET /components/{id}`) vào state; read-only Tag lấy tên trực tiếp:
- `loadedComponent?.category?.name || 'Chưa phân loại'`
- `loadedComponent?.company?.name || 'Chưa xác định công ty'` (Tag màu warning khi null)
- Xóa 2 `Form.useWatch` không còn dùng. Field vẫn **read-only** (Form.Item không có `name` → không đóng góp vào payload submit → không vướng `FIELD_LOCKED`).

**Xác minh:** `tsc -b` file này 0 lỗi · Vite module transform chứa `loadedComponent` + fallback đúng · backend test component có company → detail trả `company:{id,name}` ✅ · component cũ company=null → hiển thị "Chưa xác định công ty" ✅.

