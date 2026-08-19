# Status Colors — AspireReact Design System (mở rộng của MASTER.md)

> **LOGIC:** Đây là bảng màu bổ sung cho TRẠNG THÁI dữ liệu (data status), nằm ngoài
> bảng màu brand/UI ở `MASTER.md`. Không thay thế, không xung đột với brand palette.
> Khi định nghĩa màu trạng thái cho 1 entity mới, tra cứu bảng này trước — không tự
> bịa hex mới, không lệch tông brand.

## Nguyên tắc
- Giữ tinh thần "Accessible & Ethical": tương phản cao, không neon/gradient.
- Màu trạng thái phải PHÂN BIỆT được khi nhìn nhanh (admin data-dense scan).
- Hex chỉ định nghĩa MỘT LẦN tại `frontend/src/theme/designTokens.ts`; mọi component
  dùng qua import token, KHÔNG hard-code hex trong component.
- Trạng thái mang ý nghĩa khác nhau KHÔNG được trùng màu gây nhầm lẫn.

## Semantic palette (5 bucket chuẩn)

| Bucket | Ý nghĩa | Hex | Token trong `designTokens.ts` |
|--------|---------|-----|-------------------------------|
| **ready** | RTD / Sẵn sàng / Available | `#1677ff` | `statusColors.ready` |
| **active** | Deployed / Đã cấp phát / Hoàn thành | `#52c41a` | `statusColors.active` |
| **overdue** | Overdue / Quá hạn / Hết hạn | `#dc2626` | `statusColors.overdue` |
| **closed** | Closed / Đã đóng / Archived / Inactive | `#8c8c8c` | `statusColors.closed` |
| **pending** | Pending / Chờ xử lý (generic) | `#d48806` | `statusColors.pending` |

Lưu ý bucket **pending** `#d48806` (amber tối) được chọn ĐỂ KHÔNG trùng với màu
warning UI chung `#fa8c16` (Dashboard Low Stock / License sắp hết hạn) — cả hai
cùng "cảnh báo" nhưng khác tông, tránh hiểu nhầm.

## Ánh xạ theo entity

### AssetStatus (enum backend: `Pending=0, Deployed=1, Archived=2`)
> Lưu ý đặc thù: trong hệ thống này `AssetStatus.Pending` nghĩa là **Sẵn sàng —
> available for checkout** (xem comment enum), KHÔNG phải "chờ xử lý". Vì vậy
> "Pending" của Asset rơi vào bucket **ready** (xanh dương), không phải **pending**
> (amber). "Pending" amber chỉ dùng cho entity có trạng thái chờ-duyệt thật sự.

| Status | Bucket | Hex | Token |
|--------|--------|-----|-------|
| Pending (Sẵn sàng) | ready | `#1677ff` | `assetStatusColors['Pending']` |
| Deployed | active | `#52c41a` | `assetStatusColors['Deployed']` |
| Archived | closed | `#8c8c8c` | `assetStatusColors['Archived']` |
| giá trị lạ (fallback) | closed | `#8c8c8c` | default |

`assetStatusColors` trong `designTokens.ts` là map `Record<string,string>` từ chuỗi
status API trả về (JSON string enum) → hex. Giá trị không có trong map dùng default.

### Đối chiếu với mapping cũ (KHÔNG xung đột)
- **Maintenance status (Task D — GIỮ NGUYÊN):** in_progress `#1677ff` / completed
  `#52c41a` / closed `#8c8c8c`. Trùng đúng bộ 3 ready/active/closed ở trên → nhất
  quán, không đổi.
- **License (sắp hết hạn / còn ít chỗ — GIỮ NGUYÊN):** cam/đỏ cấp phát. Không trùng
  bucket nào ở trên (overdue đỏ `#dc2626` là màu riêng cho "quá hạn hẳn").
- **Asset hiện tại:** Pending→blue, Deployed→green, Archived→gray — TRÙNG khớp với
  `ASSET_STATUS_COLORS` đã có trong `frontend/src/types/asset.ts` (cùng 3 màu) → chỉ
  thống nhất nguồn, không đổi ngữ nghĩa.

## Action type (dùng cho Recent Activity / ActionLog)
ActionType backend trả về **string** enum (JSON). Dùng map string-keyed
`actionTypeLabels` trong `DashboardPage` (đầy đủ 20 giá trị). Giá trị không map được
→ hiển thị **chính tên enum gốc** (VD `Inspect`) thay vì chữ "Unknown".
