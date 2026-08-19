# Báo cáo tính năng Quản lý Phụ kiện (Accessories)

**Ngày cập nhật:** 09/08/2026
**Dự án:** AspireReact
**Phiên bản:** ProComponents v3.1.14-6, Ant Design v6.5.4, React 19, .NET 10, Npgsql (PostgreSQL 18.3)

---

## 1. Tổng quan

Tính năng Accessory đã được xây dựng từ đầu theo kiến trúc CQRS (Command Query Responsibility Segregation) với MediatR, hỗ trợ **cấp phát đa hình (polymorphic checkout)** cho 4 loại đối tượng: Người dùng, Phòng ban, Vị trí, và Hệ thống (SystemPosition). So với Consumables chỉ hỗ trợ cấp phát cho User, Accessories có kiến trúc mở rộng hơn nhiều — cho phép checkout đến bất kỳ thực thể nào trong hệ thống, và hỗ trợ **thu hồi từng phần (partial check-in)** với cơ chế `AssignedQty` / `ReturnedQty` dual-track.

### 🆚 So sánh Accessory vs Consumable

| Đặc điểm | Consumable | Accessory |
|---|---|---|
| **Đối tượng nhận** | Chỉ User | User, Department, Location, SystemPosition |
| **Thu hồi** | Không hỗ trợ | ✓ Thu hồi từng phần (partial check-in) |
| **Xác nhận (Confirm)** | Pending → Confirmed | Không có trạng thái — sẵn sàng ngay |
| **Ghi log hành động** | Qua `ActionLog` trong controller | Qua `ActionLogService` (CQRS) |
| **CQRS Command Handlers** | Controller trực tiếp | MediatR + DI (Create, Checkout, Checkin, Delete) |
| **Xác thực người dùng** | Keycloak `sub` → tra cứu DB | `local_user_id` claim (JIT Provisioning) |
| **Company Isolation** | Lọc frontend | Server-side validation `COMPANY_MISMATCH` |
| **ProList Layout** | Xanh (blue gradient) | Tím (purple gradient #722ed1) |
| **Card Tags** | Pending (yellow), Confirmed (green), Low Stock (red) | Sẵn sàng (green), Đang cấp phát (orange), Tồn kho thấp (red) |

### Các file liên quan

| File | Trạng thái | Vai trò |
|---|---|---|
| **Backend** |||
| `Domain/Entities/Accessory.cs` | **Cập nhật** | Entity chính, Remaining tính theo `AssignedQty - ReturnedQty` |
| `Domain/Entities/AccessoryCheckout.cs` | **Viết lại** | Polymorphic: `CheckoutType` + `TargetId` + `AssignedQty` + `ReturnedQty` |
| `Domain/Enums/AccessoryCheckoutType.cs` | **Tạo mới** | Enum: User=1, Department=2, Location=3, SystemPosition=4 |
| `Domain/Interfaces/IActionLogService.cs` | **Tạo mới** | Interface logging tập trung |
| `Domain/Interfaces/ICurrentUserService.cs` | **Tạo mới** | Đọc `local_user_id` claim |
| `Infrastructure/Services/ActionLogService.cs` | **Tạo mới** | Implement logging, Skip nếu `CreatedBy` = Guid.Empty |
| `Infrastructure/Services/CurrentUserService.cs` | **Tạo mới** | Đọc claim `local_user_id` từ HttpContext |
| `Application/Accessories/Commands/CreateAccessoryCommand.cs` | **Tạo mới** | CQRS: Tạo phụ kiện + log `ActionType.Create` |
| `Application/Accessories/Commands/CheckoutAccessoryCommand.cs` | **Tạo mới** | CQRS: Cấp phát + validate tồn kho + **Company Isolation** + log `ActionType.Checkout` |
| `Application/Accessories/Commands/CheckinAccessoryCommand.cs` | **Tạo mới** | CQRS: Thu hồi + validate `returnQty <= remainingOut` + log `ActionType.Checkin` |
| `Application/Accessories/Commands/DeleteAccessoryCommand.cs` | **Tạo mới** | CQRS: Xóa + chặn nếu có active checkouts + log `ActionType.Delete` |
| `Application/Accessories/Commands/AccessoryResult.cs` | **Tạo mới** | Shared result record (`Success`, `Message`, `AccessoryId`, `ErrorCode`) |
| `Web/Controllers/AccessoriesController.cs` | **Viết lại** | Controller gọi MediatR cho Create/Checkout/Checkin/Delete |
| `Web/Controllers/ActionLogsController.cs` | **Tạo mới** | `GET /api/v1/action-logs` — batch resolve 5 entity types |
| `Infrastructure/Persistence/AppDbContext.cs` | **Cập nhật** | AccessoryCheckout config + `HasConversion<int>()` cho CheckoutType |
| `Program.cs` | **Cập nhật** | JIT User Provisioning, DI ActionLogService/CurrentUserService, MediatR assemblies |
| `Web/Controllers/UsersController.cs` | **Cập nhật** | Thêm param `?companyId=` để lọc user theo công ty |
| `Web/Controllers/DashboardController.cs` | **Sửa** | `ch.Quantity` → `ch.AssignedQty - ch.ReturnedQty` |
| **Frontend** |||
| `services/accessories.service.ts` | **Viết lại** | Full typed API: list, get, create, update, delete, checkout, checkin, getCheckouts, getLogs |
| `pages/AccessoryListPage.tsx` | **Viết lại** | ProList purple-themed + Checkout modal + Checkin inline modal |
| `pages/AccessoryDetailPage.tsx` | **Viết lại** | `<Tabs>`: Đang cấp phát + Lịch sử hoạt động (ProTable) |
| `pages/AccessoryFormPage.tsx` | **Viết lại** | Responsive grid 4-section form với TreeSelect công ty |
| `components/accessories/AccessoryCheckoutModal.tsx` | **Tạo mới** | Polymorphic form với `<Segmented>` + dynamic Select + Company Isolation Alert |
| `components/accessories/AccessoryCheckinModal.tsx` | **Tạo mới** | Modal thu hồi với `<Descriptions>` context + `InputNumber` strict max |
| `App.tsx` | **Cập nhật** | Thêm route `/accessories/:id/view` (AccessoryDetailPage) |

---

## 2. Trang Danh sách (AccessoryListPage.tsx)

### 2.1 Kiến trúc

- **Thành phần:** `<ProList<AccessoryDto>>` với `itemRender` — toàn quyền kiểm soát layout card
- **Data fetching:** `request` prop — không cần `useState`/`useEffect` thủ công
- **Card:** Ant Design `<Card hoverable>` với `borderRadius: 12px`, `transition: all 0.25s`
- **Grid responsive:** `xs: 1, sm: 1, md: 2, lg: 2, xl: 3, xxl: 3`
- **Color theme:** Purple (`#722ed1`, gradient `#f0e6ff → #d4baff`)

### 2.2 Cấu trúc mỗi Card

```
┌──────────────────────────────────────┐
│ [Icon purple gradient  Tên PK  #mã] │ ← GiftOutlined, 48x48, border-radius 12
│                                      │
│ [Danh mục] [Trạng thái] [Tồn kho thấp]│ ← Tags: category, status, isLowStock
│                                      │
│ ┌──────────────┬──────────────────┐  │
│ │ 📍 Vị trí     │ Tên vị trí       │  │
│ │ 📦 Tổng SL    │ 100              │  │ ← Data grid 2 cột (background #fafafa)
│ │ 📦 Còn lại    │ 20 (đỏ nếu thấp)  │  │
│ └──────────────┴──────────────────┘  │
│ ───────────────────────────────────── │ ← Divider
│ [Sửa] [Xem] [Cấp phát] [Thu hồi] [Xóa]│ ← Actions: flex-end, wrap
└──────────────────────────────────────┘
```

### 2.3 Form tìm kiếm

| Trường | Loại | API Param | Nguồn dữ liệu |
|---|---|---|---|
| Tìm kiếm | text | `search` | Gõ tự do |
| Danh mục | select | `categoryId` | `GET /categories` (categoryType=3 — Accessory) |
| Vị trí | select | `locationId` | `GET /locations` |

### 2.4 Actions trên mỗi Card

Tất cả 5 nút đều hiển thị, nhưng có cơ chế disable thông minh:

| Button | Điều kiện hiển thị / Disable |
|---|---|
| **Sửa (EditOutlined)** | Luôn hiển thị — điều hướng đến form edit |
| **Xem (EyeOutlined)** | Luôn hiển thị — điều hướng đến trang chi tiết |
| **Cấp phát (SendOutlined)** | `disabled` nếu `remaining <= 0` |
| **Thu hồi (RollbackOutlined)** | `disabled` nếu `checkedOutQty <= 0` (không có gì đang cấp phát) |
| **Xóa (DeleteOutlined)** | `disabled` nếu `checkedOutQty > 0` (có active checkouts) + Popconfirm |

### 2.5 Checkout Modal (AccessoryCheckoutModal)

#### Các trường trong modal

```
┌── Cấp phát phụ kiện ─────────────────────┐
│ Tên phụ kiện                              │
│ [FPT Telecom]  Còn lại: 500  Đã cấp: 100  │ ← Tag công ty + stats
│                                           │
│ ℹ️ Phụ kiện thuộc công ty FPT Telecom.    │ ← Alert banner (chỉ khi có companyId)
│    Chỉ có thể cấp phát cho đối tượng      │
│    thuộc cùng công ty này.                 │
│                                           │
│ Loại đối tượng nhận:                      │
│ [Người dùng | Phòng ban | Vị trí | Hệ thống]│ ← Segmented block, size="large"
│                                           │
│ Người dùng:                               │
│ [_______ Chọn người dùng ▼ _________]     │ ← Dynamic Select theo CheckoutType
│ ℹ️ Chỉ hiển thị các đối tượng thuộc FPT   │
│                                           │
│ Số lượng cấp phát:                        │
│ [___ 1 ___]  (max=500)                    │ ← InputNumber
│                                           │
│ Ghi chú:                                  │
│ ┌────────────────────────────────────┐    │
│ │ Nhập ghi chú (không bắt buộc)      │    │ ← TextArea, maxLength=500, showCount
│ └────────────────────────────────────┘    │
│                                   0/500   │
│ ───────────────────────────────────────── │
│                      [Hủy]  [Cấp phát]    │
└──────────────────────────────────────────┘
```

#### Dynamic Target Fetching (Polymorphic)

| CheckoutType | Endpoint Fetch | Query Params |
|---|---|---|
| 1 — Người dùng | `GET /users` | `?companyId={accessory.companyId}&pageSize=500` |
| 2 — Phòng ban | `GET /departments` | `?companyId={accessory.companyId}&pageSize=500` |
| 3 — Vị trí | `GET /locations` | `?pageSize=500` (không có CompanyId) |
| 4 — Hệ thống | `GET /system-infos` | Filter positions có `systemInfo.companyId === accessory.companyId` |

#### Company Isolation (3 lớp)

| Lớp | Vị trí | Cơ chế |
|---|---|---|
| **UI — Filter** | `fetchTargets()` | Truyền `companyId` trong query params; client-side filter fallback |
| **UI — Alert** | Modal body | `<Alert type="info">` hiển thị ràng buộc công ty |
| **UI — Placeholder** | Select | "Chọn người dùng thuộc [CompanyName]" vs "Chọn người dùng" |
| **UI — notFoundContent** | Select | "Không có người dùng nào thuộc [CompanyName]" |
| **Server** | `CheckoutAccessoryCommandHandler` | So sánh `accessory.CompanyId` với `target.CompanyId` cho User/Department/SystemPosition; Location luôn được phép |

### 2.6 Checkin Inline (trong Modal riêng)

Khi nhấn [Thu hồi], mở `AccessoryCheckinModal`:

```
┌── Thu hồi phụ kiện ──────────────────────┐
│ Thông tin cấp phát                        │
│ ┌─────────────────────────────────────┐   │
│ │ Loại đối tượng   │ Người dùng       │   │ ← Descriptions bordered
│ │ Đối tượng nhận   │ Nguyễn Văn A     │   │
│ │ Đã cấp           │ 10               │   │
│ │ Đã thu hồi       │ 3                │   │
│ │ Còn lại có thể thu│ 7 (màu cam, đậm) │   │
│ └─────────────────────────────────────┘   │
│                                           │
│ Số lượng thu hồi:                         │
│ [___ 1 ___]  Tối đa: 7                    │ ← InputNumber max={assignedQty - returnedQty}
│                                           │
│ Ghi chú thu hồi:                          │
│ ┌────────────────────────────────────┐    │
│ │ Ví dụ: Hỏng, mất, trả nguyên trạng │    │ ← TextArea, maxLength=500
│ └────────────────────────────────────┘    │
│ ───────────────────────────────────────── │
│                    [Hủy]  [Xác nhận thu hồi]│
└──────────────────────────────────────────┘
```

### 2.7 Styling nâng cao

- Card hover: `border-color: #d6e4ff`, `box-shadow`, `translateY(-2px)`, `transition: 0.25s`
- Icon gradient: `linear-gradient(135deg, #f0e6ff, #d4baff)` (tím nhạt → tím vừa)
- Data grid: `background: #fafafa`, `border-radius: 8px`
- Low stock: `<Text type="danger" strong>` màu đỏ, font-size 15px
- Actions: flex-end với `space-between`, responsive wrap

---

## 3. Trang Chi tiết (AccessoryDetailPage.tsx)

### 3.1 Cấu trúc trang

1. **Header:** Nút "← Quay lại" + "Chi tiết phụ kiện" + badge "Tồn kho thấp" (nếu có) + buttons [Cấp phát] [Sửa]
2. **Stock Summary Cards (4 cột responsive):**
   - Tổng số lượng (nền xanh `#f6ffed`)
   - Còn lại (nền xanh `#e6f4ff` hoặc đỏ `#fff2f0` nếu low stock + phần trăm)
   - Đang cấp phát (nền tím nhạt `#f0e6ff`)
   - Ngưỡng cảnh báo (nền vàng `#fffbe6`)
3. **Thông tin chi tiết (Card):**
   - `<Descriptions bordered size="small" column={{ xs: 1, sm: 2 }}>`
   - Hiển thị: Tên, Mã (code), Danh mục (Tag purple), Vị trí, Công ty, Nhà SX, Nhà CC, Model No, Order No, Ngày mua, Đơn giá (VND), Ghi chú
4. **Tabs: Đang cấp phát + Lịch sử hoạt động**

### 3.2 Tab: Đang cấp phát (Active Checkouts)

Hiển thị các bản ghi checkout chưa được thu hồi hết (`AssignedQty > ReturnedQty`).

| Ngày cấp | Loại | Đối tượng nhận | Đã cấp | Đã thu | Còn lại | Người cấp | Ghi chú | Hành động |
|---|---|---|---|---|---|---|---|---|
| 01/01/2025 | 👤 Người dùng | Nguyễn Văn A | 10 | 3 | **7** | 🔄 admin | Ghi chú | [Thu hồi] |

- **Màu sắc badges:** User (blue), Department (cyan), Location (green), SystemPosition (purple)
- **Còn lại:** highlight vàng khi `remainingOut > 0`
- **Badge tab:** Hiển thị số lượng active checkouts (màu cam `#fa8c16`)

### 3.3 Tab: Lịch sử hoạt động (Action Logs)

Sử dụng `<ProTable>` read-only (`search={false}`, `toolBarRender={false}`, `options={false}`):

| Thời gian | Hành động | Người thực hiện | Đối tượng liên quan | Chi tiết / Ghi chú |
|---|---|---|---|---|
| 01/01 08:30 | Tạo mới (xanh) | Nguyễn Đức Toàn | - | Created accessory: Chuột Logitech... |
| 01/01 09:15 | Cấp phát (cam) | Nguyễn Đức Toàn | Nguyễn Văn A | Ghi chú · SL: 5 |
| 01/01 10:00 | Thu hồi (tím) | Nguyễn Đức Toàn | Nguyễn Văn A | Đã trả: 2 · Còn: 3 |

#### Action Type Tags (đầy đủ 10 enum values)

| Value | Enum | Label | Color |
|---|---|---|---|
| 1 | Create | Tạo mới | `blue` |
| 2 | Update | Cập nhật | `cyan` |
| 3 | Delete | Xóa | `red` |
| 4 | Checkout | Cấp phát | `green` |
| 5 | Checkin | Thu hồi | `orange` |
| 6 | Audit | Kiểm kê | `geekblue` |
| 7 | Import | Import | `lime` |
| 8 | Export | Export | `gold` |
| 9 | Accept | Accept | `purple` |
| 10 | Decline | Decline | `magenta` |

#### Target Name Resolution (batch, không N+1)

Backend `ActionLogsController` sử dụng 2-step pattern:
1. Materialize logs từ DB
2. Batch-prefetch 5 dictionaries (`Users`, `Locations`, `Departments`, `SystemPositions`, `Assets`)
3. Enrich logs với targetName từ dictionary lookup

Nếu entity đã bị xóa, hiển thị `-`.

### 3.4 Route

| Route | Component |
|---|---|
| `/accessories` | `AccessoryListPage` (danh sách) |
| `/accessories/new` | `AccessoryFormPage` (tạo mới) |
| `/accessories/:id` | `AccessoryFormPage` (chỉnh sửa) |
| `/accessories/:id/view` | `AccessoryDetailPage` (chi tiết) |

---

## 4. Backend API Endpoints

| Method | Endpoint | Mô tả | Auth Policy |
|---|---|---|---|
| `GET` | `/api/v1/accessories` | Danh sách (search, categoryId, locationId, page, pageSize) + `CompanyName`, `Remaining`, `CheckedOutQty` | `accessories.view` |
| `GET` | `/api/v1/accessories/{id}` | Chi tiết phụ kiện + Category, Manufacturer, Supplier, Location, Company | `accessories.view` |
| `POST` | `/api/v1/accessories` | **Tạo mới** — qua CQRS `CreateAccessoryCommand`; tự động log `ActionType.Create` | `accessories.create` |
| `PUT` | `/api/v1/accessories/{id}` | Cập nhật — chặn nếu có active checkouts | `accessories.edit` |
| `DELETE` | `/api/v1/accessories/{id}` | Xóa — qua CQRS `DeleteAccessoryCommand`; chặn nếu có active checkouts; log `ActionType.Delete` | `accessories.delete` |
| `POST` | `/api/v1/accessories/{id}/checkout` | **Cấp phát đa hình** — qua CQRS `CheckoutAccessoryCommand`; validate tồn kho + target + **company isolation**; log `ActionType.Checkout` | `accessories.checkout` |
| `POST` | `/api/v1/accessories/checkouts/{checkoutId}/checkin` | **Thu hồi** — qua CQRS `CheckinAccessoryCommand`; validate `returnQty <= remainingOut`; tăng `ReturnedQty`; log `ActionType.Checkin` | `accessories.checkout` |
| `GET` | `/api/v1/accessories/{id}/checkouts` | Lịch sử cấp phát — có `CreatedByUser` + `TargetName` resolved | `accessories.view` |
| `GET` | `/api/v1/action-logs?itemType=3&itemId={id}` | Lịch sử hoạt động — đầy đủ 5 loại TargetName (User, Location, Department, SystemPosition, Asset) | `Authorize` |
| `GET` | `/api/v1/users?companyId={id}` | Danh sách user (đã thêm param companyId) | `users.view` |

### Checkout Endpoint chi tiết

**`POST /api/v1/accessories/{id}/checkout`**

```json
// Request body
{
  "checkoutType": 1,        // 1=User, 2=Department, 3=Location, 4=SystemPosition
  "targetId": "guid",       // ID of the target entity
  "quantity": 5,            // 1 ≤ quantity ≤ remaining
  "note": "Ghi chú"         // Tùy chọn
}
```

**Logic validation (5 lớp):**
1. Tồn tại accessory? `Accessory not found`
2. Tồn kho đủ? `quantity <= remaining`
3. Target tồn tại? `targetValid = Users.AnyAsync(...)` theo CheckoutType
4. **Company Isolation:** Nếu `accessory.CompanyId != null`, target phải cùng công ty (trừ Location)
5. Transaction: `IExecutionStrategy.ExecuteAsync` + `BeginTransactionAsync`

**Response:**
```json
{
  "status": "success",
  "message": "5 accessory(s) checked out.",
  "data": { "id": "checkout-guid", "assignedQty": 5 }
}
```

### Checkin Endpoint chi tiết

**`POST /api/v1/accessories/checkouts/{checkoutId}/checkin`**

```json
// Request body
{
  "returnQty": 2,
  "note": "Trả lại nguyên trạng"
}
```

**Logic validation:**
1. `returnQty > 0`
2. `returnQty <= (AssignedQty - ReturnedQty)`
3. `ReturnedQty += returnQty`
4. Log `ActionType.Checkin` với metadata `{returnQty, assignedQty, totalReturned, remaining, checkoutId}`

---

## 5. Entity & Database Schema

### 5.1 Accessory Entity

```csharp
public class Accessory : IAuditable, ICompanyable
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public string? ItemNo { get; set; }
    public string? Image { get; set; }
    public string? ModelNumber { get; set; }
    public string? OrderNumber { get; set; }

    // Foreign Keys
    public Guid? CategoryId { get; set; }
    public Guid? ManufacturerId { get; set; }
    public Guid? SupplierId { get; set; }
    public Guid? LocationId { get; set; }
    public Guid? CompanyId { get; set; }

    // Inventory
    public int Qty { get; set; }
    public int MinAmt { get; set; }

    // Financial
    public decimal? PurchaseCost { get; set; }
    public DateTime? PurchaseDate { get; set; }
    public string? Notes { get; set; }

    // Computed (NotMapped)
    [NotMapped] public int Remaining => Qty - Checkouts.Sum(c => c.AssignedQty - c.ReturnedQty);
    [NotMapped] public double PercentRemaining => ...;
    [NotMapped] public bool IsLowStock => Remaining <= MinAmt;

    // Navigation
    public Category? Category { get; set; }
    public Manufacturer? Manufacturer { get; set; }
    public Supplier? Supplier { get; set; }
    public Location? Location { get; set; }
    public Company? Company { get; set; }
    public ICollection<AccessoryCheckout> Checkouts { get; set; }
}
```

### 5.2 AccessoryCheckout Entity (Polymorphic)

```csharp
public class AccessoryCheckout
{
    public Guid Id { get; set; }
    public Guid AccessoryId { get; set; }

    // Polymorphic target
    public AccessoryCheckoutType CheckoutType { get; set; }  // User=1, Dept=2, Loc=3, SysPos=4
    public Guid TargetId { get; set; }                        // ID entity nhận

    // Dual-track quantity model
    public int AssignedQty { get; set; }    // Số lượng đã cấp ban đầu
    public int ReturnedQty { get; set; }    // Số lượng đã thu hồi (partial)

    // Audit
    public Guid? CreatedByUserId { get; set; }
    public string? Note { get; set; }
    public DateTime CheckedOutAt { get; set; }

    // Computed
    [NotMapped] public int RemainingCheckedOut => AssignedQty - ReturnedQty;

    // Navigation
    public Accessory Accessory { get; set; }
    public User? CreatedByUser { get; set; }
}
```

### 5.3 Schema Migration

Bảng `accessory_checkouts` đã được restructure qua raw SQL trong `Program.cs`:

```sql
-- Thêm cột mới
ALTER TABLE accessory_checkouts ADD COLUMN IF NOT EXISTS "CheckoutType" integer NOT NULL DEFAULT 1;
ALTER TABLE accessory_checkouts ADD COLUMN IF NOT EXISTS "TargetId" uuid NOT NULL DEFAULT gen_random_uuid();
ALTER TABLE accessory_checkouts ADD COLUMN IF NOT EXISTS "AssignedQty" integer NOT NULL DEFAULT 1;
ALTER TABLE accessory_checkouts ADD COLUMN IF NOT EXISTS "ReturnedQty" integer NOT NULL DEFAULT 0;
ALTER TABLE accessory_checkouts ADD COLUMN IF NOT EXISTS "CreatedByUserId" uuid;

-- Migrate dữ liệu cũ (nếu có)
UPDATE accessory_checkouts SET "AssignedQty" = COALESCE("Quantity", 1);
UPDATE accessory_checkouts SET "TargetId" = "UserId";

-- Xóa cột cũ
ALTER TABLE accessory_checkouts DROP COLUMN IF EXISTS "Quantity";
ALTER TABLE accessory_checkouts DROP COLUMN IF EXISTS "AssignedToId";
ALTER TABLE accessory_checkouts DROP COLUMN IF EXISTS "UserId";
```

EF Core migration `RestructureAccessoryCheckouts` đã được tạo. Chạy lệnh:
```bash
cd aspire-react/aspire-react.Server
dotnet ef migrations add RestructureAccessoryCheckouts
dotnet ef database update
```

---

## 6. Kiến trúc CQRS & Services

### 6.1 Command Handlers (MediatR)

```
Controller (IMediator)
    ├── Create → CreateAccessoryCommandHandler
    │     └── _actionLogService.LogAction(ActionType.Create)
    ├── Checkout → CheckoutAccessoryCommandHandler
    │     ├── Validate tồn kho
    │     ├── Validate target tồn tại
    │     ├── Validate Company Isolation
    │     └── _actionLogService.LogAction(ActionType.Checkout, TargetType, TargetId)
    ├── Checkin → CheckinAccessoryCommandHandler
    │     ├── Validate returnQty <= remainingOut
    │     ├── ReturnedQty += returnQty
    │     └── _actionLogService.LogAction(ActionType.Checkin, TargetType, TargetId)
    └── Delete → DeleteAccessoryCommandHandler
          ├── Chặn nếu có active checkouts
          └── _actionLogService.LogAction(ActionType.Delete)
```

### 6.2 JIT User Provisioning (Program.cs — OnTokenValidated)

Mỗi request được authenticate qua JWT:

```
Keycloak Token → OnTokenValidated
    ├── Extract claims: preferred_username, email, given_name, family_name
    ├── Check local users table
    ├── Create User nếu chưa tồn tại (Guid.NewGuid(), IsActive=true)
    ├── Sync name/email nếu đã thay đổi
    └── AddClaim("local_user_id", user.Id) → có sẵn trong toàn bộ pipeline
```

```csharp
// Controller lấy local user ID không cần DB lookup
var currentUserId = _currentUserService.GetLocalUserId();
```

### 6.3 ActionLogService

```csharp
IActionLogService.LogAction(
    itemType: ItemType.Accessory,
    itemId: accessory.Id,
    actionType: ActionType.Checkout,
    loggedByUserId: currentUserId,       // từ local_user_id claim
    targetType: AssignmentTargetType.User,
    targetId: request.TargetId,
    note: request.Note,
    logMeta: JsonSerializer.Serialize(...)
);
```

- **Scoped** — chia sẻ cùng `AppDbContext` với handler
- **Skip nếu `CreatedBy == Guid.Empty`** — tránh FK violation
- **LogMeta** chứa dữ liệu JSON: `quantity`, `returnQty`, `remaining`, `checkoutType`, `targetId`

---

## 7. Chi tiết kỹ thuật

### 7.1 Vấn đề đã khắc phục

| # | Vấn đề | Giải pháp |
|---|---|---|
| 1 | Chỉ hỗ trợ checkout đến User | Polymorphic `AccessoryCheckout`: `CheckoutType` enum + `TargetId` cho 4 loại |
| 2 | Không có thu hồi | Dual-track `AssignedQty`/`ReturnedQty` + `POST /checkouts/{id}/checkin` |
| 3 | Thiếu Action Logs | `ActionLogsController` batch-resolve 5 entity types (User, Location, Dept, SysPos, Asset) |
| 4 | FK violation `FK_action_logs_users_CreatedBy` (Keycloak sub ≠ local user ID) | JIT User Provisioning: thêm `local_user_id` claim khi token validated |
| 5 | Không có Company Isolation | `CheckoutAccessoryCommandHandler` kiểm tra `accessory.CompanyId == target.CompanyId` |
| 6 | Modal checkout không lọc theo công ty | `fetchTargets()` truyền `companyId` param + Alert banner + placeholder động |
| 7 | Form tạo/sửa xếp dọc một cột | Responsive grid 4-section: Thông tin chung, Tồn kho, Tổ chức & Vị trí, Ghi chú |
| 8 | Không có lịch sử hoạt động chi tiết | `<Tabs>`: Tab "Đang cấp phát" + Tab "Lịch sử hoạt động" với ProTable read-only |
| 9 | `Department`/`SystemPosition` target name hiển thị `-` | `ActionLogsController` batch-prefetch 5 dictionaries + fallback search |
| 10 | Intermittent 401 do token hết hạn | `api-client.ts`: proactive `keycloak.updateToken(30)` trước mỗi request + retry queue |
| 11 | `DashboardController` compile error do `AccessoryCheckout.Quantity` cũ | Sửa `ch.Quantity` → `ch.AssignedQty - ch.ReturnedQty` |
| 12 | EF Core projection không hỗ trợ instance method | Materialize trước, resolve target name in-memory sau |
| 13 | Không có CQRS command handlers riêng | 4 command handlers + `AccessoryResult.cs` shared record |

### 7.2 Performance

| Kỹ thuật | Mô tả |
|---|---|
| **ProList `request` prop** | Debounce tự động, không cần `useEffect` |
| **Batch target name resolution** | 5 dictionary `ToDictionaryAsync` queries — không N+1 |
| **JIT Provisioning** | Tạo user một lần khi token validated, không DB lookup mỗi request |
| **`local_user_id` claim** | Controller đọc local ID từ claim, không cần async DB query |
| **ProTable read-only** | `search={false}`, `toolBarRender={false}`, `options={false}` — tối ưu render |
| **Parallel data loading** | `Promise.all([getDetail, getCheckouts, getLogs])` trong DetailPage |

### 7.3 Responsive Design

| Breakpoint | Grid columns | Mô tả |
|---|---|---|
| **xs** (<576px) | 1 | Mobile: 1 cột |
| **sm** (≥576px) | 1 | Tablet nhỏ: 1 cột |
| **md** (≥768px) | 2 | Tablet: 2 cột |
| **lg** (≥992px) | 2 | Desktop nhỏ: 2 cột |
| **xl** (≥1200px) | 3 | Desktop lớn: 3 cột |
| **xxl** (≥1600px) | 3 | Màn hình lớn: 3 cột |

Trang Form: `xs={24}`, `sm={12}`, `md={8}`, `lg={6}` — co giãn từ 1 đến 4 cột tùy màn hình.

---

## 8. Hướng dẫn mở rộng trong tương lai

1. **Checkout hàng loạt (Batch Checkout):** Cho phép chọn nhiều phụ kiện và cấp phát cùng lúc
2. **Import/Export CSV:** Tương tự module Assets
3. **Phân quyền chi tiết hơn:** Kiểm tra policy cho từng action (hiện tại dùng `accessories.checkout` chung cho cả checkout và checkin)
4. **Thêm trường `LocationId` cho bảng `accessories`:** Để hỗ trợ quản lý vị trí lưu trữ chi tiết
5. **Audit Trail cho Update:** Hiện tại Create/Checkout/Checkin/Delete đều có log; Update chưa được log trong phiên bản hiện tại
6. **Thông báo tồn kho thấp:** Push notification hoặc email khi `Remaining <= MinAmt`
7. **QR Code / Barcode:** Gắn mã QR cho từng phụ kiện để quét nhanh khi checkout/checkin