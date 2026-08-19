# Audit + Đề xuất thiết kế lại Layout (Sidebar + AppBar) — 2026-08-16

**Loại:** Audit tĩnh + đề xuất thiết kế. **Không có code nào bị thay đổi trong
dự án thật.** Mock minh họa là 1 file HTML/CSS/JS độc lập, tách biệt hoàn toàn
khỏi codebase (xem link Artifact cuối báo cáo).

**Phạm vi:** `AppLayout` trong [App.tsx](../aspire-react/frontend/src/App.tsx)
(dòng 50-164) — component duy nhất dựng Sidebar + Header, dùng chung cho MỌI
trang.

---

## Bước 1 — Audit hiện trạng

### 1.1 Sidebar

**Cấu trúc thực tế** (`App.tsx:60-93`, mảng `menuItems`): 11 mục top-level,
trong đó 2 mục có submenu (`inventory` → 3 mục con: Vật tư tiêu hao/Linh
kiện/Phụ kiện; `admin` → 10 mục con) → tổng ~21 điểm điều hướng.

| # | Vấn đề | Bằng chứng | Mức độ |
|---|---|---|---|
| 1 | **Menu phẳng, không phân nhóm ngữ nghĩa** | Dashboard, Vật tư, Bản quyền, Tài sản, Bảo trì, Lịch sử hệ thống, Báo cáo (nghiệp vụ) đứng **ngang hàng** với Người dùng, Nhóm, Phân quyền, Quản trị (hệ thống/quản trị) trong cùng 1 mảng phẳng (`App.tsx:60-93`) — không divider, không label nhóm | UX |
| 2 | **Active state SAI — bug xác nhận được** | `App.tsx:105`: `defaultSelectedKeys={['/assets']}` — dùng `defaultSelectedKeys` (chỉ gán 1 lần lúc mount) thay vì `selectedKeys` controlled theo `location.pathname` hiện tại. Menu **không có `useLocation()`** nào trong `AppLayout`. Hệ quả: mục "Tài sản" luôn được tô sáng bất kể đang ở trang nào — chỉ đúng tình cờ khi user đang ở `/assets` | **Bug thật, ưu tiên cao** |
| 3 | **Không permission-gate menu item nào** | `menuItems` render vô điều kiện, không có `usePermission`/filter nào trước khi đưa vào `<Menu items={menuItems}>` (`App.tsx:101-109`). User thiếu quyền vẫn thấy đủ 21 mục, bấm vào mới biết bị chặn (403 hoặc trang rỗng tùy trang con) | UX + tính nhất quán với backend (mỗi trang con đã tự gate nội dung, nhưng menu thì không) |
| 4 | **Collapse tồn tại trong code nhưng không thể bật qua UI** | `Sider collapsible collapsed={collapsed} onCollapse={setCollapsed} trigger={null}` (`App.tsx:114-119`) — `trigger={null}` tắt nút toggle mặc định của AntD, và **không có nút tùy chỉnh nào thay thế** trong JSX. `collapsed` state tồn tại nhưng không ai gọi `setCollapsed` | Bug/thiếu sót nhỏ, dễ sửa |
| 5 | **Responsive — đã làm ĐÚNG, giữ nguyên** | Dưới `md` (768px, `Grid.useBreakpoint`, `App.tsx:53-56`): Sider ẩn hoàn toàn, thay bằng `Drawer` mở qua nút hamburger (`App.tsx:113,129-136,149-161`) | Không cần sửa |

### 1.2 AppBar/Header

**Xác nhận đúng qua đọc toàn bộ JSX** (`App.tsx:128-143`): Header chỉ chứa
đúng 2 phần tử — nút hamburger (chỉ hiện khi mobile) và **1 nút
Logout/Login duy nhất**. Không có phần tử nào khác bị ẩn ở breakpoint khác —
đã đọc hết, không có `Avatar`/`Badge`/`Breadcrumb`/dropdown nào trong file.

**Dữ liệu đã có sẵn nhưng chưa dùng (quan trọng cho việc ước lượng effort):**

| Dữ liệu cần cho AppBar mới | Nguồn đã tồn tại | Cách lấy | Chi phí |
|---|---|---|---|
| Tên, email, username user hiện tại | `getUserInfo()` — [`services/keycloak.ts:55-61`](../aspire-react/frontend/src/services/keycloak.ts) | Đọc trực tiếp `keycloak.tokenParsed` (JWT đã giải mã), **đồng bộ, không gọi API**. Xác nhận **0 caller hiện tại** trong toàn bộ `frontend/src` (grep `getUserInfo` chỉ khớp định nghĩa) — hàm viết sẵn nhưng chưa từng được dùng | **Miễn phí, có ngay** |
| Superuser hay không (để hiển thị badge vai trò) | `isSuperUser()` — cùng file, đồng bộ | Đọc `keycloak.tokenParsed.realm_access`/`permission` claim | **Miễn phí** |
| **Tên công ty hiện tại của user** (quan trọng nhất cho company-scoping) | **`GET /api/v1/users/me`** — [`UsersController.cs:144-212`](../aspire-react/aspire-react.Server/Web/Controllers/UsersController.cs) đã trả đủ `CompanyName`, `DepartmentName`, `LocationName`, `JobTitle`, `Groups`, `IsSuperUser` — **endpoint đã tồn tại, đã include `Company`/`Department`/`Location`, nhưng frontend CHƯA GỌI Ở ĐÂU CẢ** (grep `/users/me` trong `frontend/src` → 0 kết quả) | 1 lần gọi API khi load AppBar (có thể cache như `usePermission` đang làm) | **Rẻ — endpoint có sẵn, không cần sửa backend, chỉ cần 1 lần fetch ở frontend** |
| Số lượng cảnh báo Low Stock | `GET /dashboard/summary` → field `lowStockCount` (đã dùng ở `DashboardPage.tsx:16,102`) | Gọi lại endpoint đã có | **Rẻ — endpoint có sẵn** |
| Số lượng bảo trì quá hạn | `GET /dashboard/summary` → field `overdueAudits` | Gọi lại endpoint đã có | **Rẻ — endpoint có sẵn** |
| Số lượng License sắp hết hạn | `GET /licenses?expiringSoon=true` (filter đã tồn tại — [`LicensesController.cs:49,82,92`](../aspire-react/aspire-react.Server/Web/Controllers/LicensesController.cs)) trả **danh sách** license sắp hết hạn, có `total` trong response phân trang | Gọi list với `pageSize=1` và đọc `total`, hoặc gọi nguyên danh sách nếu nhỏ | **Trung bình — dùng được endpoint có sẵn nhưng KHÔNG có field đếm gộp sẵn (không giống `lowStockCount`), phải tự suy ra `total` từ response phân trang — hơi vòng, không sạch bằng 2 cái trên** |
| **1 endpoint tổng hợp "tất cả cảnh báo" cho 1 lần gọi duy nhất** (thay vì AppBar phải tự gọi 3 endpoint riêng lẻ mỗi lần load) | **Không tồn tại** | — | **Cần backend bổ sung nếu muốn notification bell tối ưu** (không bắt buộc — có thể ghép 3 endpoint có sẵn ở frontend, chỉ là kém tối ưu hơn 1 lần gọi) |

**Kết luận Bước 1:** phần lớn dữ liệu cho AppBar mới (user info, company,
low-stock count, overdue-maintenance count) **đã có sẵn, chi phí gần như 0**
— chỉ riêng license-expiring-count là cần ghép thêm 1 lệnh gọi hơi vòng, và
"1 endpoint tổng hợp cảnh báo duy nhất" (tối ưu hoá, không bắt buộc) mới thật
sự cần backend làm thêm.

---

## Bước 2 — Đề xuất thiết kế mới

> **Xem mock tương tác (không đụng code thật):** [Artifact — Layout AppBar/Sidebar mới](#) *(link gửi kèm cuối phản hồi)*. Mock dùng đúng token màu từ `designTokens.ts`/`MASTER.md` (`primary #0F172A`, `accent #0369A1`, font Plus Jakarta Sans), có thể bấm thử collapse sidebar, mở dropdown user, và xem preview ở 375px.

### 2.1 Sidebar

| Đề xuất | Vấn đề UX giải quyết | Dữ liệu |
|---|---|---|
| **Phân nhóm menu**: nhóm "TỔNG QUAN" (Dashboard) / "NGHIỆP VỤ" (Vật tư, Bản quyền, Tài sản, Bảo trì, Lịch sử hệ thống, Báo cáo) / "HỆ THỐNG" (Người dùng, Nhóm, Phân quyền) / "QUẢN TRỊ" (submenu 10 mục), mỗi nhóm có label nhỏ + divider, dùng `Menu` với cấu trúc `type:'group'` hoặc divider thủ công | Người dùng phân biệt ngay "đây là nơi làm việc hàng ngày" vs "đây là khu vực cấu hình hệ thống" — giảm thời gian quét mắt tìm mục trong danh sách 21 mục phẳng | **Có sẵn** — chỉ tổ chức lại `menuItems`, không cần data mới |
| **Sửa active state**: thay `defaultSelectedKeys` bằng `selectedKeys={[location.pathname]}` (controlled, dùng `useLocation()`), kèm chỉ báo trực quan hơn màu nền (VD thanh accent bên trái mục đang chọn) | Sửa bug đang có (mục "Tài sản" bị highlight sai ở mọi trang) + giúp định hướng rõ "tôi đang ở đâu" | **Có sẵn** — `useLocation()` từ `react-router-dom` đã là dependency |
| **Thêm nút collapse thật sự dùng được**: đặt 1 nút toggle (icon `<<`/`>>`) ở chân Sider hoặc header, gọi `setCollapsed` (state đã tồn tại, chỉ thiếu nút bấm) | Cho phép người dùng chủ động thu gọn để có thêm không gian màn hình khi làm việc với bảng/form rộng | **Có sẵn** — chỉ thiếu 1 nút UI |
| **Ẩn/hiện mục menu theo quyền** (dùng `usePermissionMap()` đã có) — mục nào user không có quyền `.view` tương ứng thì không hiển thị thay vì hiển thị rồi 403 | Giảm nhiễu, tránh trải nghiệm "thấy mà không bấm được" | **Có sẵn** — `usePermissionMap()` đã tồn tại (`hooks/usePermission.ts:70-84`), chỉ cần map permission code theo từng resource sang từng menu item |
| **Badge số lượng cảnh báo** trên "Vật tư" (Low Stock) và "Bản quyền" (sắp hết hạn) — *tùy chọn, đánh giá thêm bên dưới* | Cảnh báo sớm ngay từ sidebar mà không cần vào từng trang | **Vật tư: có sẵn** (`lowStockCount`). **Bản quyền: cần ghép thêm lệnh gọi** như đã nêu ở Bước 1 |

**Không đổi:** hành vi responsive (Drawer ở <768px) — đã đúng, giữ nguyên khi
áp dụng grouping/active-state mới vào cả 2 chỗ định nghĩa menu
(`siderMenu` dùng chung cho cả Sider desktop và Drawer mobile nên chỉ cần sửa
1 nơi).

### 2.2 AppBar/Header

| Đề xuất | Vấn đề UX giải quyết | Nhóm dữ liệu |
|---|---|---|
| **Avatar (chữ cái đầu tên) + tên user + dropdown** (chứa: Xem hồ sơ → `/users/:id` nếu có, Đăng xuất) — gộp nút Logout hiện đang "trơ trọi" vào trong dropdown này | Người dùng hiện KHÔNG BIẾT mình đang đăng nhập bằng tài khoản nào (đặc biệt khi có nhiều người dùng chung máy/demo) — đây là thông tin cơ bản mọi hệ thống enterprise phải có | **Dùng dữ liệu có sẵn ngay** (`getUserInfo()`, đồng bộ, đã viết sẵn nhưng chưa gọi ở đâu) |
| **Hiển thị công ty hiện tại** (badge/text cạnh tên, ẩn nếu là Superuser vì Superuser xem được nhiều công ty) | Sau khi đã fix company-scoping ở backend (Task I-V), user cần biết rõ mình đang thao tác trong phạm vi công ty nào — tránh nhầm lẫn khi tạo asset/license tưởng thuộc công ty A nhưng thực ra tài khoản đang scope công ty B | **Cần 1 lệnh gọi tới endpoint có sẵn** `GET /users/me` (không cần sửa backend — chỉ cần frontend gọi và cache, giống pattern `usePermission` đang làm) |
| **Breadcrumb** (VD "Tài sản › Chi tiết › Laptop HP EliteBook 840") | Định hướng khi vào sâu Detail/Modal — hiện không có cách nào biết "mình đang ở tầng nào" ngoài nhìn URL | **Có sẵn một phần**: route path đã đủ cấu trúc (`/assets/:id`) để suy ra 2 cấp đầu ("Tài sản › Chi tiết") tự động từ route config; cấp thứ 3 (tên record cụ thể, VD "Laptop HP EliteBook 840") cần trang Detail tự set qua context/state — **không cần API mới, nhưng cần thêm 1 đoạn code nhỏ ở mỗi trang Detail để "khai báo" tên hiển thị** — effort thấp nhưng không phải 100% miễn phí như 2 mục trên |
| **Notification bell** (tuỳ chọn — đánh giá dưới) | Cảnh báo tổng hợp mà không cần vào Dashboard | **Hỗn hợp** — xem đánh giá riêng |
| **Style**: toàn bộ dùng token có sẵn (`designTokens.token.colorPrimary`, `colorBgContainer`, `colorTextSecondary`...), không hex mới | Nhất quán với Design System đã duyệt, tránh lặp lại vấn đề "hard-code hex" đã ghi nhận ở audit Mục 3 trước đó | Không phát sinh dữ liệu, chỉ là quy ước code |

#### Đánh giá riêng: Notification bell có nên làm không?

**Khuyến nghị: làm phiên bản đơn giản trước (ghép 2/3 nguồn có sẵn), KHÔNG
chờ backend làm endpoint tổng hợp.**

- Low Stock (`lowStockCount`) + Bảo trì quá hạn (`overdueAudits`) đã có sẵn
  100% trong `/dashboard/summary` — ghép được ngay, hiển thị badge số trên
  icon chuông, click mở dropdown liệt kê 2 nhóm cảnh báo này, link sang
  trang tương ứng.
- License sắp hết hạn: có thể ghép thêm nhưng cần 1 lệnh gọi phụ hơi vòng
  (đọc `total` từ response phân trang thay vì có field đếm sẵn) — vẫn khả
  thi ngay, không cần đợi backend, chỉ hơi kém tối ưu.
- **Không đề xuất** yêu cầu backend làm 1 endpoint tổng hợp riêng ngay từ
  đầu — nên đo xem 3 lệnh gọi ghép có đủ nhanh/đủ tốt trước, chỉ xin thêm
  endpoint tổng hợp nếu sau này thấy cần tối ưu (ít lệnh gọi hơn khi AppBar
  load ở mọi trang).

### 2.3 Rủi ro khi áp dụng thật

`AppLayout` là component **dùng chung tuyệt đối cho mọi trang** (bọc toàn bộ
`<Route>` trong `App.tsx`) — bất kỳ lỗi nào ở đây (crash, vòng lặp gọi API,
race condition với `initKeycloak()`) sẽ làm **sập toàn bộ ứng dụng**, không
riêng 1 trang. Cần lưu ý khi triển khai thật (không phải lúc audit này):

1. Lệnh gọi `GET /users/me` mới thêm vào AppBar phải cache giống
   `usePermission` (module-level cache, fetch 1 lần/phiên) — tránh gọi lại
   mỗi lần chuyển trang vì `AppLayout` bọc mọi route, re-render liên tục.
2. Nếu API `/users/me` lỗi (mất mạng, token hết hạn giữa chừng), AppBar phải
   **fallback im lặng** (ẩn phần công ty, không throw) — không được làm crash
   layout khiến cả app trắng trang.
3. Breadcrumb cấp 3 (tên record) cần 1 cơ chế "khai báo" từ trang con — nếu
   làm bằng Context, phải cẩn thận không để trang cũ quên set/quên clear gây
   hiện sai tên khi chuyển trang nhanh.
4. Nên làm **tách biệt Sidebar và AppBar thành 2 PR nhỏ** thay vì 1 PR gộp
   cả 2 + notification bell — đúng tinh thần "task nhỏ độc lập" đã áp dụng ở
   audit trước, vì đây là 3 thay đổi có thể revert độc lập nếu 1 trong 3 có
   vấn đề khi lên UI thật.
5. Test bắt buộc trước khi coi là xong (không chỉ nhìn mock): build thật,
   click qua ≥10 trang khác nhau xem breadcrumb/active-state đúng, test ở cả
   3 breakpoint 375/768/1440, test với 1 tài khoản không-superuser để xác
   nhận menu ẩn đúng mục theo quyền.

---

## Tổng kết: nhóm nào làm được ngay (rẻ) vs cần thêm

| Nhóm | Cần gì thêm | Sẵn sàng làm ngay? |
|---|---|---|
| Sidebar: phân nhóm + sửa active-state bug + nút collapse | Không cần gì thêm | **Có — dữ liệu/hạ tầng đã đủ** |
| Sidebar: ẩn/hiện theo quyền | Không cần gì thêm (`usePermissionMap` có sẵn) | **Có** |
| Sidebar: badge Low Stock | Không cần gì thêm | **Có** |
| Sidebar: badge License sắp hết hạn | Cần lệnh gọi phụ (đọc `total`) | **Có, hơi vòng** |
| AppBar: avatar + tên + dropdown + gộp Logout | Không cần gì thêm (`getUserInfo()` có sẵn, chưa từng dùng) | **Có** |
| AppBar: hiển thị công ty | Không cần sửa backend (`GET /users/me` đã có, chưa từng gọi) — chỉ cần frontend gọi + cache | **Có** |
| AppBar: breadcrumb 2 cấp đầu (theo route) | Không cần gì thêm | **Có** |
| AppBar: breadcrumb cấp 3 (tên record cụ thể) | Cần thêm đoạn code nhỏ ở mỗi trang Detail để khai báo tên | **Có, effort thấp nhưng không phải 0 giây** |
| AppBar: notification bell (Low Stock + quá hạn bảo trì) | Không cần gì thêm | **Có** |
| AppBar: notification bell (+ License sắp hết hạn) | Cần lệnh gọi phụ hơi vòng | **Có, hơi vòng** |
| AppBar: notification bell tối ưu 1-lệnh-gọi-duy-nhất | **Cần backend thêm 1 endpoint tổng hợp** | **Không — cần backend, không bắt buộc, chỉ tối ưu** |

**Không có phần nào trong đề xuất bị chặn hoàn toàn bởi thiếu backend** — mọi
thứ đều làm được ngay với dữ liệu/endpoint hiện có, trừ phiên bản "tối ưu
nhất" của notification bell (gộp 3 nguồn thành 1 lệnh gọi), vốn chỉ là cải
tiến hiệu năng, không phải điều kiện tiên quyết.

---

*Báo cáo này chỉ audit + đề xuất — chưa code vào dự án thật. Mock đính kèm
là artifact HTML/CSS/JS độc lập để xem trước hướng thiết kế trước khi duyệt.*
