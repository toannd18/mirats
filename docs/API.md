# AspireReact API Reference

> Base URL: `http://localhost:5428/api/v1` (HTTP) hoặc `https://localhost:7314/api/v1` (HTTPS)
> Auth: **JWT tự ký (local auth)** — `POST /api/v1/auth/login` (password) hoặc `/api/v1/auth/passkeys/login`
> → `Authorization: Bearer <accessToken>`; refresh token nằm trong cookie httpOnly `refreshToken`.
> (Keycloak đã bị xóa hoàn toàn ở AUTH Phase 5.)

## Authentication

Tất cả endpoint yêu cầu header `Authorization: Bearer <token>` trừ các endpoint đánh dấu `(none)` hoặc `Authenticated`.

## Health

| Method | Route | Auth | Mô tả |
|--------|-------|------|-------|
| `GET` | `/health` | None | Health check tổng |
| `GET` | `/api/v1/health` | None | API health |

---

## Dashboard

| Method | Route | Auth | Mô tả |
|--------|-------|------|-------|
| `GET` | `/api/v1/dashboard/summary` | Authenticated | 6 widgets |
| `GET` | `/api/v1/dashboard/recent-activity` | Authenticated | 20 logs |
| `GET` | `/api/v1/dashboard/assets-by-status` | Authenticated | Group by status |
| `GET` | `/api/v1/dashboard/assets-by-category` | Authenticated | Group by category |
| `GET` | `/api/v1/dashboard/low-stock` | Authenticated | Low stock items |
| `GET` | `/api/v1/dashboard/monthly-checkout-trend` | Authenticated | 12-month trend |

---

## Users

| Method | Route | Auth |
|--------|-------|------|
| `GET` | `/api/v1/users` | `users.view` |
| `GET` | `/api/v1/users/me` | Authenticated |
| `GET` | `/api/v1/users/{id:guid}` | `users.view` |
| `POST` | `/api/v1/users` | `users.create` |
| `PUT` | `/api/v1/users/{id:guid}` | `admin` (không phải `users.edit` — xem ghi chú bên dưới) |
| `DELETE` | `/api/v1/users/{id:guid}` | `users.delete` |
| `PUT` | `/api/v1/users/{id:guid}/groups` | `admin` | Gán nhóm cho user (thay thế toàn bộ); **chống self-lockout** (`SELF_LOCKOUT`) + ghi ActionLog |

### `PUT /api/v1/users/{id:guid}` — patch semantics & sentinel `Guid.Empty`

> Policy của endpoint này là **`admin`** (không phải `users.edit` — `UsersController.UpdateUser`),
> giống `PUT /users/{id}/groups`. Người gọi thường (không superuser) còn phải cùng công ty với user
> bị sửa, nếu không → `404` (hide-existence).

Update là **patch**: field **không gửi** → giữ nguyên giá trị hiện có. Với 3 field tham chiếu kiểu
`Guid?`, JSON `null` **không phân biệt được** với "không gửi", nên muốn **xoá** tham chiếu thì client
gửi **sentinel `Guid.Empty`**:

| Giá trị gửi | Ý nghĩa |
|---|---|
| *(không có field trong JSON)* | **Giữ nguyên** giá trị hiện có |
| `"00000000-0000-0000-0000-000000000000"` (`Guid.Empty`) | **Xoá về null** — `companyId` → user thành *floater* (không thuộc công ty nào); `departmentId`/`locationId` → bỏ gán |
| GUID thật | Gán giá trị đó |

Năm field text:
- `employeeNumber`, `jobTitle`: không gửi → giữ nguyên; gửi chuỗi rỗng `""` → xoá nội dung.
- `firstName`, `lastName`, `email`: không gửi → **giữ nguyên**; gửi giá trị → cập nhật (tên được `trim`,
  email được `lowercase`); gửi chuỗi rỗng `""` hoặc email sai định dạng → `400` (validator `NotEmpty` /
  `EmailAddress`) — 3 field bắt buộc về giá trị nên **không thể bị xoá** qua payload.

Ràng buộc phía server (người gọi thường; **superuser bỏ qua toàn bộ** các check phạm vi):

- `companyId` mới phải **thuộc phạm vi người gọi** (công ty của mình hoặc công ty con) → nếu không:
  `400` `COMPANY_MISMATCH`. Sentinel floater luôn được phép.
- `departmentId`/`locationId` mới: phải **tồn tại** → nếu không: `400` `RESOURCE_NOT_FOUND`;
  nếu trỏ tới một công ty thật thì công ty đó phải thuộc phạm vi người gọi → nếu không:
  `400` `COMPANY_MISMATCH`.
- `firstName`/`lastName`/`email` là **bắt buộc về giá trị nhưng không bắt buộc có mặt** trong payload
  (patch semantics, BUG-O 2026-10-18): không gửi → giữ nguyên; gửi `""` → `400` (field bắt buộc
  không thể xoá). Trước bản fix này payload **phải** gửi đủ 3 field, nếu không sẽ `400` — xem
  `docs/BACKLOG.md` § BUG-O.

**Ví dụ 1 — payload tối thiểu (chỉ đổi tên; công ty/phòng ban/địa điểm giữ nguyên):**

```http
PUT /api/v1/users/3f1c9a4e-7b2d-4c88-9f01-2a5b6c7d8e9f HTTP/1.1
Authorization: Bearer <accessToken>
Content-Type: application/json

{
  "id": "3f1c9a4e-7b2d-4c88-9f01-2a5b6c7d8e9f",
  "firstName": "Nguyen",
  "lastName": "Van B",
  "email": "nva@example.com"
}
```

**Ví dụ 2 — chuyển user thành *floater* và bỏ phòng ban/địa điểm:**

```http
PUT /api/v1/users/3f1c9a4e-7b2d-4c88-9f01-2a5b6c7d8e9f HTTP/1.1
Authorization: Bearer <accessToken>
Content-Type: application/json

{
  "id": "3f1c9a4e-7b2d-4c88-9f01-2a5b6c7d8e9f",
  "firstName": "Nguyen",
  "lastName": "Van B",
  "email": "nva@example.com",
  "companyId": "00000000-0000-0000-0000-000000000000",
  "departmentId": "00000000-0000-0000-0000-000000000000",
  "locationId": "00000000-0000-0000-0000-000000000000"
}
```

→ `200` với `data.companyId/departmentId/locationId = null`.

**Ví dụ 3 — chỉ đổi cờ (payload tối thiểu thật sự; tên/email/liên hệ giữ nguyên):**

```http
PUT /api/v1/users/3f1c9a4e-7b2d-4c88-9f01-2a5b6c7d8e9f HTTP/1.1
Authorization: Bearer <accessToken>
Content-Type: application/json

{
  "id": "3f1c9a4e-7b2d-4c88-9f01-2a5b6c7d8e9f",
  "isSuperUser": false
}
```

→ `200`; `firstName`/`lastName`/`email`/`employeeNumber`/`jobTitle` **không đổi**, `isSuperUser` = `false`.
Lưu ý: hạ quyền **superuser cuối cùng** của hệ thống bị guard `SELF_LOCKOUT` chặn (`400`).

> Quy ước sentinel `Guid.Empty` này đã dùng sẵn trong dự án (`CompanyScopeService` trả `Guid.Empty`
> cho user không công ty; `AssetMaintenance.CompanyId = Guid.Empty` = floater). Trước bản fix
> 2026-10-02, payload thiếu field **xoá** `companyId`/`departmentId`/`locationId`/`employeeNumber`/
> `jobTitle` (BACKLOG N1); ai cần "xoá có chủ đích" thì gửi sentinel như trên.

---

## Assets

| Method | Route | Auth |
|--------|-------|------|
| `GET` | `/api/v1/assets` | `assets.view` |
| `GET` | `/api/v1/assets/selectlist` | Authenticated |
| `GET` | `/api/v1/assets/tree` | `assets.view` |
| `GET` | `/api/v1/assets/{id:guid}` | `assets.view` |
| `POST` | `/api/v1/assets` | `assets.create` |
| `PATCH` | `/api/v1/assets/{id:guid}` | `assets.edit` |
| `DELETE` | `/api/v1/assets/{id:guid}` | `assets.delete` |
| `GET` | `/api/v1/assets/{id}/history` | `assets.view` |
| `GET` | `/api/v1/assets/{id}/timeline` | `assets.view` |

### Asset Lifecycle

| Method | Route | Auth |
|--------|-------|------|
| `POST` | `/api/v1/assets/{id}/checkout` | `assets.checkout` |
| `POST` | `/api/v1/assets/{id}/checkin` | `assets.checkin` |
| `POST` | `/api/v1/assets/bytag/{tag}/checkout` | `assets.checkout` |
| `POST` | `/api/v1/assets/bytag/{tag}/checkin` | `assets.checkin` |
| `POST` | `/api/v1/assets/{id}/audit` | `assets.audit` |
| `POST` | `/api/v1/assets/bulk` | `assets.edit` |
| `POST` | `/api/v1/assets/audit/bulk` | `assets.audit` |
| `POST` | `/api/v1/assets/{id}/accept` | Authenticated |
| `POST` | `/api/v1/assets/{id}/decline` | Authenticated |
| `GET` | `/api/v1/assets/due-checkin` | `assets.checkin` |
| `GET` | `/api/v1/assets/due-audit` | `assets.audit` |
| `POST` | `/api/v1/assets/labels` | `assets.view` |

### Asset Timeline (Lý lịch thiết bị) — [LL-3]

`GET /api/v1/assets/{id:guid}/timeline?cursor=<opaque>&limit=50` · `assets.view`

Trả về DÒNG THỜI GIAN của MỘT thiết bị (**mới nhất trước**), trộn 4 nguồn:

| Nguồn | Sinh sự kiện | `eventType` | `refType` | Mốc `occurredAt` |
|-------|--------------|-------------|-----------|------------------|
| `asset_installations` | mỗi dòng (đã bỏ dòng bị đính chính `SupersededAt != null`) → 1 sự kiện LẮP; nếu dòng đã đóng (`EndedAt != null`) → thêm 1 sự kiện THÁO | `INSTALL`, `REMOVE` | `assetInstallation` | `StartedAt` / `EndedAt` |
| `action_logs` | mọi bản ghi `ItemType = Asset` + `ItemId = asset`, chưa soft-delete | tên `ActionType` dạng SCREAMING_SNAKE (`CREATE`, `UPDATE`, `CHECKOUT`, `CHECKIN`, `AUDIT`, `ARCHIVE`, `UNARCHIVE`, `CONFIRM`, `DISPOSE`, `MARK_DAMAGED`, `STOCK_IN`, `UPDATE_REJECTED`, …) | `actionLog` | `ActionDate` |
| `maintenance_campaign_device_snapshots` | thiết bị được đưa vào một đợt bảo dưỡng (snapshot chứa asset) | `MAINTENANCE_CAMPAIGN` | `maintenanceCampaign` (`refId` = `CampaignId`) | `MaintenanceCampaign.StartDate` |
| `asset_maintenances` | phiếu bảo dưỡng / sửa chữa (chưa soft-delete) | `MAINTENANCE` | `assetMaintenance` | `StartDate` |

> Với 2 nguồn cuối, `occurredAt` **chính là** ngày bắt đầu của phiếu/đợt (mốc nghiệp vụ người dùng đặt) — DTO
> **không** có thêm trường `<…>StartDate` trùng giá trị; FE hiển thị "Bắt đầu" từ `occurredAt`.

**Khử trùng — điều kiện lọc phải giữ nguyên khi thêm nguồn mới**

- `action_logs`: **BỎ** `Checkout`/`Checkin` có `TargetType = SystemPosition`, vì chính thao tác đó đã được biểu diễn bằng sự kiện `INSTALL`/`REMOVE` ở nguồn 1 (không bỏ sẽ lặp). Điều kiện chính xác:
  `NOT (TargetType = SystemPosition AND ActionType IN (Checkout, Checkin))`.
- **KHÔNG dùng bảng `assignments`** làm nguồn: nội dung của nó đã có trong `action_logs` (sẽ trùng).
- ActionLog của phiếu bảo dưỡng (`ItemType = AssetMaintenance`) và của đợt bảo dưỡng (`ItemType = MaintenanceCampaign`) KHÔNG vào timeline thiết bị (nguồn ActionLog chỉ nhận `ItemType = Asset`).

**DTO**

| Trường | Kiểu | Ghi chú |
|--------|------|---------|
| `eventType` | string | Xem bảng nguồn ở trên. **Ổn định** — FE dùng để chọn icon/màu. |
| `occurredAt` | ISO-8601 UTC (có `Z`) | Thời điểm SỰ KIỆN xảy ra (KHÔNG phải thời điểm tạo bản ghi). Với `MAINTENANCE`/`MAINTENANCE_CAMPAIGN` đây là ngày **bắt đầu** của phiếu/đợt. |
| `title` | string | Tiêu đề tiếng Việt ghép sẵn (VD `Lắp vào vị trí Kệ A1`, `Tháo khỏi vị trí Kệ A1`, `Cấp phát`, `Sửa chữa: Thay quạt`). |
| `details` | string? | Chi tiết phụ dạng **VĂN BẢN**: hệ thống / địa điểm / đối tượng / ghi chú, ngăn cách bằng `" · "`. **KHÔNG bao giờ chứa ngày/giờ đã format** — mọi mốc thời gian nằm ở trường có cấu trúc (`occurredAt`, `maintenanceCompletionDate`) để FE tự format theo múi giờ người dùng. |
| `actorName` | string? | Người thực hiện; `null` khi không xác định (VD dòng backfill không có người ghi). Riêng nguồn `MAINTENANCE_CAMPAIGN` = **người phụ trách đợt** (`MaintenanceCampaign.ReviewerId`). |
| `refType` | string? | Loại bản ghi nguồn để mở link: `assetInstallation` \| `actionLog` \| `assetMaintenance` \| `maintenanceCampaign`. |
| `refId` | guid? | Id bản ghi nguồn (FE mở link theo `refType`). Bản ghi có thể đã bị xoá ⇒ FE phải chịu được 404. |
| `isEstimated` | bool | `true` khi **mốc thời gian là ước tính**: chỉ đúng với sự kiện `INSTALL` của dòng `Source = Backfill` (backfill chỉ ước tính `StartedAt`; mốc THÁO luôn là mốc thật). |
| `maintenanceCompletionDate` | ISO-8601 UTC (có `Z`) \| null | Ngày **hoàn thành** của phiếu (`asset_maintenances.CompletionDate`). Chỉ có giá trị khi `eventType = MAINTENANCE`; `null` tại `MAINTENANCE` nghĩa là **phiếu chưa hoàn thành** (FE tự hiển thị nhãn — server không gửi chuỗi `"Chưa hoàn thành"`). |

**Quy ước ngày giờ (ràng buộc với FE ở LL-4)**

- **Mọi mốc thời gian trong DTO là UTC và có hậu tố `Z`.** Server **không** format ngày/giờ thành chuỗi ở bất kỳ
  trường nào (`details` chỉ chứa văn bản/nhãn, không chứa ngày).
- FE hiển thị bằng `new Date(x).toLocaleString('vi-VN')` (chuỗi có `Z` ⇒ JS quy đổi đúng về giờ máy người dùng).
  **Không** tự ghép chuỗi ngày từ `details`, không tự cộng/trừ múi giờ.
- Lý do: server format ngày sẽ khoá cứng múi giờ + định dạng và lệch với các màn hình khác (xem backlog **MT-TZ**).

**Phân trang — CURSOR, không offset**

- Thứ tự toàn phần: `(occurredAt DESC, refId DESC, nguồn DESC)` — có khoá phá thế bằng nhau nên phân trang **không mất / không lặp** kể cả khi nhiều sự kiện trùng `occurredAt`.
- `limit`: mặc định **50**, tối đa **200** (`≤ 0` → 50; `> 200` → 200 — **chuẩn hoá**, không báo lỗi).
- Truyền lại `pagination.nextCursor` qua `?cursor=...`; `nextCursor = null` ⇒ hết dữ liệu. Con trỏ là **ĐỤC** (base64url) — client không được tự dựng.

**Response**

```json
{
  "status": "success",
  "data": [
    {
      "eventType": "REMOVE",
      "occurredAt": "2026-10-17T09:30:00Z",
      "title": "Tháo khỏi vị trí Kệ A1",
      "details": "Hệ thống: Nhà ga T1 · ghi chú lý lịch",
      "actorName": "Nguyễn Văn A",
      "refType": "assetInstallation",
      "refId": "0f0c9b1e-0000-0000-0000-000000000000",
      "isEstimated": false,
      "maintenanceCompletionDate": null
    },
    {
      "eventType": "MAINTENANCE",
      "occurredAt": "2026-10-15T00:00:00Z",
      "title": "Sửa chữa: Thay quạt",
      "details": null,
      "actorName": "Nguyễn Văn A",
      "refType": "assetMaintenance",
      "refId": "3b7e5a90-1111-2222-3333-444455556666",
      "isEstimated": false,
      "maintenanceCompletionDate": "2026-10-16T00:00:00Z"
    }
  ],
  "pagination": { "limit": 50, "count": 2, "hasMore": true, "nextCursor": "MjAyNjEwMTdUMDkzMDAwWnwwZjBjOWIxZXww" }
}
```

> Sự kiện `MAINTENANCE` cho thấy quy ước: ngày **bắt đầu** = `occurredAt`, ngày **hoàn thành** =
> `maintenanceCompletionDate` (cả hai đều UTC có `Z`); `details` là `null` vì phiếu bảo dưỡng không có văn bản phụ
> nào khác. Với phiếu chưa hoàn thành thì `maintenanceCompletionDate` = `null`.

**Lỗi**

| HTTP | Điều kiện | `error_code` |
|------|-----------|--------------|
| 404 | Asset không tồn tại **hoặc** ngoài phạm vi công ty của người dùng (giống `GET /assets/{id}` — hide-existence) | — (chỉ `status` + `message`) |
| 400 | `cursor` không giải mã được | `INVALID_CURSOR` |

**Ghi chú triển khai (đã kiểm chứng trên Postgres thật — bằng chứng LL-3)**

- **Server KHÔNG format ngày/giờ thành chuỗi (quyết định 2026-10-18, phương án A).** Trước đây `details` chứa các
  chuỗi ghép `"Bắt đầu: dd/MM/yyyy"`, `"Hoàn thành: dd/MM/yyyy"`, `"Bắt đầu đợt: dd/MM/yyyy"` — cách đó khoá cứng
  múi giờ/định dạng của server và buộc FE đọc ngày trong văn bản. Nay: mốc bắt đầu = `occurredAt`, mốc hoàn thành =
  `maintenanceCompletionDate` (đều UTC có `Z`); nhãn "chưa hoàn thành" do FE suy ra từ `null`. Kèm dọn 2 trường
  lookup không còn ai dùng (`TimelineCampaignInfo.StartDate`, `.ReviewerId`).
  Test chặn tái phát: `Timeline_Details_NeverContainFormattedDates`,
  `Timeline_MaintenanceEvent_CompletionDate_IsUtcOrNull`, `Timeline_CampaignEvent_StartDateIsOccurredAt_NotADetailsString`,
  `Timeline_Json_ExposesZuluTimestamps_AndNoDateStringInDetails`.

- **`occurredAt` luôn là UTC có hậu tố `Z`.** Cột `asset_maintenances.StartDate` là `timestamp without time zone`
  (khác 3 nguồn còn lại đều `timestamp with time zone`), nên giá trị đọc ra phải được **chuẩn hoá `Kind = Utc`**
  trước khi trả về; ngược lại JSON mất hậu tố `Z` và client hiểu sai múi giờ khi trộn 4 nguồn.
  Chiều ngược lại, khi truyền mốc `occurredAt` của con trỏ xuống **so sánh** với cột này phải hạ về
  `Unspecified` — nếu không Npgsql ném `Cannot write DateTime with Kind=UTC to PostgreSQL type 'timestamp without time zone'`
  (HTTP 500 khi lật sang trang 2; EF InMemory **không** phát hiện được).
- **`actorName` của `MAINTENANCE_CAMPAIGN` không phụ thuộc kích thước trang.** Id người phụ trách phải được đăng ký
  vào lô tra cứu tên của chính trang đó (nếu không, `limit=1` trả `actorName = null` còn `limit=3` lại có tên —
  bug thật đã gặp và đã sửa).
- **Kế hoạch truy vấn** (EXPLAIN ANALYZE trên DB dev): nguồn `action_logs` (≈2.000 dòng) dùng
  `Index Scan using IX_action_logs_ItemType_ItemId`; nguồn lý lịch khi bảng lớn (mô phỏng ≈22.000 dòng trong
  transaction rồi ROLLBACK) dùng `Index Scan Backward using IX_asset_installations_AssetId_StartedAt` +
  `Incremental Sort` — **không** sequential scan bảng lớn. Các bảng nhỏ (`asset_installations` 6 dòng,
  `asset_maintenances` 11 dòng) bị planner chọn `Seq Scan` — tối ưu ở quy mô đó.

---

## Consumables

| Method | Route | Auth |
|--------|-------|------|
| `GET` | `/api/v1/consumables` | `consumables.view` |
| `GET` | `/api/v1/consumables/{id:guid}` | `consumables.view` |
| `POST` | `/api/v1/consumables` | `consumables.create` |
| `PUT` | `/api/v1/consumables/{id:guid}` | `consumables.edit` |
| `DELETE` | `/api/v1/consumables/{id:guid}` | `consumables.delete` |
| `POST` | `/api/v1/consumables/{id}/checkout` | `consumables.checkout` |
| `GET` | `/api/v1/consumables/low-stock` | `consumables.view` |

## Components

| Method | Route | Auth |
|--------|-------|------|
| `GET` | `/api/v1/components` | `components.view` |
| `GET` | `/api/v1/components/{id:guid}` | `components.view` |
| `POST` | `/api/v1/components` | `components.create` |
| `PUT` | `/api/v1/components/{id:guid}` | `components.edit` |
| `DELETE` | `/api/v1/components/{id:guid}` | `components.delete` |
| `POST` | `/api/v1/components/{id}/assign` | `components.checkout` |
| `POST` | `/api/v1/components/{id}/remove` | `components.checkout` |

## Accessories

| Method | Route | Auth |
|--------|-------|------|
| `GET` | `/api/v1/accessories` | `accessories.view` |
| `GET` | `/api/v1/accessories/{id:guid}` | `accessories.view` |
| `POST` | `/api/v1/accessories` | `accessories.create` |
| `PUT` | `/api/v1/accessories/{id:guid}` | `accessories.edit` |
| `DELETE` | `/api/v1/accessories/{id:guid}` | `accessories.delete` |
| `POST` | `/api/v1/accessories/{id}/checkout` | `accessories.checkout` |

## Licenses

| Method | Route | Auth |
|--------|-------|------|
| `GET` | `/api/v1/licenses` | `licenses.view` |
| `GET` | `/api/v1/licenses/{id:guid}` | `licenses.view` |
| `POST` | `/api/v1/licenses` | `licenses.create` |
| `PUT` | `/api/v1/licenses/{id:guid}` | `licenses.edit` |
| `DELETE` | `/api/v1/licenses/{id:guid}` | `licenses.delete` |
| `POST` | `/api/v1/licenses/{id}/assign` | `licenses.checkout` |
| `POST` | `/api/v1/licenses/{id}/remove` | `licenses.checkout` |

## Groups & Permissions

| Method | Route | Auth |
|--------|-------|------|
| `GET` | `/api/v1/groups` | `admin` |
| `GET` | `/api/v1/groups/{id:guid}` | `admin` |
| `POST` | `/api/v1/groups` | `admin` |
| `PUT` | `/api/v1/groups/{id:guid}` | `admin` |
| `DELETE` | `/api/v1/groups/{id:guid}` | `admin` |
| `PUT` | `/api/v1/groups/{id}/permissions` | `admin` | **Chống self-lockout** (`SELF_LOCKOUT`) + ghi ActionLog; group `IsSystem` chặn rename/delete |
| `GET` | `/api/v1/permissions` | Authenticated | Catalog toàn bộ permission (group theo resource) — nguồn cho frontend |
| `GET` | `/api/v1/permissions/check` | Authenticated |
| `GET` | `/api/v1/permissions/matrix` | `admin` |

## Admin Categories

| Resource | Endpoints |
|----------|-----------|
| Models | `GET/POST` `/api/v1/models`, `GET/PUT/DELETE` `/api/v1/models/{id}` |
| Categories | `GET/POST` `/api/v1/categories`, `GET/PUT/DELETE` `/api/v1/categories/{id}` |
| Manufacturers | `GET/POST` `/api/v1/manufacturers`, `GET/PUT/DELETE` `/api/v1/manufacturers/{id}` |
| Suppliers | `GET` `/api/v1/suppliers` |
| Locations | `GET/POST` `/api/v1/locations`, `GET/PUT/DELETE` `/api/v1/locations/{id}` |
| Depreciations | `GET` `/api/v1/depreciations` |

## Custom Fields

| Method | Route | Auth |
|--------|-------|------|
| `GET` | `/api/v1/custom-fields` | `customfields.view` |
| `GET` | `/api/v1/custom-fields/{id:guid}` | `customfields.view` |
| `POST` | `/api/v1/custom-fields` | `customfields.create` |
| `PUT` | `/api/v1/custom-fields/{id:guid}` | `customfields.edit` |
| `DELETE` | `/api/v1/custom-fields/{id:guid}` | `customfields.delete` |

## Reports

| Method | Route | Auth |
|--------|-------|------|
| `GET` | `/api/v1/reports/custom` | `reports.view` |
| `GET` | `/api/v1/reports/depreciation` | `reports.view` |
| `GET` | `/api/v1/reports/audit` | `reports.view` |
| `GET` | `/api/v1/reports/checkout-history` | `reports.view` |

## Import/Export

| Method | Route | Auth |
|--------|-------|------|
| `POST` | `/api/v1/import/assets` | `import` |
| `GET` | `/api/v1/import/templates/assets` | `import` |
| `GET` | `/api/v1/export/assets` | `assets.view` |
| `GET` | `/api/v1/export/consumables` | `consumables.view` |