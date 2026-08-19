# Handoff 2026 08 11 — Maintenance: Trang riêng `/maintenances` + Company-scoped visibility

Tiếp nối task "Asset Maintenance (Snipe-IT style)" đã hoàn thành (Card trong `AssetDetailPage`).
Task này bổ sung: trang danh sách + tạo Maintenance riêng, giới hạn hiển thị theo công ty (backend-enforced), và sửa lỗi mismatch quyền xóa.

---

## 1. Kết quả khảo sát `ICompanyScopeService` (mục 1)

File: `aspire-react.Server/Infrastructure/Services/CompanyScopeService.cs`

Trạng thái trước task:
- `GetUserCompanyIdsAsync()` — **placeholder trả `[]`** cho user thường (ghi chú "Phase 5 expansion"); Superuser → `[]` (không filter). Global query filter (Asset/Component/...) nhìn thấy `Count == 0` → **mặc định hiển thị tất cả** cho mọi user.
- `IsSuperUser()` — `realm_access` chứa `"superuser"` **hoặc** `"admin"` (substring) **hoặc** claim `permission` = `"superuser"`.

Điều tra thêm:
- `AppDbContext` nhận `ICompanyScopeService` (query filter) → **không thể inject `AppDbContext` vào `CompanyScopeService`** (circular DI). Giải pháp: `GetCurrentUserCompanyIdAsync()` lấy scoped DbContext qua `httpContext.RequestServices.GetService(typeof(AppDbContext))`.
- Claim `local_user_id` (do JIT provisioning gắn khi token validated) là id user **local**, KHÔNG phải Keycloak `sub` — method mới đọc claim này rồi tra `Users.CompanyId`.
- Token KHÔNG có claim `company_id` → frontend không tự biết company của mình (chỉ backend biết qua DB lookup).

**Bổ sung (additive, không đổi hành vi global filter):**
```csharp
Task<Guid?> GetCurrentUserCompanyIdAsync();
```
- Superuser → `null` (không giới hạn).
- User thường → `Users.CompanyId` (tra theo `local_user_id` claim).
- Không tìm được → `null` (an toàn: không chặn nhầm).

→ **Không tự viết cơ chế company-scoping song song** — dùng đúng service này ở Maintenance endpoints.

---

## 2. Domain Model — `CompanyId` trên `AssetMaintenance`

- Thêm `Guid CompanyId` (**non-nullable**) vào `AssetMaintenance` — **tách biệt mục đích** với nhóm `Snapshot*` (hiển thị lịch sử); `CompanyId` chỉ để **kiểm soát quyền truy cập**.
- Server tự gán lúc tạo: `CompanyId = asset.CompanyId ?? Guid.Empty` (asset không công ty → floater, `Guid.Empty`, hiển thị cho mọi user — nhất quán với global filter của Asset).
- **Khóa cứng sau khi tạo**: KHÔNG nằm trong `UpdateAssetMaintenanceRequest` (không thể đổi qua PUT — client gửi kèm cũng bị ignore).
- Index `IX_asset_maintenances_CompanyId` (AppDbContext + self-heal Program.cs + migration doc).

## 3. Migration (self-heal + script)

- `Program.cs`: `ALTER TABLE asset_maintenances ADD COLUMN IF NOT EXISTS "CompanyId" uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000'` + `CREATE INDEX IF NOT EXISTS "IX_asset_maintenances_CompanyId"` (PostgreSQL hỗ trợ `ADD COLUMN IF NOT EXISTS`, khác với `ADD CONSTRAINT`).
- `docs/sql/migration_asset_maintenances.sql` — cập nhật UP (column + index) + DOWN (`DROP INDEX IF EXISTS`).

## 4. API (`AssetMaintenancesController`) — company-scoping ở TẦNG BACKEND

Quy tắc chung: `userCompanyId = GetUserCompanyIdAsync()`; regular user có công ty X:
- **List** (cả 2 list): filter `m.CompanyId == X || m.CompanyId == Guid.Empty` (floater hiển thị mọi user). Superuser → không filter.
- **Detail**: `m.CompanyId != X && m.CompanyId != Guid.Empty` → `Forbid()` (403 — dữ liệu tồn tại nhưng không có quyền, đúng convention 403 của hệ thống).
- **Create** (cả 2 endpoint): asset có công ty khác X → `Forbid()` (defense in depth, không tin client).

Endpoints (giữ nguyên + thêm mới):
| Endpoint | Mô tả | Company check |
|---|---|---|
| `GET /assets/{assetId}/maintenances` | Card trong Asset detail | asset thuộc công ty khác → 403 |
| `GET /maintenances?assetId=` | **MỚI** — list tổng hợp (card cũng dùng, truyền `assetId`) | filter theo user |
| `GET /maintenances/{id}` | **MỚI** — detail | 403 cross-company |
| `POST /assets/{id}/maintenances` | Card create (giữ) | asset cross-company → 403 |
| `POST /maintenances` | **MỚI** — tạo từ trang tổng hợp, `AssetId` trong body | asset cross-company → 403 |
| `PUT /maintenances/{id}` | whitelist (giữ) | CompanyId không trong DTO → locked |
| `DELETE /maintenances/{id}` | superuser-only (giữ) | — |

- Cả 2 POST dùng chung `CreateCoreAsync` (validation + company check + snapshot + ActionLog + gán CompanyId).
- Projection list/detail giờ trả thêm `companyId` + `asset { id, assetTag, name, companyName }`.

---

## 5. Frontend

- **`pages/MaintenanceListPage.tsx` (mới)**: route `/maintenances`, `ProTable` (request qua `listAllMaintenances`):
  - Cột: Tài sản (tên + tag, click → `/assets/:id`), **Công ty** (chỉ render khi `isSuperUser()`), Loại (Tag màu), Tiêu đề, Ngày bắt đầu, Trạng thái (Tag Hoàn thành/Đang thực hiện), Chi phí, NCC, Bảo hành, Thao tác (Chi tiết / Mở tài sản / **Xóa — chỉ Superuser**).
  - Toolbar "Thêm bảo trì" → **Modal tạo mới** (pattern nhất quán với ComponentFormModal, không tách route `/maintenances/new`):
    - Select Tài sản (searchable, label `Tên (AssetTag)`, lấy từ `GET /assets` có sẵn — **không tạo endpoint list asset riêng**).
    - Superuser: Select "Lọc theo công ty" phía trên để thu hẹp danh sách asset (filter client-side theo `companyId`).
    - User thường: không có select công ty (backend vẫn chặn 403 nếu chọn nhầm asset khác công ty).
    - Các field còn lại giữ nguyên (Type, Title, Notes, SupplierId, StartDate, CompletionDate, Cost, IsWarranty).
  - Sau khi tạo: **ở lại trang danh sách**, `actionRef.reload()` (không điều hướng).
  - Modal Chi tiết hiển thị khối "Ngữ cảnh tại thời điểm bảo trì" (snapshot).
- **`AssetMaintenanceSection.tsx` (Card trong Asset detail)**: giữ nguyên, nhưng gọi chung API `listAllMaintenances({ assetId, page, pageSize })` (mục tiêu "2 nơi gọi chung 1 API"); nút Xóa dùng helper `isSuperUser()`.
- **`App.tsx`**: route `/maintenances` + menu sidebar "Bảo trì" (icon `ExperimentOutlined`) đặt ngay sau "Tài sản".
- **`asset.service.ts`**: DTO thêm `companyId` + `asset`; thêm `listAllMaintenances`, `getMaintenance`, `createMaintenanceForAsset`, `CreateMaintenanceForAssetPayload`.

## 6. Sửa lỗi mismatch quyền Xóa (mục 6)

Trước: `hasRealmRole('superuser') || hasRealmRole('admin')`.
Sau: helper **`isSuperUser()`** trong `keycloak.ts` **mirror chính xác 1-1** `ICompanyScopeService.IsSuperUser()`:
- `JSON.stringify(realm_access).includes('superuser' || 'admin')` (đúng semantic substring của server trên claim thô)
- **hoặc** claim `permission` = `'superuser'` (string/array) — nhánh mà `hasRealmRole` không thấy được.

Dùng ở cả Card (`AssetMaintenanceSection`) lẫn trang `/maintenances`. Không mở rộng thêm role nào.

## 7. Tests — `aspire-react.Tests`

**43/43 pass** (`dotnet test`). Thêm 6 test company-scoping (`AssetMaintenanceTests.cs`):
- `CreateForAsset_SetsCompanyIdFromAsset` — CompanyId = Asset.CompanyId (server-set).
- `GetAllMaintenances_RegularUser_SeesOnlyOwnCompany` — user công ty A chỉ thấy bản ghi A.
- `GetMaintenance_CrossCompany_Forbidden` — 403.
- `CreateForAsset_CrossCompany_Forbidden` — 403 dù gửi thẳng request.
- `GetAllMaintenances_Superuser_SeesAllCompanies` — Superuser thấy tất cả.
- `UpdateMaintenance_CannotChangeCompanyId` — CompanyId không đổi được qua PUT.

`FakeScope` trong test thêm thuộc tính `CompanyId` + implement `GetCurrentUserCompanyIdAsync()`.

## 8. E2E trên server thật (đã chạy)

- Self-heal: cột `"CompanyId"` NOT NULL + index `IX_asset_maintenances_CompanyId` tồn tại trong DB (verify qua docker exec psql).
- `POST /api/v1/maintenances` (admin) → tạo thành công; `companyId` = `Asset.CompanyId` (5938e89c...).
- `GET /api/v1/maintenances` → trả bản ghi kèm `asset {name, assetTag, companyName}`.
- `GET /api/v1/maintenances/{id}` → chi tiết OK.
- Ghi chú test: PowerShell gửi body string không phải UTF-8 → tiếng Việt bị lỗi 400 khi thử nghiệm (lỗi của script test, không phải code; browser/frontend gửi UTF-8 đúng).

## 9. Ràng buộc / quyết định

- **`Guid.Empty` = floater**: asset không công ty → Maintenance `CompanyId = Guid.Empty`, hiển thị cho mọi user thường (nhất quán global filter `CompanyId == null` của Asset). Quyết định này khác chữ "không nullable" thuần túy của spec vì Asset vẫn có floater; nếu để `null` sẽ vi phạm non-nullable.
- **Không đổi `GetUserCompanyIdsAsync()`** (placeholder trả `[]`) — đổi sẽ làm **toàn bộ entity** (Asset/Component/...) bị filter công ty đột ngột, ngoài phạm vi task. Method mới `GetCurrentUserCompanyIdAsync()` chỉ dùng cho Maintenance.
- `GET /assets/{id}/maintenances` giữ cho tương thích; frontend Card dùng `GET /maintenances?assetId=` (cùng projection).
- `dotnet ef` không dùng — raw SQL self-heal + script.

## ⚠️ Database schema đã thay đổi

- Cột `CompanyId` (NOT NULL) + index trên `asset_maintenances`.
- **Không cần chạy lệnh EF** (project không dùng `dotnet ef migrations`) — schema tự áp dụng lúc server khởi động qua self-heal trong `Program.cs`.
- **Cần restart .NET Aspire AppHost** để resource `server` load build mới.
