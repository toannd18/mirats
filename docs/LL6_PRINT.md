# LL-6 — In hồ sơ lý lịch thiết bị (browser print, không thư viện PDF phía server)

> Trạng thái: **đã triển khai** (GĐ1 route + tải dữ liệu, GĐ2 bố cục/CSS in, GĐ3 kiểm chứng thật).
> Code là nguồn sự thật: `frontend/src/features/asset/pages/AssetTimelinePrintPage.tsx`,
> `frontend/src/features/asset/utils/timelinePrint.ts`, `AssetTimelinePrintPage.css`.

## 1. Cách dùng

1. Mở chi tiết thiết bị → tab **Lý lịch** (tab mặc định).
2. Chọn bộ lọc **Sự kiện chính** hoặc **Tất cả** (Segmented cạnh nút in) — bản in dùng ĐÚNG bộ lọc đang chọn.
3. Bấm **In lý lịch** → mở **tab mới** `/assets/:id/ly-lich/in?types=…` (không sidebar/menu).
4. Trang in tự tải đủ dữ liệu rồi **tự gọi `window.print()`**; hộp thoại in của trình duyệt hiện ra
   (chọn máy in / "Save as PDF"). Nút **In lại** ở góc phải cho lần in tiếp theo (bị ẩn khi in).

Quyền: `assets.view`. Thiết bị ngoài phạm vi công ty / không tồn tại ⇒ trang **404 "Không tìm thấy thiết bị"**.

## 2. Dữ liệu & giới hạn

- API `GET /assets/{id}/timeline`: `limit` **tối đa 200** (server kẹp), phân trang **cursor**,
  trả `pagination.hasMore` + `nextCursor`, dữ liệu **mới nhất trước**.
- Trang in tải **tuần tự** với `limit=200` và đảo thành **CŨ → MỚI** để in.
- **Giới hạn 5.000 sự kiện/lần in.** Vượt (hoặc đúng 5.000 mà còn trang) ⇒ **KHÔNG in** —
  hiện thông báo gợi ý chọn **"Sự kiện chính"** (hoặc thu hẹp phạm vi). Hệ thống **không bao giờ in bản
  bị cắt bớt**.
- Ước lượng số request: `ceil(N / 200)` (217 sự kiện ⇒ 2 request; 5.000 ⇒ 25 request).

## 3. Bố cục A4

- Khổ **A4 dọc**, lề `12mm 12mm 16mm 12mm`.
- Trang 1: tiêu đề **LÝ LỊCH THIẾT BỊ** + dòng phụ (mã, tên, **nhãn bộ lọc**, tổng số sự kiện).
- Khối thông tin thiết bị (3 cột) lấy từ `GET /assets/{id}`: mã, tên, serial, model (+ model number),
  danh mục, hãng SX, nhà cung cấp, công ty, vị trí, trạng thái (+ "chưa xác nhận"), đang giữ, ngày mua,
  giá mua, bảo hành, số đơn hàng, cấp phát/thu hồi gần nhất, kiểm kê kế tiếp; ghi chú (full-width).
- Bảng sự kiện **CŨ → MỚI**: `Thời điểm | Sự kiện | Chi tiết | Người thực hiện`.
  - Mốc **ước tính** (dòng backfill): tiền tố **`≈`** + footnote cuối bảng.
  - Sự kiện đính chính: hậu tố **`[Đã đính chính]`**.
- Chân trang (mọi trang): `Người in: <họ tên>` · `Thời điểm in: dd/MM/yyyy HH:mm` · `Bộ lọc: …`.

## 4. Số trang `x / y` và phương án dự phòng

- Dùng **margin box** của CSS Paged Media:

  ```css
  @page {
    size: A4 portrait;
    margin: 12mm 12mm 16mm 12mm;
    @bottom-center { content: "Trang " counter(page) " / " counter(pages); }
  }
  ```

- **Đã đo thật** trên Chrome **155.0.8059.85** và Edge **155.0.4283.62**: in ra đúng `Trang 1 / 7` …
  `Trang 7 / 7`.
- **Dự phòng** khi trình duyệt không hỗ trợ margin box: khối `@bottom-center` bị **bỏ qua** (không lỗi
  layout) và chân trang `position: fixed; bottom: 0` (đã có sẵn) vẫn in **người in / thời điểm in / bộ lọc**
  trên mọi trang — chỉ **thiếu số trang**. Phương án khác: bật "Headers and footers" của hộp thoại in
  (số trang do trình duyệt chèn, không kiểm soát được vị trí/định dạng).
- Header bảng lặp mỗi trang: `thead { display: table-header-group }`.
- Không cắt đôi một dòng: `tr { break-inside: avoid; page-break-inside: avoid }`.
  Lưu ý: nếu một dòng **cao hơn 1 trang** thì trình duyệt buộc phải cắt — thiết kế dữ liệu in nên giữ
  `details` ngắn.
- **Chờ FONT trước khi in:** `printWhenReady()` trong `AssetTimelinePrintPage.tsx` (dòng ~118-125) `await
  document.fonts.ready` rồi mới gọi `window.print()`; auto-print (dòng ~136) và nút "In lại" (dòng ~213)
  dùng CÙNG hàm. In trước khi font tải xong ⇒ trình duyệt có thể dùng fallback font ⇒ sai số trang/ngắt dòng.

## 5. Kiểm chứng thật (GĐ3 + bổ sung 2026-10-20)

### 5.1. Bộ dữ liệu lớn (217 sự kiện)

**Môi trường:** DB tạm (container Postgres riêng) + API `127.0.0.1:5514` + Vite **5180**; fixture tạo qua API:
1 thiết bị + **215 lần cập nhật** + 1 lần tạo + 1 lần cấp phát tới vị trí hệ thống = **217 sự kiện**.

| Tiêu chí | Chrome 155 | Edge 155 |
|---|---|---|
| Số trang | **7** | **7** |
| Khổ giấy | 209,9 × 297,0 mm (A4) | 209,9 × 297,0 mm (A4) |
| `<thead>` lặp mỗi trang | **7/7** | **7/7** |
| Chân trang (người in/thời điểm in) | **7/7** | **7/7** |
| Số trang `Trang x / y` | `1/7` … `7/7` | `1/7` … `7/7` |
| Tổng số dòng in ra | **217** (215 cập nhật + 1 tạo + 1 cấp phát) | **217** |
| Dòng bị cắt đôi | **0** (mọi cặp "old → new" nằm trọn 1 trang; không có mảnh `→` lẻ) | **0** |
| Thứ tự | dòng đầu = sự kiện **cũ nhất** ("Tạo mới") | giống |

### 5.2. PDF mẫu thứ hai — đủ dấu hiệu `≈` và `[Đã đính chính]`

**Môi trường:** DB tạm MỚI (container riêng) + API `127.0.0.1:5516` + Vite **5182**; in ở chế độ
**"Sự kiện chính"** (`types=INSTALL,REMOVE,MAINTENANCE,MAINTENANCE_CAMPAIGN`).

Fixture:
1. Cấp phát thiết bị tới một **vị trí hệ thống** (tạo dòng lý lịch đang mở, `Source = Api`).
2. **Đính chính THẬT qua API** `POST /assets/{id}/installations/{entryId}/correct` (đổi `note`, `reason` ≥ 10 ký tự)
   ⇒ dòng mới `Source = Correction`, `CorrectsEntryId != null` ⇒ timeline trả `isCorrection = true`.
3. **Một dòng backfill:** ⚠️ **NGOẠI LỆ FIXTURE** — dòng `Source = Backfill` được **INSERT thẳng bằng SQL**
   trên **DB TẠM** (hệ thống không có API tạo backfill; dữ liệu thật chỉ sinh từ lần chuyển dữ liệu LL-1).
   Dùng khoảng **đã đóng trong quá khứ** (`2026-01-01 08:00→09:00Z`) để không vi phạm exclusion constraint.

Kết quả timeline "Sự kiện chính": **3 sự kiện** (INSTALL backfill `isEstimated=true`, REMOVE kết thúc backfill,
INSTALL đính chính `isCorrection=true`).

| Tiêu chí (phân tích text PDF) | Kết quả |
|---|---|
| Số trang / khổ | **1** / **A4** (209,9 × 297,0 mm) |
| Dấu `≈` trên dòng thời điểm | **CÓ** (`≈ 01/01/2026 15:00 …`) |
| Chú thích cuối bảng | **CÓ** ("≈ Thời điểm ước tính — được suy ra khi chuyển dữ liệu cũ sang lý lịch (backfill)…") |
| `[Đã đính chính]` | **CÓ** (dòng `Lắp đặt tại: Ke LL6B… [Đã đính chính]`) |
| `<thead>` / số trang | **1/1** / `Trang 1 / 1` |

> Ghi chú: `≈` gắn với **sự kiện INSTALL của dòng backfill** (mốc bắt đầu). Sự kiện REMOVE suy ra từ
> `EndedAt` của cùng dòng hiện không mang `≈` (hành vi sẵn có của timeline API, không thuộc phạm vi LL-6).

### 5.3. Ảnh/PDF mẫu (thư mục `screenshots/`, gitignored)

`ll6-print-button-1440.png` · `ll6-print-page-1440.png` (full-page 1440) · `ll6-print-sample.pdf`
(Chrome, 217 sự kiện / 7 trang) · **`ll6-print-sample2.pdf`** (Chrome, "Sự kiện chính", có `≈` + chú thích +
`[Đã đính chính]`).

## 6. Test tự động

```bash
cd aspire-react/frontend
node --test tests/timelinePrint.test.ts
```

Bao phủ: nhiều trang/cursor/limit, đúng 5.000 (in được), vượt 5.000 (ném `TimelineTooLargeError`),
lỗi mạng, cursor lặp, khử trùng id, và `formatEventTime` (`≈`).

## 7. Giới hạn đã biết

- In là thao tác phía client: cần đăng nhập (refresh cookie) — mở tab in trong cùng trình duyệt.
- Ẩn/hiện theo `@media print`: nút "In lại", link quay lại, toolbar đều bị ẩn khi in.
- Số trang phụ thuộc font/bản dịch trình duyệt; nếu cần "x / y" trên trình duyệt cũ, xem §4.
