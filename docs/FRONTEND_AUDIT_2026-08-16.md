# Audit tĩnh Frontend AspireReact — 2026-08-16

**Loại:** Audit tĩnh (đọc code, không chạy Aspire stack, không sửa bất kỳ file nào).
**Phạm vi:** `aspire-react/frontend/src/` (~63 file, ~14.100 dòng) đối chiếu với
`design-system/aspirereact/MASTER.md`, `STATUS-COLORS.md`,
`frontend/src/theme/designTokens.ts`, và các quy ước trong `CLAUDE.md`/
`docs/DEVELOPMENT_WORKFLOW.md`.
**Không có gì trong báo cáo này đã được sửa trong code.**

---

## Tóm tắt điều hành

| Chỉ số | Kết quả |
|---|---|
| Trang/route "mồ côi" thật sự (không menu, không code trỏ tới) | 0 — mọi route đều active (menu hoặc programmatic) |
| File chết xác nhận (0 import ở bất kỳ đâu) | 3 file (~684 dòng): `pages/admin/ModelListPage.tsx`, `components/assets/AssetFormModal.tsx`, `components/assets/ActionLogTimeline.tsx` |
| Bug điều hướng nghiêm trọng đang chạy | 1 — nút Cấp phát/Thu hồi trên `AssetListPage` gọi route không tồn tại → redirect âm thầm về `/` |
| Trang import `theme/designTokens.ts` (statusColors/assetStatusColors) | **0/45 file nghiệp vụ** — chỉ `App.tsx` (theme shell) và `DashboardPage.tsx` dùng |
| Bản đồ màu trạng thái độc lập, không qua token | ≥6 bản (Asset, Accessory, Component, Consumable, Maintenance, License) |
| Bản `ACTION_TYPE_TAGS` bị khai riêng, lệch nhau | 3 bản (Dashboard 20 giá trị chuẩn dạng string-key, ActionLogTable 17 giá trị number-key, Accessory 10 giá trị lệch màu, Consumable 6 giá trị lệch màu) |
| Vi phạm bug-class "enum number-key vs backend string" (mục 6) | **0 xác nhận còn sống** — mọi chỗ dùng `Record<number,...>` đã kiểm chứng đều được backend cấp field số thật hoặc có bước normalize string→number đúng (chi tiết ở Mục 6) |
| Nút hành động thiếu hoàn toàn permission gate | 0 trên trang đang route thật (1 trên `ModelListPage.tsx` — nhưng file này là dead code) |
| Điểm chọn công ty không dùng `CompanyTreeSelect` | 6/13 |
| Vi phạm contrast text 4.5:1 (`#999`) | 5 chỗ |
| Emoji dùng làm icon (vi phạm MASTER.md) | 2 chỗ |

---

## Mục 1 — Kiểm kê route/trang

Toàn bộ 30 `<Route>` trong `App.tsx:186-328` đối chiếu với `menuItems`
(`App.tsx:60-93`) và toàn bộ `navigate(`/`<Link to=` trong `frontend/src`.

**Kết luận: không có route nào "mồ côi" theo đúng nghĩa (route tồn tại nhưng
không ai trỏ tới, kể cả programmatic).** Mọi route không có mục menu riêng
(`/assets/:id`, `/assets/:id/edit`, `/accessories/new`, `/accessories/:id`,
`/accessories/:id/view`, `/users/:id`, `/systems/:id`, và các route
`/consumables|components|licenses` phụ) đều được gọi từ `navigate()`/`<Link>`
ở đâu đó — xem bảng chi tiết trong phần "Chức năng thừa/trùng lặp" bên dưới
cho các trường hợp đáng chú ý.

**File chết xác nhận (không route, không import ở bất kỳ đâu):**

| File | Bằng chứng | Ghi chú |
|---|---|---|
| `pages/admin/ModelListPage.tsx` | `App.tsx` chỉ import `AssetModelListPage` (khác file, dòng 37); grep `ModelListPage` toàn repo chỉ khớp chính file này | Gọi API `/models` (khác `/asset-models` mà bản đang dùng gọi) — bản cũ song song. **Không có bất kỳ `usePermission`/`isSuperUser` nào** trên nút Thêm/Xóa của file này, nhưng vô hại vì không route |
| `components/assets/AssetFormModal.tsx` | Grep toàn repo chỉ khớp định nghĩa trong chính file (365 dòng) | Bị thay bởi `CreateAssetFlowModal` viết inline trong `AssetListPage.tsx:191` |
| `components/assets/ActionLogTimeline.tsx` | Grep toàn repo chỉ khớp chính file (160 dòng) | Bị thay bởi `ActionLogTable.tsx` |

**Bug điều hướng nghiêm trọng (không phải "mồ côi" nhưng cùng bản chất — route
không tồn tại):**

`AssetListPage.tsx:131,133` gọi `navigate(\`/assets/${id}/allocate\`)` và
`navigate(\`/assets/${id}/recall\`)` cho nút "Cấp phát"/"Thu hồi" trên Card ở
trang danh sách. **Hai route này không được khai báo trong `App.tsx`** — khớp
route catch-all `*` (`App.tsx:328`) → redirect thẳng về `/`. Người dùng bấm
nút, thao tác **thất bại âm thầm, không có thông báo lỗi**. Trong khi đó
`AssetDetailPage.tsx` xử lý đúng bằng cách mở `AssetAllocationModal`/
`AssetRecallModal` tại chỗ (state cục bộ, không navigate) — 2 luồng đã lệch
nhau khi migrate. → xem Nhóm Task 🔴 #2 bên dưới.

---

## Mục 2 — Chức năng thừa/trùng lặp

### 2.1 Service method không còn caller

| Method | File:dòng | Ghi chú |
|---|---|---|
| `consumablesApi.*` (toàn bộ 7 method: list/get/create/update/delete/checkout/lowStock) | `services/consumables.service.ts:13-21` | 0 caller — mọi nơi tự gọi `apiClient` trực tiếp, trùng URL string ở 5 file (`ConsumableFormModal.tsx`, `ConsumableDetailPage.tsx`, `ConsumableListPage.tsx`, `ConsumableCheckoutModal.tsx`) |
| `accessoriesApi.list/create/update` | `services/accessories.service.ts:112,114,115` | 0 caller — `AccessoryListPage.tsx:293`, `AccessoryFormPage.tsx:81,102,105` tự gọi `apiClient` trực tiếp |
| `assetService.getHistory` | `services/asset.service.ts:140` | 0 caller |
| `assetService.listMaintenances` | `services/asset.service.ts:143` | 0 caller — nơi cần dùng `listAllMaintenances({assetId,...})` thay thế |
| `componentsApi.assign` / `.remove` | `services/components.service.ts:74-75` | 0 caller |
| `licensesApi.getSeats` | `services/licenses.service.ts:92` | 0 caller |

### 2.2 Logic bị viết trùng lặp thay vì tái sử dụng

- **`MAINTENANCE_TYPE_LABELS`/`MAINTENANCE_TYPE_VALUE`** khai trùng byte-for-byte
  giữa `components/maintenances/MaintenanceTable.tsx:21-30,41-50` (export) và
  `components/assets/AssetMaintenanceSection.tsx:12-21,27-36` (khai lại, không
  export). Riêng `MAINTENANCE_TYPE_COLORS` (`MaintenanceTable.tsx:32-35`)
  **không tồn tại** ở bản sao — `AssetMaintenanceSection.tsx:280,450` render
  Tag loại bảo trì **không màu**, trong khi `MaintenanceTable.tsx:419-420,548-549`
  có màu → cùng khái niệm hiển thị khác nhau tùy nơi bấm vào.

- **`ACTION_TYPE_TAGS` khai độc lập ở 3 nơi, đã lệch nhau (không chỉ thiếu, mà
  còn SAI màu ở các entry chung):**
  - Chuẩn (export, 17 entry, 1-17): `components/assets/ActionLogTable.tsx:32-50`
  - `pages/AccessoryDetailPage.tsx:23-34` — chỉ 10 entry (1-10); entry 1,2,4,5,6,7,8,9,10
    dùng **màu khác** bản chuẩn (vd 1: chuẩn `green` → local `blue`; 9: chuẩn
    `green`/"Chấp nhận" → local `purple`/"Accept" — **tiếng Anh** lẫn vào UI
    tiếng Việt); thiếu hoàn toàn 11-17.
  - `pages/ConsumableDetailPage.tsx:80-87` — chỉ 6 entry (1,2,3,4,5,11), màu
    cũng lệch bản chuẩn ở 1,2,4,5,11; thiếu 6-10,12-17.
  - Cả hai đều **không import** bản chuẩn. Fallback khi giá trị không khớp
    (`AccessoryDetailPage.tsx:156`, `ConsumableDetailPage.tsx:229`) hiện
    **tên enum tiếng Anh thô** (`record.actionType`, vd "Reopen", "Dispose")
    thay vì nhãn tiếng Việt đã dịch — với Accessory là 7/17 giá trị, với
    Consumable là 11/17 giá trị action-type sẽ hiện sai kiểu này.

- **Không có `utils/` hay formatter dùng chung** — `formatDate`/`formatDateTime`
  khai riêng lẻ ở ≥9 file (`AccessoryDetailPage.tsx:96`, `AssetDetailPage.tsx:41`,
  `LicenseDetailModal.tsx:17,21`, `ConsumableDetailPage.tsx:156`,
  `components/assets/LicenseUsageTable.tsx:5,9`, `AssetMaintenanceSection.tsx:38`,
  `SystemDetailPage.tsx:24`, `AssetListPage.tsx:174`, `LicenseListPage.tsx:28`);
  `formatMoney` khai lại ở `AssetMaintenanceSection.tsx:42` và `AssetListPage.tsx:186`.

### 2.3 Asset/Accessory: Form pattern cũ (navigate) chưa migrate sang Modal

Xác nhận bằng file:line — **Consumable/Component/License đã hoàn tất chuyển
sang Modal mở tại chỗ; Asset và Accessory thì chưa**, đúng như nghi vấn nêu
trong đề bài (tiền lệ `ConsumableFormPage` đã xóa):

| Domain | Nút | Hành vi | Route đích |
|---|---|---|---|
| Consumable | Thêm/Sửa | `location.pathname` → mở `ConsumableFormModal` tại chỗ (`ConsumableListPage.tsx:87-100`, comment "Task A lesson") | route trỏ về lại chính `ConsumableListPage` |
| Component | Thêm/Sửa | tương tự, `ComponentListPage.tsx:83-88` mở `ComponentFormModal` | trỏ về `ComponentListPage` |
| License | Thêm/Sửa/Xem | tương tự, `LicenseListPage.tsx:106-114` mở `LicenseFormModal`/`LicenseDetailModal`/`LicenseCheckoutModal` | trỏ về `LicenseListPage` |
| **Asset** | Sửa | `navigate(\`/assets/${id}/edit\`)` (`AssetListPage.tsx:125`) | route riêng `AssetFormPage` (full page) |
| **Asset** | Tạo | `CreateAssetFlowModal` inline (`AssetListPage.tsx:75,149,191`) | **không nhất quán ngay trong chính trang này** — Tạo=modal, Sửa=navigate trang khác |
| **Accessory** | Thêm/Sửa/Xem | `navigate('/accessories/new')`, `navigate(\`/accessories/${id}\`)`, `navigate(\`/accessories/${id}/view\`)` (`AccessoryListPage.tsx:284,426,434`) | 3 route riêng, không Modal nào |

`AssetFormPage.tsx`/`AccessoryFormPage.tsx` **được dùng thật, không phải dead
code** — đây là kiến trúc cũ chưa migrate, không phải rác.

---

## Mục 3 — Đối chiếu Design System theo từng trang chính

**Phát hiện nền tảng, áp dụng cho toàn bộ 45 file nghiệp vụ đã audit:**
grep `designTokens|statusColors|assetStatusColors` trên toàn `frontend/src`
chỉ khớp `App.tsx` (theme shell qua `ConfigProvider`) và `theme/designTokens.ts`
chính nó, cộng `DashboardPage.tsx`. **Không một trang nghiệp vụ nào (Asset,
Accessory, Component, Consumable, License, Maintenance, User, Group, Admin,
System) import token màu trạng thái đã định nghĩa.** Mỗi nơi tự vẽ lại map
màu bằng Ant preset-name hoặc hex hard-code. Đây là gap lớn nhất của toàn bộ
audit — chi tiết theo từng domain bên dưới.

### 3.1 Asset

| Hạng mục | Kết quả |
|---|---|
| Hard-code màu | `types/asset.ts:23-27` (`ASSET_STATUS_COLORS`) là bản sao **song song, khác cơ chế** với `assetStatusColors` (hex) ở `designTokens.ts:76-80` — bản trong `types/asset.ts` mới là cái thực sự được dùng (`SystemDetailPage.tsx:152` import từ đây); `assetStatusColors` trong token là **dead code**. Thêm hex thô: `AssetListPage.tsx:100`, `AssetFormModal.tsx:157`, `AssetRecallModal.tsx:47`, `AssetArchiveModal.tsx:47`, `AssetFormPage.tsx:122,133` |
| Đối chiếu STATUS-COLORS.md | Đúng *ngữ nghĩa* (Pending→ready, Deployed→active, Archived→closed) nhưng sai *nguồn* (preset-name tay, không qua hex token) |
| Pre-delivery | Responsive: Pass (`grid={{xs:1,sm:1,md:2,lg:2,xl:3,xxl:3}}`). Reduced-motion: Pass (global CSS). Chip overflow: cần lưu ý (assetTag không ellipsis, rủi ro thấp) |
| Đánh giá | **Visual hierarchy: Đạt** — thứ tự tên→mã→badge→data-grid→actions rõ ràng. **Micro-interaction: Cần cải thiện** — `AssetListPage.tsx:97` khai `transition:'box-shadow 0.2s'` nhưng Card không có `hoverable`, không `:hover` nào dùng tới → khai báo chết. **Loading/empty/error: Đạt** — `Empty` có CTA, `Spin`+`message.error` đầy đủ. **Density: Đạt** — đúng dial 8/10. |

### 3.2 Accessory

| Hạng mục | Kết quả |
|---|---|
| Hard-code màu | Gradient `#f0e6ff→#d4baff` (`AccessoryListPage.tsx:29`), `#722ed1` (`:324`); `ACTION_TYPE_TAGS` riêng (xem Mục 2.2); 5 cặp bg/border hex cho stat-card (`AccessoryDetailPage.tsx:306-321`) — **copy-paste giống hệt** sang `ConsumableDetailPage.tsx:347-370` |
| Đối chiếu STATUS-COLORS.md | **Xung đột ngữ nghĩa cụ thể**: "Sẵn sàng/chưa dùng" (`AccessoryListPage.tsx:358-366`) tô `color="success"` (~xanh lá `#52c41a`) — đúng ra bucket **ready** phải xanh dương `#1677ff`; xanh lá đang bị Asset dùng cho "Deployed/đang dùng" — **hai trạng thái đối lập ở hai entity khác nhau cùng màu xanh lá**, dễ nhầm khi scan nhanh nhiều màn hình. "Đang cấp phát" dùng `orange` — không khớp bucket nào trong 5 bucket chuẩn. |
| Pre-delivery | Chip overflow: **Fail** — `record.itemNo` chỉ `whiteSpace:'nowrap'`, thiếu `maxWidth`/`overflow`/`textOverflow` (`AccessoryListPage.tsx:331-346`) |
| Đánh giá | **Visual hierarchy: Đạt**. **Micro-interaction: Cần cải thiện** — Card có `hoverable` thật (transition hoạt động, khác Asset) nhưng không `onClick` → shadow "mời click" nhưng bấm vùng trống không làm gì (affordance mismatch). **Loading: Cần cải thiện** — Checkin modal dùng text thường thay vì `Spin` (`AccessoryListPage.tsx:510-512`). **Density: Đạt**. |

### 3.3 Component (linh kiện)

| Hạng mục | Kết quả |
|---|---|
| Hard-code màu | Gradient `#f0f5ff→#adc6ff` (`ComponentListPage.tsx:48`); `UNIT_STATUS_TAGS` riêng (`ComponentDetailPage.tsx:19-24`): InStock=green (đáng lẽ ready=xanh dương), Allocated=orange (không khớp bucket nào) |
| Pre-delivery | Chip overflow: **Pass — domain duy nhất fix đúng** (`ComponentListPage.tsx:264-282` có đủ `maxWidth/overflow/textOverflow`), dù comment trong code tự nhận "đồng bộ Accessory/Consumable" — **thực tế 2 domain kia CHƯA áp dụng fix này** (ngược lại với comment) |
| Đánh giá | **Visual hierarchy: Đạt**. **Micro-interaction: Cần cải thiện** — cùng affordance-mismatch `hoverable` không `onClick`. **Loading/empty/error: Cần cải thiện** — `ComponentDetailPage.tsx:162` gộp chung trạng thái loading và "không tìm thấy" vào 1 `Spin` → id sai sẽ hiện spinner treo vô thời hạn thay vì thông báo lỗi. **Density: Đạt** (`Descriptions column={4} bordered`). |

### 3.4 Consumable

| Hạng mục | Kết quả |
|---|---|
| Hard-code màu | Gradient riêng (`:45`); `ACTION_TYPE_TAGS` bản thứ 3 (Mục 2.2); 4 cặp hex stat-card **giống hệt copy** từ Accessory |
| Đối chiếu STATUS-COLORS.md | `isConfirmed ? 'success' : 'warning'` cho Đã/Chờ xác nhận — ánh xạ **gần đúng nhất** với bucket pending trong 4 domain, nhưng dùng Ant preset `warning` (~`#faad14`) thay vì hex chuẩn `#d48806`, không import token |
| Pre-delivery | Chip overflow: **Fail** — cùng lỗi thiếu `maxWidth` như Accessory (`ConsumableListPage.tsx:281-296`), khác Component đã fix |
| Đánh giá | **Visual hierarchy: Đạt**. **Micro-interaction: Cần cải thiện** — cùng affordance-mismatch. **Loading/empty/error: Đạt** — phân biệt rõ Spin/Empty/error, tốt hơn Component. **Density: Đạt**. |

### 3.5 License

| Hạng mục | Kết quả |
|---|---|
| Hard-code màu | `DEFAULT_CATEGORY_COLOR='#2f54eb'`; cam/đỏ hết hạn — **đúng như STATUS-COLORS.md cho phép giữ nguyên**; nhưng "còn trống ghế" có **2 cách tô khác nhau cho cùng ngữ nghĩa** không nằm trong tài liệu: hex `#389e0d/#cf1322` (`LicenseListPage.tsx:377`) vs CSS keyword `green/red` (`LicenseDetailModal.tsx:135`) — drift thật |
| `scroll={{x:true}}` | Đủ ở mọi bảng trong domain này |
| Pre-delivery | **cursor:pointer FAIL** — `LicenseListPage.tsx:302` dùng `<a onClick>` không `href` → mất pointer mặc định |
| Đánh giá | **Đạt** tổng thể — visual hierarchy tốt, loading/empty/error đầy đủ mọi luồng, density đúng dial. Chip serial có ellipsis đúng chuẩn — nên nhân rộng mẫu này sang domain khác. |

### 3.6 Maintenance

| Hạng mục | Kết quả — **phát hiện quan trọng nhất mục 3** |
|---|---|
| Hard-code màu | `MAINTENANCE_STATUS_BADGE_COLORS={in_progress:'#1677ff',completed:'#52c41a',closed:'#8c8c8c'}` (`MaintenanceTable.tsx:84-86`) — giá trị **đúng bằng** `statusColors.ready/active/closed` nhưng chép tay, không import. Trong khi đó Tag trạng thái cạnh bên (`:359-360`) dùng preset AntD `success`/`processing`, và `designTokens.ts:22,25` đã **override** `colorSuccess='#16A34A'`, `colorInfo='#0369A1'` (khác mặc định AntD `#52c41a`/`#1677ff`). Kết quả: **icon badge và Tag trạng thái trên cùng 1 Card hiển thị 2 sắc xanh khác nhau cho cùng một trạng thái** — hệ quả trực tiếp của việc không dùng chung nguồn màu. |
| Ghi chú thêm | `MaintenanceTable.tsx:360` dùng icon `CloseOutlined` (biểu tượng phủ định) cho trạng thái "Đang thực hiện" — sai ngữ nghĩa icon |
| Pre-delivery | cursor:pointer **FAIL** (`<a onClick>` không href, dòng 426, cùng lỗi License). Responsive **FAIL** — `Form.Item style={{width:400}}` (`MaintenanceListPage.tsx:197`) trong `<Space flexWrap>` không breakpoint riêng, rộng hơn viewport 375px |
| Đánh giá | **Cần cải thiện** — Micro-interaction: Cần cải thiện (color mismatch + icon sai nghĩa + cursor bug). Visual hierarchy/Loading/Density: Đạt. |

### 3.7 User / Group / Permission

| Hạng mục | Kết quả |
|---|---|
| Hard-code màu | `UserDetailPage.tsx:66`: "Đã khóa" (Inactive) tô `color="red"` — **đúng ra thuộc bucket closed (#8c8c8c xám)** theo STATUS-COLORS.md nhưng đang đỏ như "overdue", dễ nhầm với cảnh báo quá hạn |
| `scroll={{x:true}}` | Đủ ở cả 3 trang |
| Pre-delivery | **Contrast FAIL 5 lần**: `#999` trên nền trắng ≈2.85:1 (< 4.5:1 yêu cầu) — `GroupListPage.tsx:91`, `PermissionMatrixPage.tsx:83,95,148,218`. **Emoji-as-icon FAIL**: `PermissionMatrixPage.tsx:220` dùng `⚠️` — vi phạm trực tiếp MASTER.md |
| Đánh giá | **Cần cải thiện**, kéo bởi 2 vi phạm a11y trên. UserFormModal dùng nhãn tiếng Anh (First Name/Last Name) giữa UI tiếng Việt — lệch tông. |

### 3.8 Admin master-data (10 trang)

| Hạng mục | Kết quả |
|---|---|
| Hard-code màu | `CategoryListPage.tsx:50,198` dùng `#1890ff` — hex AntD v4 cũ, không khớp cả accent hiện tại (`#0369A1`) lẫn bucket `ready` (`#1677ff`) |
| Emoji-as-icon | **FAIL** — `StatusLabelListPage.tsx` dòng 1: `render:(v)=>v?'✅':'❌'` ×2 |
| `scroll={{x:true}}` thiếu | 1 chỗ: `SystemInfoListPage.tsx:175` — bảng lồng trong `expandedRowRender` |
| Tính năng thiếu (không phải UI) | `StatusLabelListPage.tsx`, `DepreciationListPage.tsx` không có CRUD/permission gate nào (chỉ list+reload), khác 8 trang admin chị em còn lại đều gate đủ create/edit/delete |
| Đánh giá | **Cần cải thiện** — Micro-interaction: Yếu (emoji + thiếu scroll). Tính nhất quán chức năng: Yếu (2/10 trang thiếu CRUD). |

### 3.9 Reports / System pages

| Hạng mục | Kết quả |
|---|---|
| Hard-code màu | `SystemDetailPage.tsx:152` dùng `ASSET_STATUS_COLORS` từ `types/asset.ts` (không phải `assetStatusColors` chính thống trong `designTokens.ts`) — cùng vấn đề "2 nguồn song song" nêu ở Mục 3.1 |
| Pre-delivery | **Table loading FAIL** — `ReportsPage.tsx` bảng khấu hao không có prop `loading=` dù nút "Tải báo cáo" có `loading` riêng. cursor:pointer **Pass, mẫu tốt**: `SystemDetailPage.tsx:242` dùng `onRow` style cursor:pointer đúng chuẩn |
| Đánh giá | **SystemDetailPage: Đạt**. **SystemHistoryPage: Đạt**. **ReportsPage: Yếu** — thiếu loading Table, không permission-gate 2/3 tab, code chưa format (1 dòng minified). |

### Gap lặp lại nhiều nhất toàn Mục 3 (ứng viên "lan rộng Design System" ưu tiên cao nhất)

1. **0/45 file nghiệp vụ import `theme/designTokens.ts`** — mọi màu trạng thái
   là bản chép tay hoặc preset AntD, dẫn tới ít nhất 2 trường hợp có bằng
   chứng cụ thể là **cùng 1 trạng thái hiển thị 2 màu khác nhau trên cùng 1
   màn hình** (Maintenance icon-vs-tag; Asset dead-token-vs-live-copy).
2. Bucket **ready** (xanh dương) bị nhầm thành xanh lá (bucket **active**) ở
   Accessory ("Sẵn sàng") và Component ("InStock") — đối lập ngữ nghĩa với
   Asset dùng xanh lá cho "Deployed".
3. `ACTION_TYPE_TAGS`: 3 bản độc lập, lệch màu, lệch số lượng entry.
4. Chip/badge overflow: fix đã có ở Component nhưng chưa lan sang
   Accessory/Consumable dù comment code tự nhận đã đồng bộ.
5. `<a onClick>` thiếu `href` mất `cursor:pointer` mặc định: License, Maintenance.
6. Text `#999` dưới ngưỡng contrast: 5 lần, tập trung Group/Permission.

---

## Mục 4 — ProComponents

| Trang | Pattern hiện tại | Kỳ vọng | Đạt? |
|---|---|---|---|
| Asset/Accessory/Consumable/Component/License List | `ProList` | `ProList` | **Đạt** cả 5 |
| Detail pages (sub-table log/seat) | `ProTable` | `ProTable` | **Đạt** |
| User/Group/Permission/8-10 trang Admin | `ProTable` | `ProTable` | **Đạt** |
| `pages/admin/StatusLabelListPage.tsx` | `<Table>` thô | `ProTable` | **Chưa đạt** |
| `pages/admin/DepreciationListPage.tsx` | `<Table>` thô | `ProTable` | **Chưa đạt** |
| `pages/admin/ModelListPage.tsx` | `<Table>` thô + Modal tự chế | `ProTable` (nếu còn dùng) | **Chưa đạt / dead code** (xem Mục 1) |
| `pages/ReportsPage.tsx` | `<Table>` thô | `ProTable` (khuyến nghị, không bắt buộc) | **Chưa đạt**, ưu tiên thấp |

Form pattern (đã chi tiết ở Mục 2.3): Component/License/Consumable **đạt**
(Modal tại chỗ); **Asset/Accessory chưa đạt** (vẫn navigate sang page riêng).

---

## Mục 5 — Permission / Company-scoping

- **Toàn bộ ~50 call site `usePermission('<code>')`** đối chiếu với
  `Infrastructure/Authorization/PermissionCatalog.cs` — **không có mã quyền
  nào frontend gọi mà backend không định nghĩa** (không có nút "chết" do
  typo permission code).
- **`UserListPage.tsx:29`**: `usePermission('admin')` — **đúng**, khớp chính
  xác policy `admin` mà backend `UsersController.UpdateUser` yêu cầu (có
  comment giải thích lý do tại chỗ).
- **`isSuperUser()`** — toàn bộ 7 call site đều thuộc 2 nhóm hợp lệ theo quy
  ước: cột/filter công ty chỉ-superuser (Asset/Component/License/Maintenance
  List), hoặc nút "Mở lại bảo trì" (Reopen) — **không có trường hợp lạm dụng**
  làm gate chính cho create/edit/delete/checkout thông thường.
- **Vấn đề ngữ nghĩa (không phải lỗ hổng nghiêm trọng):**
  `ReportsPage.tsx` gate nút "Xuất Assets CSV" bằng `usePermission('assets.view')`
  thay vì key `export` đã có sẵn trong `PermissionCatalog.cs:120`. Người chỉ
  có quyền **xem** tài sản (không có quyền export) vẫn thấy và bấm được nút
  xuất CSV — sai gate, không phải nút chết.
- **Nút hành động thiếu gate hoàn toàn:** chỉ có trên `pages/admin/ModelListPage.tsx`
  (dead code, không route — xem Mục 1). Mọi trang đang route thật đều có gate
  đầy đủ cho Create/Edit/Delete/Checkout/Checkin/Confirm/Archive/Recall/Allocate.
- **CompanyTreeSelect — 7/13 điểm chọn công ty đạt chuẩn.** 6 điểm không dùng
  component dùng chung:
  - `pages/admin/SystemInfoListPage.tsx:219-221`, `LocationListPage.tsx:266-277`,
    `DepartmentListPage.tsx:120-121` — `<Select>` phẳng, **mất khả năng chọn
    công ty con qua cây phân cấp**.
  - `components/users/UserFormModal.tsx:281-296` — tự viết lại `TreeSelect` +
    logic fetch `/companies` riêng thay vì tái dùng.
  - `pages/LicenseListPage.tsx:224-232`, `pages/ComponentListPage.tsx:178-191`
    — `<Select>` phẳng cho bộ lọc (mức độ thấp hơn, nhưng `AssetListPage.tsx:344`
    chứng minh filter cũng dùng được `CompanyTreeSelect`, nên tính nhất quán
    vẫn bị phá).
  - (`components/assets/AssetFormModal.tsx` cũng tự viết TreeSelect riêng
    nhưng file này là dead code — xem Mục 1, không cần sửa riêng, sẽ mất khi xóa file.)

---

## Mục 6 — Bug-class enum string-vs-number

**Phương pháp:** grep toàn bộ `frontend/src` cho `Record<number,...>`,
`case <số>:`, `<trường> === <số>`, object literal khoá số — sau đó **đọc từng
điểm khớp để xác minh dữ liệu được index thực sự là gì** (không chỉ dựa vào
việc tồn tại một `Record<number,...>`).

**Kết luận: không có vi phạm còn sống của đúng bug-class đã fix ở Dashboard**
(`actionTypeLabels` cũ dùng key số trong khi backend serialize enum thành
string → luôn miss). Chi tiết từng điểm nghi vấn:

| Vị trí | `Record<number,...>` có thật không | Vì sao AN TOÀN |
|---|---|---|
| `services/accessories.service.ts:69,76` (`CHECKOUT_TYPE_LABEL/COLOR_BY_VALUE`) | Có | Có bước normalize tường minh `CHECKOUT_TYPE_BY_NAME: Record<string,number>` (dòng 62-67) chuyển string→number TRƯỚC khi index — đúng pattern chuẩn, có comment giải thích rõ lý do (dòng 83-86) |
| `components/maintenances/MaintenanceTable.tsx:21,32` + `components/assets/AssetMaintenanceSection.tsx:12` (`MAINTENANCE_TYPE_LABELS/COLORS`) | Có | Luôn được index qua `MAINTENANCE_TYPE_VALUE[record.type]` (string→number map, dòng 41-49) trước — xác nhận tại mọi call site (`MaintenanceTable.tsx:398,548`, `AssetMaintenanceSection.tsx:147,280,450`). An toàn, nhưng **trùng lặp 2 file** (đã ghi ở Mục 2.2) |
| `pages/AccessoryDetailPage.tsx:23`, `pages/ConsumableDetailPage.tsx:80`, `components/assets/ActionLogTable.tsx:32` (`ACTION_TYPE_TAGS`) | Có | Backend **trả thật một field số** `ActionTypeValue = (int)l.ActionType` (xác nhận tại `ActionLogsController.cs:47,134,208,241`) song song với `ActionType.ToString()` — index theo `record.actionTypeValue` (số thật từ API, không phải suy diễn) là đúng. An toàn về mặt type, nhưng 2 bản Accessory/Consumable **thiếu/lệch entry** (đã ghi ở Mục 2.2, không phải bug string-vs-number) |
| `components/accessories/AccessoryCheckoutModal.tsx:87-119` (`switch(type){case 1...4}`) | Có (switch, không phải map) | `type` là state UI cục bộ (radio/select người dùng chọn trong modal), không phải giá trị deserialize từ enum backend — không áp dụng bug-class này |
| `types/asset.ts:9-11` (`normalizeAssetStatus`) | Không phải `Record`, nhưng có so sánh `=== 0/1/2` | Đây chính là **mẫu đúng** để tham khảo — so sánh cả string lẫn number, có comment giải thích "Backend serializes AssetStatus as int in some payloads" |

**Rà soát bổ sung:** grep toàn bộ `.ts`/`.tsx` cho `Record<number,`,
`(status|type) === <số>`, `case <số>:`, `{0: ...}` không tìm thấy điểm nào
khác ngoài các bảng trên; `api-client.ts:71,117` các `=== 401/403` là mã HTTP,
không liên quan.

**Ghi chú cho tương lai:** không phải vì hiện tại an toàn mà nên giữ nguyên
pattern — 3 bản `ACTION_TYPE_TAGS`/2 bản `MAINTENANCE_TYPE_*` độc lập là rủi ro
drift thật (đã xảy ra rồi, xem Mục 2.2), và là lý do nên hợp nhất dù bản thân
chúng chưa gây lỗi "Unknown" như trường hợp Dashboard đã fix.

---

## Kế hoạch nhóm task (Mục 1-6)

Các nhóm dưới đây độc lập với nhau trừ khi ghi chú "Phụ thuộc". Không nhóm
nào tự động bao gồm nhóm khác — chọn nhóm nào theo mức ưu tiên phù hợp.

### 🔴 Ưu tiên cao — lỗi đang chạy / dọn ngay, rủi ro thấp

| # | Tên | Phạm vi | Rủi ro | Phụ thuộc |
|---|---|---|---|---|
| **T1** | Xóa 3 file dead code | `pages/admin/ModelListPage.tsx`, `components/assets/AssetFormModal.tsx`, `components/assets/ActionLogTimeline.tsx` (~684 dòng) — xác nhận lại 0 import trước khi xóa | Rất thấp | Không |
| **T2** | Fix bug điều hướng Cấp phát/Thu hồi Asset | `AssetListPage.tsx:131,133` gọi route không tồn tại → redirect âm thầm. Sửa tối thiểu: mở `AssetAllocationModal`/`AssetRecallModal` tại chỗ (đã có sẵn component, đã dùng đúng ở `AssetDetailPage.tsx`) thay vì `navigate()` | Thấp — chỉ đổi cách mở modal đã tồn tại | Không (độc lập với T9 — xem ghi chú T9) |
| **T3** | Dọn service method chết + trùng URL string | 6 method 0-caller (Mục 2.1) — xóa hoặc thay caller hiện có (Consumable/Accessory) sang gọi qua service thay vì `apiClient` trực tiếp | Thấp | Không |

### 🟠 Ưu tiên trung bình — lan rộng Design System + nhất quán UI

| # | Tên | Phạm vi | Rủi ro | Phụ thuộc |
|---|---|---|---|---|
| **T4** | Chuẩn hoá nguồn màu trạng thái | Xóa `assetStatusColors` dead trong `designTokens.ts` HOẶC biến nó thành nguồn duy nhất; thay `ASSET_STATUS_COLORS` (`types/asset.ts`), `UNIT_STATUS_TAGS` (`ComponentDetailPage.tsx`), màu "Sẵn sàng"/"Đang cấp phát" (Accessory), badge Maintenance (`MAINTENANCE_STATUS_BADGE_COLORS`) đều import từ 1 nguồn `statusColors`. Sửa riêng: Accessory/Component "sẵn sàng" nên đổi sang bucket `ready` (xanh dương) thay vì xanh lá | Trung bình — đụng nhiều file, cần review màu thực tế trên UI trước/sau | Không, nhưng nên làm cùng đợt với T5/T6 vì cùng chủ đề màu |
| **T5** | Hợp nhất `ACTION_TYPE_TAGS` về 1 nguồn | Thay 2 bản cục bộ ở `AccessoryDetailPage.tsx`/`ConsumableDetailPage.tsx` bằng import từ `ActionLogTable.tsx` (17 entry) hoặc hợp nhất luôn với `actionTypeLabels` string-based của Dashboard thành 1 map duy nhất | Thấp-trung bình | Không |
| **T6** | Hợp nhất `MAINTENANCE_TYPE_*` | Xóa bản khai lại trong `AssetMaintenanceSection.tsx`, import từ `MaintenanceTable.tsx` (đã export sẵn) | Thấp | Không |
| **T7** | Đồng bộ CompanyTreeSelect | Thay `<Select>` phẳng bằng `CompanyTreeSelect` tại `SystemInfoListPage.tsx`, `LocationListPage.tsx`, `DepartmentListPage.tsx`, `UserFormModal.tsx` (tự viết lại TreeSelect) | Trung bình — `UserFormModal` có logic cascade Department/Location riêng cần giữ | Không |
| **T8** | Sửa vi phạm a11y/pre-delivery cụ thể | 5 chỗ `#999` (contrast) → `colorTextSecondary`; 2 chỗ emoji-as-icon → SVG icon (`PermissionMatrixPage.tsx:220`, `StatusLabelListPage.tsx`); 2 chỗ `<a onClick>` thiếu `href` mất cursor (`LicenseListPage.tsx:302`, `MaintenanceTable.tsx:426`); chip overflow thiếu ở Accessory/Consumable itemNo | Thấp — mỗi điểm sửa độc lập, có thể chia nhỏ hơn nữa | Không |
| **T9** | Migrate Asset/Accessory sang Modal tại chỗ | Đưa Form Asset/Accessory về pattern Modal giống Consumable/Component/License; sẽ tự động thay thế cách sửa tạm ở T2 bằng giải pháp triệt để hơn | Trung bình-cao — đụng routing + form logic 2 domain lớn nhất | Nên làm SAU T2 (T2 là fix nhanh 2 dòng, không cần đợi T9; T9 là redesign lớn hơn, có thể làm riêng sau) |
| **T10** | Tạo formatter dùng chung | 1 file `utils/format.ts` cho `formatDate`/`formatDateTime`/`formatMoney`, thay ≥11 điểm khai trùng | Thấp | Không |
| **T11** | Sửa gate export sai key ở ReportsPage | Đổi `usePermission('assets.view')` → `usePermission('export')` cho nút Xuất CSV; thêm `loading=` cho Table báo cáo khấu hao | Thấp | Không |

### 🟡 Ưu tiên dài hạn — nâng cấp ProComponents

| # | Tên | Phạm vi | Rủi ro | Phụ thuộc |
|---|---|---|---|---|
| **T12** | Chuyển `StatusLabelListPage`/`DepreciationListPage`/`ReportsPage` sang `ProTable` | Đồng bộ toolbar/request/valueType/Popconfirm chuẩn | Thấp-trung bình | Nên cân nhắc cùng lúc bổ sung CRUD/permission gate cho 2 trang Admin đang thiếu (ngoài phạm vi audit này, ghi nhận riêng) |
| **T13** | Thêm `scroll={{x:true}}` cho bảng lồng | `SystemInfoListPage.tsx:175` (`expandedRowRender`) | Rất thấp | Không |

---

## Mục 7 — Đánh giá kiến trúc Feature-Driven (riêng, không gắn 🔴/🟠/🟡)

> Đây là quyết định kiến trúc, khác bản chất với các vấn đề thẩm mỹ/nhất quán
> ở Mục 1-6 — trình bày tách biệt theo đúng yêu cầu.

### 7.1 Import graph thực tế theo domain

| Domain | Pages | Components riêng | Service | Phụ thuộc ra ngoài domain |
|---|---|---|---|---|
| Asset | List/Detail/FormPage | `assets/AssetAllocationModal`, `AssetArchiveModal`, `AssetRecallModal`, `AssetMaintenanceSection` (+2 file chết) | `asset.service.ts` | Bị Maintenance phụ thuộc ngược |
| Accessory | List/Form/Detail | `accessories/AccessoryCheckinModal`, `AccessoryCheckoutModal` | `accessories.service.ts` | Không |
| Component | List/Detail | `ComponentFormModal.tsx` (đặt lệch, thẳng ở `components/`) | `components.service.ts` | Dùng `ActionLogTable` (Asset) |
| Consumable | List/Detail | `consumables/ConsumableCheckoutModal`, `ConsumableFormModal` | `consumables.service.ts` | Không |
| License | List (không Detail/Form page riêng) | `LicenseCheckoutModal/DetailModal/FormModal` (đặt lệch, thẳng ở `components/`) | `licenses.service.ts` | Bị Asset/System/User phụ thuộc ngược |
| Maintenance | List | `maintenances/MaintenanceTable`, `MaintenanceCompleteModal` | **Không có** — dùng `asset.service.ts` | Phụ thuộc chặt vào Asset (không phải domain độc lập thật sự) |
| User | List/Detail | `users/UserFormModal` | Gọi thẳng `api-client` | Dùng `LicenseUsageTable` (Asset) |
| Group/Permission | List/Matrix | `groups/GroupFormModal` | `groups.service.ts` | Không |
| Admin master-data | 11 trang | Không | Gọi thẳng `api-client` | Không |
| System/Dashboard/Reports | 4 trang | Không sở hữu | `systems.service.ts` | **Trang lai ghép nhất** — kéo `ActionLogTable` (Asset), `LicenseUsageTable`+`licensesApi` (Asset/License), `MaintenanceTable` (Maintenance) |

### 7.2 Coupling thực đo được

Trên 20 file component "thuộc domain" (loại 2 file chết + `CompanyTreeSelect`
global): **17/20 (85%) chỉ dùng nội bộ đúng domain sở hữu** — coupling thực tế
thấp dù thư mục là type-based. 3/20 cross-domain thật, cả 3 đều generic (0
import nội bộ, không mang logic riêng của domain sở hữu):

- `components/assets/ActionLogTable.tsx` → dùng bởi Asset + Component +
  System + SystemHistory (4 domain)
- `components/assets/LicenseUsageTable.tsx` → dùng bởi Asset + System + User
  (3 domain)
- `components/maintenances/MaintenanceTable.tsx` → dùng bởi Maintenance + System

Maintenance phụ thuộc ngược vào tầng service của Asset (không có
`maintenances.service.ts` riêng) — về bản chất là sub-feature của Asset, không
phải domain ngang hàng.

### 7.3 Global/shared thực sự

`services/api-client.ts` (27 file, mọi domain), `hooks/usePermission.ts` (19
file), `services/keycloak.ts` (9 file), `components/common/CompanyTreeSelect.tsx`
(6/9 domain nghiệp vụ), `theme/designTokens.ts` (theme shell), `ProtectedRoute.tsx`
(route guard hạ tầng). Ranh giới mờ, KHÔNG nên gọi là global dù trông giống:
`types/asset.ts` (chỉ 2 domain: Asset + SystemDetail), `licenses.service.ts`
(License sở hữu, bị 3 domain khác tiêu thụ qua `LicenseUsageTable`).

### 7.4 Kết luận vị trí trên phổ type-based ↔ feature-based

**Type-based về hình thức, nhưng coupling thực tế đã gần feature-based**
(85% component chỉ dùng nội bộ 1 domain). Vấn đề thật không phải "dùng chéo
lung tung" mà là: (a) 3 component generic bị "mắc kẹt" trong folder domain
khác thay vì ở tầng shared, (b) 4 file License/Component đặt lệch phẳng ở gốc
`components/`, (c) Maintenance không có tầng service riêng. Đây là vấn đề tổ
chức/naming, không phải kiến trúc phụ thuộc chồng chéo thật sự — chi phí
refactor thấp hơn một codebase coupling cao thật.

### 7.5 Cấu trúc feature-based đề xuất (dùng file thật)

```
src/shared/
  ProtectedRoute.tsx, CompanyTreeSelect.tsx, api-client.ts, keycloak.ts,
  usePermission.ts, designTokens.ts
  components/ActionLogTable.tsx        # chuyển từ assets/
  components/LicenseUsageTable.tsx     # chuyển từ assets/

src/features/asset/        (AssetListPage/DetailPage/FormPage + AssetAllocationModal/
                             ArchiveModal/RecallModal/MaintenanceSection + asset.service.ts
                             — KHÔNG migrate AssetFormModal.tsx/ActionLogTimeline.tsx, xóa theo T1)
src/features/accessory/    (List/Form/Detail + CheckinModal/CheckoutModal + accessories.service.ts)
src/features/component/    (List/Detail + ComponentFormModal + components.service.ts)
src/features/consumable/   (List/Detail + CheckoutModal/FormModal + consumables.service.ts)
src/features/license/      (List + CheckoutModal/DetailModal/FormModal + licenses.service.ts)
src/features/maintenance/  (List + MaintenanceTable/CompleteModal — cross-import asset.service.ts
                             hoặc tách maintenance.service.ts riêng, xem ghi chú)
src/features/user/         (List/Detail + UserFormModal)
src/features/group/        (List/Matrix + GroupFormModal + groups.service.ts)
src/features/admin-masterdata/  (11 trang, tự chứa)
src/features/system-dashboard/  (SystemHistory/SystemDetail/Dashboard/Reports + systems.service.ts
                                  — trang tổng hợp, chấp nhận import chéo từ shared/ + các feature khác)
```

Điểm mơ hồ cần quyết định tường minh, không né tránh:
- `ActionLogTable`/`LicenseUsageTable` đưa vào `shared/` chỉ là hợp thức hoá
  thực tế (dữ liệu ActionLog/LicenseSeat vốn cross-cutting theo entity bất kỳ),
  không giải quyết câu hỏi domain nào "sở hữu" khái niệm này.
- `MaintenanceTable` chỉ 2 domain dùng — có thể giữ trong
  `features/maintenance/components/` và chấp nhận 1 cross-feature import từ
  System, thay vì đẩy lên `shared/` (mức dùng lại chưa đủ rộng).
- Maintenance nên tách `maintenance.service.ts` riêng khi di chuyển, thay vì
  giữ cross-import `features/asset` vĩnh viễn (sẽ là smell dài hạn nếu không tách).

### 7.6 Effort/rủi ro và phân phase (theo coupling thấp → cao, giống style Task S1/S2a/S2b backend)

| Phase | Domain | Số file | LOC ~ | Rủi ro |
|---|---|---|---|---|
| 0 | Xóa dead code (T1) | 2 xóa | 524 | Rất thấp |
| 1 | Consumable | 5 | 1.410 | Thấp |
| 2 | Accessory | 6 | 1.900 | Thấp |
| 3 | Group/Permission | 5 | 680 | Thấp |
| 4 | Admin master-data | 11 | 1.800 | Thấp, cơ học |
| 5 | Trích xuất `shared/` (ActionLogTable, LicenseUsageTable) | 2 di chuyển | — | Trung bình — nên làm sớm, là điều kiện tiên quyết cho phase 6/7 |
| 6 | User | 4 | 740 | Thấp-trung bình |
| 7 | Component | 4 | 1.585 | Trung bình |
| 8 | License | 5 | 1.260 | Trung bình — nhiều nơi tiêu thụ |
| 9 | Maintenance | 3 | 1.010 | Trung bình-cao — cần quyết định tách service |
| 10 | Asset | 9 | 1.900 | Cao — domain trung tâm nhất |
| 11 | System/Dashboard/Reports | 5 | 960 | Cao (ít code) — chỉ làm sau khi Asset/License/Maintenance đã ổn định path |

Mỗi phase là 1 PR "chỉ move + sửa import path", không đổi logic — review bằng
`git mv` + diff import, dễ verify.

### 7.7 Quan hệ với các nhóm task Mục 1-6

**Khuyến nghị: làm SAU CÙNG, độc lập với các fix ở Mục 1-6.** Lý do:

1. Nếu move file trước rồi mới sửa bug/permission/màu, mọi PR sau phải diff
   trên đường dẫn đã đổi — khó đối chiếu lại với báo cáo audit này (tham
   chiếu theo đường dẫn hiện tại). Ngược lại, sửa trước rồi move sau → mỗi PR
   move là pure rename, review nhanh, `git log --follow` vẫn theo được.
2. T9 (migrate Asset/Accessory sang Modal) và T4/T5/T6 (màu) là thay đổi logic
   trên chính các file sẽ bị move — nên làm trước hoặc trong cùng phase move
   của domain đó, không nên double-touch 2 lần liên tiếp.
3. Route cleanup ở `App.tsx` ít tương tác với việc move — có thể làm song song.
4. Permission fix (T11) nên làm trước move — thay đổi nội dung nhỏ, không nên
   trộn với thay đổi cấu trúc file (đúng nguyên tắc "không gộp nhiều loại rủi
   ro trong 1 approval").

**Thứ tự tổng thể đề xuất:** (a) T1-T3 (🔴) → (b) T4-T11 (🟠, theo lựa chọn ưu
tiên của người dùng) → (c) T12-T13 (🟡) → (d) chạy `scripts/audit-sweeps.ps1`
xác nhận sạch → (e) mới bắt đầu Phase 0-11 của Mục 7.

---

*Báo cáo này chỉ audit — không có file code nào bị thay đổi. Người dùng chọn
nhóm task nào để triển khai trước.*
