# ERROR_CODES.md — Bảng tra cứu mã lỗi HTTP API

> **Nguồn sự thật duy nhất** của tài liệu này là **code**: mọi error_code được quét trực tiếp từ
> `aspire-react.Application/` (handlers) + `aspire-react.Server/Web/Controllers/` (controller-level
> guards) + `aspire-react.Infrastructure/Authentication/` (auth sống hiện tại: `TokenService`,
> `PasswordChangeGateMiddleware`, `WebAuthn/Fido2Service`, `AuthAttemptService`) +
> `aspire-react.Infrastructure/Services/` (allocation services). Lần quét gần nhất: **2026-10-02**,
> sau AUTH Phase 5 (Keycloak đã bị xóa hoàn toàn — auth local JWT tự ký + passkey tùy chọn).
>
> Cập nhật tài liệu này khi: thêm endpoint mới có error_code, thêm/thay error_code trong handler,
> hoặc đổi shape lỗi của một controller. Code vẫn là nguồn chuẩn nếu tài liệu lệch.

---

## 1. Quy ước chung

### 1.1 Hai style key: `error_code` (snake_case) vs `errorCode` (camelCase)

Body lỗi chuẩn của dự án:

```json
{ "status": "error", "message": "<thông điệp tiếng Việt/Anh>", "error_code": "SOME_CODE" }
```

**Hai controller dùng `errorCode` (camelCase) — KHÁC BIỆT verbatim từ trước migration, KHÔNG
thống nhất nếu chưa sửa đồng bộ frontend:**

| Controller | Endpoints | Ghi chú |
|---|---|---|
| `GroupsController` | Create/Update/Delete/UpdateGroupPermissions | `SELF_LOCKOUT`, `SYSTEM_GROUP_LOCKED` |
| `UsersController` | CreateUser/UpdateUser/DeleteUser/UpdateUserGroups | `SELF_LOCKOUT` (trừ UpdateUserGroups dùng `error_code` — xem 1.7) |

**Mọi controller khác dùng `error_code` (snake_case).** Thêm controller mới → mặc định `error_code`.

### 1.2 404 hide-existence vs 403 Forbid — khi nào dùng cái nào

| Tình huống | Trả về | Lý do |
|---|---|---|
| Đọc/sửa/xóa một **resource cụ thể** ngoài company scope | **404** "Not found." (không error_code) | Hide existence — không lộ sự tồn tại của resource công ty khác (Task K/S1) |
| **Create** với CompanyId ngoài scope | **400** `COMPANY_MISMATCH` | Đang tạo mới, chưa có gì để ẩn (Task L2) |
| Company **không tồn tại** làm target import | **403** Forbid() | Authorization violation — Task IMPORT-T5 (người dùng "không được" chứ không phải "không thấy") |
| Hành vi superuser-only (Reopen/Delete maintenance, Groups class) | **403** Forbid() | Thiếu quyền — policy authorization |
| Model/DTL validation lỗi | **400** + error_code | Đầu vào sai |

⚠️ **Trap (a) của AssetMaintenances**: trong cùng controller, reads + Close/Inspect out-of-scope
→ **403**, nhưng Update out-of-scope → **404** — verbatim từ trước migration, KHÔNG "sửa cho đồng
bộ". Xem chi tiết tại section AssetMaintenances.

### 1.3 Company-scoping mismatch pattern (`COMPANY_MISMATCH` 400)

Pattern chuẩn (Task L2) — check ĐẦU TIÊN trong handler, trước mọi mutation:

```csharp
var userCompanyId = await _companyScope.GetCurrentUserCompanyIdAsync();
if (userCompanyId.HasValue && request.CompanyId.HasValue && request.CompanyId.Value != userCompanyId.Value)
    return new XResult(false, "Bạn chỉ được tạo ... cho công ty của mình.", "COMPANY_MISMATCH"); // → 400
```

- **Regular user** (scope = company): chỉ tạo cho company của mình hoặc **floater** (`CompanyId = null`).
- **Superuser** (scope = null): bỏ qua, tạo cho company bất kỳ.
- Blocked request **không tạo row và không tạo ActionLog** (BuildLogEntry trả null khi `!Success`).

`COMPANY_MISMATCH` (400) nay áp dụng cho **cả `PUT /users/{id}`** — không chỉ Create: scope của
`CompanyId` **MỚI** trong request được kiểm tra trước mọi mutation (`UpdateUserCommand`; `Guid.Empty`
= sentinel "clear → floater" luôn được phép), kèm `DepartmentId`/`LocationId` tham chiếu. Tham chiếu
**không tồn tại** (DepartmentId/LocationId) trả `RESOURCE_NOT_FOUND` (400) — cùng file
`Application/Users/Commands/UserReferenceScope.cs` dùng chung cho Create + Update.

Áp dụng tại: Departments, Locations (BUG-G), Consumables, Accessories, Components (kèm
`COMPANY_REQUIRED`/`INVALID_COMPANY`), SystemInfos, Assets (Create), Users (Create + Update — trong
handler, không còn controller-guard),
MaintenanceTemplates (kèm `INVALID_COMPANY`), MaintenanceCampaigns executors
(`EXECUTOR_COMPANY_MISMATCH` — so company của executor với company của **hệ thống**, không phải
của user), Licenses seats (`LICENSE_COMPANY_MISMATCH` — so với company của **license**),
AssetMaintenances assignees (`ASSIGNEE_COMPANY_MISMATCH` — so với company của **bản ghi**),
ComponentAllocation (`COMPONENT_COMPANY_REQUIRED`, `COMPANY_MISMATCH`), ConsumableAllocation
(`CONSUMABLE_COMPANY_MISMATCH`).

### 1.4 Dup-check pattern (case-sensitive vs case-insensitive)

| Loại check | Case | Dùng tại |
|---|---|---|
| Tên hiển thị (Name) | **case-sensitive** (`x.Name == name`) | Departments, Categories (Name+CategoryType), Manufacturers (Name + Code), Suppliers (Name + Code), AssetModels (BUG-H), Companies (Code), MaintenanceTemplates (Name+SystemInfoId), Assets (AssetTag — `DUPLICATE_ASSET_TAG`) |
| **Slug / định danh kỹ thuật** | **case-insensitive** (`.Name.ToLower() == ...`) | **Groups (PermissionGroup)** — BUG-K: group name là role-like identifier, "Admin"/"admin" = trùng |
| Tên hiển thị Update | Chỉ check **khi name thay đổi thật** (re-send current name = no-op), exclude self (`x.Id != id`) | Mọi Update có dup-check (BUG-E/F/H/I/K pattern) |

Dup-check **không hồi tố**: chỉ áp dụng cho request mới; dữ liệu trùng tạo trước fix vẫn tồn tại
(audit dữ liệu thật đã làm cho BUG-F/K/I trước khi fix).

### 1.5 409 Conflict — trường hợp duy nhất

| Code | Endpoint | Ý nghĩa |
|---|---|---|
| `RESULT_CONCURRENT_WRITE` | `POST /maintenance/campaigns/{id}/results` | BUG-D retry-merge cạn 3 lần retry do race INSERT-vs-INSERT trên unique key (DeviceSnapshot × Item × Param). Không phải 500 — client được yêu cầu thử lại. |

Đây là **case 409 duy nhất** của API.

### 1.6 Response lỗi KHÔNG có error_code (chỉ `status` + `message`)

- **400 400-file-guard ImportExport**: "No file provided." / "Chỉ hỗ trợ file .xlsx." (file thiếu
  hoặc sai định dạng — verbatim, không error_code).
- **400 ImportExport `COMPANY_REQUIRED`**: có error_code (đây là case duy nhất có, khác 2 case trên).
- **400 soft-fail các handler không gắn ErrorCode**: Departments empty/dup-name ("Tên phòng ban
  không được để trống." / "Tên phòng ban đã tồn tại."), Categories dup ("Tên danh mục đã tồn tại."),
  CustomFields empty/dup ("Field name is required." / "Field slug is required." / "A field with
  this slug already exists." — style tiếng Anh của section), Groups (BUG-K), License Create
  (NAME_REQUIRED/CATEGORY_* — có error_code, xem bảng).
- **404** mọi controller: không có error_code (chỉ message) — trừ VERSION_NOT_FOUND/ITEM_NOT_FOUND/
  PARAM_NOT_FOUND của MaintenanceTemplates vẫn 404 + message riêng (không error_code).
- **403** Forbid() mọi controller: body rỗng.
- **401**: từ authentication layer (không phải controller), body rỗng/ProblemDetails.
- **401 auth controller (`/auth/*`)**: `{status,message}` **KHÔNG có error_code** — xem section Auth
  (`USER_NOT_FOUND` ở `/auth/password` + passkeys, refresh cookie hết hạn/hỏng). Đây là **khác biệt
  có chủ ý** với `USER_NOT_FOUND` 404 của `PUT`/`DELETE /users/{id}`.

Quy ước: **error_code gắn với lỗi có thể phân loại máy được** (FE switch-case); lỗi "con người
đọc message là đủ" (empty-name, dup-name hiển thị trực tiếp) nhiều chỗ không gắn — verbatim.

### 1.7 Ngoại lệ nhỏ: UsersController.UpdateUserGroups

Endpoint duy nhất của UsersController dùng **`error_code` snake** (không phải `errorCode`) —
`GROUP_NOT_FOUND` / `SELF_LOCKOUT` (verbatim M1-era quirk). Xem bảng section Users.

---

## 2. Tra cứu theo Controller

### Auth (`/api/v1/auth`) — **error_code snake_case** (auth local, không còn Keycloak)

Nguồn: `Web/Controllers/AuthController.cs` + `Application/Auth/Commands/` (`AuthCommands.cs`,
`PasswordCommands.cs`, `WebAuthnCommands.cs`) + `Infrastructure/Authentication/`
(`PasswordChangeGateMiddleware.cs`, `WebAuthn/Fido2Service.cs`).

| Code | HTTP | Endpoint | Điều kiện |
|---|---|---|---|
| `INVALID_CREDENTIALS` | 400 | POST /auth/login | Sai username **hoặc** sai password **hoặc** user inactive/chưa có `PasswordHash` — message **generic** ("Sai tên đăng nhập hoặc mật khẩu."), không lộ user enumeration |
| `INVALID_CREDENTIALS` | 400 | POST /auth/password | `CurrentPassword` không khớp (message "Mật khẩu hiện tại không đúng.") |
| `INVALID_CREDENTIALS` | **401** | POST /auth/passkeys/login | Assertion fail: challenge hết hạn (`PASSKEY_ASSERTION_EXPIRED`), JSON hỏng (`PASSKEY_MALFORMED_RESPONSE`) hoặc credential/user không hợp lệ — handler **gộp tất cả** thành `INVALID_CREDENTIALS` (401, chống enumeration) |
| `ACCOUNT_LOCKED` | 400 | POST /auth/login | (a) IP bị chặn burst, hoặc (b) per-username lockout leo thang còn hiệu lực (fail lần 5 → 60s, lần 6+ → 2^n, cap 15 phút; dựa trên `AuthAttemptService`). Message "Tài khoản tạm thời bị khóa do đăng nhập sai nhiều lần..." |
| `MUST_CHANGE_PASSWORD` | **403** | **MỌI endpoint** (trừ `/users/me` + `/auth/password`) | Session mang claim `pwd_change=1` — `PasswordChangeGateMiddleware` chặn trước authorization. Message "Bạn phải đổi mật khẩu trước khi sử dụng hệ thống." |
| `PASSKEYS_DISABLED` | **403** | POST /auth/passkeys/login/options, POST /auth/passkeys/login | Feature flag WebAuthn đang tắt (`IWebAuthnService.IsEnabledAsync()` false) — rejected TRƯỚC khi xử lý assertion |
| `PASSKEY_REGISTRATION_EXPIRED` | 400 | POST /auth/passkeys/register | Không tìm thấy `CredentialCreateOptions` trong cache cho user (hết TTL / chưa gọi `/register/options`) |
| `PASSKEY_ALREADY_REGISTERED` | 400 | POST /auth/passkeys/register | `credentialId` đã đăng ký. ⚠️ Handler + `AuthController.PasskeyRegisterErrorText` **có** map mã này, nhưng `Fido2Service` hiện **không ném** nó: uniqueness callback fail → `Fido2VerificationException` → trả `PASSKEY_VERIFICATION_FAILED` |
| `PASSKEY_MALFORMED_RESPONSE` | 400 | POST /auth/passkeys/register | `AttestationJson` không deserialize được thành `AuthenticatorAttestationRawResponse` |
| `PASSKEY_VERIFICATION_FAILED` | 400 | POST /auth/passkeys/register | Attestation/signature/format verification fail (`Fido2VerificationException`) — không leak chi tiết |
| `PASSKEY_ASSERTION_EXPIRED` | — | POST /auth/passkeys/login | Ném bởi `Fido2Service` khi challenge assertion hết hạn/không có trong cache, **nhưng bị handler gộp** thành `INVALID_CREDENTIALS` (401) — client không bao giờ thấy mã này |
| `USER_NOT_FOUND` | **401** | POST /auth/password, POST /auth/passkeys/register/options | User từ claim `local_user_id` không tồn tại/inactive → body `{status,message}` **KHÔNG có error_code**. ⚠️ **Khác** `USER_NOT_FOUND` 404 của Users PUT/DELETE |
| (không có error_code) | 401 | POST /auth/refresh, POST /auth/passkeys/login | Refresh cookie thiếu/hết hạn/tái sử dụng (reuse-detection revoke mọi session) → `{status,message}` không error_code |
| (không có error_code) | 404 | DELETE /auth/passkeys/{id} | Passkey không tồn tại hoặc thuộc user khác (hide-existence; handler trả `RESOURCE_NOT_FOUND` nhưng controller **không** gắn error_code) |
| (FluentValidation — **không phải** error_code) | 400 | POST /auth/password | `newPassword` < 8 ký tự → body `{status,message:"Validation failed.",errors}` từ `ValidationExceptionHandler` |

**Không có error_code** (chỉ `status`+`message`): POST /auth/logout luôn 200 `{status:"success"}`
(idempotent); POST /auth/login `ACCOUNT_LOCKED`/`INVALID_CREDENTIALS` **có** error_code, nhưng POST
/auth/refresh + POST /auth/passkeys/login 401 thì không.

### Assets (`/api/v1/assets`)

| Code | HTTP | Endpoint | Điều kiện |
|---|---|---|---|
| `COMPANY_MISMATCH` | 400 | POST /assets (Create) | Regular user tạo asset cho company khác |
| `NOT_FOUND` | 404 | PUT /assets/{id}, DELETE, POST {id}/confirm, POST {id}/archive, POST {id}/unarchive | Asset không tồn tại **hoặc** ngoài scope (hide-existence) |
| `ASSET_ARCHIVED` | 400 | PUT /assets/{id}, POST {id}/checkout | Asset đã lưu trữ |
| `DUPLICATE_ASSET_TAG` | 400 | PUT /assets/{id} | AssetTag trùng (Create: tag trùng bị FluentValidation chặn — message "Mã tài sản đã tồn tại trong hệ thống.", không error_code) |
| `CONFIRMED_ASSET_LOCKED` | 400 | PUT /assets/{id} | Sửa field bị khóa sau khi confirm (chỉ Name/Notes sửa được) |
| `ASSET_CONFIRMED_CANNOT_DELETE` | 400 | DELETE /assets/{id} | Xóa asset đã confirm |
| `ASSET_CHECKED_OUT` | 400 | DELETE /assets/{id} | Asset đang cấp phát |
| `ASSET_HAS_ASSIGNMENTS` | 400 | DELETE /assets/{id} | Asset có lịch sử cấp phát (delete-guard) |
| `ASSET_HAS_MAINTENANCES` | 400 | DELETE /assets/{id} | Asset có phiếu bảo trì (delete-guard) |
| `ASSET_USED_BY_COMPONENT` | 400 | DELETE /assets/{id} | Asset đang được linh kiện dùng (delete-guard) |
| `ALREADY_CONFIRMED` | 400 | POST {id}/confirm | Confirm asset đã confirm |
| `ALREADY_ARCHIVED` | 400 | POST {id}/archive | Archive asset đã archive |
| `NOT_ARCHIVED` | 400 | POST {id}/unarchive | Unarchive asset chưa archive |
| `ASSET_NOT_FOUND` | 400 | POST {id}/checkout, /checkin, /audit | Asset không tồn tại (trong tx handler) |
| `ASSET_ALREADY_CHECKED_OUT` | 400 | POST {id}/checkout | Asset đang được giữ |
| `ASSET_NOT_DEPLOYABLE` | 400 | POST {id}/checkout | Status ≠ Pending |
| `ASSET_NOT_CHECKED_OUT` | 400 | POST {id}/checkin | Asset không đang được cấp |
| `ASSET_NOT_DEPLOYED` | 400 | POST {id}/checkin | Status ≠ Deployed |
| `LOCATION_REQUIRED` | 400 | POST {id}/checkout | Checkout tới SystemPosition thiếu LocationId |
| `COMPANY_MISMATCH` (checkout) | 400 | POST {id}/checkout | Target không cùng company với asset |

### Categories (`/api/v1/categories`)

| Code | HTTP | Endpoint | Điều kiện |
|---|---|---|---|
| `NOT_FOUND` | 404 | PUT, DELETE /{id} | Không tồn tại (global — không company-scope) |
| `CATEGORY_IN_USE` | 400 | DELETE /{id} | Đang được Model/Asset tham chiếu |

(Create/Update dup: "Tên danh mục đã tồn tại." 400 **không error_code** — BUG-F.)

### Locations (`/api/v1/locations`)

| Code | HTTP | Endpoint | Điều kiện |
|---|---|---|---|
| `NOT_FOUND` | 404 | PUT, DELETE /{id} | Không tồn tại hoặc ngoài scope |
| `LOCATION_IN_USE` | 400 | DELETE /{id} | Đang được tham chiếu |
| `COMPANY_MISMATCH` | 400 | POST (Create) | Regular user tạo cho company khác (**BUG-G fix**) |

### Departments (`/api/v1/departments`)

| Code | HTTP | Endpoint | Điều kiện |
|---|---|---|---|
| `NOT_FOUND` | 404 | PUT, DELETE /{id} | Không tồn tại hoặc ngoài scope |
| `DEPARTMENT_IN_USE` | 400 | DELETE /{id} | Đang được tham chiếu |
| `COMPANY_MISMATCH` | 400 | POST (Create) | Regular user tạo cho company khác |

(Create/Update empty/dup name: 400 **không error_code** — BUG-E.)

### Manufacturers (`/api/v1/manufacturers`)

| Code | HTTP | Endpoint | Điều kiện |
|---|---|---|---|
| `NOT_FOUND` | 404 | PUT, DELETE /{id} | Không tồn tại |
| `MANUFACTURER_IN_USE` | 400 | DELETE /{id} | Đang được tham chiếu |

(Dup Name/Code Create+Update: 400 không error_code — "Tên NSX đã tồn tại." / "Mã NSX đã tồn tại.")

### Suppliers (`/api/v1/suppliers`)

| Code | HTTP | Endpoint | Điều kiện |
|---|---|---|---|
| `NOT_FOUND` | 404 | PUT, DELETE /{id} | Không tồn tại |
| `SUPPLIER_IN_USE` | 400 | DELETE /{id} | Đang được tham chiếu |

(Dup Name/Code: 400 không error_code — "Tên NCC đã tồn tại." / "Mã NCC đã tồn tại.")

### AssetModels (`/api/v1/models`)

| Code | HTTP | Endpoint | Điều kiện |
|---|---|---|---|
| `NOT_FOUND` | 404 | PUT, DELETE /{id} | Không tồn tại |
| `MODEL_IN_USE` | 400 | DELETE /{id} | Đang được Asset tham chiếu (DeleteModel handler) |
| `RESOURCE_NOT_FOUND` | 400 | POST, PUT | FK (Manufacturer/Category/Depreciation/Fieldset) không tồn tại — **BUG-H fix**, thay raw 500 |

(Dup/empty name: 400 không error_code — "Tên model đã tồn tại." / "Tên model không được để trống." — BUG-H.)

### Companies (`/api/v1/companies`)

| Code | HTTP | Endpoint | Điều kiện |
|---|---|---|---|
| `NOT_FOUND` | 404 | PUT, DELETE /{id} | Không tồn tại |
| `COMPANY_IN_USE` | 400 | DELETE /{id} | Đang có Users/Assets/Systems tham chiếu |

(Mã trùng: 400 không error_code — "Mã công ty 'X' đã tồn tại."; code "NOCO" reserved — 400.)

### CustomFields (`/api/v1/custom-fields`)

| Code | HTTP | Endpoint | Điều kiện |
|---|---|---|---|
| `NOT_FOUND` | 404 | PUT, DELETE /{id} | Không tồn tại |
| `CUSTOM_FIELD_IN_USE` | 400 | DELETE /{id} | Đang được Fieldset/Asset tham chiếu |

(Empty name/slug + dup slug: 400 không error_code — BUG-I.)

### Groups (`/api/v1/groups`) — **errorCode camelCase**

| Code | HTTP | Endpoint | Điều kiện |
|---|---|---|---|
| `NOT_FOUND` | 404 | PUT, DELETE /{id}, PUT permissions | Group không tồn tại |
| `SYSTEM_GROUP_LOCKED` | 400 | PUT /{id}, DELETE /{id} | Group hệ thống không rename/xóa được |
| `SELF_LOCKOUT` | 400 | DELETE /{id}, PUT permissions | Tự khóa quyền quản trị của chính mình (PermissionLockoutGuard) |
| (BUG-K empty/dup name) | 400 | POST, PUT /{id} | "Group name is required." / "A group with this name already exists." — không errorCode |

### Users (`/api/v1/users`) — **errorCode camelCase** (trừ UpdateUserGroups + scope/reference: `error_code` snake)

> [AUTH Phase 5] CreateUser nay LOCAL-ONLY: password do admin cấp + `MustChangePassword=true`.

| Code | HTTP | Endpoint | Điều kiện |
|---|---|---|---|
| `USER_NOT_FOUND` | 404 | PUT /{id}, DELETE /{id} | User không tồn tại hoặc ngoài scope (body **không** errorCode) — **khác** `USER_NOT_FOUND` 401 của `/auth/*` |
| `SELF_LOCKOUT` | 400 | PUT /{id}, DELETE /{id} | Hạ/vô hiệu hóa superuser/quản trị cuối cùng (`errorCode` camelCase) |
| `SELF_LOCKOUT` | 400 | PUT {id}/groups | Cùng guard, nhưng `error_code` snake (quirk §1.7) |
| `GROUP_NOT_FOUND` | 400 | PUT {id}/groups | Group không tồn tại (`error_code` snake — quirk) |
| `COMPANY_MISMATCH` | 400 | POST, **PUT /{id}** | Create: regular user tạo user cho company khác. Update: scope của `CompanyId` **MỚI** (sentinel `Guid.Empty` = clear→floater được phép) — body `error_code` snake |
| `RESOURCE_NOT_FOUND` | 400 | POST, **PUT /{id}** | `DepartmentId`/`LocationId` tham chiếu **không tồn tại** (`UserReferenceScope`, BUG-H precedent) — body `error_code` snake |
| `VALIDATION_ERROR` | 400 | POST | **Chỉ còn** password ban đầu < 8 ký tự (message tiếng Việt "Mật khẩu ban đầu phải có ít nhất 8 ký tự.") — body **không** error_code |
| (FluentValidation) | 400 | POST, PUT /{id} | Create: trùng **username/email** ("Username already exists." / "Email already exists.") + mọi rule format → `{status,message:"Validation failed.",errors:{...}}` từ `ValidationExceptionHandler` (chạy qua MediatR `ValidationBehavior`). Update: `email` trùng user khác → "Email already in use by another user." (username không đổi được nên không có rule trùng username) |

### ImportExport (`/api/v1/import/*`, `/export/*`)

| Code | HTTP | Endpoint | Điều kiện |
|---|---|---|---|
| `COMPANY_REQUIRED` | 400 | POST import/{reference,asset-models,assets,components,accessories,consumables,systems} | Thiếu companyId |
| (Forbid) | 403 | Tất cả import (trừ system-positions) | Company target ngoài scope/không tồn tại |
| (No error_code) | 400 | Tất cả import | "No file provided." / "Chỉ hỗ trợ file .xlsx." |

(system-positions: KHÔNG chọn company — B0.4 inheritance; file-guard trước, không có company-guard.
Import response thành công là FLAT: `status/created/failed/rows/errors` — không wrapper `data`.)

### MaintenanceTemplates (`/api/v1/maintenance/templates`)

| Code | HTTP | Endpoint | Điều kiện |
|---|---|---|---|
| `NOT_FOUND` | 404 | Mọi endpoint con | Template không tồn tại/ngoài scope |
| `VERSION_NOT_FOUND` | 404 | version/item/param endpoints | Version không thuộc template |
| `ITEM_NOT_FOUND` / `PARAM_NOT_FOUND` | 404 | item/param endpoints | Item/Param không thuộc version |
| `NAME_REQUIRED` | 400 | POST | Thiếu tên template |
| `SYSTEM_INFO_REQUIRED` | 400 | POST | Thiếu SystemInfoId |
| `INVALID_COMPANY` | 400 | POST | CompanyId không tồn tại |
| `COMPANY_MISMATCH` | 400 | POST | Regular user tạo cho company khác |
| `TEMPLATE_NAME_TAKEN` | 400 | POST, PUT | Trùng (SystemInfoId, Name) |
| `FIELD_LOCKED` | 400 | PUT /{id} | Đổi CompanyId / đổi SystemInfoId khi đã có campaign pin |
| `TEMPLATE_IN_USE` | 400 | DELETE /{id} | Có campaign pin bất kỳ version |
| `VERSION_ALREADY_PUBLISHED` | 400 | publish (re-), DELETE version | Version đã publish |
| `TEMPLATE_VERSION_IN_USE` | 400 | PUT/DELETE version, item/param ops | Campaign pin version (immutable) |
| `ITEM_NAME_REQUIRED` / `INVALID_CYCLE_MONTHS` / `INVALID_ORDER` / `ITEM_ORDER_TAKEN` | 400 | items CRUD | Validation item |
| `PARAM_REQUIRED` / `THRESHOLD_OPERATOR_REQUIRED` / `THRESHOLD_VALUE_REQUIRED` | 400 | params CRUD | [MC-10] ngưỡng bắt buộc |
| `INVALID_POSITION` | 400 | items CRUD | Position không thuộc hệ thống của template |

### MaintenanceCampaigns (`/api/v1/maintenance/campaigns`)

| Code | HTTP | Endpoint | Điều kiện |
|---|---|---|---|
| `SYSTEM_INFO_REQUIRED` | 400 | POST | Thiếu SystemInfoId |
| `SYSTEM_NOT_FOUND` | 404 | POST | System không tồn tại/ngoài scope |
| `END_BEFORE_START` | 400 | POST | EndDate < StartDate |
| `TEMPLATE_NOT_FOUND` (NOT_FOUND) | 404 | POST | TemplateId chỉ định không tồn tại |
| `TEMPLATE_SYSTEM_MISMATCH` | 400 | POST | Template không thuộc hệ thống |
| `NO_TEMPLATE` | 400 | POST | Hệ thống chưa có template |
| `AMBIGUOUS_TEMPLATE` | 400 | POST | Nhiều template, thiếu templateId |
| `NO_CURRENT_VERSION` | 400 | POST | Template chưa publish version hiện hành |
| `INVALID_REVIEWER` | 400 | POST | Reviewer không tồn tại |
| `INVALID_EXECUTOR` | 400 | POST | Executor không tồn tại |
| `EXECUTOR_COMPANY_MISMATCH` | 400 | POST | Executor khác company với hệ thống |
| `CAMPAIGN_ALREADY_IN_PROGRESS` | 400 | POST | Đã có campaign InProgress trên hệ thống (BUG-A FOR UPDATE race-safe) |
| `NOT_FOUND` | 404 | GET/{id}, results, complete | Campaign không tồn tại/ngoài scope |
| `CAMPAIGN_COMPLETED` | 400 | results upsert/delete | Campaign đã hoàn thành |
| `RESULT_TARGET_REQUIRED` | 400 | POST results | Thiếu snapshot/item id |
| `INVALID_DEVICE_SNAPSHOT` | 400 | POST results | Snapshot không thuộc campaign |
| `INVALID_CHECKLIST_ITEM` | 400 | POST results | Item không thuộc version đã pin |
| `INVALID_ITEM_POSITION` | 400 | POST results | Cặp (item, snapshot) ngoài phạm vi [MC-7c] |
| `STANDARD_PARAM_NOT_APPLICABLE` / `STANDARD_PARAM_REQUIRED` | 400 | POST results | [MC-9] param rules |
| `INVALID_STANDARD_PARAM` | 400 | POST results | Param không thuộc item |
| `RESULT_CONCURRENT_WRITE` | **409** | POST results | BUG-D retry cạn — thử lại |
| `RESULT_NOT_FOUND` | 404 | DELETE results | Result không tồn tại |
| `CAMPAIGN_ALREADY_COMPLETED` | 400 | POST complete | Đã hoàn thành |
| `CAMPAIGN_RESULTS_INCOMPLETE` | 400 | POST complete | Thiếu kết quả (message chứa "x/y bản ghi") |

### AssetMaintenances (`/api/v1/maintenances`) — ⚠️ trap (a): reads/Close/Inspect 403, Update 404

| Code | HTTP | Endpoint | Điều kiện |
|---|---|---|---|
| `NOT_FOUND` | 404 | PUT /{id}, DELETE /{id} | Không tồn tại/ngoài scope (Update dùng 404!) |
| `FORBIDDEN` (Forbid 403) | 403 | reads, POST close/inspect/reopen, DELETE | Ngoài scope (403!) / Reopen+Delete: superuser-only |
| `MAINTENANCE_CLOSED` | 400 | PUT, inspect | Bản ghi đã đóng (immutable) |
| `MAINTENANCE_ALREADY_CLOSED` | 400 | POST close | Đóng lần 2 |
| `MAINTENANCE_NOT_COMPLETED_YET` | 400 | close, inspect | Thiếu CompletionDate |
| `MAINTENANCE_NOT_INSPECTED_YET` | 400 | POST close | Chưa inspect (workflow 3 bước) |
| `MAINTENANCE_NOT_CLOSED` | 400 | POST reopen | Chưa đóng |
| `FIELD_LOCKED` | 400 | PUT | Đổi StartDate |
| `COMPLETION_BEFORE_START` | 400 | PUT | CompletionDate < StartDate |
| `INVALID_COST` | 400 | PUT | Cost âm |
| `INVALID_SUPPLIER` | 400 | PUT | Supplier không tồn tại |
| `TITLE_REQUIRED` | 400 | POST | Thiếu tiêu đề |
| `MAX_5_ASSIGNEES` | 400 | PUT | Quá 5 assignee |
| `INVALID_ASSIGNEE` | 400 | PUT | Assignee không tồn tại |
| `ASSIGNEE_COMPANY_MISMATCH` | 400 | PUT | Assignee khác company với bản ghi |

### Licenses (`/api/v1/licenses`)

| Code | HTTP | Endpoint | Điều kiện |
|---|---|---|---|
| `NOT_FOUND` | 404 | PUT, DELETE /{id} | Không tồn tại |
| `LICENSE_IN_USE` | 400 | DELETE /{id} | Đã có seat cấp phát |
| `NAME_REQUIRED` / `CATEGORY_REQUIRED` / `CATEGORY_INVALID` | 400 | POST | Validation |
| `FIELD_LOCKED` | 400 | PUT | Đổi Category/Company |
| `CANNOT_REDUCE_SEATS_IN_USE` | 400 | PUT | Giảm totalSeats dưới mức đang dùng |
| `SEAT_NOT_FOUND` | 400 | seats/{id}/checkout, /checkin | Seat không tồn tại |
| `SEAT_ALREADY_ASSIGNED` | 400 | seats/{id}/checkout | Seat đã cấp (FOR UPDATE mutex) |
| `SEAT_NOT_ASSIGNED` | 400 | seats/{id}/checkin | Seat chưa cấp |
| `LICENSE_NOT_REASSIGNABLE` | 400 | seats/{id}/checkin | License không cho cấp lại |
| `NO_AVAILABLE_SEATS` | 400 | seats/checkout (by license) | Hết chỗ trống |
| `TARGET_REQUIRED` / `TARGET_NOT_FOUND` / `SEAT_TARGET_AMBIGUOUS` | 400 | seats checkout | Target rules |
| `INVALID_TARGET_TYPE` | 400 | seats checkout | TargetType lạ |
| `LICENSE_COMPANY_MISMATCH` | 400 | seats checkout | Target khác company với license |

### Components (`/api/v1/components`)

| Code | HTTP | Endpoint | Điều kiện |
|---|---|---|---|
| `NOT_FOUND` | 404 | PUT, DELETE /{id} | Không tồn tại |
| `COMPONENT_HAS_ALLOCATION_HISTORY` | 400 | DELETE /{id} | Đã từng cấp phát (delete-guard) |
| `FIELD_LOCKED` | 400 | PUT /{id} | Đổi field khóa |
| `CATEGORY_REQUIRED` / `INVALID_CATEGORY` | 400 | POST | Category rules |
| `COMPANY_REQUIRED` / `INVALID_COMPANY` | 400 | POST | Company rules |
| `COMPANY_MISMATCH` | 400 | POST | Regular user tạo cho company khác |

(ComponentAllocationService — allocations/stock endpoints: `ASSET_NOT_FOUND`, `SERIAL_NOT_FOUND`,
`SERIAL_NOT_ALLOCATED`, `INSUFFICIENT_STOCK`, `INSUFFICIENT_ALLOCATION`, `INVALID_QUANTITY`,
`MISSING_TARGET`, `NOT_SERIAL`, `EMPTY_SERIALS`, `DUPLICATE_SERIAL`, `ALREADY_DELETED`,
`COMPONENT_UNIT_HAS_ALLOCATION_HISTORY`, `COMPONENT_COMPANY_REQUIRED` — tất cả 400.)

### Consumables (`/api/v1/consumables`)

| Code | HTTP | Endpoint | Điều kiện |
|---|---|---|---|
| `NOT_FOUND` | 404 | PUT, DELETE /{id} | Không tồn tại |
| `CONSUMABLE_HAS_CHECKOUTS` | 400 | DELETE /{id} | Đã từng cấp phát |
| `FIELD_LOCKED` | 400 | PUT /{id} | Đổi company khi có checkout |
| `CONFIRMED_CONSUMABLE_LOCKED` | 400 | PUT /{id} | Sửa field khóa sau confirm |
| `COMPANY_MISMATCH` | 400 | POST | Regular user tạo cho company khác |
| `CONSUMABLE_NOT_CONFIRMED` | 400 | POST {id}/checkout | Chưa confirm |
| `INVALID_QUANTITY` | 400 | checkout | Quantity ≤ 0 |
| `INSUFFICIENT_STOCK` | 400 | checkout | Không đủ tồn |
| `TARGET_REQUIRED` / `TARGET_NOT_FOUND` | 400 | checkout | Target rules |
| `CONSUMABLE_COMPANY_MISMATCH` | 400 | checkout | Target khác company với vật tư |

### Accessories (`/api/v1/accessories`)

| Code | HTTP | Endpoint | Điều kiện |
|---|---|---|---|
| `NOT_FOUND` | 404 | DELETE /{id}, checkout | Không tồn tại |
| `ACCESSORY_HAS_CHECKOUTS` | 400 | DELETE /{id} | Đã từng cấp phát |
| `FIELD_LOCKED` | 400 | PUT /{id} | Đổi company khi có checkout |
| `COMPANY_MISMATCH` | 400 | POST, checkout | Company rules |
| `INSUFFICIENT_STOCK` | 400 | POST {id}/checkout | Không đủ tồn |
| `TARGET_NOT_FOUND` | 400 | checkout | Target không tồn tại |
| `CHECKOUT_NOT_FOUND` | 400 | POST {id}/checkin | Checkout record không tồn tại |
| `INVALID_RETURN_QTY` / `EXCEEDS_CHECKED_OUT` | 400 | checkin | Return quantity rules |

### ComponentUnits (`/api/v1/component-units`)

| Code | HTTP | Endpoint | Điều kiện |
|---|---|---|---|
| `NOT_FOUND` | 404 | DELETE /{id} | Không tồn tại (Update/other → 400 cùng code) |
| `ALREADY_DELETED` | 400 | DELETE /{id} | Serial đã xóa trước đó |
| `COMPONENT_UNIT_HAS_ALLOCATION_HISTORY` | 400 | DELETE /{id} | Đã từng cấp phát |

(Update status qua UpdateComponentUnitStatus service: codes tương tự ComponentAllocation.)

### SystemInfos + Positions (`/api/v1/system-infos`)

| Code | HTTP | Endpoint | Điều kiện |
|---|---|---|---|
| `NOT_FOUND` | 404 | mọi endpoint con | Không tồn tại |
| `COMPANY_MISMATCH` | 400 | POST (Create) | Regular user tạo cho company khác |
| `FIELD_LOCKED` | 400 | PUT /{id} | Đổi CompanyId |
| `POSITION_IN_USE_BY_CHECKLIST` | 400 | DELETE position/system | Position đang được checklist tham chiếu |
| `SYSTEM_IN_USE_BY_CAMPAIGN` | 400 | DELETE /{id} | Hệ thống đang có campaign |
| (Dup code) | 400 | POST system/position | "Mã hệ thống đã tồn tại." / "Mã vị trí đã tồn tại." — không error_code |

### ActionLogs / Dashboard / Reports / Permissions / ComponentUnits-logs (read-only)

Read-only controllers — **không có error_code nghiệp vụ**. Lỗi chỉ là 404 ("Không tìm thấy lịch
sử." / "Not found.") hoặc rỗng data theo scope. `GET /reports/checkout-history` với date filter
đã fix BUG-L (SpecifyKind UTC).

---

## 3. Bảng tra nhanh error_code → Controller (Ctrl+F)

> Định dạng: `CODE` — controller (HTTP)

- `ACCESSORY_HAS_CHECKOUTS` — Accessories (400)
- `ACCOUNT_LOCKED` — Auth login (400)
- `ALREADY_ARCHIVED` — Assets (400)
- `ALREADY_CONFIRMED` — Assets (400)
- `ALREADY_DELETED` — ComponentUnits (400)
- `AMBIGUOUS_TEMPLATE` — MaintenanceCampaigns (400)
- `ASSET_ARCHIVED` — Assets (400)
- `ASSET_CHECKED_OUT` — Assets (400)
- `ASSET_HAS_ASSIGNMENTS` — Assets (400)
- `ASSET_HAS_MAINTENANCES` — Assets (400)
- `ASSET_ALREADY_CHECKED_OUT` — Assets (400)
- `ASSET_NOT_DEPLOYABLE` — Assets (400)
- `ASSET_NOT_DEPLOYED` — Assets (400)
- `ASSET_NOT_CHECKED_OUT` — Assets (400)
- `ASSET_NOT_FOUND` — Assets/Components/Consumables allocation paths (400)
- `ASSET_USED_BY_COMPONENT` — Assets (400)
- `ASSIGNEE_COMPANY_MISMATCH` — AssetMaintenances (400)
- `CANNOT_REDUCE_SEATS_IN_USE` — Licenses (400)
- `CATEGORY_INVALID` — Licenses (400)
- `CATEGORY_IN_USE` — Categories (400)
- `CATEGORY_REQUIRED` — Components, Licenses (400)
- `CHECKOUT_NOT_FOUND` — Accessories (400)
- `COMPANY_IN_USE` — Companies (400)
- `COMPANY_MISMATCH` — Departments, Locations, Consumables, Accessories, Components, SystemInfos, Assets (Create + Checkout), MaintenanceTemplates, MaintenanceCampaigns (executors: `EXECUTOR_COMPANY_MISMATCH`), Licenses (`LICENSE_COMPANY_MISMATCH`), AssetMaintenances (`ASSIGNEE_COMPANY_MISMATCH`), ComponentAllocation (`COMPONENT_COMPANY_REQUIRED`), ConsumableAllocation (`CONSUMABLE_COMPANY_MISMATCH`), Users (Create + Update, 400) — **tất cả 400**
- `COMPANY_REQUIRED` — ImportExport (400), Components (400)
- `COMPONENT_COMPANY_REQUIRED` — Components (allocation) (400)
- `COMPONENT_HAS_ALLOCATION_HISTORY` — Components (400)
- `COMPONENT_UNIT_HAS_ALLOCATION_HISTORY` — ComponentUnits (400)
- `CONFIRMED_ASSET_LOCKED` — Assets (400)
- `CONFIRMED_CONSUMABLE_LOCKED` — Consumables (400)
- `CONSUMABLE_COMPANY_MISMATCH` — Consumables checkout (400)
- `CONSUMABLE_HAS_CHECKOUTS` — Consumables (400)
- `CONSUMABLE_NOT_CONFIRMED` — Consumables checkout (400)
- `CUSTOM_FIELD_IN_USE` — CustomFields (400)
- `DEPARTMENT_IN_USE` — Departments (400)
- `DUPLICATE_ASSET_TAG` — Assets (400)
- `DUPLICATE_SERIAL` — Components (400)
- `EMPTY_SERIALS` — Components (400)
- `END_BEFORE_START` — MaintenanceCampaigns (400), AssetMaintenances (400)
- `EXCEEDS_CHECKED_OUT` — Accessories (400)
- `EXECUTOR_COMPANY_MISMATCH` — MaintenanceCampaigns (400)
- `FIELD_LOCKED` — Assets (CONFIRMED_ASSET_LOCKED riêng), Consumables, Accessories, Licenses, SystemInfos, AssetMaintenances, MaintenanceTemplates (400)
- `FORBIDDEN` — AssetMaintenances (→ 403 Forbid), ImportExport (→ 403)
- `GROUP_NOT_FOUND` — Users UpdateUserGroups (400, error_code snake — quirk)
- `INSUFFICIENT_ALLOCATION` — Components (400)
- `INSUFFICIENT_STOCK` — Components, Consumables, Accessories (400)
- `INVALID_ASSIGNEE` — AssetMaintenances (400)
- `INVALID_CATEGORY` — Components, Licenses (400)
- `INVALID_CHECKLIST_ITEM` — MaintenanceCampaigns (400)
- `INVALID_COMPANY` — Components, MaintenanceTemplates (400)
- `INVALID_COST` — AssetMaintenances (400)
- `INVALID_CYCLE_MONTHS` — MaintenanceTemplates (400)
- `INVALID_DEVICE_SNAPSHOT` — MaintenanceCampaigns (400)
- `INVALID_ITEM_POSITION` — MaintenanceCampaigns (400)
- `INVALID_ORDER` — MaintenanceTemplates (400)
- `INVALID_POSITION` — MaintenanceTemplates (400)
- `INVALID_QUANTITY` — Components, Consumables (400)
- `INVALID_RETURN_QTY` — Accessories (400)
- `INVALID_STANDARD_PARAM` — MaintenanceCampaigns (400)
- `INVALID_CREDENTIALS` — Auth login (400), Auth password (400), Auth passkeys/login (**401**)
- `INVALID_SUPPLIER` — AssetMaintenances (400)
- `INVALID_TARGET_TYPE` — Licenses (400)
- `ITEM_NAME_REQUIRED` — MaintenanceTemplates (400)
- `ITEM_NOT_FOUND` — MaintenanceTemplates (404)
- `ITEM_ORDER_TAKEN` — MaintenanceTemplates (400)
- `LICENSE_COMPANY_MISMATCH` — Licenses seats (400)
- `LICENSE_IN_USE` — Licenses (400)
- `LICENSE_NOT_REASSIGNABLE` — Licenses (400)
- `LOCATION_IN_USE` — Locations (400)
- `LOCATION_REQUIRED` — Assets checkout (400)
- `MAINTENANCE_ALREADY_CLOSED` / `MAINTENANCE_CLOSED` / `MAINTENANCE_NOT_CLOSED` / `MAINTENANCE_NOT_COMPLETED_YET` / `MAINTENANCE_NOT_INSPECTED_YET` — AssetMaintenances (400)
- `MANUFACTURER_IN_USE` — Manufacturers (400)
- `MAX_5_ASSIGNEES` — AssetMaintenances (400)
- `MISSING_TARGET` — Components (400)
- `MODEL_IN_USE` — AssetModels (400)
- `MUST_CHANGE_PASSWORD` — **mọi endpoint** trừ `/users/me` + `/auth/password` (**403**; middleware)
- `NO_AVAILABLE_SEATS` — Licenses (400)
- `NO_CURRENT_VERSION` — MaintenanceCampaigns (400)
- `NO_TEMPLATE` — MaintenanceCampaigns (400)
- `NOT_ARCHIVED` — Assets (400)
- `NOT_FOUND` — mọi controller (→ 404; ComponentUnits Delete cũng dùng; MaintenanceTemplates con endpoints 404 + message riêng)
- `NOT_SERIAL` — Components (400)
- `PARAM_NOT_FOUND` — MaintenanceTemplates (404)
- `PARAM_REQUIRED` — MaintenanceTemplates (400)
- `PASSKEY_ALREADY_REGISTERED` / `PASSKEY_MALFORMED_RESPONSE` / `PASSKEY_REGISTRATION_EXPIRED` / `PASSKEY_VERIFICATION_FAILED` — Auth passkeys/register (400). `PASSKEY_ASSERTION_EXPIRED` bị handler gộp thành `INVALID_CREDENTIALS` (401)
- `PASSKEYS_DISABLED` — Auth passkeys login (login/options + login) (**403**)
- `POSITION_IN_USE_BY_CHECKLIST` — SystemInfos (400)
- `RESOURCE_NOT_FOUND` — AssetModels (400, BUG-H), Users Create/Update (DepartmentId/LocationId, 400)
- `RESULT_CONCURRENT_WRITE` — MaintenanceCampaigns (**409** — duy nhất)
- `RESULT_NOT_FOUND` — MaintenanceCampaigns (404)
- `RESULT_TARGET_REQUIRED` — MaintenanceCampaigns (400)
- `SEAT_ALREADY_ASSIGNED` — Licenses (400)
- `SEAT_NOT_ASSIGNED` — Licenses (400)
- `SEAT_NOT_FOUND` — Licenses (400)
- `SEAT_TARGET_AMBIGUOUS` — Licenses (400)
- `SELF_LOCKOUT` — Groups + Users (400; camelCase errorCode)
- `SERIAL_NOT_ALLOCATED` — Components (400)
- `SERIAL_NOT_FOUND` — Components (400)
- `STANDARD_PARAM_NOT_APPLICABLE` / `STANDARD_PARAM_REQUIRED` — MaintenanceCampaigns (400)
- `SUPPLIER_IN_USE` — Suppliers (400)
- `SYSTEM_GROUP_LOCKED` — Groups (400)
- `SYSTEM_IN_USE_BY_CAMPAIGN` — SystemInfos (400)
- `SYSTEM_INFO_REQUIRED` — MaintenanceTemplates, MaintenanceCampaigns (400)
- `SYSTEM_NOT_FOUND` — MaintenanceCampaigns (404)
- `TARGET_NOT_FOUND` — Accessories, Licenses, Consumables (400)
- `TARGET_REQUIRED` — Licenses, Consumables (400)
- `TEMPLATE_IN_USE` — MaintenanceTemplates (400)
- `TEMPLATE_NAME_TAKEN` — MaintenanceTemplates (400)
- `TEMPLATE_SYSTEM_MISMATCH` — MaintenanceCampaigns (400)
- `TEMPLATE_VERSION_IN_USE` — MaintenanceTemplates (400)
- `THRESHOLD_OPERATOR_REQUIRED` / `THRESHOLD_VALUE_REQUIRED` — MaintenanceTemplates (400)
- `TITLE_REQUIRED` — AssetMaintenances (400)
- `USER_NOT_FOUND` — Users PUT/DELETE (404, camelCase errorCode — body không errorCode), Auth `/auth/password` + passkeys/register/options (**401**, không error_code)
- `VALIDATION_ERROR` — Users (400 — chỉ password ban đầu < 8 ký tự)
- `VERSION_ALREADY_PUBLISHED` — MaintenanceTemplates (400)
- `VERSION_NOT_FOUND` — MaintenanceTemplates (404)
