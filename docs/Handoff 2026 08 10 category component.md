# Handoff: Thêm Category cho Component
_Ngày: 2026-08-10 — Trạng thái: hoàn tất, build 0 lỗi, test 19/19 pass, đã validate end-to-end trên server thật. **Chưa được người dùng tự kiểm tra lại.**_

---

## 1️⃣ Kết quả kiểm tra trước khi code

**Kết luận: `Category` ĐÃ TỒN TẠI sẵn và dùng chung cho nhiều entity — KHÔNG tạo mới bảng/API.**

| Kiểm tra | Kết quả |
|---|---|
| Entity `Category` | ✅ Đã có (`Domain/Entities/Category.cs`) |
| Enum `CategoryType` | ✅ Đã có sẵn **`Component = 4`** (cùng Asset=1, Consumable=2, Accessory=3, License=5) |
| Bảng `categories` | ✅ Đã tồn tại (EnsureCreated) |
| API `/api/v1/categories` | ✅ Đã có trong `AdminController` (GET/POST/PUT/DELETE) — nhưng **chưa có filter `type`**, **chưa có delete-guard**, **chưa ghi ActionLog** |
| `Component.CategoryId` + nav `Category` | ✅ Đã có từ trước (entity + FK trong `AppDbContext` + projection trong `ComponentsController`) |
| `CreateComponentRequest` | ✅ Đã có tham số `CategoryId` |

→ **Quyết định:** tái sử dụng toàn bộ, chỉ **nâng cấp** (thêm filter `type`, bắt buộc category khi tạo, delete-guard, ActionLog, FK → RESTRICT, frontend).

## 2️⃣ Backend

### Entity / Model / Migration
- `Domain/Enums/ItemType.cs`: thêm `Category = 7` (để ghi ActionLog cho Category).
- `Infrastructure/Persistence/AppDbContext.cs`: `Component.Category` FK đổi `SetNull` → **`Restrict`** + thêm `HasIndex(CategoryId)`.
- `Program.cs` (raw SQL self-heal): `CREATE TABLE IF NOT EXISTS categories` (an toàn) + `ALTER TABLE components ADD COLUMN IF NOT EXISTS "CategoryId" uuid` + `CREATE INDEX IX_components_CategoryId` + **recreate FK `ON DELETE RESTRICT`**.
- `docs/sql/migration_component_category.sql` **(mới)**: UP/DOWN + seed mẫu (RAM/Ổ cứng/Cáp/Phụ kiện khác).

### API (AdminController — nâng cấp)
- `GET /api/v1/categories?type=Component` — **filter theo `CategoryType`** (trả đúng danh mục Component, không lẫn Asset/Accessory).
- `POST/PUT/DELETE /categories` — **ghi ActionLog** (`ItemType.Category`) khi tạo/sửa/xóa.
- `DELETE /categories/{id}` — **delete-guard**: chặn nếu Category đang được `Components` (kể cả soft-delete), `AssetModel`, `Consumables`, `Accessories`, `Licenses` tham chiếu → `400 {"error_code":"CATEGORY_IN_USE"}`.

### API (ComponentsController)
- `POST /components` — **bắt buộc `CategoryId`** → `400 CATEGORY_REQUIRED`; validate category phải thuộc loại Component → `400 INVALID_CATEGORY`.
- `GET /components` — thêm param `uncategorized` để lọc "Chưa phân loại".

### 🐛 2 bug quan trọng phát hiện & sửa qua end-to-end test
> **Lưu ý: bug #1 tồn tại từ task Serial/Bulk trước, chỉ được phát hiện ở task này.**

1. **`GetCurrentUserId()` đọc `sub` (Keycloak UUID) ≠ local user id** → ghi ActionLog bị FK violation 500.
   - Sửa ở cả 3 controller: `AdminController`, `ComponentsController`, `ComponentUnitsController`.
   - Fix: ưu tiên claim **`local_user_id`** (được JIT provisioning gắn), fallback `sub`.
2. **`BeginTransactionAsync` trực tiếp fail với `NpgsqlRetryingExecutionStrategy`** — exception "does not support user-initiated transactions".
   - Sửa `Create` + `RunTransactional`.
   - Fix: theo đúng pattern có sẵn của codebase (`CheckoutAssetCommand`) — bọc trong `strategy.ExecuteAsync<IActionResult>(async () => ...)`.

## 3️⃣ Frontend

| File | Thay đổi |
|---|---|
| `ComponentFormPage.tsx` | Select Category (searchable, bắt buộc `*`) cạnh field Name + quick-add ("+ Thêm" trong `dropdownRender`, POST `/categories` với `categoryType: 4` rồi tự chọn); edit mode set `categoryId` từ response |
| `ComponentListPage.tsx` | Cột "Danh mục" (tag tên hoặc tag `warning` "Chưa phân loại"); filter dropdown category + option "Chưa phân loại" (gửi `uncategorized=true`) |
| `ComponentDetailPage.tsx` | Mục "Danh mục" hiển thị tag hoặc "Chưa phân loại" |
| `CategoryListPage.tsx` | Thêm filter theo loại (Asset/Consumable/Accessory/Component/License) — tái sử dụng trang quản lý danh mục sẵn có, không tạo trang riêng |

## 4️⃣ Tests — 19/19 PASS (14 cũ + 5 mới)

`aspire-react.Tests/CategoryAndComponentTests.cs` (mới):
- `CreateComponent_WithoutCategory_RejectedWithCategoryRequired` — 400 CATEGORY_REQUIRED
- `CreateComponent_WithNonComponentCategory_Rejected` — 400 INVALID_CATEGORY
- `DeleteCategory_InUseByComponent_RejectedWithCategoryInUse` — CATEGORY_IN_USE + category còn tồn tại
- `DeleteCategory_Unused_Succeeds_AndLogsDelete` — xóa thành công + ActionLog Delete được ghi
- `GetCategories_ByTypeComponent_ReturnsOnlyComponentCategories` — filter đúng, không lẫn Asset

> Ghi chú kỹ thuật: anonymous type internal → `dynamic` không đọc được qua assembly boundary → test round-trip qua `JsonSerializer` với `JsonSerializerDefaults.Web` (khớp contract camelCase của API thật).

## 5️⃣ Validation & Changelog

- `dotnet build` (aspire-react.sln): 0 errors · `dotnet test`: 19/19 pass · frontend: các file sửa `tsc` sạch (lỗi còn lại là có sẵn từ trước).
- **End-to-end trên server thật (đã restart)**: tạo category Component ✅ · `?type=Component` filter ✅ · tạo Component Bulk kèm category ✅ (hết 500) · xóa category đang dùng → CATEGORY_IN_USE ✅ · tạo Component Serial + 2 serial ban đầu → QTY=2 ✅.

### ⚠️ RULE: EF CORE DATABASE SYNC
Schema đã thay đổi: thêm `components."CategoryId"` (uuid, index `IX_components_CategoryId`, FK → `categories.Id` **ON DELETE RESTRICT**). Project không dùng `dotnet ef`.

**Cách áp dụng đúng (đã làm):** raw SQL self-heal trong `Program.cs` tự chạy khi server start; script thủ công tại `docs/sql/migration_component_category.sql`. Server đã được restart qua Aspire (port hiện tại ~54645 — có thể đổi sau lần restart tiếp theo).

### File tạo mới
- `docs/sql/migration_component_category.sql`
- `aspire-react.Tests/CategoryAndComponentTests.cs`

### File sửa
- `Domain/Enums/ItemType.cs`
- `Infrastructure/Persistence/AppDbContext.cs`
- `Web/Controllers/AdminController.cs`
- `Web/Controllers/ComponentsController.cs`
- `Web/Controllers/ComponentUnitsController.cs` (sửa `GetCurrentUserId`)
- `Program.cs`
- `frontend/src/pages/ComponentFormPage.tsx`
- `frontend/src/pages/ComponentListPage.tsx`
- `frontend/src/pages/ComponentDetailPage.tsx`
- `frontend/src/pages/admin/CategoryListPage.tsx`

> Lưu ý: `CreateComponentRequest` đã có sẵn `CategoryId` nên frontend gửi `categoryId` là khớp. Dữ liệu Component cũ chưa phân loại vẫn hoạt động (CategoryId nullable ở DB; UI hiện tag "Chưa phân loại" + filter riêng để admin cập nhật dần).

---

## ✅ Checklist kiểm tra lại (chưa làm — để mai làm)

- [ ] `git status` / `git diff` — đối chiếu đúng danh sách file đã đổi ở trên.
- [ ] Restart Aspire AppHost, xem log raw SQL self-heal chạy không lỗi.
- [ ] `dotnet test` lại trên máy mình (không chỉ tin báo cáo agent).
- [ ] Test tay: tạo Component Bulk có category.
- [ ] Test tay: tạo Component Serial có category + nhập vài serial.
- [ ] Test tay: xóa 1 category đang dùng → xác nhận báo lỗi `CATEGORY_IN_USE` đúng như mô tả.
- [ ] Test tay: filter "Chưa phân loại" trong danh sách Component hoạt động đúng với dữ liệu cũ.
- [ ] Kiểm tra kỹ 2 bug đã sửa (`GetCurrentUserId`, `NpgsqlRetryingExecutionStrategy`) — vì bug #1 từng tồn tại âm thầm ở task trước mà không bị phát hiện ngay, nên đáng để test thêm các luồng ActionLog khác (Serial/Bulk cũ) xem có bị ảnh hưởng phụ không.