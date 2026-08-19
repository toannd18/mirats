# GIAI ĐOẠN 1: DATA & BUSINESS LOGIC LÕI — Snipe-IT

> **Tác giả:** Phân tích kiến trúc — 2026-08-05
> **Phạm vi:** Models, Migrations, Relationships, Accessors
> **Framework:** Laravel 12 / PHP 8.2+

---

## 1. SƠ ĐỒ CƠ SỞ DỮ LIỆU CỐT LÕI (MERMAID ERD)

```mermaid
erDiagram
    companies ||--o{ assets : "owns"
    companies ||--o{ consumables : "owns"
    companies ||--o{ accessories : "owns"
    companies ||--o{ components : "owns"
    companies ||--o{ users : "owns"
    companies ||--o{ locations : "owns"

    manufacturers ||--o{ models : "produces"
    manufacturers ||--o{ consumables : "produces"
    manufacturers ||--o{ accessories : "produces"
    manufacturers ||--o{ components : "produces"
    manufacturers ||--o{ licenses : "produces"

    categories ||--o{ models : "classifies (asset)"
    categories ||--o{ consumables : "classifies (consumable)"
    categories ||--o{ accessories : "classifies (accessory)"
    categories ||--o{ components : "classifies (component)"
    categories ||--o{ licenses : "classifies (license)"

    models ||--o{ assets : "templates → instances"
    models }o--|| custom_fieldsets : "fieldset_id"
    models }o--|| depreciations : "depreciation_id"

    custom_fieldsets ||--o{ custom_fields : "many-to-many via custom_field_custom_fieldset"

    assets }o--|| status_labels : "status_id"
    assets }o--|| locations : "location_id (denormalized)"
    assets }o--|| locations : "rtd_location_id (default)"
    assets }o--|| suppliers : "supplier_id"
    assets ||--o{ asset_maintenances : "maintenance history"
    assets ||--o{ action_logs : "item_type=Asset → item_id"
    assets ||--o{ license_seats : "asset_id each seat"
    assets }o--o{ users : "polymorphic assigned_to (when assigned_type=User)"
    assets }o--o{ locations : "polymorphic assigned_to (when assigned_type=Location)"
    assets }o--o{ assets : "polymorphic assigned_to (when assigned_type=Asset → Parent-Child)"
    assets }o--o{ components : "many-to-many via components_assets"
    assets ||--o{ accessory_checkouts : "polymorphic target → assets"

    consumables ||--o{ consumables_users : "checkout history"
    consumables_users }o--|| users : "assigned_to → user"
    consumables }o--|| locations : "location_id"
    consumables }o--|| suppliers : "supplier_id"

    accessories ||--o{ accessory_checkouts : "checkout history"
    accessories }o--|| locations : "location_id"
    accessories }o--|| suppliers : "supplier_id"

    components ||--o{ components_assets : "assignment pivot"
    components_assets }o--|| assets : "assigned to asset"
    components }o--|| locations : "location_id"

    licenses ||--o{ license_seats : "seats"
    license_seats }o--|| assets : "assigned asset"

    action_logs }o--|| users : "created_by (admin who did it)"
    action_logs ||--o{ locations : "location_id snapshot"

    users }o--|| locations : "location_id"
    users }o--|| departments : "department_id"

    locations ||--o| locations : "parent_id (hierarchy)"

    status_labels {
        int id PK
        varchar name
        bool deployable
        bool pending
        bool archived
        text notes
    }

    assets {
        int id PK
        varchar asset_tag
        varchar name
        varchar serial
        int model_id FK
        int status_id FK
        int location_id FK
        int rtd_location_id FK
        int supplier_id FK
        int company_id FK
        int assigned_to "polymorphic ID"
        varchar assigned_type "polymorphic type: User|Location|Asset"
        decimal purchase_cost
        date purchase_date
        int warranty_months
        date asset_eol_date
        bool eol_explicit
        datetime expected_checkin
        datetime last_checkout
        datetime last_checkin
        datetime last_audit_date
        date next_audit_date
        int checkin_counter
        int checkout_counter
        int requests_counter
        bool physical
        bool requestable
        bool archived
        varchar accepted "pending|accepted|declined"
        bool byod
        varchar order_number
        text notes
        text image
        timestamp created_at
        timestamp updated_at
        timestamp deleted_at
    }

    models {
        int id PK
        varchar name
        varchar model_number
        int manufacturer_id FK
        int category_id FK
        int depreciation_id FK
        int fieldset_id FK
        int eol "months"
        text notes
        bool requestable
        bool show_mac_address
        timestamp created_at
        timestamp updated_at
        timestamp deleted_at
    }

    consumables {
        int id PK
        varchar name
        varchar item_no
        varchar model_number
        int category_id FK
        int manufacturer_id FK
        int location_id FK
        int supplier_id FK
        int company_id FK
        int qty "current stock"
        int min_amt "reorder threshold"
        decimal purchase_cost
        date purchase_date
        varchar order_number
        bool requestable
        text image
        timestamp created_at
        timestamp updated_at
        timestamp deleted_at
    }

    consumables_users {
        int id PK
        int user_id FK
        int consumable_id FK
        int assigned_to "target user"
        timestamp created_at
        timestamp updated_at
    }

    action_logs {
        int id PK
        varchar item_type "polymorphic: Asset|License|Consumable|..."
        int item_id "polymorphic ID"
        varchar target_type "polymorphic: User|Asset|Location|..."
        int target_id "polymorphic ID"
        varchar action_type "checkout|checkin|audit|update|..."
        int created_by FK→users
        int location_id FK
        int company_id FK
        text note
        text log_meta "JSON blob of old/new values"
        varchar filename
        varchar remote_ip
        varchar user_agent
        varchar action_source "gui|api|cli"
        datetime action_date
        timestamp created_at
        timestamp updated_at
    }

    components {
        int id PK
        varchar name
        varchar serial
        int qty
        int min_amt
        int category_id FK
        int company_id FK
        int location_id FK
        int manufacturer_id FK
        int supplier_id FK
        decimal purchase_cost
        date purchase_date
        varchar order_number
        varchar model_number
        text notes
        timestamp created_at
        timestamp updated_at
        timestamp deleted_at
    }

    components_assets {
        int id PK
        int component_id FK
        int asset_id FK
        int assigned_qty
        int created_by
        text note
        timestamp created_at
    }

    accessories {
        int id PK
        varchar name
        int qty
        int min_amt
        int category_id FK
        int company_id FK
        int location_id FK
        int manufacturer_id FK
        int supplier_id FK
        decimal purchase_cost
        date purchase_date
        varchar order_number
        varchar model_number
        bool requestable
        text image
        text notes
        timestamp created_at
        timestamp updated_at
        timestamp deleted_at
    }

    accessory_checkouts {
        int id PK
        int accessory_id FK
        int assigned_to "polymorphic ID"
        varchar assigned_type "polymorphic type: User|Asset|Location"
        int created_by
        text note
        timestamp created_at
        timestamp updated_at
    }

    license_seats {
        int id PK
        int license_id FK
        int asset_id FK nullable
        text notes
        int created_by
        timestamp created_at
    }

    licenses {
        int id PK
        varchar name
        varchar serial
        int seats "total seats"
        int manufacturer_id FK
        int category_id FK
        int supplier_id FK
        int company_id FK
        decimal purchase_cost
        date purchase_date
        date expiration_date
        varchar license_email
        varchar license_name
        varchar order_number
        varchar purchase_order
        bool maintained
        int depreciation_id FK
        int min_amt
        bool reassignable
        text notes
        timestamp created_at
        timestamp updated_at
        timestamp deleted_at
    }
```

---

## 2. TÓM TẮT LUỒNG NGHIỆP VỤ

### 2.1 Asset lồng nhau (Parent-Child)

#### 2.1.1 Mô hình polymorphic

Bảng `assets` sử dụng cặp cột **`assigned_to` + `assigned_type`** để thực hiện quan hệ đa hình (polymorphic). Một asset có thể được checkout tới một trong ba loại target:

| `assigned_type` | Target | Ý nghĩa |
|---|---|---|
| `App\Models\User` | Người dùng | Asset đang do một nhân viên sử dụng |
| `App\Models\Location` | Địa điểm | Asset đang ở một vị trí cụ thể |
| `App\Models\Asset` | Tài sản khác | Asset con được gắn vào một asset cha |

#### 2.1.2 Mối quan hệ Eloquent

```php
// Asset.php

// TRUY VẤN XUÔI: "Asset này đang được gán cho ai/cái gì?"
public function assignedTo()
{
    return $this->morphTo('assigned', 'assigned_type', 'assigned_to')->withTrashed();
}

// TRUY VẤN NGƯỢC: "Những asset nào được gán VÀO asset này?"
public function assignedAssets()
{
    return $this->morphMany(self::class, 'assigned', 'assigned_type', 'assigned_to')->withTrashed();
}
```

##### Ví dụ thực tế:

```
Máy chủ Dell R740 (asset cha, id=100)
  ├── Card mạng Intel X540 (asset con, assigned_to=100, assigned_type=App\Models\Asset)
  ├── Ổ cứng SSD Samsung (asset con, assigned_to=100, assigned_type=App\Models\Asset)
  └── RAM Kingston 32GB  (asset con, assigned_to=100, assigned_type=App\Models\Asset)
```

Khi gọi `$mayChu->assignedAssets` → trả về collection gồm card mạng, ổ cứng, RAM.

#### 2.1.3 Cơ chế phân giải vị trí đệ quy `assetLoc()`

Đây là method quan trọng nhất để xác định vị trí **thực tế** của một asset trong chuỗi phân cấp cha-con:

```php
// Asset.php — dòng 828-862
public function assetLoc($iterations = 1, $first_asset = null)
{
    if (! empty($this->assignedType())) {
        // Nếu asset ĐANG LÀ CON của một asset khác → đệ quy lên cha
        if ($this->assignedType() == self::ASSET) {
            if ($iterations > 10) {
                throw new \Exception('Asset assignment Loop for Asset ID: '.e($first_asset->id));
            }
            $assigned_to = self::find($this->assigned_to);
            if ($assigned_to) {
                return $assigned_to->assetLoc($iterations + 1, $first_asset);
            }
        }
        // Nếu asset được gán TRỰC TIẾP cho Location
        if ($this->assignedType() == self::LOCATION) {
            if ($this->assignedTo) {
                return $this->assignedTo;  // Trả về Location object
            }
        }
        // Nếu asset được gán cho User → trả về location của user
        if ($this->assignedType() == self::USER) {
            if (($this->assignedTo) && $this->assignedTo->userLoc) {
                return $this->assignedTo->userLoc;
            }
            return $this->defaultLoc; // Fallback: RTD location
        }
    }
    return $this->defaultLoc; // Không được gán cho ai
}
```

**Nguyên lý:** Đệ quy đi lên chuỗi phân cấp cho đến khi gặp Location hoặc User, tối đa 10 cấp. Có cơ chế chống circular reference (throw Exception nếu vượt 10 cấp).

**Ví dụ minh họa:**
```
Card mạng (asset con, id=200)
  └─ assigned_to = Máy chủ (asset cha, id=100) [assigned_type=Asset]
       └─ assigned_to = User "Nguyen Van A" [assigned_type=User]
            └─ userLoc = Location "Phòng Server Tầng 3"
```

Khi gọi `$cardMang->assetLoc()`:
1. `assignedType()` = `'asset'` → đệ quy tìm cha
2. Tìm thấy Máy chủ (id=100), gọi `$mayChu->assetLoc(2, $cardMang)`
3. Máy chủ `assignedType()` = `'user'` → `$mayChu->assignedTo->userLoc`
4. Kết quả: Location "Phòng Server Tầng 3"

#### 2.1.4 Cơ chế lan truyền vị trí

| Sự kiện | Lan truyền xuống con cấp 1? | Lan truyền đệ quy (cháu)? |
|---|---|---|
| Checkout cha → Location | ✅ CÓ | ❌ KHÔNG — cần `snipeit:sync-asset-locations` |
| Checkin cha (trả về kho) | ✅ CÓ | ❌ KHÔNG |
| Checkout cha → User | ❌ KHÔNG | ❌ KHÔNG |

**Lưu ý:** Cột `location_id` trên `assets` là **denormalized data** — được sao chép trực tiếp để tối ưu query filter nhưng có thể lệch pha. Method `assetLoc()` luôn trả về kết quả chính xác real-time nhưng tốn kém hơn về hiệu năng (N+1 query khi gọi cho collection).

**Lệnh đồng bộ thủ công:**
```bash
php artisan snipeit:sync-asset-locations
```
Lệnh này duyệt tất cả asset con, gọi `assetLoc()` cho từng cái, và ghi lại `location_id` mới.

---

### 2.2 Cách tính toán số lượng Consumable động

#### 2.2.1 Khác biệt kiến trúc: Asset vs Consumable

| Khía cạnh | `assets` (Tài sản) | `consumables` (Vật tư) |
|---|---|---|
| **Đơn vị quản lý** | Từng **cá thể** riêng biệt | Một **dòng** = một LOẠI vật tư |
| **Số lượng** | Không có cột qty (1 asset = 1 thực thể) | Có cột `qty` — tổng số lượng tồn kho |
| **Định danh** | `asset_tag` + `serial` (truy vết cá thể) | `item_no` + `model_number` (định danh dòng) |
| **Ngưỡng cảnh báo** | Không có | Có `min_amt` — cảnh báo sắp hết |

#### 2.2.2 Cơ chế tính số lượng

```php
// Consumable.php

// TỔNG SỐ ĐÃ CHECKOUT (số lượt cấp phát)
public function numCheckedOut()
{
    // Ưu tiên dùng withCount() đã eager-load để tránh N+1
    return $this->consumables_users_count ?? $this->users()->count();
}

// SỐ CÒN LẠI
public function numRemaining()
{
    $checkedout = $this->numCheckedOut();
    $total = $this->qty;
    return $total - $checkedout;
}

// PHẦN TRĂM CÒN LẠI
public function percentRemaining()
{
    if ($this->consumables_users_count == 0) {
        return 100;
    }
    if (($this->qty == '') || ($this->qty == 0)) {
        return 0;
    }
    return ($this->qty - $this->consumables_users_count) / $this->qty * 100;
}

// TỔNG CHI PHÍ
public function totalCostSum()
{
    return $this->purchase_cost !== null ? $this->qty * $this->purchase_cost : null;
}
```

#### 2.2.3 Bảng pivot `consumables_users`

Bảng pivot ghi nhận mỗi lần cấp phát vật tư cho người dùng:

| Cột | Ý nghĩa |
|---|---|
| `id` | PK |
| `user_id` | Người tạo bản ghi cấp phát |
| `consumable_id` | Vật tư được cấp |
| `assigned_to` | ID người nhận (có thể khác user_id) |
| `created_at` / `updated_at` | Timestamps |

Mối quan hệ trong Eloquent:
```php
// Consumable → Users: BelongsToMany qua consumables_users
public function users(): Relation
{
    return $this->belongsToMany(
        User::class,
        'consumables_users',   // pivot table
        'consumable_id',        // foreign key on pivot → consumables
        'assigned_to'           // foreign key on pivot → users
    )->withPivot('created_by')->withTrashed()->withTimestamps();
}
```

**Công thức tính số lượng tồn kho:**
```
qty_remaining = consumables.qty - COUNT(consumables_users WHERE consumable_id = X)
```

**Lưu ý hiệu năng:** Controller API luôn gọi `withCount('users as consumables_users_count')` trước khi truy vấn collection để tránh N+1 queries khi tính `numRemaining()` cho từng consumable.

#### 2.2.4 Pattern tương tự: Components và Accessories

Cả **Components** và **Accessories** cũng sử dụng cùng pattern "quản lý kho theo số lượng":

| Model | Bảng pivot | Cột qty | Cơ chế đếm checkout |
|---|---|---|---|
| Consumable | `consumables_users` | `qty` | `users()->count()` → `consumables_users_count` |
| Component | `components_assets` | `qty` | `unconstrainedAssets()->sum('assigned_qty')` → `sum_unconstrained_assets` |
| Accessory | `accessories_checkout` | `qty` | `checkouts()->count()` → `checkouts_count` |

Cả ba đều có các method tương tự: `numCheckedOut()`, `numRemaining()`, `percentRemaining()`.

---

## 3. PHÂN TÍCH RELATIONSHIPS VÀ ACCESSORS

### 3.1 Bản đồ quan hệ đầy đủ của Asset Model

```
┌──────────────────────────────────────────────────────────────────────────┐
│                            ASSET MODEL                                    │
│                         (app/Models/Asset.php)                            │
├──────────────────────────────────────────────────────────────────────────┤
│                                                                          │
│  ┌─── belongsTo ───────────────────────────────────────────────────┐    │
│  │  model()       → AssetModel     (model_id)                       │    │
│  │  status()      → Statuslabel    (status_id)                      │    │
│  │  location()    → Location       (location_id)                    │    │
│  │  defaultLoc()  → Location       (rtd_location_id)                │    │
│  │  supplier()    → Supplier       (supplier_id)                    │    │
│  │  company()     → Company        (company_id)                     │    │
│  └──────────────────────────────────────────────────────────────────┘    │
│                                                                          │
│  ┌─── hasOneThrough ────────────────────────────────────────────────┐    │
│  │  manufacturer()  → Manufacturer  (thông qua AssetModel)           │    │
│  │  category()      → Category      (thông qua AssetModel)           │    │
│  │  depreciation()  → Depreciation   (thông qua AssetModel)          │    │
│  └──────────────────────────────────────────────────────────────────┘    │
│                                                                          │
│  ┌─── morphTo (polymorphic) ────────────────────────────────────────┐    │
│  │  assignedTo() → User | Location | Asset  (assigned_to + type)     │    │
│  └──────────────────────────────────────────────────────────────────┘    │
│                                                                          │
│  ┌─── morphMany (polymorphic ngược) ────────────────────────────────┐    │
│  │  assignedAssets()     → Asset[]    (asset con)                    │    │
│  │  assignedAccessories()→ AccessoryCheckout[]                       │    │
│  └──────────────────────────────────────────────────────────────────┘    │
│                                                                          │
│  ┌─── hasMany ──────────────────────────────────────────────────────┐    │
│  │  assetlog()      → Actionlog[]  (item_type=Asset, item_id)        │    │
│  │  licenseseats()  → LicenseSeat[] (asset_id)                       │    │
│  │  maintenances()  → Maintenance[] (asset_id)                       │    │
│  └──────────────────────────────────────────────────────────────────┘    │
│                                                                          │
│  ┌─── belongsToMany ────────────────────────────────────────────────┐    │
│  │  components()   → Component[]   (pivot: components_assets)        │    │
│  │  licenses()     → License[]     (pivot: license_seats)            │    │
│  └──────────────────────────────────────────────────────────────────┘    │
│                                                                          │
│  ┌─── hasManyThrough ───────────────────────────────────────────────┐    │
│  │  accessories() → Accessory[] (thông qua AccessoryCheckout,         │    │
│  │                  only assigned_type = Asset::class)                │    │
│  └──────────────────────────────────────────────────────────────────┘    │
│                                                                          │
│  ┌─── Scopes truy vấn ──────────────────────────────────────────────┐    │
│  │  Hardware()      → physical=1                                     │    │
│  │  RTD()           → null assigned_to + deployable status           │    │
│  │  Deployed()      → assigned_to > 0                                │    │
│  │  Pending()       → pending status                                 │    │
│  │  Archived()      → archived status                                │    │
│  │  Undeployable()  → undeployable status                            │    │
│  │  NotArchived()   → not_archived status                            │    │
│  │  NotYetAccepted()→ accepted='pending'                              │    │
│  │  Rejected()      → accepted='rejected'                             │    │
│  │  Accepted()      → accepted='accepted'                             │    │
│  │  RequestableAssets() → requestable=1 + status constraints         │    │
│  │  DueForAudit()   → next_audit_date trong khoảng cảnh báo          │    │
│  │  OverdueForAudit()→ next_audit_date < now                         │    │
│  │  DueForCheckin() → expected_checkin trong khoảng cảnh báo          │    │
│  │  OverdueForCheckin()→ expected_checkin < now                      │    │
│  └──────────────────────────────────────────────────────────────────┘    │
│                                                                          │
└──────────────────────────────────────────────────────────────────────────┘
```

#### Điểm đặc biệt về `hasOneThrough`

Asset không có trực tiếp `manufacturer_id` hay `category_id` trong bảng của mình. Thay vào đó, các quan hệ này đi **xuyên qua** AssetModel:

```php
// Asset.php — dòng 676-683
public function manufacturer()
{
    // Asset → AssetModel (model_id) → Manufacturer (manufacturer_id)
    return $this->hasOneThrough(Manufacturer::class, AssetModel::class,
        'id', 'id', 'model_id', 'manufacturer_id');
}

public function category()
{
    // Asset → AssetModel (model_id) → Category (category_id)
    return $this->hasOneThrough(Category::class, AssetModel::class,
        'id', 'id', 'model_id', 'category_id');
}
```

**Lợi ích:** Dữ liệu được chuẩn hóa — manufacturer và category chỉ lưu trên Models, mọi Asset cùng model tự động kế thừa.

---

### 3.2 Accessors — Trả về tên mô tả thay vì chỉ ID

Snipe-IT sử dụng **Laravel Accessors** (cả kiểu cũ và kiểu mới — Attribute cast) để luôn trả về dữ liệu đã được định dạng, không bao giờ trả về raw ID cho người dùng.

#### 3.2.1 Accessor kiểu mới (Laravel 9+ Attribute)

```php
// Asset.php

// WARRANTY: Tính ngày hết hạn bảo hành từ purchase_date + warranty_months
protected function warrantyExpires(): Attribute
{
    return Attribute::make(
        get: fn ($value, $attributes) =>
            ($attributes['warranty_months'] && $attributes['purchase_date'])
                ? Carbon::parse($attributes['purchase_date'])
                    ->addMonths((int) $attributes['warranty_months'])
                : null,
    );
}

// Formatted versions — trả về object { formatted, date, datetime } cho API
protected function warrantyExpiresFormattedDate(): Attribute
{
    return Attribute::make(
        get: fn () => Helper::getFormattedDateObject(
            $this->warrantyExpires, 'date', false
        )
    );
}

// EOL: Tính từ purchase_date + model.eol (hoặc asset_eol_date nếu explicit)
protected function eolDate(): Attribute
{
    return Attribute::make(
        get: function ($value, $attributes) {
            if ($attributes['asset_eol_date'] && $attributes['eol_explicit'] == '1') {
                return Carbon::parse($attributes['asset_eol_date']);
            } elseif ($attributes['purchase_date'] && $this->model
                && ((int) $this->model->eol > 0)) {
                return Carbon::parse($attributes['purchase_date'])
                    ->addMonths((int) $this->model->eol);
            }
            return null;
        }
    );
}

// LOCATION/COMPANY: Chuẩn hóa NULL — không bao giờ lưu 0
protected function locationId(): Attribute
{
    return Attribute::make(
        get: fn ($value) => ($value === null || (int) $value === 0) ? null : (int) $value,
        set: fn ($value) => ($value === '' || $value === null || (int) $value === 0) ? null : (int) $value,
    );
}
```

#### 3.2.2 Accessor kiểu cũ — `getDisplayNameAttribute()`

```php
// Asset.php — dòng 322-325
public function getDisplayNameAttribute()
{
    // Ủy quyền cho Presenter để format tên hiển thị
    return $this->present()->name();
}

// Dạng chi tiết hơn cho admin
public function getDetailedNameAttribute()  // dòng 616-625
{
    if ($this->assignedto) {
        $user_name = $this->assignedto->present()->name();
    } else {
        $user_name = 'Unassigned';
    }
    return $this->asset_tag.' - '.$this->name.' ('.$user_name.') '
        .($this->model ? $this->model->name : '');
}
```

#### 3.2.3 Diff-for-humans Accessors

Mỗi loại ngày tháng đều có accessor trả về `diffForHumans()` (tiếng Việt có thể cấu hình qua Carbon locale):

```php
protected function eolDiffForHumans(): Attribute
{
    return Attribute::make(
        get: fn () => $this->eolDate
            ? Carbon::parse($this->eolDate)->diffForHumans()
            : null,
    );
}

protected function warrantyExpiresDiff(): Attribute
{
    return Attribute::make(
        get: fn () => $this->warrantyExpires
            ? round(Carbon::now()->diffInDays($this->warrantyExpires))
            : null,
    );
}
```

Kết quả: API luôn trả về các trường như `"eol_diff_for_humans": "6 tháng nữa"` thay vì yêu cầu frontend tự tính.

---

### 3.3 Trait `Searchable` — Tìm kiếm xuyên quan hệ

Trait `Searchable` (trong `app/Models/Traits/Searchable.php`) cho phép mọi model định nghĩa:

```php
// Asset.php
protected $searchableAttributes = [
    'name', 'asset_tag', 'serial', 'order_number', 'purchase_cost',
    'notes', 'created_at', 'updated_at', 'purchase_date', 'expected_checkin',
    'next_audit_date', 'last_audit_date', 'last_checkin', 'last_checkout',
    'asset_eol_date',
];

protected $searchableRelations = [
    'status'       => ['name'],
    'supplier'     => ['name'],
    'company'      => ['name'],
    'defaultLoc'   => ['name'],
    'location'     => ['name'],
    'model'        => ['name', 'model_number', 'eol'],
    'category'     => ['name'],
    'manufacturer' => ['name'],
    'assigned_to'  => ['name'],   // ← Polymorphic! Xử lý đặc biệt trong advancedTextSearch
];

// Alias cho API: key API → relation name thực tế
protected $searchableRelationAliases = [
    'status_label'  => 'status',
    'assigned_to'   => 'assignedTo',
    'model_number'  => 'model',
    'rtd_location'  => 'defaultLoc',
];
```

**Cách hoạt động:** Khi API nhận filter `?search=iPhone`, hệ thống tự động JOIN các bảng liên quan và tìm trong `models.name`, `categories.name`, `manufacturers.name`, `assigned_users.first_name`, `assigned_users.last_name`, `assigned_locations.name`, `assigned_assets.name` — tất cả trong một query. Người dùng không cần biết cấu trúc bảng bên dưới.

---

### 3.4 Polymorphic trong Actionlog

Bảng `action_logs` là trung tâm audit trail của toàn hệ thống, sử dụng **hai polymorphic relationships**:

```
action_logs
├── item_type + item_id → "CÁI GÌ bị tác động?" (Asset, License, Consumable...)
└── target_type + target_id → "TÁC ĐỘNG TỚI AI/CÁI GÌ?" (User, Asset, Location...)
```

```php
// Actionlog.php
public function item()
{
    return $this->morphTo('item')->withTrashed();
    // item_type = 'App\Models\Asset' → trả về Asset instance
    // item_type = 'App\Models\License' → trả về License instance
}

public function target()
{
    return $this->morphTo('target')->withTrashed();
    // target_type = 'App\Models\User' → người nhận checkout
    // target_type = 'App\Models\Asset' → asset cha trong Parent-Child
}
```

Khi checkout một asset cho user:
- `item_type` = `App\Models\Asset`, `item_id` = asset.id
- `target_type` = `App\Models\User`, `target_id` = user.id
- `action_type` = `checkout`

Điều này cho phép audit trail thống nhất cho **tất cả** các loại entity (asset, license, consumable, accessory, component) mà không cần nhiều bảng log riêng biệt.

---

### 3.5 Presenter Pattern — Tầng trình bày

Snipe-IT sử dụng Presenter pattern (thông qua trait `Presentable` và package `robclancy/presenter`) để tách biệt logic hiển thị khỏi Model:

```php
// Asset.php
protected $presenter = AssetPresenter::class;

public function getDisplayNameAttribute()
{
    return $this->present()->name();  // Ủy quyền cho AssetPresenter
}
```

**Ví dụ từ AssetPresenter** (`app/Presenters/AssetPresenter.php`):
- `name()` — Format: "Mã thẻ - Tên asset (Tên model, Serial)"
- `statusMeta()` — Trả về CSS class, icon cho status label
- `warrantyText()` — "Còn 30 ngày" / "Đã hết hạn"

Điều này đảm bảo:
- **Không bao giờ** trả về raw ID từ API (luôn có descriptive text đi kèm)
- Logic hiển thị không nằm trong Model (giữ Model "skinny")
- Dễ dàng thay đổi format hiển thị mà không ảnh hưởng business logic

---

### 3.6 CompanyableScope — Multi-tenant tự động

Trait `CompanyableTrait` (dùng bởi Asset, Consumable, Accessory, Component, License, User...) tự động thêm global scope để filter theo `company_id` khi FMCS được bật:

```php
// CompanyableScope.php
// Tự động thêm WHERE company_id IN (...) cho mọi query
// khi Setting::getSettings()->full_multiple_companies_support == '1'
```

Điều này có nghĩa là developer không cần thêm `->where('company_id', ...)` vào mọi query — hệ thống tự động filter theo quyền công ty của user hiện tại.

---

### 3.7 Kiến trúc kế thừa Model

```
SnipeModel (base)
├── Depreciable (abstract, thêm depreciation logic)
│   └── Asset
├── Accessory
├── Consumable
├── Component
├── License
├── AssetModel
├── Category
├── Statuslabel
├── Actionlog
├── User
└── ...
```

**Depreciable** (`app/Models/Depreciable.php`) là abstract class cung cấp method `depreciate()` cho cả Asset và License — cả hai loại đều có thể áp dụng khấu hao. Đây là một ví dụ về **Template Method Pattern**: logic khấu hao được định nghĩa một lần và tái sử dụng.

---

## 4. TỔNG KẾT KIẾN TRÚC

| Đặc điểm | Mô tả |
|---|---|
| **Polymorphic Assignment** | `assets.assigned_to` + `assigned_type` cho phép gán asset cho User, Location, hoặc Asset khác (Parent-Child) |
| **Parent-Child đệ quy** | `assetLoc()` đệ quy tối đa 10 cấp, có chống circular reference |
| **Denormalized Location** | `location_id` được cache trên asset để tối ưu query; `assetLoc()` là "source of truth" |
| **Inventory Management** | Consumables/Components/Accessories dùng `qty - checkout_count` pattern |
| **Searchable Trait** | Tìm kiếm xuyên quan hệ tự động, JOIN được alias cho API |
| **Presenter Pattern** | Tách logic hiển thị khỏi Model, luôn trả về descriptive text |
| **Multi-tenant** | CompanyableScope tự động filter theo công ty |
| **Audit Trail** | Actionlog dùng dual polymorphic (item + target) cho mọi loại entity |
| **hasOneThrough** | Asset → Model → Manufacturer/Category/Depreciation (chuẩn hóa dữ liệu) |

---

> **Kết thúc Giai đoạn 1.** Sẵn sàng cho Giai đoạn 2: Controllers & API Layer.