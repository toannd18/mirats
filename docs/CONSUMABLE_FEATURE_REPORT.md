# Báo cáo tính năng Quản lý Vật tư Tiêu hao (Consumables)

**Ngày cập nhật:** 08/08/2026  
**Dự án:** AspireReact  
**Phiên bản:** ProComponents v3.1.14-6, Ant Design v6.5.4, React 19, .NET 10

---

## 1. Tổng quan

Tính năng Consumable đã được thiết kế lại toàn bộ từ `<Table>` thủ công sang **Ant Design ProList** với layout card premium, responsive, và đầy đủ chức năng CRUD + checkout + lịch sử cấp phát.

### Các file liên quan

| File | Trạng thái | Vai trò |
|---|---|---|
| `frontend/src/pages/ConsumableListPage.tsx` | **Viết lại hoàn chỉnh** | Trang danh sách (ProList card layout) + modal checkout |
| `frontend/src/pages/ConsumableDetailPage.tsx` | **Tạo mới** | Trang xem chi tiết + lịch sử cấp phát |
| `frontend/src/pages/ConsumableFormPage.tsx` | Giữ nguyên | Trang tạo/sửa vật tư (Form) |
| `frontend/src/App.tsx` | **Cập nhật** | Thêm route `/consumables/:id/view` + `ConfigProvider locale={viVN}` |
| `frontend/package.json` | **Cập nhật** | Nâng cấp `@ant-design/pro-components` lên v3.1.14-6 |
| `Server/Web/Controllers/ConsumablesController.cs` | **Mở rộng** | Thêm endpoint GET checkouts, sửa checkout logic, thêm CompanyName/CompanyId |
| `Server/Domain/Entities/ConsumableCheckout.cs` | **Mở rộng** | Thêm `CreatedByUserId` + `CreatedByUser` navigation |
| `Server/Program.cs` | **Cập nhật** | Thêm `ALTER TABLE consumable_checkouts ADD COLUMN CreatedByUserId` |

---

## 2. Trang Danh sách (ConsumableListPage.tsx)

### 2.1 Kiến trúc

- **Thành phần:** `<ProList<ConsumableDto>>` với `itemRender` — toàn quyền kiểm soát layout card
- **Data fetching:** `request` prop — không cần `useState`/`useEffect` thủ công
- **Card:** Ant Design `<Card hoverable>` với `borderRadius: 12px`, `transition: all 0.25s`
- **Grid responsive:** `xs: 1, sm: 1, md: 2, lg: 2, xl: 3, xxl: 3`

### 2.2 Cấu trúc mỗi Card

```
┌──────────────────────────────────────┐
│ [Icon gradient   Tên vật tư  #mã]   │ ← Header: icon xanh 48x48 + name + itemNo badge
│                                      │
│ [Danh mục] [Trạng thái] [Tồn kho thấp]│ ← Tags: category, status, isLowStock
│                                      │
│ ┌──────────────┬──────────────────┐  │
│ │ 📍 Vị trí     │ Tên vị trí       │  │
│ │ 📦 Tổng SL    │ 100              │  │ ← Data grid 2 cột (background #fafafa)
│ │ 📦 Còn lại    │ 20 (đỏ nếu thấp)  │  │
│ └──────────────┴──────────────────┘  │
│ ───────────────────────────────────── │ ← Divider
│           [Sửa] [Xác nhận] [Xóa]      │ ← Actions: flex-end, wrap
└──────────────────────────────────────┘
```

### 2.3 Form tìm kiếm

| Trường | Loại | API Param | Nguồn dữ liệu |
|---|---|---|---|
| Tìm kiếm | text | `search` | Gõ tự do |
| Danh mục | select | `categoryId` | `GET /categories` (categoryType=2) |
| Vị trí | select | `locationId` | `GET /locations` |

Các cột tìm kiếm được định nghĩa trong `searchColumns` với `hideInTable: true`, truyền vào `columns` prop của ProList.

### 2.4 Actions theo trạng thái

| Trạng thái | Buttons hiển thị |
|---|---|
| **Pending** | [Sửa] → form edit · [Xác nhận (Popconfirm)] · [Xóa (Popconfirm danger)] |
| **Confirmed** | [Xem] → trang chi tiết · [Cấp phát (ghost primary)] → modal checkout |

### 2.5 Checkout Modal

#### Các trường trong modal

```
┌── Cấp phát vật tư ──────────────────────┐
│ Tên vật tư                               │
│ [FPT Telecom]  Còn lại: 500              │ ← Tag công ty + remaining
│                                          │
│ Người nhận:                              │
│ [_____ Chọn người nhận ▼ ___________]    │ ← Select searchable, chỉ hiển thị
│ ℹ️ Chỉ hiển thị người dùng cùng công ty   │   user cùng companyId
│                                          │
│ Số lượng cấp phát:                       │
│ [___ 1 ___]  (min=1, max=remaining)      │ ← InputNumber
│                                          │
│ Ghi chú:                                 │
│ ┌────────────────────────────────────┐   │
│ │ Nhập ghi chú (không bắt buộc)      │   │ ← TextArea, maxLength=500, showCount
│ └────────────────────────────────────┘   │
│                                   0/500  │
│ ──────────────────────────────────────── │
│                      [Hủy]  [Cấp phát]   │ ← Cấp phát disabled nếu chưa chọn user
└──────────────────────────────────────────┘
```

#### Validation khi cấp phát (3 lớp)

| Lớp | Vị trí | Kiểm tra |
|---|---|---|
| **UI** | `InputNumber` | `max={checkoutTarget?.remaining}` — không cho phép nhập vượt quá |
| **UI** | `okButtonProps` | `disabled: !checkoutUserId` — nút "Cấp phát" disabled nếu chưa chọn người nhận |
| **Client** | `handleCheckout()` | `checkoutQty > remaining` → thông báo "Không thể cấp phát quá số lượng còn lại" |
| **Client** | `handleCheckout()` | `checkoutQty < 1` → thông báo "Số lượng phải > 0" |
| **Client** | `handleCheckout()` | `!checkoutUserId` → thông báo "Vui lòng chọn người nhận" |
| **Server** | `ConsumablesController` | `if (r.Quantity > remaining) return BadRequest("Insufficient stock")` |

#### Lọc người nhận theo công ty

| Tình huống | Hành vi |
|---|---|
| **Vật tư có `companyId`** | Chỉ hiển thị người dùng `companyId === record.companyId` |
| **Vật tư không có công ty** | Hiển thị tất cả người dùng |
| **Không có user phù hợp** | `notFoundContent`: "Không có người dùng nào trong công ty này" |
| **Select placeholder** | "Chọn người nhận (cùng công ty)" nếu có companyId |
| **Hint text** | "Chỉ hiển thị người dùng cùng công ty với vật tư" |

### 2.6 Styling nâng cao

- Card hover: `border-color: #d6e4ff`, `box-shadow`, `translateY(-2px)`, `transition: 0.25s`
- Icon gradient: `linear-gradient(135deg, #e6f4ff, #bae0ff)`
- Data grid: `background: #fafafa`, `border-radius: 8px`
- Low stock: `<Typography.Text type="danger" strong>` màu đỏ, font-size 15px
- Actions: `paddingTop: 12px`, `wrap`, `size={[4, 8]}` để không chồng lấn trên mobile
- Sử dụng `typography.toLocaleString('vi-VN')` cho định dạng số

---

## 3. Trang Chi tiết (ConsumableDetailPage.tsx)

### 3.1 Mục đích
Khi nhấn nút "Xem" trên card Confirmed, người dùng được chuyển đến trang chi tiết hiển thị đầy đủ thông tin và lịch sử cấp phát.

### 3.2 Cấu trúc trang

1. **Header:** Nút "← Quay lại" + "Chi tiết vật tư" + badge "Tồn kho thấp" (nếu có)
2. **Stock Summary Cards (3 cột responsive):**
   - Tổng số lượng (nền xanh `#f6ffed`)
   - Còn lại (nền xanh `#e6f4ff` hoặc đỏ `#fff2f0` nếu low stock + phần trăm)
   - Ngưỡng cảnh báo (nền vàng `#fffbe6`)
3. **Thông tin chi tiết (Card):**
   - `<Descriptions bordered size="small" column={{ xs: 1, sm: 2 }}>`
   - Hiển thị: Tên, Mã (code), Danh mục (Tag), Vị trí, Công ty, Nhà SX, Nhà CC, Model No, Order No, Ngày mua, Đơn giá (VND), Ghi chú
   - Nút "Chỉnh sửa" góc phải trên
4. **Lịch sử cấp phát (Card):**

   | Ngày | Người nhận | Người cấp phát | SL | Ghi chú |
   |---|---|---|---|---|
   | 01/01/2025 | 👤 Nguyễn Văn A | 🔄 Trần Thị B | 5 | Ghi chú ABC |
   | 02/01/2025 | 👤 Lê Văn C | 🔄 admin | 3 | - |

   - **Người nhận:** Hiển thị `firstName + lastName`, fallback `userName`, fallback `-`
   - **Người cấp phát:** Hiển thị `createdByFirstName + createdByLastName`, fallback `createdByName`, fallback `-`
   - Empty state: "Chưa có lịch sử cấp phát nào"
   - API: `GET /consumables/{id}/checkouts`

### 3.3 Route

`/consumables/:id/view` → `ConsumableDetailPage`

---

## 4. Backend API endpoints

| Method | Endpoint | Mô tả | File |
|---|---|---|---|
| `GET` | `/api/v1/consumables` | Danh sách consumables (search, categoryId, locationId, page, pageSize). Bao gồm `CompanyId`, `CompanyName` | ConsumablesController |
| `GET` | `/api/v1/consumables/{id}` | Chi tiết consumable + company, manufacturer, supplier, location | ConsumablesController |
| `GET` | `/api/v1/consumables/{id}/checkouts` | **Tạo mới** — lịch sử cấp phát (User, CreatedByUser) | ConsumablesController |
| `POST` | `/api/v1/consumables` | Tạo mới consumable | ConsumablesController |
| `PUT` | `/api/v1/consumables/{id}` | Cập nhật consumable (chỉ khi Pending) | ConsumablesController |
| `DELETE` | `/api/v1/consumables/{id}` | Xóa consumable (chỉ khi Pending) | ConsumablesController |
| `PUT` | `/api/v1/consumables/{id}/confirm` | Xác nhận consumable (Pending → Confirmed) | ConsumablesController |
| `POST` | `/api/v1/consumables/{id}/checkout` | **Cấp phát** — lưu UserId, CreatedByUserId, quantity, note | ConsumablesController |
| `GET` | `/api/v1/categories` | Danh sách danh mục (lọc categoryType=2) | Search form |
| `GET` | `/api/v1/locations` | Danh sách vị trí | Search form |
| `GET` | `/api/v1/users` | Danh sách người dùng (lọc theo companyId) | Checkout modal |

### Checkout Endpoint chi tiết

**`POST /api/v1/consumables/{id}/checkout`**

```json
// Request body
{
  "userId": "guid",       // ID người nhận (bắt buộc từ frontend)
  "quantity": 5,          // Số lượng (1 ≤ quantity ≤ remaining)
  "note": "Ghi chú"       // Ghi chú (tùy chọn)
}
```

**Logic lưu trữ:**
- `ConsumableCheckout.UserId` = `r.UserId` (người nhận từ frontend) hoặc `currentUserId` (người đang đăng nhập)
- `ConsumableCheckout.CreatedByUserId` = `currentUserId` (người thực hiện cấp phát — tra cứu từ `Users` bằng `preferred_username` JWT claim)
- `ActionLog.CreatedBy` = `currentUserId` (chỉ tạo log nếu user tồn tại trong DB local)

**Transaction:** Sử dụng `IExecutionStrategy.ExecuteAsync<IActionResult>()` để tương thích với `NpgsqlRetryingExecutionStrategy`.

---

## 5. Entity & Database Schema

### 5.1 ConsumableCheckout Entity

```csharp
public class ConsumableCheckout
{
    public Guid Id { get; set; }
    public Guid ConsumableId { get; set; }
    public Guid UserId { get; set; }              // Người nhận
    public Guid? AssignedToId { get; set; }
    public Guid? CreatedByUserId { get; set; }    // Người cấp phát (MỚI)
    public int Quantity { get; set; }
    public string? Note { get; set; }
    public DateTime CheckedOutAt { get; set; }

    // Navigation
    public Consumable Consumable { get; set; }
    public User User { get; set; }                // Người nhận
    public User? CreatedByUser { get; set; }      // Người cấp phát (MỚI)
}
```

### 5.2 Schema Migration

Thêm cột vào bảng `consumable_checkouts` qua raw SQL trong `Program.cs`:

```sql
ALTER TABLE consumable_checkouts
ADD COLUMN IF NOT EXISTS "CreatedByUserId" uuid;
```

Cũng đã tạo EF Core migration `AddCreatedByUserIdToConsumableCheckout` (chưa áp dụng — dùng raw SQL pattern thay thế).

---

## 6. Chi tiết kỹ thuật

### 6.1 Vấn đề đã khắc phục

| # | Vấn đề | Giải pháp |
|---|---|---|
| 1 | ProTable mặc định tiếng Trung | Thêm `ConfigProvider locale={viVN}` vào `App.tsx` |
| 2 | ProComponents v2 peer dep conflict với antd v6 | Nâng cấp lên v3.1.14-6 (beta) |
| 3 | `hideInSearch` không tồn tại trong v3 | Dùng `search: false` thay thế |
| 4 | `metas` API deprecated | Chuyển sang `itemRender` — toàn quyền kiểm soát card |
| 5 | ProCard `bordered`/`bodyStyle` không tồn tại trong v3 | Thay bằng `Card` từ antd với `styles.body` |
| 6 | Avatar `src=""` warning | Di chuyển icon gradient vào title slot, không dùng avatar meta |
| 7 | Dữ liệu `companyName`/`categoryName` không khớp API | Sửa `ConsumableDto` dùng `category.name` + `location.name` thay vì flat fields; thêm `.Include(c => c.Company)` |
| 8 | Nút actions chồng lấn | `paddingTop: 12px`, `wrap`, `size={[4, 8]}` |
| 9 | Form tìm kiếm không hoạt động | Thêm `searchColumns` với `hideInTable: true` và truyền vào `columns` prop |
| 10 | Thiếu trang xem chi tiết | Tạo `ConsumableDetailPage.tsx` với Descriptions + checkout history |
| 11 | Thiếu endpoint GET /checkouts | Thêm `[HttpGet("{id:guid}/checkouts")]` vào ConsumablesController |
| 12 | FK violation `FK_action_logs_users_CreatedBy` | Sửa `GetCurrentUserId()` → `GetCurrentUserIdAsync()` tra cứu `Users.Id` bằng `preferred_username` JWT claim, không parse Keycloak `sub` GUID |
| 13 | `NpgsqlRetryingExecutionStrategy` không hỗ trợ `BeginTransactionAsync()` | Bọc transaction trong `strategy.ExecuteAsync<IActionResult>()` |
| 14 | Modal checkout thiếu chọn người nhận | Thêm `Select` với danh sách users, lọc theo `companyId` |
| 15 | Modal checkout thiếu trường ghi chú | Thêm `Input.TextArea` với `maxLength=500`, `showCount` |
| 16 | Cảnh báo "Không có người dùng" xuất hiện sai | Dùng biến local `options` thay vì state `userOptions` (state chưa cập nhật kịp) |
| 17 | Thiếu tên công ty trong modal + dữ liệu | Thêm `CompanyName` vào projection backend, hiển thị `<Tag>` trong modal |
| 18 | Thiếu cột "Người cấp phát" trong lịch sử | Thêm `CreatedByUserId` vào entity + `CreatedByUser` navigation + column trong frontend |
| 19 | `column CreatedByUserId does not exist` | Thêm `ALTER TABLE consumable_checkouts ADD COLUMN IF NOT EXISTS "CreatedByUserId"` vào `Program.cs` schema update block |

### 6.2 Performance

- ProList `request` prop xử lý debounce tự động cho search
- Không sử dụng `useEffect` cho data fetching — giảm re-render
- `useCallback` cho `loadCompanies` để tránh re-fetch không cần thiết
- User list được cache trong state, chỉ fetch một lần khi mở modal

### 6.3 Responsive Design

| Breakpoint | Grid columns | Card layout |
|---|---|---|
| **xs** (<576px) | 1 | Mobile: 1 cột, cards xếp dọc |
| **sm** (≥576px) | 1 | Tablet nhỏ: 1 cột |
| **md** (≥768px) | 2 | Tablet: 2 cột |
| **lg** (≥992px) | 2 | Desktop nhỏ: 2 cột |
| **xl** (≥1200px) | 3 | Desktop lớn: 3 cột |
| **xxl** (≥1600px) | 3 | Màn hình lớn: 3 cột |

---

## 7. Hướng dẫn mở rộng trong tương lai

1. **Khởi động lại AppHost** — để schema update (`ALTER TABLE consumable_checkouts ADD COLUMN CreatedByUserId`) có hiệu lực
2. **Thêm `export CSV`** — tương tự `GET /export/consumables` đã có trong backend
3. **Bộ lọc nâng cao** — thêm `manufacturerId`, `supplierId` vào search form
4. **Batch checkout** — cho phép cấp phát nhiều vật tư cùng lúc
5. **Phân quyền chi tiết** — hiển thị/ẩn các nút action dựa trên policy của user hiện tại
6. **Thêm pagination cho GET /checkouts** — hiện tại trả về toàn bộ lịch sử