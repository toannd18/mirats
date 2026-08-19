# GIAI ĐOẠN 2: TẦNG API, CONTROLLERS & VALIDATION RULES — Snipe-IT

> **Tác giả:** Phân tích kiến trúc — 2026-08-05
> **Phạm vi:** `app/Http/Controllers/Api/`, `app/Http/Requests/`, `app/Http/Transformers/`
> **Framework:** Laravel 12 / PHP 8.2+

---

## 1. DANH SÁCH API ENDPOINTS CỐT LÕI

Tất cả API đều nằm dưới prefix `/api/v1/` và đi qua middleware `api` + `api-throttle`.

### 1.1 Assets (Hardware)

| Method | Route | Controller Method | Mô tả |
|---|---|---|---|
| `GET` | `/api/v1/hardware` | `AssetsController@index` | Danh sách tất cả assets (filter, sort, search, pagination) |
| `GET` | `/api/v1/hardware/{id}` | `AssetsController@show` | Chi tiết một asset |
| `POST` | `/api/v1/hardware` | `AssetsController@store` | Tạo asset mới |
| `PATCH` | `/api/v1/hardware/{asset}` | `AssetsController@update` | Cập nhật asset |
| `DELETE` | `/api/v1/hardware/{id}` | `AssetsController@destroy` | Xóa asset |
| `POST` | `/api/v1/hardware/{id}/checkout` | `AssetsController@checkout` | Checkout asset cho user/location/asset |
| `POST` | `/api/v1/hardware/{id}/checkin` | `AssetsController@checkin` | Checkin asset về kho |
| `POST` | `/api/v1/hardware/bytag/{tag}/checkout` | `AssetsController@checkoutByTag` | Checkout bằng asset tag |
| `POST` | `/api/v1/hardware/bytag/{tag}/checkin` | `AssetsController@checkinByTag` | Checkin bằng asset tag |
| `POST` | `/api/v1/hardware/checkinbytag` | `AssetsController@checkinByTag` | Checkin qua body (hỗ trợ serial) |
| `POST` | `/api/v1/hardware/{id}/audit` | `AssetsController@audit` | Kiểm kê asset |
| `POST` | `/api/v1/hardware/audit/bulk` | `AssetsController@bulkAudit` | Kiểm kê hàng loạt |
| `GET` | `/api/v1/hardware/selectlist` | `AssetsController@selectlist` | Select2 dropdown (search by name/tag) |
| `GET` | `/api/v1/hardware/{action}/{upcoming_status}` | `AssetsController@index` | Filter theo audit/checkin (due/overdue) |
| `GET` | `/api/v1/hardware/{asset}/history` | `AssetsController@history` | Lịch sử action log của asset |
| `PATCH` | `/api/v1/hardware/bulk` | `AssetsController@bulkUpdate` | Cập nhật hàng loạt |
| `POST` | `/api/v1/hardware/labels` | `AssetsController@labels` | Generate labels |

#### 1.1.1 Payload Request: POST checkout asset

```json
{
  "checkout_to_type": "user",
  "assigned_user": 42,
  "assigned_asset": null,
  "assigned_location": null,
  "status_id": 2,
  "checkout_at": "2026-08-05",
  "expected_checkin": "2026-09-05",
  "note": "Bàn giao máy tính cho nhân viên mới",
  "name": "MacBook Pro - Nguyen Van A",
  "requestable": false
}
```

**Validation rules** (từ `AssetCheckoutRequest`):
- `checkout_to_type`: **required**, phải là `asset`, `location`, hoặc `user`
- `assigned_user` / `assigned_asset` / `assigned_location`: **required_without_all** (ít nhất một trong ba)
- `status_id`: **nullable**, `exists:status_labels,id,deployable,1`
- `checkout_at`: **nullable**, date hợp lệ
- `expected_checkin`: **nullable**, date hợp lệ
- `note`: **required** (chỉ khi setting `require_checkinout_notes = true`)

#### 1.1.2 JSON Response: GET danh sách assets

Transform qua `AssetsTransformer`, wrapper bởi `DatatablesTransformer`. Cấu trúc minh họa:

```
{
  "total": 1500,
  "rows": [
    {
      "id": 123,
      "name": "MacBook Pro 16-inch",
      "asset_tag": "MAC-0042",
      "serial": "C02ZX1234ABCD",
      "model":           { "id": 5, "name": "MacBook Pro 16\" 2023" },
      "model_number":    "A2991",
      "status_label":    { "id": 2, "name": "Ready to Deploy",
                           "status_type": "deployable", "status_meta": "label-success" },
      "category":        { "id": 3, "name": "Laptops",            "tag_color": "#3498db" },
      "manufacturer":    { "id": 1, "name": "Apple Inc.",          "tag_color": null },
      "supplier":        { "id": 10, "name": "Apple Reseller",     "tag_color": null },
      "location":        { "id": 7, "name": "Tầng 2 - VP Hà Nội" },
      "rtd_location":    { "id": 7, "name": "Tầng 2 - VP Hà Nội" },
      "assigned_to": {
        "id": 42, "username": "nguyenvana", "name": "Nguyen Van A",
        "first_name": "Van A", "last_name": "Nguyen",
        "email": "nguyenvana@company.com", "employee_number": "NV0042",
        "jobtitle": "Kỹ sư phần mềm", "type": "user"
      },
      "warranty_months": "36 months",
      "warranty_expires": { "formatted": "2026-06-15" },
      "purchase_date":    { "formatted": "2023-06-15" },
      "purchase_cost":    "$2,499.00",
      "book_value":       "$1,249.50",
      "age":              "3 năm trước",
      "eol":              "36 months",
      "asset_eol_date":   { "formatted": "2026-06-15" },
      "last_checkout":    { "formatted": "2026-08-05 09:30:00" },
      "expected_checkin": { "formatted": "2026-09-05 00:00:00" },
      "checkin_counter":  2,
      "checkout_counter": 3,
      "requests_counter": 0,
      "user_can_checkout": false,
      "custom_fields": {
        "Màu sắc": {
          "field": "_snipeit_mau_sac_1",
          "value": "Space Gray", "field_format": "TEXT", "element": "text"
        }
      },
      "available_actions": {
        "checkout": false, "checkin": true, "clone": true,
        "restore": false, "update": true, "audit": true, "delete": false
      }
    }
  ]
}
```

**Đặc điểm quan trọng của AssetsTransformer:**

| Đặc điểm | Mô tả |
|---|---|
| **Không raw ID** | Mỗi quan hệ (`model`, `category`, `manufacturer`, `status_label`, `assigned_to`, `supplier`, `location`, `rtd_location`) đều là object `{id, name, tag_color?}` |
| **Polymorphic `assigned_to`** | Tự động phát hiện loại target (user/location/asset) và trả về cấu trúc JSON phù hợp. User: có `username`, `email`, `employee_number`, `jobtitle`. Location/Asset: chỉ `id` + `name` + `type` |
| **Info-disclosure guard** | Nếu caller có `assets.view` nhưng bị deny `users.view`, assigned_to chỉ trả về `{id, type, name}` — không lộ PII |
| **Ngày tháng** | Mọi ngày qua `Helper::getFormattedDateObject()` → `{formatted, datetime, date}` |
| **Custom Fields** | Transform riêng trong vòng lặp fieldset, hỗ trợ encrypted fields với gate check |
| **`available_actions`** | Mỗi asset response kèm object permissions để frontend biết nút nào được hiển thị |
| **`user_can_checkout`** | Boolean computed từ `$asset->availableForCheckout()` |
| **`book_value`** | Giá trị sau khấu hao, computed qua Depreciable trait |

---

### 1.2 Consumables

| Method | Route | Controller Method | Mô tả |
|---|---|---|---|
| `GET` | `/api/v1/consumables` | `ConsumablesController@index` | Danh sách consumables |
| `GET` | `/api/v1/consumables/{id}` | `ConsumablesController@show` | Chi tiết consumable |
| `POST` | `/api/v1/consumables` | `ConsumablesController@store` | Tạo consumable mới |
| `PUT/PATCH` | `/api/v1/consumables/{id}` | `ConsumablesController@update` | Cập nhật consumable |
| `DELETE` | `/api/v1/consumables/{id}` | `ConsumablesController@destroy` | Xóa consumable |
| `POST` | `/api/v1/consumables/{id}/checkout` | `ConsumablesController@checkout` | Cấp phát consumable cho user |
| `GET` | `/api/v1/consumables/{id}/users` | `ConsumablesController@getDataView` | Danh sách user đã nhận consumable |
| `GET` | `/api/v1/consumables/selectlist` | `ConsumablesController@selectlist` | Select2 dropdown |
| `GET` | `/api/v1/consumables/{id}/history` | `ConsumablesController@history` | Lịch sử action log |

#### 1.2.1 Payload Request: POST checkout consumable

```json
{
  "assigned_to": 42,
  "checkout_qty": 3,
  "note": "Cấp phát mực in cho phòng IT"
}
```

**Không dùng FormRequest riêng** — validation thực hiện **inline trong Controller**:

```php
// ConsumablesController@checkout — các bước validation

// 1. Kiểm tra consumable tồn tại
if (! $consumable = Consumable::with('users')->find($id)) {
    return error: 'admin/consumables/message.does_not_exist';
}

// 2. Authorization
$this->authorize('checkout', $consumable);

// 3. Kiểm tra tồn kho (advisory — không lock)
if ($consumable->numRemaining() <= 0) {
    return error: 'admin/consumables/message.checkout.unavailable';
}

// 4. Kiểm tra category hợp lệ
if (! $consumable->category) {
    return error: 'general.invalid_item_category_single';
}

// 5. Kiểm tra số lượng checkout ≤ tồn kho
if ($consumable->checkout_qty > $consumable->numRemaining()) {
    return error: unavailable (requested X, remaining Y);
}

// 6. Resolve user (unscoped để phân biệt "not found" vs "other company")
$user = User::withoutGlobalScopes()->find($request->input('assigned_to'));

// 7. Loại bỏ user đã soft-deleted
if ($user && ! empty($user->deleted_at)) { $user = null; }

// 8. FMCS — user phải cùng công ty với consumable
if (FMCS && user không thuộc company của consumable) {
    return error: 'general.error_user_company';
}

// 9. Database transaction + lockForUpdate (concurrency guard)
DB::transaction(function () use ($consumable, ...) {
    $locked = Consumable::whereKey($consumable->id)->lockForUpdate()->first();
    if (! $locked || $locked->numRemaining() < $consumable->checkout_qty) {
        $errorResponse = checkout.unavailable;
        return;
    }
    for ($i = 0; $i < $consumable->checkout_qty; $i++) {
        $consumable->users()->attach($consumable->id, [...]);
    }
    event(new CheckoutableCheckedOut(...));
});
```

#### 1.2.2 JSON Response: GET danh sách consumables

Transform qua `ConsumablesTransformer`:

```
{
  "total": 45,
  "rows": [
    {
      "id": 7,
      "name": "Mực in HP 85A (CE285A)",
      "image": "https://...",
      "category":     { "id": 15, "name": "Mực in", "tag_color": "#e74c3c" },
      "company":      { "id": 1, "name": "Công ty TNHH ABC" },
      "item_no":      "CE285A",
      "location":     { "id": 4, "name": "Kho vật tư" },
      "manufacturer": { "id": 3, "name": "HP Inc." },
      "supplier":     { "id": 8, "name": "VPP Supplier" },
      "min_amt":          5,
      "model_number":     "85A",
      "remaining":        12,
      "percent_remaining": 60,
      "order_number":     "PO-2026-0042",
      "purchase_cost":    "$45.00",
      "total_cost":       "$900.00",
      "purchase_date":    { "formatted": "2026-01-15" },
      "qty":              20,
      "created_by":       { "id": 1, "name": "Admin" },
      "created_at":       { "formatted": "2026-01-15 10:30:00" },
      "user_can_checkout": true,
      "available_actions": {
        "checkout": true, "checkin": true, "update": true,
        "delete": false, "clone": true
      }
    }
  ]
}
```

**Đặc điểm ConsumablesTransformer:**
- `remaining` = `numRemaining()` (tính từ `qty - consumables_users_count`)
- `percent_remaining` = `round(percentRemaining())`
- `user_can_checkout` = `true` nếu `numRemaining() > 0`
- `total_cost` = `qty * purchase_cost`
- Tất cả FK đều resolve thành `{id, name, tag_color}`

---

### 1.3 Hai Format API Response Chuẩn

#### 1.3.1 Action CRUD / Checkout-Checkin

```php
Helper::formatStandardApiResponse('success', $payload, $message)
→ { "status": "success", "messages": "...", "payload": { ... } }

Helper::formatStandardApiResponse('error', $payload, $message)
→ { "status": "error", "messages": "...", "payload": { ... } }
```

#### 1.3.2 Danh sách (Datatables)

```php
(new DatatablesTransformer)->transformDatatables($rows, $total)
→ { "total": 1500, "rows": [...] }
```

---

## 2. RULE VALIDATION & GUARD CLAUSES

### 2.1 FormRequest: `AssetCheckoutRequest`

```php
public function rules()
{
    $rules = [
        // exists_undeleted: rejects soft-deleted targets
        'assigned_user'     => 'numeric|nullable|required_without_all:assigned_asset,assigned_location|exists_undeleted:users,id',
        'assigned_asset'    => 'numeric|nullable|required_without_all:assigned_user,assigned_location|exists_undeleted:assets,id',
        'assigned_location' => 'numeric|nullable|required_without_all:assigned_user,assigned_asset|exists_undeleted:locations,id',
        'status_id'         => 'nullable|exists:status_labels,id,deployable,1',
        'checkout_to_type'  => 'required|in:asset,location,user',
        'checkout_at'       => 'nullable|date',
        'expected_checkin'  => 'nullable|date',
        'requestable'       => 'nullable|boolean',
    ];

    if ($settings->require_checkinout_notes) {
        $rules['note'] = 'required|string';
    }
    return $rules;
}
```

| Rule | Ý nghĩa |
|---|---|
| `required_without_all` | Phải có ít nhất 1 trong 3 target: user, asset, hoặc location |
| `exists_undeleted:users,id` | Target phải tồn tại và KHÔNG bị soft-deleted |
| `exists:status_labels,id,deployable,1` | Status label phải là deployable |
| `checkout_to_type` whitelist | `in:asset,location,user` |
| `note` conditionally required | Chỉ bắt buộc khi settings yêu cầu |

### 2.2 FormRequest: `StoreAssetRequest`

```php
public function rules(): array
{
    $modelRules = (new Asset)->getRules();

    // assigned_to / assigned_type bị unset — chỉ được set qua checkout flow
    // (assigned_user/asset/location) để đảm bảo có audit log
    unset($modelRules['assigned_to'], $modelRules['assigned_type']);

    return array_merge(
        $modelRules,
        ['status_id' => [new AssetCannotBeCheckedOutToNondeployableStatus]],
        parent::rules(),
    );
}
```

**Custom Rule Object:** `AssetCannotBeCheckedOutToNondeployableStatus` — đảm bảo asset không được tạo với status non-deployable nếu có `assigned_user`. Kế thừa rules từ `Asset::$rules`. `prepareForValidation` tự động parse `last_audit_date`, format `purchase_cost`, gán `company_id`.

### 2.3 FormRequest: `StoreConsumableRequest`

```php
public function rules(): array
{
    return array_merge(
        ['category_type' => 'in:consumable'],  // Bắt buộc category là loại consumable
        parent::rules(),
    );
}
```

`prepareForValidation` tự động lookup category và merge `category_type` vào request data. `category_type.in` từ chối nếu user chọn category không phải loại consumable.

### 2.4 FormRequest: `AccessoryCheckoutRequest` — Pattern kiểm tra tồn kho

```php
public function prepareForValidation(): void
{
    $this->diff = ($this->accessory->numRemaining() - $this->checkout_qty);
    $this->merge([
        'checkout_qty'                     => $this->checkout_qty ?? 1,
        'number_remaining_after_checkout'  => (int) ($this->accessory->numRemaining() - $this->checkout_qty),
        'number_currently_remaining'       => (int) $this->accessory->numRemaining(),
        'checkout_difference'              => (int) $this->diff,
    ]);
}

public function rules(): array
{
    return [
        'number_remaining_after_checkout' => ['min:0', 'required', 'integer'],
        'checkout_qty'                    => ['integer', 'lte:number_currently_remaining', 'min:1'],
    ];
}
```

Pattern: tính `numRemaining() - checkout_qty`. Nếu < 0 → validation fail với message `"Số lượng checkout (X) vượt quá số còn lại (Y)"`.

### 2.5 Luồng Guard Clause Đầy Đủ — Asset Checkout

```
┌───────────────────────────────────────────────────────────────────────┐
│                     LUỒNG CHECKOUT ASSET                               │
├───────────────────────────────────────────────────────────────────────┤
│                                                                       │
│  1. FormRequest Validation (AssetCheckoutRequest)                     │
│     ├─ checkout_to_type required + in:asset|location|user             │
│     ├─ assigned_user/asset/location required_without_all              │
│     ├─ exists_undeleted (reject soft-deleted targets)                 │
│     └─ status_id must be deployable (if provided)                     │
│                                                                       │
│  2. Authorization Gate                                                │
│     ├─ Gate::allows('checkout', Asset::class) — quyền checkout chung  │
│     └─ Gate::allows('checkout', $asset) — quyền trên asset cụ thể     │
│                                                                       │
│  3. Business Rule Check                                               │
│     └─ $asset->availableForCheckout()                                 │
│         ├─ assigned_to == null                                        │
│         ├─ deleted_at == null                                         │
│         └─ status.deployable == 1 && status.archived == 0             │
│                                                                       │
│  4. Target Resolution (unscoped)                                      │
│     ├─ User/Location/Asset::withoutGlobalScopes()->find()             │
│     └─ Exclude soft-deleted targets                                   │
│                                                                       │
│  5. FMCS Company Check                                                │
│     └─ checkoutCompanyMismatchResponse($asset, $target)               │
│         └─ Asset và target phải cùng company_id (nếu FMCS bật)        │
│                                                                       │
│  6. Concurrency Guard (DB Transaction + lockForUpdate)                │
│     └─ DB::transaction() → Asset::lockForUpdate()                     │
│         └─ Re-check availableForCheckout() trên locked row            │
│                                                                       │
│  7. Execute Checkout                                                  │
│     └─ $asset->checkOut($target, ...)                                 │
│         ├─ $this->assignedTo()->associate($target)                    │
│         ├─ Set location_id                                            │
│         └─ event(new CheckoutableCheckedOut(...))                     │
│                                                                       │
└───────────────────────────────────────────────────────────────────────┘
```

**Concurrency guard** gọi `availableForCheckout()` **hai lần**:
1. Lần 1 (advisory): Trước transaction, không lock → nhanh
2. Lần 2 (authoritative): Trong transaction, `lockForUpdate()` → đảm bảo không race condition

Pattern này cũng áp dụng cho Consumable và License checkout.

---

## 3. XỬ LÝ NGOẠI LỆ (EXCEPTION HANDLING)

### 3.1 HTTP Status Code Convention

Snipe-IT **KHÔNG dùng HTTP status codes convention** — hầu hết response đều trả về **HTTP 200** và phân biệt qua JSON body. Ngoại lệ: Validation errors từ FormRequest → **422**, Authorization → **403**.

| Tình huống | HTTP | Cấu trúc Response |
|---|---|---|
| **Validation thất bại** (FormRequest) | `422` | `{"message": "...", "errors": {"field": ["..."]}}` |
| **Asset không tồn tại** | `200` | `{"status": "error", "messages": "Asset does not exist."}` |
| **Asset không khả dụng để checkout** | `200` | `{"status": "error", "payload": {"asset": "..."}, "messages": "not available"}` |
| **Target không tồn tại** | `200` | `{"status": "error", "payload": {"target_id": ..., "target_type": "..."}, "messages": "target does not exist"}` |
| **FMCS company mismatch** | `200` | `{"status": "error", "payload": {...}, "messages": "error_user_company"}` |
| **Consumable hết hàng** | `200` | `{"status": "error", "messages": "consumable not available"}` |
| **Checkout vượt tồn kho** | `200` | `{"status": "error", "messages": "requested X, remaining Y"}` |
| **Category không hợp lệ** | `200` | `{"status": "error", "messages": "Invalid category type"}` |
| **Unauthorized (Gate)** | `403` | Laravel default exception |
| **Rate limit exceeded** | `429` | Laravel default throttle |

### 3.2 Hàm Helper `formatStandardApiResponse`

```php
public static function formatStandardApiResponse($status, $payload = null, $message = null)
{
    return compact('status', 'messages', 'payload');
    // 'messages' từ $message (translation key hoặc raw string)
    // 'payload' chứa context data để frontend hiển thị chi tiết lỗi
}
```

### 3.3 Ví dụ Error Response

**Checkout asset đã được assigned:**
```json
HTTP 200 OK
{
  "status": "error",
  "messages": "Asset MAC-0042 is not available for checkout.",
  "payload": { "asset": "MAC-0042" }
}
```

**Checkout consumable vượt số lượng:**
```json
HTTP 200 OK
{
  "status": "error",
  "messages": "That consumable is not available for checkout. Requested 10, remaining 3",
  "payload": null
}
```

**Checkout với target soft-deleted:**
```json
HTTP 200 OK
{
  "status": "error",
  "messages": "Checkout target for asset MAC-0042 is invalid - user does not exist.",
  "payload": {
    "asset": { "id": 123, "asset_tag": "MAC-0042" },
    "target_id": 999,
    "target_type": "user"
  }
}
```

### 3.4 CheckoutNotAllowed Exception

```php
// Asset::checkOut()
if ($this->is($target)) {
    throw new CheckoutNotAllowed('You cannot check an asset out to itself.');
}
```

`CheckoutNotAllowed` extends `\RuntimeException`. Không được catch bởi default handler → **500 Internal Server Error**. Hệ thống giả định không bao giờ xảy ra (vì `assigned_asset` đã validate `exists_undeleted` và UI không cho phép chọn chính mình).

### 3.5 Circular Reference Exception

```php
// Asset::assetLoc()
if ($iterations > 10) {
    throw new \Exception('Asset assignment Loop for Asset ID: ' . e($first_asset->id));
}
```

Defensive programming — phát hiện vòng lặp parent-child. Không có HTTP error format đặc biệt, sẽ thành 500 error.

---

## 4. TỔNG KẾT

| Đặc điểm | Mô tả |
|---|---|
| **API Response Format** | Action: `{status, messages, payload}`. Listing: `{total, rows}` |
| **Transformer Pattern** | Mọi response qua Transformer, wrapper: `DatatablesTransformer` |
| **Descriptive Names** | KHÔNG raw ID — luôn `{id, name}` cho mọi FK |
| **Custom Dates** | `Helper::getFormattedDateObject()` → `{formatted, date, datetime}` |
| **Info-Disclosure Guard** | Gate check trong Transformer — stripped PII khi thiếu permission |
| **Multi-layer Validation** | FormRequest → Gate Authorization → Business Rules → Concurrency Lock |
| **Concurrency Guard** | Advisory check → `lockForUpdate()` authoritative re-check trong transaction |
| **FMCS Enforcement** | `withoutGlobalScopes()` resolve → explicit company check → clear error message |
| **Status Code Convention** | Lỗi business → HTTP 200 + error body; validation → 422; auth → 403 |
| **`exists_undeleted` Rule** | Custom rule chặn soft-deleted targets từ request layer |

---

> **Kết thúc Giai đoạn 2.** Sẵn sàng cho Giai đoạn 3.