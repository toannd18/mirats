# GIAI ĐOẠN 3: AUTHORIZATION, SCOPES & DYNAMIC FIELDS — Snipe-IT

> **Tác giả:** Phân tích kiến trúc — 2026-08-05  
> **Phạm vi:** `app/Policies/`, `app/Providers/`, `app/Models/` (User, Group, CustomField, CustomFieldset, Company), `app/Listeners/`  
> **Framework:** Laravel 12 / PHP 8.2+

---

## 1. CƠ CHẾ PHÂN QUYỀN (PERMISSIONS & POLICIES)

### 1.1 Lưu trữ Permissions dưới dạng JSON Blob

Snipe-IT không dùng bảng riêng cho từng permission mà lưu **tất cả permissions dưới dạng JSON** trong cột `permissions` trên cả `users` và `permission_groups`.

**Schema:**

```
users.permissions → JSON (VARCHAR/TEXT)
permission_groups.permissions → JSON (VARCHAR/TEXT)
```

**Cấu trúc JSON:**

```json
{
  "assets.view": 1,
  "assets.create": 1,
  "assets.edit": 1,
  "assets.delete": 1,
  "assets.checkout": 1,
  "assets.checkin": 1,
  "assets.audit": 0,
  "consumables.view": 1,
  "consumables.create": 0,
  "consumables.edit": -1,
  "admin": -1
}
```

**Quy ước giá trị:**
| Giá trị | Ý nghĩa |
|---|---|
| `1` | **Grant** — Cho phép |
| `0` | **Not Set** — Không có ý kiến (fall through để check group) |
| `-1` | **Deny** — Cấm tuyệt đối (override cả group grant) |

### 1.2 User & Group Model Methods

#### User::decodePermissions()

```php
// Đọc cột permissions và decode thành mảng
public function decodePermissions()
{
    if (is_array($this->permissions)) {
        $this->permissions = json_encode($this->permissions);
    }
    $permissions = json_decode($this->permissions ?? '{}', JSON_OBJECT_AS_ARRAY);
    // Cast tất cả value sang int
    foreach ($permissions as $permission => $value) {
        if (! is_int($permission)) {
            $permissions[$permission] = (int) $value;
        }
    }
    return $permissions ?: new \stdClass;
}
```

#### User::checkPermissionSection() — Logic phân giải quyền

```php
protected function checkPermissionSection($section)
{
    $user_groups = $this->groups;
    $user_permissions = json_decode(json_encode($this->permissions), true);

    // PRIORITY 1: Explicit User Grant
    if (isset($user_permissions[$section]) && $user_permissions[$section] == '1') {
        return true;
    }

    // PRIORITY 2: Explicit User Deny
    if (isset($user_permissions[$section]) && $user_permissions[$section] == '-1') {
        return false;
    }

    // PRIORITY 3: Group Grant
    foreach ($user_groups as $user_group) {
        $group_permissions = (array) json_decode($user_group->permissions, true);
        if (isset($group_permissions[$section]) && $group_permissions[$section] == '1') {
            return true;
        }
    }

    // PRIORITY 4: Default Deny
    return false;
}
```

#### User::hasAccess() — Interface chính

```php
public function hasAccess($section)
{
    if ($this->isSuperUser()) {
        return true;  // Superuser bypass tất cả
    }
    return $this->checkPermissionSection($section);
}
```

#### User::isSuperUser()

```php
public function isSuperUser()
{
    return $this->checkPermissionSection('superuser');
}
```

#### User::isAdmin()

```php
public function isAdmin()
{
    return $this->checkPermissionSection('admin');
}
```

### 1.3 Luồng phân quyền đầy đủ

```
┌─────────────────────────────────────────────────────────────────────────┐
│                    LUỒNG PHÂN QUYỀN (PERMISSION RESOLUTION)              │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                         │
│  1. Gate::before() — AuthServiceProvider (CHẶN ĐẦU TIÊN)               │
│     └─ if ($user->isSuperUser()) → TRUE (cho tất cả abilities)          │
│        └─ NGOẠI LỆ: locked-on-demo → FALSE                              │
│                                                                         │
│  2. SnipePermissionsPolicy::before() (CHẶN THỨ HAI)                     │
│     ├─ if ($user->hasAccess('admin')) → TRUE (admin bypass)             │
│     └─ if (!$item instanceof Model) → return null (đi tiếp)             │
│     └─ if (!Company::isCurrentUserHasAccess($item)) → FALSE (FMCS block) │
│                                                                         │
│  3. Policy Method (index/view/create/update/delete/checkout/checkin)    │
│     └─ $user->hasAccess('{columnName}.{ability}')                       │
│        └─ checkPermissionSection($section)                              │
│                                                                         │
│  4. checkPermissionSection() Resolution                                 │
│     ├─ PRIORITY 1: User permissions có giá trị '1'  → TRUE              │
│     ├─ PRIORITY 2: User permissions có giá trị '-1' → FALSE             │
│     ├─ PRIORITY 3: Bất kỳ Group nào có giá trị '1' → TRUE               │
│     └─ PRIORITY 4: Không tìm thấy grant nào → FALSE (default deny)      │
│                                                                         │
└─────────────────────────────────────────────────────────────────────────┘
```

**Tóm tắt ưu tiên (từ cao xuống thấp):**
1. **Superuser** — Gate::before() → luôn TRUE
2. **Admin** — `$user->hasAccess('admin')` → TRUE
3. **FMCS Block** — `Company::isCurrentUserHasAccess($item)` → FALSE nếu khác công ty
4. **Explicit User Grant** — `permissions[section] = 1` → TRUE
5. **Explicit User Deny** — `permissions[section] = -1` → FALSE (override cả group)
6. **Group Grant** — Bất kỳ group nào có `permissions[section] = 1` → TRUE
7. **Default Deny** — Không tìm thấy → FALSE

### 1.4 Cây kế thừa Policy

```
SnipePermissionsPolicy (abstract)
├── columnName() — abstract, mỗi policy trả về key (ví dụ: 'assets', 'consumables')
├── before() — admin bypass + FMCS check
├── index(), view(), create(), update(), delete() — generic CRUD
├── checkout() — generic checkout permission
├── history(), journal(), files(), manageFiles()
└── manage()

CheckoutablePermissionsPolicy extends SnipePermissionsPolicy
├── checkout() — check 'checkout' permission
├── checkin()  — check 'checkin' permission (THÊM MỚI so với SnipePermissionsPolicy)
└── manage()   — compound: checkout OR checkin OR edit

AssetPolicy extends CheckoutablePermissionsPolicy
├── columnName() = 'assets'
├── viewRequestable() — check 'assets.view.requestable'
├── audit() — check 'assets.audit'
└── files() — check 'assets.files'

AccessoryPolicy, ConsumablePolicy, ComponentPolicy, LicensePolicy...
└── (tương tự, mỗi cái chỉ cần override columnName())
```

**Điểm quan trọng:** Policy nào chỉ cần CRUD cơ bản thì chỉ cần `columnName()`. Policy nào cần checkout/checkin thì extend `CheckoutablePermissionsPolicy`. Code rất DRY.

### 1.5 AuthServiceProvider — Đăng ký Policies

```php
// AuthServiceProvider::$policies
Accessory::class  => AccessoryPolicy::class,
Asset::class      => AssetPolicy::class,
AssetModel::class => AssetModelPolicy::class,
Category::class   => CategoryPolicy::class,
Component::class  => ComponentPolicy::class,
Consumable::class => ConsumablePolicy::class,
// ... 20+ policies
User::class       => UserPolicy::class,
```

### 1.6 General Gates (định nghĩa thủ công)

```php
// AuthServiceProvider::boot()

Gate::before(fn($user) => $user->isSuperUser() ? true : null);

Gate::define('admin', fn($user) => $user->hasAccess('admin'));
Gate::define('import', fn($user) => $user->hasAccess('import'));
Gate::define('reports.view', fn($user) => $user->hasAccess('reports.view'));
Gate::define('assets.view.encrypted_custom_fields', fn($user) => $user->hasAccess('assets.view.encrypted_custom_fields'));
Gate::define('self.two_factor', fn($user) => $user->hasAccess('self.two_factor') || $user->hasAccess('admin'));
Gate::define('self.api', fn($user) => $user->hasAccess('self.api'));
Gate::define('view.selectlists', fn($user) => /* compound check: update/create/checkout/checkin/audit của Asset, License, Component, Consumable, Accessory, User */);
Gate::define('backend.interact', fn($user) => /* compound check: view của Statuslabel, AssetModel, Category, Manufacturer, Supplier, Department, Location, Company, CustomField, CustomFieldset, Depreciation */);
```

---

## 2. GIỚI HẠN TẦM NHÌN DỮ LIỆU — FMCS (MULTI-TENANT)

### 2.1 Tổng quan

Full Multiple Company Support (FMCS) được bật qua setting `full_multiple_companies_support`. Khi bật, mọi query Eloquent lên các model có trait `CompanyableTrait` sẽ **tự động** bị filter theo công ty của user hiện tại.

### 2.2 CompanyableScope — Global Scope

```php
// app/Models/CompanyableScope.php
final class CompanyableScope implements Scope
{
    public function apply(Builder $builder, Model $model)
    {
        return Company::scopeCompanyables($builder);
    }
}
```

**Cách hoạt động:** Mỗi khi Eloquent chạy query trên một model có `CompanyableTrait`, Laravel tự động gọi `CompanyableScope::apply()` → `Company::scopeCompanyables()`.

### 2.3 Company::scopeCompanyables() — Core Logic

```php
public static function scopeCompanyables($query, $column = 'company_id', $table_name = null)
{
    // Nếu FMCS tắt → không filter (return nguyên query)
    if (Setting::getSettings()->full_multiple_companies_support != '1') {
        return $query;
    }

    // Nếu user là superuser → không filter
    if (auth()->user() && auth()->user()->isSuperUser()) {
        return $query;
    }

    return self::scopeCompanyablesDirectly($query, $column, $table_name);
}

private static function scopeCompanyablesDirectly($query, $column, $table)
{
    $userCompanyIds = self::getCurrentUserCompanyIds();

    if (empty($userCompanyIds)) {
        // User không thuộc công ty nào → chỉ thấy item không có company
        return $query->whereNull($column);
    }

    if (Setting::getSettings()->null_company_is_floater) {
        // "Floater mode": item không có company = system-wide
        return $query->where(function ($q) use ($userCompanyIds, $column) {
            $q->whereIn($column, $userCompanyIds)->orWhereNull($column);
        });
    }

    // Default: chỉ thấy item trong company của mình
    return $query->whereIn($column, $userCompanyIds);
}
```

### 2.4 Company::getCurrentUserCompanyIds() — Caching + Parent-Child

```php
private static function getCurrentUserCompanyIds(): array
{
    $userId = auth()->id();

    // Per-request cache
    if (isset(self::$companyIdsCache[$userId])) {
        return self::$companyIdsCache[$userId];
    }

    // Query pivot table company_user (tránh Eloquent để tránh recursion)
    $directIds = DB::table('company_user')
        ->where('user_id', $userId)
        ->pluck('company_id')
        ->toArray();

    if (empty($directIds)) {
        return self::$companyIdsCache[$userId] = [];
    }

    // Expand: user thuộc parent company → auto có access tới child companies
    $childIds = DB::table('companies')
        ->whereIn('parent_id', $directIds)
        ->pluck('id')
        ->toArray();

    return self::$companyIdsCache[$userId] = array_values(
        array_unique(array_merge($directIds, $childIds))
    );
}
```

**Kết quả SQL tự động thêm:**
```sql
-- Khi user thuộc company 1, 3:
WHERE assets.company_id IN (1, 3)

-- Khi user không thuộc company nào:
WHERE assets.company_id IS NULL

-- Khi "floater mode" bật:
WHERE (assets.company_id IN (1, 3) OR assets.company_id IS NULL)
```

### 2.5 Company::isCurrentUserHasAccess() — FMCS Check cho Policy

```php
// Dùng trong SnipePermissionsPolicy::before()
public static function isCurrentUserHasAccess($companyable)
{
    if (Setting::getSettings()->full_multiple_companies_support != '1') {
        return true;  // FMCS tắt → mọi thứ đều accessible
    }

    if (! $companyable) {
        return false;
    }

    // Superuser bỏ qua tất cả
    if (auth()->user() && auth()->user()->isSuperUser()) {
        return true;
    }

    return self::isCurrentUserAuthorizedCompany($companyable);
}
```

### 2.6 CompanyableChildScope — Cho các model con

```
CompanyableChildScope → dùng cho model không có company_id trực tiếp
                        (ví dụ: action_logs, license_seats)
                        → tự động join lên model cha để lấy company_id
```

### 2.7 Trait CompanyableTrait

```php
trait CompanyableTrait
{
    public static function bootCompanyableTrait()
    {
        static::addGlobalScope(new CompanyableScope);
    }

    // Interface ICompanyableChild:
    // getCompanyableParents() → trả về tên relation cha
}
```

**Các model sử dụng CompanyableTrait:** Asset, Consumable, Accessory, Component, License, User, Location, Supplier, Department...

### 2.8 withoutGlobalScopes() — Pattern quan trọng

Trong checkout flow, controller gọi `User::withoutGlobalScopes()->find($id)` để phân biệt:
- "User không tồn tại" (ID sai)
- "User tồn tại nhưng khác công ty" (FMCS block)

Sau khi resolve, controller tự check FMCS và trả về error message phù hợp.

---

## 3. CƠ CHẾ TRƯỜNG TÙY CHỈNH (CUSTOM FIELDS — EAV PATTERN)

### 3.1 Khái niệm

Snipe-IT không dùng bảng EAV (Entity-Attribute-Value) riêng. Thay vào đó, khi admin tạo một Custom Field mới, hệ thống **tự động ALTER TABLE assets** để thêm cột vật lý vào bảng `assets`. Điều này cho phép query và sort trực tiếp trên cột custom field mà không cần JOIN phức tạp.

### 3.2 Ba thành phần chính

```
┌──────────────────────┐     ┌─────────────────────┐     ┌──────────────────────┐
│   CustomField         │     │   CustomFieldset     │     │   AssetModel          │
│   (custom_fields)     │     │   (custom_fieldsets) │     │   (models)            │
├──────────────────────┤     ├─────────────────────┤     ├──────────────────────┤
│ id                    │     │ id                   │     │ id                   │
│ name                  │◄───►│ name                 │     │ fieldset_id ─────────┼──► custom_fieldsets.id
│ format                │ M:M │                      │◄───►│ name                 │
│ element               │pivot│                      │ 1:M │ ...                  │
│ field_values          │     │                      │     │                      │
│ field_encrypted       │     │                      │     │                      │
│ db_column             │     │                      │     │                      │
│ help_text             │     │                      │     │                      │
│ show_in_email         │     │                      │     │                      │
│ is_unique             │     │                      │     │                      │
│ display_checkin       │     │                      │     │                      │
│ display_checkout      │     │                      │     │                      │
│ display_audit         │     │                      │     │                      │
└──────────────────────┘     └─────────────────────┘     └──────────────────────┘
```

### 3.3 Luồng tạo Custom Field mới

```
1. Admin tạo Custom Field qua UI/API
   ├─ name: "Màu sắc"
   ├─ format: "ANY"
   ├─ element: "text"
   └─ field_encrypted: false

2. CustomField::boot() → event created
   ├─ name_to_db_name("Màu sắc") → "_snipeit_mau_sac"
   ├─ convertUnicodeDbSlug() → "_snipeit_mau_sac_42" (thêm ID để unique)
   └─ ALTER TABLE assets ADD COLUMN _snipeit_mau_sac_42 TEXT NULL;
                               (DATE/DATETIME nếu format là DATE/DATETIME)

3. Lưu db_column = "_snipeit_mau_sac_42" vào custom_fields

4. Admin gán CustomField vào CustomFieldset
   └─ INSERT INTO custom_field_custom_fieldset (fieldset_id, field_id, required, order)

5. Admin gán CustomFieldset vào AssetModel
   └─ UPDATE models SET fieldset_id = X WHERE id = Y
```

### 3.4 CustomField::name_to_db_name()

```php
public static function name_to_db_name($name)
{
    // Chuyển "Màu sắc" → "_snipeit_mau_sac"
    return '_snipeit_'.preg_replace('/[^a-zA-Z0-9]/', '_', strtolower($name));
}
```

### 3.5 CustomField::convertUnicodeDbSlug()

```php
public function convertUnicodeDbSlug($original = null)
{
    $name = $original ?: $this->name;
    $id = $this->id ?: 'xx';
    $long_slug = '_snipeit_'.Utf8Slugger::slugify($name, '_');
    return substr($long_slug, 0, 50).'_'.$id;
    // Ví dụ: "_snipeit_mau_sac_42" (cắt ở 50 ký tự + "_" + ID)
}
```

### 3.6 CustomFieldset::validation_rules()

CustomFieldset chịu trách nhiệm sinh ra validation rules cho tất cả custom fields trong fieldset:

```php
public function validation_rules(): array
{
    $rules = [];
    foreach ($this->fields as $field) {
        $rule = [];

        // Required/NULLABLE dựa trên pivot
        if ($field->field_encrypted != '1' || Gate::allows('admin')) {
            $rule[] = ($field->pivot->required == '1') ? 'required' : 'nullable';
        }

        // Unique check
        if ($field->is_unique == '1') {
            $rule[] = 'unique_undeleted';
        }

        // Format validation
        if ($field->attributes['format'] != '') {
            $rule[] = $field->attributes['format']; // e.g., 'email', 'numeric', 'date'
        }

        $rules[$field->db_column_name()] = $rule;

        // Encrypted fields: swap standard rules for encrypted-aware rules
        if ($field->field_encrypted) {
            // Replace 'email' → EmailEncrypted, 'numeric' → NumericEncrypted, etc.
            // Custom Rule Objects that decrypt before validating
        }
    }
    return $rules;
}
```

### 3.7 Đọc giá trị Custom Field từ Asset

```php
// AssetsTransformer::transformAsset()

if ($asset->model && $asset->model->fieldset && $asset->model->fieldset->fields->count() > 0) {
    foreach ($asset->model->fieldset->fields as $field) {
        if ($field->isFieldDecryptable($asset->{$field->db_column})) {
            $decrypted = Helper::gracefulDecrypt($field, $asset->{$field->db_column});
            $value = Gate::allows('assets.view.encrypted_custom_fields')
                ? $decrypted
                : strtoupper(trans('admin/custom_fields/general.encrypted'));
        } else {
            $value = $asset->{$field->db_column};
        }

        // Format DATE/DATETIME values
        if (in_array($field->format, ['DATE', 'DATETIME']) && !is_null($value)) {
            $value = Helper::getFormattedDateObject($value, ...);
        }

        $fields_array[$field->name] = [
            'field'  => e($field->db_column),
            'value'  => $value,
            'field_format' => $field->format,
            'element' => $field->element,
        ];
    }
    $array['custom_fields'] = $fields_array;
}
```

**Logic:** Asset model có `$field->db_column` là tên cột thực trên bảng `assets`. Access qua `$asset->{$field->db_column}` là Eloquent dynamic property — đọc trực tiếp từ cột đã được ALTER TABLE.

### 3.8 Định dạng (Format) & Element Type

| Format | Laravel Rule | Element Types | Encryptable? |
|---|---|---|---|
| `ANY` | (none) | Tất cả | ✅ |
| `NUMERIC` | `numeric` | text | ✅ |
| `EMAIL` | `email` | text | ✅ |
| `DATE` | `date` | date_picker (only) | ❌ (native column) |
| `DATETIME` | `date_format:Y-m-d H:i:s` | datetime_picker (only) | ❌ (native column) |
| `URL` | `url` | text | ✅ |
| `IP` / `IPV4` / `IPV6` | `ip` / `ipv4` / `ipv6` | text | ✅ |
| `MAC` | `regex:...` | text | ✅ |
| `PHONE` / `FAX` | `string` / `present` | text | ✅ |
| `BOOLEAN` | `boolean` | text, checkbox | ✅ |
| `CUSTOM REGEX` | user-provided regex | text (only) | ✅ |
| `ALPHA` / `ALPHA-DASH` / `ALPHA-NUMERIC` | `alpha` / `alpha_dash` / `alpha_num` | text | ✅ |

**Lưu ý quan trọng:**
- **DATE/DATETIME** tạo native column → **không thể đổi format sau khi tạo** (hard block)
- **Encrypted fields** dùng Rule Objects riêng (`EmailEncrypted`, `NumericEncrypted`...) để decrypt trước khi validate
- **listbox, checkbox, radio** yêu cầu `field_values` (danh sách options, phân cách bởi newline)

### 3.9 Lifecycle Events của CustomField

```
created  → ALTER TABLE assets ADD COLUMN → save db_column
updating → Nếu name thay đổi → ALTER TABLE assets RENAME COLUMN → save db_column mới
         → Nếu format thay đổi từ/tới DATE/DATETIME → HARD BLOCK (không cho phép)
deleting → ALTER TABLE assets DROP COLUMN
```

### 3.10 Đặc điểm: KHÔNG phải EAV thuần túy

| EAV Pattern truyền thống | Snipe-IT Approach |
|---|---|
| Bảng riêng: `entity_id`, `attribute_id`, `value` | **ALTER TABLE assets** mỗi khi thêm field |
| Query cần JOIN/pivot phức tạp | Query trực tiếp trên cột assets |
| Performance kém với nhiều fields | Performance tốt (native SQL columns) |
| Không cần migration | Cần quyền ALTER TABLE trên DB |

---

## 4. HỆ THỐNG SỰ KIỆN (EVENTS & LISTENERS)

### 4.1 Danh sách Events

| Event | Fire khi | Dữ liệu |
|---|---|---|
| `CheckoutableCheckedOut` | Asset, License, Consumable, Accessory, Component được checkout | `checkoutable`, `checkedOutTo`, `checkedOutBy`, `note`, `originalValues`, `quantity`, `signInPlace` |
| `CheckoutableCheckedIn` | Asset, License, Accessory, Component được checkin | `checkoutable`, `checkedOutTo`, `checkedInBy`, `note`, `action_date`, `originalValues` |
| `CheckoutablesCheckedOutInBulk` | Bulk checkout nhiều assets | Collection of checkout data |
| `CheckoutAccepted` | User chấp nhận EULA | `acceptance` (CheckoutAcceptance model) |
| `CheckoutDeclined` | User từ chối EULA | `acceptance` (CheckoutAcceptance model) |
| `UserMerged` | Merge 2 user accounts | `merged_from`, `merged_to` |

### 4.2 Đăng ký Listeners (EventServiceProvider)

```php
// app/Providers/EventServiceProvider.php

protected $listen = [
    Login::class  => [LogSuccessfulLogin::class],
    Failed::class => [LogFailedLogin::class],
];

protected $subscribe = [
    LogListener::class,                      // Xử lý tất cả Checkoutable* events + UserMerged
    CheckoutableListener::class,             // Gửi email + webhook
    CheckoutablesCheckedOutInBulkListener::class,  // Bulk checkout notification
];
```

### 4.3 LogListener — Ghi Actionlog

```php
class LogListener
{
    public function subscribe($events)
    {
        $events->listen(CheckoutableCheckedIn::class,  'onCheckoutableCheckedIn');
        $events->listen(CheckoutableCheckedOut::class, 'onCheckoutableCheckedOut');
        $events->listen(CheckoutAccepted::class,       'onCheckoutAccepted');
        $events->listen(CheckoutDeclined::class,       'onCheckoutDeclined');
        $events->listen(UserMerged::class,             'onUserMerged');
    }

    public function onCheckoutableCheckedOut($event)
    {
        $event->checkoutable->logCheckout(
            $event->note,
            $event->checkedOutTo,
            $event->checkoutable->last_checkout,
            $event->originalValues,
            $event->quantity
        );
    }

    public function onCheckoutableCheckedIn($event)
    {
        $event->checkoutable->logCheckin(
            $event->checkedOutTo,
            $event->note,
            $event->action_date,
            $event->originalValues
        );
    }
}
```

**Kết quả:** Mỗi lần checkout/checkin → một dòng mới trong `action_logs`.

### 4.4 CheckoutableListener — Gửi Email & Webhook

Đây là listener phức tạp nhất (~567 dòng), xử lý toàn bộ notification flow:

```
onCheckedOut($event)
│
├─ 1. shouldNotSendAnyNotifications? → return (skip list)
│
├─ 2. getCheckoutAcceptance() → tạo CheckoutAcceptance nếu category yêu cầu
│
├─ 3. Quyết định gửi gì:
│   ├─ shouldSendCheckoutEmailToUser?
│   │   └─ requireAcceptance? OR getEula()? OR checkin_email? → TRUE
│   ├─ shouldSendEmailToAlertAddress?
│   │   └─ admin_cc_email exists? → TRUE
│   ├─ shouldSkipInitialAcceptanceEmail?
│   │   └─ signInPlace? AND (acceptance OR eula) → skip email (user đã ký tại chỗ)
│   └─ shouldSendWebhookNotification?
│       └─ webhook_endpoint exists? → TRUE
│
├─ 4. Gửi EMAIL (nếu cần):
│   ├─ getCheckoutMailType() → Chọn Mailable class theo loại checkoutable:
│   │   ┌─────────────────────┬────────────────────────────┐
│   │   │ Asset               │ CheckoutAssetMail           │
│   │   │ Accessory           │ CheckoutAccessoryMail       │
│   │   │ Consumable          │ CheckoutConsumableMail      │
│   │   │ Component           │ CheckoutComponentMail       │
│   │   │ LicenseSeat         │ CheckoutLicenseMail         │
│   │   └─────────────────────┴────────────────────────────┘
│   ├─ getNotifiableUser() → Resolve người nhận:
│   │   ├─ checkedOutTo là Asset → lấy asset đó → assignedTo
│   │   ├─ checkedOutTo là Location → lấy location.manager
│   │   └─ checkedOutTo là User → chính user đó
│   ├─ generateEmailRecipients() → Tính TO/CC:
│   │   ├─ sendToUser && sendToAdmin → TO: user, CC: admin
│   │   ├─ sendToUser && !sendToAdmin → TO: user
│   │   └─ !sendToUser && sendToAdmin → TO: admin
│   └─ Mail::to($to)->send($mailable)
│
└─ 5. Gửi WEBHOOK (nếu cần):
    ├─ getCheckoutNotification() → Chọn Notification class theo loại:
    │   ├─ CheckoutAssetNotification
    │   ├─ CheckoutAccessoryNotification
    │   ├─ CheckoutConsumableNotification
    │   ├─ CheckoutComponentNotification
    │   └─ CheckoutLicenseSeatNotification
    ├─ Nếu Microsoft Teams (workflows endpoint):
    │   └─ TeamsNotification::success()->sendMessage()
    └─ Nếu Slack/Generic webhook:
        └─ Notification::route('slack', $endpoint)->notify($notification)
```

**Các Mail/Notification classes đều có variant cho từng loại entity**, đảm bảo email/webhook chứa đúng context (asset tag, serial, model name, etc.).

### 4.5 Luồng sự kiện khi Checkout Asset

```
Controller::checkout()
│
├─ $asset->checkOut($target, ...)
│   ├─ $this->assignedTo()->associate($target)
│   ├─ $this->save()
│   └─ event(new CheckoutableCheckedOut($asset, $target, $admin, $note, $originalValues))
│
├─ [LARAVEL EVENT DISPATCHER]
│
├─ LogListener::onCheckoutableCheckedOut()
│   └─ $asset->logCheckout(...) → INSERT action_logs (audit trail)
│
├─ CheckoutableListener::onCheckedOut()
│   ├─ CreateCheckoutAcceptanceAction::run() (nếu category yêu cầu acceptance)
│   ├─ Mail::to(user)->send(CheckoutAssetMail) (nếu category cho phép email)
│   └─ Notification::route('slack', ...)->notify(CheckoutAssetNotification) (nếu có webhook)
│
└─ Return HTTP 200 {"status": "success", ...}
```

---

## 5. TỔNG KẾT

| Đặc điểm | Mô tả |
|---|---|
| **Permission Storage** | JSON blob trong `users.permissions` + `permission_groups.permissions` |
| **Permission Resolution** | Superuser → Admin → User Explicit Grant (1) → User Explicit Deny (-1) → Group Grant → Default Deny |
| **Policy Hierarchy** | `SnipePermissionsPolicy` → `CheckoutablePermissionsPolicy` → concrete policies (DRY) |
| **Gate::before()** | Superuser bypass TẤT CẢ abilities (ngoại trừ demo mode) |
| **FMCS Auto-Scoping** | `CompanyableScope` tự động thêm `WHERE company_id IN (...)` vào mọi query |
| **FMCS User Resolver** | `Company::getCurrentUserCompanyIds()` — cache per-request, auto-expand parent→children |
| **Floater Mode** | Items không có company_id → system-wide visible (khi `null_company_is_floater = true`) |
| **Custom Fields** | ALTER TABLE approach — tạo cột thực trên `assets`, không phải EAV bảng riêng |
| **Custom Field Lifecycle** | created → ALTER ADD; updating name → ALTER RENAME; deleting → ALTER DROP; format DATE↔TEXT → HARD BLOCK |
| **Encrypted Fields** | Custom Rule Objects (EmailEncrypted, NumericEncrypted...) + Gate check để view |
| **Event System** | 6 events, 3 subscribers (LogListener, CheckoutableListener, BulkListener) |
| **Notification Dispatch** | Polymorphic — mỗi loại entity có Mail/Notification class riêng |
| **Audit Trail** | LogListener ghi mọi checkout/checkin/accept/decline vào `action_logs` |

---

> **Kết thúc Giai đoạn 3.** Hoàn tất bộ 3 tài liệu phân tích kiến trúc Snipe-IT.