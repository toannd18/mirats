# Handoff 2026 08 12 — License module: Mục 0 audit + Subtask A (backend hardening) + B (tests)

> Lộ trình tổng thể (đã chia nhỏ): **A** Backend → **B** Tests → **C** Frontend list+form → **D** Frontend seats/checkout → **E** liên kết 2 chiều + changelog.
> Handoff này ghi nhận **mục 0 (audit)** + hoàn thành **A + B**, kèm bổ sung **Hệ thống (SystemPosition) làm đối tượng nhận thứ 3** theo yêu cầu chèn thêm.

---

## 1. Kết quả kiểm tra mục 0 — License module ĐÃ TỒN TẠI (Phase 4/7)

### Đã có sẵn
- `License.cs` (Name, Serial, Seats, ExpirationDate, PurchaseCost/Date, OrderNumber, Notes, ManufacturerId, CategoryId, CompanyId) + `LicenseSeat.cs` (LicenseId, AssetId?, UserId?, Note, AssignedAt) + `LicensesController` (GET list/detail, POST create → **đã sinh seats**, PUT, DELETE, POST `/assign` + `/remove`) + permissions `licenses.view/create/edit/delete/checkout` + `ItemType.License=5`, `CategoryType.License=5` + `LicenseListPage`/`LicenseFormPage`/`licenses.service.ts`.
- **Không có**: company-scoping thật sự, Reassignable, seat-sync, delete-guard, tests.

### Quyết định kế thừa (không phá phần đã có)
- Giữ tên cột `Serial` (thay vì rename `LicenseKey`) và `UserId`/`AssetId` (thay vì `AssignedUserId`/`AssignedAssetId`) — tránh migration phá dữ liệu/UI cũ. `AssignedAt` = thời điểm checkout (thay vì thêm `CheckedOutAt`).
- Endpoint mới `/checkout` + `/checkin` theo spec; giữ `/assign` + `/remove` làm alias tương thích.

## 2. Domain + Migration (Subtask A)
- `License` thêm: `Reassignable` (bool, default true), `SupplierId`, `TerminationDate`, `MinSeats`, `DeletedAt`.
- `LicenseSeat` thêm: `SeatNumber` (int, unique per license), `SystemPositionId` (target thứ 3), `CreatedAt/UpdatedAt`.
- **3 loại đối tượng nhận** (theo convention Accessory: luôn cấp cấp `SystemPosition` con, không bao giờ SystemInfo cha): `LicenseSeatTargetType { User=1, Asset=2, SystemPosition=3 }`.
- DB: CHECK `CK_license_seats_single_target` (`<= 1` — chỉ cấm ≥2; service layer enforce "đúng 1" lúc checkout) + FKs (licenses CASCADE, assets/users/system_positions SET NULL, suppliers/manufacturers SET NULL) + unique `(LicenseId, SeatNumber)` + backfill SeatNumber.
- `Program.cs` self-heal v5 + `docs/sql/migration_licenses.sql` UP/DOWN.

## 3. API (Subtask A) — company-scoping đầy đủ (`GetCurrentUserCompanyIdAsync`, 404 cho ngoài scope)
| Endpoint | Hành vi |
|---|---|
| `GET /licenses` | company-scope + filter search/categoryId/companyId + `expiringSoon` (30 ngày) + `lowSeats` (<= MinSeats); trả `expiringSoon/isExpired/isLowSeats/assignedSeats/availableSeats`. |
| `GET /licenses/{id}` · `GET /licenses/{id}/seats` | Detail + danh sách seat (`seatNumber`, `assigned`, `targetType`, user/asset/systemPosition{systemInfoName}, note, assignedAt). |
| `POST /licenses` | Bắt buộc Name/Seats>=1/Category (CategoryType=License); regular user bị ép company của mình; **sinh N seats (SeatNumber 1..N)**; ActionLog Create. |
| `PUT /licenses/{id}` | Whitelist; **CategoryId/CompanyId khóa** (`FIELD_LOCKED`); **seat-sync**: tăng → sinh thêm, giảm → cần đủ seat trống (`CANNOT_REDUCE_SEATS_IN_USE`); ActionLog Update. |
| `DELETE /licenses/{id}` | **Delete-guard**: đã checkout (hoặc có ActionLog Checkout) → `LICENSE_IN_USE`; soft-delete `DeletedAt`; ActionLog Delete. |
| `POST /licenses/{id}/checkout` | `{seatId?, targetType, targetId, note}` — seat trống hoặc auto-pick (`NO_AVAILABLE_SEATS`); target tồn tại + **cùng company** (`LICENSE_COMPANY_MISMATCH`); **đúng 1 trong 3** (`TARGET_REQUIRED`/`INVALID_TARGET_TYPE`/`SEAT_TARGET_AMBIGUOUS`); ActionLog Checkout. |
| `POST /licenses/{id}/checkin` | Seat đã gán; **`Reassignable=false` → `LICENSE_NOT_REASSIGNABLE`**; xóa target + thời gian; ActionLog Checkin. |
| `/assign` · `/remove` | Alias legacy (map sang checkout/checkin; cả 2 target → `SEAT_TARGET_AMBIGUOUS`). |

## 4. Testing (Subtask B) — **93/93 pass** (72 cũ + **21 mới** `LicenseTests.cs`)
- Tạo → đủ 5 seats (1..5); regular user bị ép company.
- Tăng seats → thêm; giảm seats không đủ trống → `CANNOT_REDUCE_SEATS_IN_USE`; giảm đủ trống → OK; đổi company → `FIELD_LOCKED`.
- Checkout User/Asset/**SystemPosition** → đúng field + chỉ 1 trong 3; thiếu target → `TARGET_REQUIRED`; legacy cả 2 → `SEAT_TARGET_AMBIGUOUS`; seat đã gán → `SEAT_ALREADY_ASSIGNED`.
- Checkin `Reassignable=false` → `LICENSE_NOT_REASSIGNABLE`; true → trả về trống.
- Company: checkout user/SystemPosition khác công ty → `LICENSE_COMPANY_MISMATCH`; GET ngoài scope → 404.
- Delete sau checkout → `LICENSE_IN_USE`; chưa checkout → OK (soft-delete).
- Lọc `expiringSoon`/`lowSeats` đúng ngưỡng; `GET /seats` trả `seatNumber` + `targetType` + assigned.

## 5. Xác minh server thật (AppHost đã start, token admin)
- Self-heal v5 áp dụng (bảng/columns/constraints OK — server health 200).
- Tạo license 3 seats → seats 1,2,3 ✅; checkout SystemPosition **khác công ty** → `LICENSE_COMPANY_MISMATCH` ✅; checkout **cùng công ty** (Node Điều khiển 01 / Hệ thống Dây chuyền SX) → 200 ✅; `GET /seats` hiển thị `type=SystemPosition, sys=..., pos=...` ✅; checkin → 200 ✅.
- Demo data giữ lại trong DB: category "Software License" + license "Windows Pro DEMO" (3 seats) — phục vụ Subtask C/D.

## ⚠️ Database schema đã thay đổi (v5)
- Cột mới trên `licenses` + `license_seats` + bảng seats constraint/index — tự áp dụng qua self-heal khi server khởi động (đã restart). Không dùng `dotnet ef`.

## 🔜 Còn lại (Subtask C/D/E — chưa làm lượt này)
- **C**: `LicenseListPage` nâng cấp (cảnh báo hết hạn/ít chỗ, cột Công ty) + form **Modal** (thay form trang riêng) + quick-add Category/Supplier/Manufacturer.
- **D**: modal chi tiết seats (bảng seat, nút Checkout/Checkin disable khi !Reassignable) + checkout modal (Radio 3 loại: Người dùng/Tài sản/Hệ thống + select filter company).
- **E**: mục "License đang sử dụng" ở AssetDetailPage/User + **tab License thứ 4** trong `SystemDetailPage`; cập nhật changelog cuối.

---

# ✅ Subtask C — Frontend: LicenseListPage nâng cấp + form Modal (2026-08-12)

## 1. Đã làm
- **`services/licenses.service.ts`** (viết lại gọn): types `LicenseListItem`, `LicenseDetailDto`, `LicenseSeatDto`, `CreateLicensePayload`, `UpdateLicensePayload`, `CheckoutLicensePayload` + methods `list/get/getSeats/create/update/delete/checkout/checkin` (getSeats/checkout/checkin sẵn cho Subtask D).
- **`components/LicenseFormModal.tsx`** (mới): mirror `ComponentFormModal` — Modal chia 5 nhóm field (Thông tin chung, Số chỗ, Hết hạn & hợp đồng, Nhà sản xuất & mua hàng, Ghi chú); **quick-add** Category (type=5)/Company/Supplier/Manufacturer; **lock field** Category + Company dạng `LockedFieldTag` (Tag + 🔒 + tooltip) khi sửa; Company chỉ hiện cho Superuser lúc tạo (regular user bị ép công ty bởi server); edit gửi **whitelist** (không gửi categoryId/companyId — khóa); dùng `destroyOnHidden`/`popupRender` (antd v6, không deprecation warning).
- **`pages/LicenseListPage.tsx`** (viết lại): deep-link pattern `/**/licenses/new` + `/**/licenses/:id/edit` mở Modal trên list (pattern ComponentListPage); cột **Tên | Danh mục | Công ty | Tổng ghế | Còn trống (đỏ/ít) | Ngày hết hạn (Tag đỏ = Hết hạn, cam = Sắp hết hạn 30 ngày) | Thao tác (Sửa/Xóa)**; filters **Tìm kiếm + Danh mục + Sắp hết hạn/đã hết hạn + Còn ít chỗ**; Xóa hiển thị message lỗi từ server (kể cả `LICENSE_IN_USE`).
- **`App.tsx`**: `/licenses/new` + `/licenses/:id/edit` → `LicenseListPage` (bỏ form trang riêng); `/licenses/:id` → redirect `/licenses`.

## 2. Xác minh (server thật + Playwright, admin)
- **List**: 1 dòng demo "Windows Pro DEMO | Software License | Công ty Cổ phần ABC | 3 | 3 | -" — headers đúng 7 cột. Ảnh `docs/screenshots/license-list-c-1440.png`.
- **Create modal**: đủ 5 nhóm field + quick-add; Ảnh `docs/screenshots/license-create-modal-c-1440.png`.
- **Edit modal**: Danh mục "Software License" + Công ty "Công ty Cổ phần ABC" hiện dạng **locked tag**; prefill đúng. Ảnh `docs/screenshots/license-edit-modal-c-1440.png`.
- **Vòng lặp tạo/sửa**: sửa tên license qua modal → lưu → list refresh hiển thị tên mới ✅.
- `tsc --noEmit` 0 lỗi; console 0 error (sau khi fix `destroyOnHidden`/`popupRender`).

## 🔜 Subtask D (chưa làm — chờ xác nhận)
- Modal chi tiết seats (bảng seat: Số thứ tự/Trạng thái/Đang gán cho/ngày cấp/ghi chú) + Checkout (Radio 3 loại Người dùng/Tài sản/Hệ thống) + Checkin (disable khi !Reassignable). `licenses.service.ts` đã có sẵn `getSeats/checkout/checkin` + `LicenseSeatDto` + `CheckoutLicensePayload`.

---

# ✅ Subtask D — Frontend: Detail seats + Checkout modal (3 Radio) + Checkin guard (2026-08-12)

## 1. Đã làm
- **`components/LicenseCheckoutModal.tsx`** (mới): Modal cấp phát seat — **Radio 3 lựa chọn Người dùng / Tài sản / Hệ thống** (optionType=button); Select theo loại đối tượng **lọc theo công ty license** (User `/users`, Asset `/assets`, Hệ thống `/system-infos` → flatten SystemPosition theo `SystemInfo.CompanyId` — luôn cấp cấp con như Accessory); placeholder/notFoundContent động; ghi chú; submit `licensesApi.checkout({seatId?, targetType, targetId, note})` (seatId null = auto-pick).
- **`components/LicenseDetailModal.tsx`** (mới): header Descriptions (Danh mục, Công ty, **Reassignable Có/Không**, Tổng ghế, Đã cấp, Còn trống màu, Ngày hết hạn cảnh báo, Serial, MinSeats) + **bảng seat** (Số thứ tự `#N`, Trạng thái Trống/Đã cấp, Đang gán cho với **Tag phân loại Người dùng (geekblue) / Tài sản (cyan) / Hệ thống (purple)** + tên, Ngày cấp, Ghi chú, Thao tác); **Checkout** (seat trống) mở `LicenseCheckoutModal`; **Checkin** (seat đã gán) **disable + tooltip "License không cho phép thu hồi (Reassignable = false)"** khi `!reassignable`; sau checkout/checkin reload seat + refresh list.
- **`pages/LicenseListPage.tsx`**: thêm nút **"Chi tiết"** → `/licenses/:id` mở `LicenseDetailModal` (deep-link, không xung đột `/new`/`/:id/edit`).
- **`App.tsx`**: `/licenses/:id` → `LicenseListPage` (mở detail modal).

## 2. Xác minh (server thật + Playwright, admin) — đúng 3 ảnh yêu cầu
- 📸 `docs/screenshots/license-checkout-modal-3radio-1440.png` — **Checkout Modal đủ 3 Radio: Người dùng | Tài sản | Hệ thống** (seat #1, Windows Pro DEMO).
- 📸 `docs/screenshots/license-checkout-system-1440.png` — chọn **Radio "Hệ thống"** → Select hiện các SystemPosition cùng công ty ("Hệ thống Dây chuyền SX — Node Điều khiển 01/02").
- 📸 `docs/screenshots/license-detail-checkin-disabled-1440.png` — license **Reassignable = Không** (OEM, 1 seat gán cho "Tài sản PLC S7-1500 (AST-DEM-001)") → nút **Checkin bị DISABLE** (eval xác nhận `disabled: true`) + tooltip giải thích.
- 📸 `docs/screenshots/license-detail-system-assigned-1440.png` — sau checkout qua UI cho seat #1 → "Đã cấp | **Hệ thống** Node Điều khiển 01 — Hệ thống Dây chuyền SX | 10:06:02", seat #2 còn "Trống | Checkout".
- **Console 0 error / 0 warning**; `tsc --noEmit` 0 lỗi; backend tests **93/93** (không đổi backend).

## 🔜 Subtask E (chưa làm — không tự gộp)
- Mục "License đang sử dụng" ở AssetDetailPage / User + **tab License thứ 4** trong SystemDetailPage + changelog cuối.

---

# ✅ Subtask E — Liên kết 2 chiều (Asset/System) + verify màu cảnh báo (2026-08-12)

## 1. Backend — 2 endpoint đọc mới (`LicensesController`, policy `licenses.view`)
- `GET /api/v1/licenses/for-asset/{assetId}` — seats đang gán cho Asset.
- `GET /api/v1/licenses/for-system/{systemInfoId}` — seats đang gán cho SystemPosition của hệ thống (cấp con như Accessory).
- Cả 2: company-scope (404 ngoài scope), trả `licenseName`, `serial`, `seatNumber`, `assignedAt`, `note`, `expiringSoon`/`isExpired`, `company`, `systemPosition{code,name}`.

## 2. Frontend
- **`services/licenses.service.ts`**: type `LicenseUsageRow` + `licensesApi.forAsset`/`forSystem`.
- **`components/assets/LicenseUsageTable.tsx`** (mới): bảng đọc-only License | (Vị trí trong hệ thống nếu scope=system) | Ngày cấp | Cảnh báo hết hạn | Ghi chú + Empty state.
- **`AssetDetailPage.tsx`**: Card **"License đang sử dụng"** (`<LicenseUsageTable scope={{type:'asset'}}>`).
- **`SystemDetailPage.tsx`**: **tab License thứ 4** (icon KeyOutlined, badge đếm) → `<LicenseUsageTable scope={{type:'system'}}>`.

## 3. Verify — ảnh thật (Playwright, admin)
- 📸 `docs/screenshots/asset-detail-license-used-1440.png` — Asset **PLC S7-1500 (AST-DEM-001)** → Card "License đang sử dụng" hiện **"License OEM Khong hoan lai seat #1"** (ngày cấp 10:02:51, **Sắp hết hạn 22/8/2026** orange) — đúng demo đã cam kết.
- 📸 `docs/screenshots/system-detail-license-tab-1440.png` — **tab License thứ 4** (badge 2) ở SystemDetailPage Hệ thống Dây chuyền SX → **Windows Pro DEMO seat #1 → Node Điều khiển 01 (POS-001-NOD)** + seat #2 → Node 02 — đúng demo đã cam kết.
- 📸 `docs/screenshots/license-list-warnings-1440.png` — **verify màu cảnh báo treo từ Subtask C**: "License Het han Demo" → **Hết hạn 7/8/2026 (Tag đỏ)**; "License OEM Khong hoan lai" → **Sắp hết hạn 22/8/2026 (Tag cam)** + **0 (ít) (Tag đỏ low-seats)**; "Windows Pro DEMO" → **1 (ít)**.
- Console **0 error / 0 warning**; `tsc --noEmit` 0 lỗi; backend tests **93/93** (thêm 2 endpoint đọc — không đổi logic đã test).

## 🏁 Tổng kết chuỗi task License
- **A** Backend hardening (company-scope, reassignable, seat-sync, delete-guard, **SystemPosition target thứ 3**) ✅
- **B** 21 test License mới (93/93) ✅
- **C** ListPage nâng cấp + form Modal + quick-add + lock field ✅
- **D** Detail seats + Checkout modal (3 Radio) + Checkin guard (disable khi !Reassignable) ✅
- **E** Liên kết 2 chiều Asset/System + verify màu cảnh báo ✅
- Changelog: handoff này (`docs/Handoff 2026 08 12 license module subtask A B.md`).

---

# ✅ Hoàn tất — "License đang sử dụng" ở trang chi tiết User (2026-08-12)

## 1. Đã làm
- **Backend**: `GET /api/v1/licenses/for-user/{userId}` (`LicensesController`, policy `licenses.view`) — seats đang gán cho User, company-scope, projection giống for-asset/for-system.
- **Frontend**:
  - `licenses.service.ts`: `licensesApi.forUser`.
  - `components/assets/LicenseUsageTable.tsx`: mở rộng `scope.type` thêm `'user'` (dùng chung đúng component đã có, không nhân bản).
  - `pages/UserDetailPage.tsx` (mới): route `/users/:id` — nút Quay lại + title tên user + Card "Thông tin người dùng" (Descriptions) + **Card "License đang sử dụng"** (`<LicenseUsageTable scope={{type:'user'}}>`).
  - `pages/UserListPage.tsx`: nút **Chi tiết** (EyeOutlined) → `/users/{id}` — ⚠️ **KHẮC PHỤC 2026-08-12**: bản đầu chỉ thêm import (`EyeOutlined`, `useNavigate`) nhưng **chưa thêm nút vào cột Thao tác** và **thiếu `const navigate = useNavigate();`** (cả 2 replacement Node thêm nút/khai báo fail âm thầm do `\n` literal không khớp CRLF). → Báo cáo cũ \"UserListPage đã có nút Chi tiết\" là **SAI** (nút chưa từng tồn tại trong code, không phải bị ẩn bởi điều kiện role). Đã thêm nút eye **trước** Sửa/Xóa (thứ tự giống ComponentListPage/LicenseListPage) + khai báo `navigate`; xác minh bằng ảnh thật + click thật (dưới).
  - `App.tsx`: route `/users/:id` → `UserDetailPage`.

## 2. Demo data (tạo thêm để minh họa)
- Chưa có seat nào gán cho User → đã **insert user local "demo.user"** (Demo User, Công ty Cổ phần ABC) thẳng vào DB + **checkout seat #3 của "Windows Pro DEMO (đã sửa)"** cho user đó (HTTP 200, `for-user` trả 1 seat).

## 3. Xác minh (server thật + Playwright, admin) — ảnh thật
- 📸 `docs/screenshots/user-detail-license-1440.png` — **UserDetailPage "Demo User"**: Thông tin người dùng (Công ty Cổ phần ABC, Hoạt động) + **Card "License đang sử dụng"** hiện **"Windows Pro DEMO (đã sửa) seat #3"** (ngày cấp 10:37:31, ghi chú "cau phoi cho demo user").
- UserListPage: dòng "Demo User" có nút **Chi tiết** (eye) hoạt động — 📸 `docs/screenshots/user-list-3-icons-1440.png` (**cả 3 icon eye/edit/delete** trên dòng Demo User, xác minh bằng ảnh đúng màn hình UserListPage — không suy luận từ màn hình khác) + 📸 `docs/screenshots/user-detail-license-card-1440.png` (UserDetailPage sau khi **click thật** nút eye → `/users/4f7c1c1e-...-000000000001`, Card "License đang sử dụng" hiện **"Windows Pro DEMO (đã sửa) seat #3"**).
- Console **0 error / 0 warning** · `tsc --noEmit` 0 lỗi · backend tests **93/93**.

## 🎉 Module License — ĐÓNG HOÀN CHỈNH A→E
A Backend hardening · B 21 test (93/93) · C List + form Modal · D Detail seats + Checkout/Checkin · E liên kết 2 chiều **Asset / User / System** + màu cảnh báo.
