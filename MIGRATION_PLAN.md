# KẾ HOẠCH CHUYỂN ĐỔI (MIGRATION PLAN)

## Snipe-IT (Laravel/PHP/MySQL) → AspireReact (.NET 9 + React + Ant Design + .NET Aspire)

> **Tác giả:** System Architect — 2026-08-05
> **Phiên bản:** 1.1
> **Trạng thái:** Draft — Đã cập nhật (Phase 6, Frontend Arch, DevOps, JSONB, DoD)

---

## Mục lục

1. [Tổng quan mục tiêu](#1-tổng-quan-mục-tiêu)
2. [Phân tích hiện trạng & Thách thức kỹ thuật](#2-phân-tích-hiện-trạng--thách-thức-kỹ-thuật)
3. [Kiến trúc hệ thống mới (To-Be)](#3-kiến-trúc-hệ-thống-mới-to-be)
   - 3.1 Clean Architecture — Cấu trúc thư mục chi tiết
   - 3.1.4 Chuẩn hóa API Response
   - 3.2 Sơ đồ thành phần .NET Aspire
   - 3.3 Thiết kế Database chi tiết
   - 3.3.6 Giải pháp lai Custom Fields: EAV vs JSONB vs ALTER TABLE
   - 3.4 Frontend Architecture
4. [Kế hoạch triển khai — Vertical Slices](#4-kế-hoạch-triển-khai--vertical-slices)
   - 4.0 Bảng tổng thời gian & Sơ đồ phụ thuộc
   - Phase 0 đến Phase 6
5. [Rủi ro & Giảm thiểu](#5-rủi-ro--giảm-thiểu)
6. [Chiến lược di trú dữ liệu](#6-chiến-lược-di-trú-dữ-liệu)
7. [Tiêu chí nghiệm thu](#7-tiêu-chí-nghiệm-thu)
8. [Triển khai & Vận hành (DevOps)](#8-triển-khai--vận-hành-devops)

---

## 1. Tổng quan mục tiêu

### 1.1 Mục đích

Chuyển đổi toàn bộ hệ thống quản lý tài sản IT Snipe-IT (hiện đang chạy trên **Laravel 12 / PHP 8.2+ / MySQL**) sang kiến trúc hiện đại **.NET 9 Web API + React TypeScript + Ant Design + .NET Aspire**. Hệ thống đích là nền tảng quản lý hạ tầng viễn thông/IT cấp doanh nghiệp, phục vụ hàng nghìn người dùng với yêu cầu cao về bảo mật, hiệu năng, và khả năng mở rộng.

### 1.2 Công nghệ target

| Tầng | Công nghệ | Ghi chú |
|------|----------|---------|
| **Orchestration** | .NET Aspire 13.4 | Quản lý vòng đời ứng dụng, service discovery, telemetry |
| **Backend API** | C# .NET 9, ASP.NET Core Web API | RESTful API, Minimal API cho health checks |
| **Database** | PostgreSQL 16 | Thay thế MySQL; hỗ trợ JSONB, Recursive CTE, Row-Level Security |
| **ORM** | Entity Framework Core 9 | Code First + Migrations |
| **Cache** | Redis 7 | Distributed cache, session store |
| **Auth** | Keycloak 26 (OpenID Connect) | Thay thế auth nội bộ của Laravel |
| **Frontend** | React 19 + TypeScript 5 + Vite | SPA với HMR |
| **UI Framework** | Ant Design 6 | Design system cho enterprise |
| **State Management** | Zustand | Nhẹ, TypeScript-first |
| **API Client** | Axios + React Query (TanStack) | Caching, refetch, optimistic update |
| **Testing** | xUnit + Testcontainers + Playwright | Unit, Integration, E2E |
| **Monitoring** | OpenTelemetry + Grafana + Prometheus | Distributed tracing, metrics, alerts |
| **CI/CD** | GitHub Actions + Docker Compose | Build, test, deploy tự động |

### 1.3 Bối cảnh nghiệp vụ

Hệ thống quản lý hạ tầng viễn thông/IT đòi hỏi:

1. **Quản lý thiết bị lồng nhau phức tạp (Nested Assets Parent-Child)**: Một tủ rack chứa nhiều máy chủ, mỗi máy chủ chứa card mạng, ổ cứng, RAM. Mỗi thành phần đều là một asset được theo dõi riêng nhưng vị trí thực tế phụ thuộc vào asset cha.

2. **Xác thực qua OpenID Connect**: Tích hợp với Keycloak (hoặc Active Directory Federation Services) để đồng bộ danh tính với hệ thống doanh nghiệp hiện có.

3. **Phân quyền chi tiết và giới hạn dữ liệu theo khu vực (FMCS)**: Nhân viên chi nhánh Hà Nội chỉ thấy tài sản của Hà Nội; admin tổng thấy toàn bộ. Phân quyền chi tiết đến từng hành động (view, create, edit, delete, checkout, checkin, audit).

4. **Quản lý vòng đời tài sản**: Checkout/Checkin, audit định kỳ, khấu hao, warranty tracking.

5. **Quản lý vật tư tiêu hao**: Mực in, cáp mạng, ốc vít — theo dõi số lượng tồn kho, cảnh báo ngưỡng tối thiểu.

---

## 2. Phân tích hiện trạng & Thách thức kỹ thuật

### 2.1 Tóm tắt kiến trúc hệ thống gốc (Snipe-IT)

Từ phân tích 3 tài liệu (`giai-doan-1-data-va-business-logic-loi.md`, `giai-doan-2-api-va-validation.md`, `giai-doan-3-security-va-dynamic-fields.md`), kiến trúc Snipe-IT có những đặc điểm sau:

#### A. Data Layer (Nguồn: Giai đoạn 1)

- **20+ bảng dữ liệu** xoay quanh `assets` (tài sản), `consumables` (vật tư), `accessories` (phụ kiện), `components` (linh kiện), `licenses` (bản quyền).
- **Polymorphic Assignment**: Bảng `assets` dùng cặp cột `assigned_to` (int) + `assigned_type` (varchar — tên class PHP) để gán tài sản cho một trong ba loại target: `App\Models\User`, `App\Models\Location`, `App\Models\Asset`. Cơ chế này vừa hỗ trợ checkout thông thường (gán cho user/location) vừa tạo quan hệ Parent-Child (gán asset con vào asset cha).
- **Parent-Child đệ quy**: Method `assetLoc()` đệ quy đi lên chuỗi phân cấp (tối đa 10 cấp) cho đến khi gặp User hoặc Location để xác định vị trí thực tế. Có cơ chế chống circular reference (throw Exception nếu vượt 10 cấp).
- **Denormalized Location**: Cột `location_id` trên `assets` được cache từ `assetLoc()` để tối ưu query filter nhưng có thể lệch pha. Cần cronjob `snipeit:sync-asset-locations` để đồng bộ định kỳ.
- **Dynamic Stock Calculation**: Consumables, Components, Accessories đều dùng pattern `qty - count(pivot_records)` để tính số lượng tồn kho qua PHP Accessors.
- **Searchable Trait**: Tự động JOIN các bảng liên quan để tìm kiếm xuyên quan hệ.
- **Presenter Pattern**: Logic hiển thị tách khỏi Model, không bao giờ trả về raw ID.

#### B. API Layer (Nguồn: Giai đoạn 2)

- **RESTful API** dưới prefix `/api/v1/`, 18+ endpoints cho Assets, 8+ cho Consumables, tương tự cho Accessories, Components, Licenses, Users.
- **Hai format response**: `{status, messages, payload}` cho action CRUD/Checkout; `{total, rows}` cho listing (Datatables).
- **Transformer Pattern**: Mọi FK được resolve thành `{id, name, tag_color?}` thay vì raw ID. `AssetsTransformer` xử lý đặc biệt polymorphic `assigned_to`: tự động phát hiện loại target và trả về cấu trúc JSON phù hợp, kèm info-disclosure guard (ẩn PII khi thiếu permission `users.view`).
- **Multi-layer Validation cho Checkout**: FormRequest (syntax/semantic) → Gate Authorization → Business Rules (`availableForCheckout()`) → Concurrency Lock (`lockForUpdate()` trong DB transaction).
- **Concurrency Guard Pattern**: Kiểm tra `availableForCheckout()` hai lần — lần 1 không lock (advisory check, nhanh), lần 2 `lockForUpdate()` trong transaction (authoritative, chống race condition). Pattern này áp dụng cho cả Consumable và License checkout.
- **Custom Validation Rules**: `exists_undeleted` (chặn soft-deleted targets), `AssetCannotBeCheckedOutToNondeployableStatus`.
- **Non-standard HTTP Codes**: Lỗi business → HTTP 200 + error body; validation → 422; authorization → 403 (hiếm).

#### C. Security & Dynamic Fields (Nguồn: Giai đoạn 3)

- **JSON Permission System**: Tất cả permissions lưu trong cột JSON (`users.permissions`, `permission_groups.permissions`). Mỗi permission key (ví dụ: `assets.view`) có giá trị: `1` = Grant, `0` = Not Set, `-1` = Deny. User Deny override cả Group Grant.
- **Permission Resolution Chain**: `Superuser (Gate::before)` → `Admin (hasAccess('admin'))` → `User Grant (1)` → `User Deny (-1)` → `Group Grant (1)` → `Default Deny`.
- **Policy Hierarchy (DRY)**: Abstract `SnipePermissionsPolicy` → `CheckoutablePermissionsPolicy` (thêm checkin) → concrete policies. Mỗi policy chỉ cần override `columnName()`.
- **FMCS Scoping (Multi-tenant)**: `CompanyableScope` (Laravel Global Scope) tự động thêm `WHERE company_id IN (user's company IDs)` vào mọi query Eloquent. Hỗ trợ Parent-Child company (user ở parent auto access child companies). "Floater mode": item không có `company_id` → system-wide visible.
- **Custom Fields — ALTER TABLE Approach (không EAV)**: Khi admin tạo custom field, hệ thống **ALTER TABLE assets ADD COLUMN `_snipeit_<slug>_<id>`** trực tiếp. Cho phép query và sort như native column. Hỗ trợ encrypted fields (dùng Rule Objects riêng để decrypt trước khi validate). DATE/DATETIME format → native column type, không thể đổi format sau khi tạo.
- **Event-Driven Audit Trail**: 6 events (`CheckoutableCheckedOut`, `CheckoutableCheckedIn`, `CheckoutAccepted`, etc.) với 3 subscribers. `LogListener` ghi `action_logs`. `CheckoutableListener` gửi email + webhook notifications. Auditing qua bảng `action_logs` với dual polymorphic (`item_type`/`item_id` + `target_type`/`target_id`).

### 2.2 Năm thách thức kỹ thuật chính

| # | Thách thức | Hệ thống gốc (Snipe-IT) | Hệ thống đích (.NET 9 + React) | Độ phức tạp |
|---|-----------|------------------------|-------------------------------|-------------|
| 1 | **Polymorphic Assignment** | `assigned_to` + `assigned_type` (string-based Eloquent morphTo) | Bảng `Assignments` với discriminator enum + Navigation Properties strongly-typed | **Cao** — cần thiết kế lại toàn bộ mô hình dữ liệu |
| 2 | **Dynamic Stock Calculation** | PHP computed accessors (`numRemaining()`, `percentRemaining()`) | EF Core computed properties + LINQ queries với eager loading | **Trung bình** — cần tối ưu N+1 query |
| 3 | **JSON Permissions Migration** | JSON blob + manual resolution trong `checkPermissionSection()` | ASP.NET Core Policy-based Authorization + `PermissionHandler` custom | **Cao** — mapping 1-1 giữa JSON key và .NET Claim |
| 4 | **FMCS Scoping (Global Query Filter)** | Laravel `CompanyableScope` (Global Scope) | EF Core `HasQueryFilter` + `ICompanyable` interface | **Trung bình** — tương đương về concept |
| 5 | **Custom Fields Engine** | PHP `ALTER TABLE` trực tiếp từ code | Giải pháp lai: JSONB cho MVP + ALTER TABLE migration managed cho production | **Cao** — cần chiến lược chuyển đổi linh hoạt |

#### Phân tích chi tiết từng thách thức

##### Thách thức 1: Polymorphic Assignment → Navigation Properties

**Hệ thống gốc**: Một asset có thể được gán cho User, Location, hoặc Asset khác thông qua cặp cột `assigned_to` (int) + `assigned_type` (string). Laravel Eloquent hỗ trợ `morphTo()` / `morphMany()` để xử lý. Khi query `$asset->assignedTo`, Eloquent tự động đọc `assigned_type`, resolve class, và JOIN đến bảng tương ứng.

**Vấn đề với .NET/EF Core**: EF Core không có cơ chế polymorphic tương đương Eloquent morphTo. Cần thiết kế lại.

**Giải pháp đề xuất — Bảng `Assignments` với discriminator**:

```csharp
// Enum discriminator
public enum AssignmentTargetType
{
    User = 1,
    Location = 2,
    Asset = 3   // Parent-Child
}

// Entity
public class Assignment
{
    public int Id { get; set; }
    public int AssetId { get; set; }              // Asset được gán
    public AssignmentTargetType TargetType { get; set; }
    public int TargetId { get; set; }              // ID của User/Location/Asset
    public DateTime AssignedAt { get; set; }
    public string? Note { get; set; }
    public int AssignedById { get; set; }          // Admin thực hiện checkout

    // Navigation
    public Asset Asset { get; set; } = null!;
    public User? AssignedUser { get; set; }
    public Location? AssignedLocation { get; set; }
    public Asset? AssignedParentAsset { get; set; }
    public User AssignedBy { get; set; } = null!;
}

// Asset entity
public class Asset
{
    public int Id { get; set; }
    // ... other fields

    // Thay vì assigned_to/assigned_type:
    public int? CurrentAssignmentId { get; set; }
    public Assignment? CurrentAssignment { get; set; }

    // Child assets (Parent-Child)
    public ICollection<Assignment> ChildAssignments { get; set; } = new List<Assignment>();
}
```

**EF Core Configuration**:

```csharp
modelBuilder.Entity<Assignment>(entity =>
{
    entity.HasOne(a => a.Asset)
          .WithOne(a => a.CurrentAssignment)
          .HasForeignKey<Asset>(a => a.CurrentAssignmentId)
          .OnDelete(DeleteBehavior.SetNull);

    entity.HasOne(a => a.AssignedUser)
          .WithMany()
          .HasForeignKey(a => a.TargetId)
          .HasPrincipalKey<User>(u => u.Id)
          .OnDelete(DeleteBehavior.SetNull)
          .HasConstraintName("FK_Assignment_User");

    entity.HasOne(a => a.AssignedLocation)
          .WithMany()
          .HasForeignKey(a => a.TargetId)
          .OnDelete(DeleteBehavior.SetNull)
          .HasConstraintName("FK_Assignment_Location");

    entity.HasOne(a => a.AssignedParentAsset)
          .WithMany(asset => asset.ChildAssignments)
          .HasForeignKey(a => a.TargetId)
          .OnDelete(DeleteBehavior.SetNull)
          .HasConstraintName("FK_Assignment_ParentAsset");
});
```

**Trade-off**: Không thể dùng một FK duy nhất cho ba bảng khác nhau. Thay vào đó, `Navigation Properties` cho phép EF Core tự động resolve đúng entity khi `TargetType` phù hợp. Ở tầng Application, chúng ta viết extension method `GetAssignedTargetAsync()` để phân nhánh theo `TargetType` và trả về DTO.

##### Thách thức 2: Dynamic Stock Calculation

**Hệ thống gốc**: PHP accessors tính toán real-time mỗi lần gọi:

```php
public function numRemaining() {
    return $this->qty - $this->users()->count();
}
```

**Giải pháp .NET**: Sử dụng computed properties trong entity + LINQ eager loading:

```csharp
public class Consumable
{
    public int Id { get; set; }
    public int Qty { get; set; }          // Tổng số lượng nhập kho
    public int MinAmt { get; set; }        // Ngưỡng cảnh báo

    public ICollection<ConsumableCheckout> Checkouts { get; set; } = new List<ConsumableCheckout>();

    // Computed properties (không map vào DB)
    [NotMapped]
    public int Remaining => Qty - Checkouts.Count;

    [NotMapped]
    public double PercentRemaining => Qty > 0
        ? Math.Round((double)Remaining / Qty * 100, 2)
        : 0;

    [NotMapped]
    public bool IsLowStock => Remaining <= MinAmt;

    [NotMapped]
    public decimal? TotalCost => PurchaseCost.HasValue
        ? Qty * PurchaseCost.Value
        : null;
}
```

**Quan trọng**: Khi query danh sách consumables, phải luôn `.Include(c => c.Checkouts)` hoặc dùng DTO projection để tránh N+1.

```csharp
// Query tối ưu
var consumables = await _context.Consumables
    .Include(c => c.Checkouts)
    .Select(c => new ConsumableDto
    {
        Id = c.Id,
        Name = c.Name,
        Qty = c.Qty,
        Remaining = c.Qty - c.Checkouts.Count,
        PercentRemaining = c.Qty > 0
            ? Math.Round((double)(c.Qty - c.Checkouts.Count) / c.Qty * 100, 2)
            : 0,
        IsLowStock = (c.Qty - c.Checkouts.Count) <= c.MinAmt
    })
    .ToListAsync();
```

##### Thách thức 3: JSON Permissions → .NET Claims & Policies

**Hệ thống gốc**: Tất cả permissions lưu trong JSON blob:

```json
{
  "assets.view": 1,
  "assets.create": 1,
  "assets.checkout": -1,
  "consumables.view": 0
}
```

Logic phân giải: Superuser → Admin → User Grant → User Deny → Group Grant → Default Deny.

**Giải pháp .NET**:

A. **Lưu trữ**: Mỗi permission trở thành một record trong bảng `UserPermissions`:

```csharp
// UserPermission entity
public class UserPermission
{
    public int UserId { get; set; }
    public string PermissionKey { get; set; } = string.Empty;  // "assets.view"
    public PermissionValue Value { get; set; }                  // Grant, NotSet, Deny
}

// GroupPermission entity
public class GroupPermission
{
    public int GroupId { get; set; }
    public string PermissionKey { get; set; } = string.Empty;
    public PermissionValue Value { get; set; }
}

public enum PermissionValue
{
    Deny = -1,
    NotSet = 0,
    Grant = 1
}
```

B. **Resolution Handler**:

```csharp
public class PermissionHandler : AuthorizationHandler<PermissionRequirement>
{
    private readonly AppDbContext _context;

    public PermissionHandler(AppDbContext context)
    {
        _context = context;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        var userId = int.Parse(context.User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

        // 1. Superuser bypass
        if (context.User.HasClaim("permission", "superuser"))
        {
            context.Succeed(requirement);
            return;
        }

        // 2. Admin bypass
        if (await HasPermission(userId, "admin", PermissionValue.Grant))
        {
            context.Succeed(requirement);
            return;
        }

        // 3-4. User explicit Grant/Deny
        var userPerm = await GetUserPermission(userId, requirement.PermissionKey);
        if (userPerm == PermissionValue.Deny)
        {
            context.Fail();
            return;
        }
        if (userPerm == PermissionValue.Grant)
        {
            context.Succeed(requirement);
            return;
        }

        // 5. Group Grant
        var groupIds = await _context.UserGroups
            .Where(ug => ug.UserId == userId)
            .Select(ug => ug.GroupId)
            .ToListAsync();

        var hasGroupGrant = await _context.GroupPermissions
            .AnyAsync(gp => groupIds.Contains(gp.GroupId)
                         && gp.PermissionKey == requirement.PermissionKey
                         && gp.Value == PermissionValue.Grant);

        if (hasGroupGrant)
        {
            context.Succeed(requirement);
            return;
        }

        // 6. Default Deny
        context.Fail();
    }
}
```

C. **Đăng ký Policy**:

```csharp
// Program.cs
builder.Services.AddAuthorization(options =>
{
    var permissionKeys = new[]
    {
        "assets.view", "assets.create", "assets.edit", "assets.delete",
        "assets.checkout", "assets.checkin", "assets.audit",
        "consumables.view", "consumables.create", "consumables.edit",
        "consumables.delete", "consumables.checkout",
        // ... tất cả permission keys
    };

    foreach (var key in permissionKeys)
    {
        options.AddPolicy(key, policy =>
            policy.Requirements.Add(new PermissionRequirement(key)));
    }
});

builder.Services.AddScoped<IAuthorizationHandler, PermissionHandler>();
```

D. **Sử dụng trong Controller**:

```csharp
[Authorize(Policy = "assets.checkout")]
[HttpPost("api/v1/assets/{id}/checkout")]
public async Task<IActionResult> Checkout(int id, CheckoutRequest request)
{
    // ...
}
```

##### Thách thức 4: FMCS Scoping — EF Core Global Query Filter

**Giải pháp**: EF Core `HasQueryFilter` + `ICompanyable` interface.

```csharp
// Interface
public interface ICompanyable
{
    int? CompanyId { get; set; }
}

// Entity
public class Asset : ICompanyable
{
    public int? CompanyId { get; set; }
    // ...
}

// DbContext configuration
modelBuilder.Entity<Asset>().HasQueryFilter(a =>
    EF.Property<int?>(a, "CompanyId") == null ||
    _currentUserCompanyIds.Contains(EF.Property<int?>(a, "CompanyId"))
);
```

**Caching danh sách Company IDs của user hiện tại**: Sử dụng `IHttpContextAccessor` + cache per-request.

```csharp
public class CompanyScopeService
{
    private readonly AppDbContext _context;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private List<int>? _cachedCompanyIds;

    public CompanyScopeService(AppDbContext context, IHttpContextAccessor httpContextAccessor)
    {
        _context = context;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task<List<int>> GetCurrentUserCompanyIdsAsync()
    {
        if (_cachedCompanyIds != null) return _cachedCompanyIds;

        var userId = int.Parse(_httpContextAccessor.HttpContext!.User
            .FindFirst(ClaimTypes.NameIdentifier)!.Value);

        // Direct company memberships
        var directIds = await _context.UserCompanies
            .Where(uc => uc.UserId == userId)
            .Select(uc => uc.CompanyId)
            .ToListAsync();

        // Expand: parent → child
        var childIds = await _context.Companies
            .Where(c => c.ParentId.HasValue && directIds.Contains(c.ParentId.Value))
            .Select(c => c.Id)
            .ToListAsync();

        _cachedCompanyIds = directIds.Union(childIds).ToList();
        return _cachedCompanyIds;
    }
}
```

##### Thách thức 5: Custom Fields Engine — Giải pháp lai (xem chi tiết tại Mục 3.3.6)

**Tóm tắt**: Hệ thống gốc dùng `ALTER TABLE` trực tiếp — nhanh cho query nhưng rủi ro cao trong production. Ở hệ thống đích, chúng tôi đề xuất giải pháp lai: dùng cột **JSONB** trong PostgreSQL cho giai đoạn MVP (an toàn, linh hoạt), sau đó migrate sang native columns qua managed migration khi hệ thống ổn định. Chi tiết phân tích so sánh và code tại Mục 3.3.6.

---

## 3. Kiến trúc hệ thống mới (To-Be)

### 3.1 Clean Architecture — Cấu trúc thư mục chi tiết

```
aspire-react/
├── aspire-react.sln
├── aspire.config.json
├── nuget.config
│
├── aspire-react.AppHost/                 # .NET Aspire Orchestrator
│   ├── AppHost.cs                        # Định nghĩa tất cả resources
│   ├── aspire-react.AppHost.csproj
│   ├── appsettings.json
│   └── appsettings.Development.json
│
├── aspire-react.ServiceDefaults/         # Shared defaults (resilience, telemetry)
│   ├── Extensions.cs                     # AddServiceDefaults()
│   ├── aspire-react.ServiceDefaults.csproj
│   └── ...
│
├── aspire-react.Server/                  # ASP.NET Core Web API
│   ├── Domain/                           # Entities, Enums, Value Objects, Interfaces
│   │   ├── Entities/
│   │   │   ├── Asset.cs
│   │   │   ├── Assignment.cs
│   │   │   ├── AssetModel.cs
│   │   │   ├── Category.cs
│   │   │   ├── Manufacturer.cs
│   │   │   ├── Supplier.cs
│   │   │   ├── Location.cs
│   │   │   ├── StatusLabel.cs
│   │   │   ├── Consumable.cs
│   │   │   ├── ConsumableCheckout.cs
│   │   │   ├── Component.cs
│   │   │   ├── ComponentAssignment.cs
│   │   │   ├── Accessory.cs
│   │   │   ├── AccessoryCheckout.cs
│   │   │   ├── License.cs
│   │   │   ├── LicenseSeat.cs
│   │   │   ├── ActionLog.cs
│   │   │   ├── User.cs
│   │   │   ├── PermissionGroup.cs
│   │   │   ├── UserPermission.cs
│   │   │   ├── GroupPermission.cs
│   │   │   ├── Company.cs
│   │   │   ├── Department.cs
│   │   │   ├── Depreciation.cs
│   │   │   ├── CustomField.cs
│   │   │   ├── CustomFieldset.cs
│   │   │   └── Maintenance.cs
│   │   ├── Enums/
│   │   │   ├── AssignmentTargetType.cs
│   │   │   ├── ActionType.cs
│   │   │   ├── CustomFieldFormat.cs
│   │   │   ├── PermissionValue.cs
│   │   │   └── AssetAcceptanceStatus.cs
│   │   ├── ValueObjects/
│   │   │   ├── Money.cs
│   │   │   └── DateRange.cs
│   │   └── Interfaces/
│   │       ├── ICompanyable.cs
│   │       ├── ISearchable.cs
│   │       └── IAuditable.cs
│   │
│   ├── Application/                      # Use Cases, DTOs, Service Interfaces
│   │   ├── Common/
│   │   │   ├── Interfaces/
│   │   │   │   ├── IApplicationDbContext.cs
│   │   │   │   ├── ICurrentUserService.cs
│   │   │   │   └── IDateTime.cs
│   │   │   ├── Models/
│   │   │   │   ├── PaginatedList.cs
│   │   │   │   └── ApiResponse.cs
│   │   │   └── Behaviors/
│   │   │       └── AuthorizationBehavior.cs
│   │   ├── Assets/
│   │   │   ├── Commands/
│   │   │   │   ├── CreateAssetCommand.cs
│   │   │   │   ├── UpdateAssetCommand.cs
│   │   │   │   ├── DeleteAssetCommand.cs
│   │   │   │   ├── CheckoutAssetCommand.cs
│   │   │   │   ├── CheckinAssetCommand.cs
│   │   │   │   └── BulkUpdateAssetsCommand.cs
│   │   │   ├── Queries/
│   │   │   │   ├── GetAssetsQuery.cs
│   │   │   │   ├── GetAssetByIdQuery.cs
│   │   │   │   ├── GetAssetHistoryQuery.cs
│   │   │   │   └── GetAssetTreeQuery.cs
│   │   │   ├── DTOs/
│   │   │   │   ├── AssetDto.cs
│   │   │   │   ├── AssetListDto.cs
│   │   │   │   ├── CheckoutRequestDto.cs
│   │   │   │   └── AssetTreeNodeDto.cs
│   │   │   └── Validators/
│   │   │       ├── CheckoutAssetValidator.cs
│   │   │       └── CreateAssetValidator.cs
│   │   ├── Consumables/
│   │   │   ├── Commands/
│   │   │   │   ├── CheckoutConsumableCommand.cs
│   │   │   │   └── ...
│   │   │   ├── Queries/
│   │   │   │   ├── GetConsumablesQuery.cs
│   │   │   │   └── GetLowStockQuery.cs
│   │   │   └── DTOs/
│   │   │       └── ConsumableDto.cs
│   │   ├── Companies/
│   │   │   └── Commands/Queries/DTOs
│   │   ├── Users/
│   │   │   ├── Commands/Queries/DTOs
│   │   │   └── Validators/
│   │   ├── Permissions/
│   │   │   ├── PermissionRequirement.cs
│   │   │   └── PermissionHandler.cs
│   │   ├── Reports/
│   │   │   ├── Queries/
│   │   │   │   ├── GetDashboardSummaryQuery.cs
│   │   │   │   ├── GetAssetsByCategoryQuery.cs
│   │   │   │   └── GetDepreciationReportQuery.cs
│   │   │   └── DTOs/
│   │   │       ├── DashboardSummaryDto.cs
│   │   │       └── ReportDto.cs
│   │   └── CustomFields/
│   │       ├── Commands/
│   │       │   ├── CreateCustomFieldCommand.cs
│   │       │   └── DeleteCustomFieldCommand.cs
│   │       ├── Queries/
│   │       │   └── GetCustomFieldValuesQuery.cs
│   │       └── Services/
│   │           └── CustomFieldService.cs
│   │
│   ├── Infrastructure/                   # Persistence, External Services
│   │   ├── Persistence/
│   │   │   ├── AppDbContext.cs
│   │   │   ├── Configurations/           # EF Core Fluent API
│   │   │   │   ├── AssetConfiguration.cs
│   │   │   │   ├── AssignmentConfiguration.cs
│   │   │   │   └── ...
│   │   │   └── Migrations/              # EF Core Migrations
│   │   ├── Services/
│   │   │   ├── CurrentUserService.cs
│   │   │   ├── CompanyScopeService.cs
│   │   │   ├── CustomFieldService.cs
│   │   │   ├── FileStorageService.cs
│   │   │   ├── CsvExportService.cs
│   │   │   └── DateTimeService.cs
│   │   └── Identity/
│   │       └── KeycloakConfiguration.cs
│   │
│   ├── Web/                              # Presentation (Controllers, Middleware)
│   │   ├── Controllers/
│   │   │   ├── AssetsController.cs
│   │   │   ├── ConsumablesController.cs
│   │   │   ├── AccessoriesController.cs
│   │   │   ├── ComponentsController.cs
│   │   │   ├── LicensesController.cs
│   │   │   ├── UsersController.cs
│   │   │   ├── CompaniesController.cs
│   │   │   ├── LocationsController.cs
│   │   │   ├── CategoriesController.cs
│   │   │   ├── ModelsController.cs
│   │   │   ├── ManufacturersController.cs
│   │   │   ├── SuppliersController.cs
│   │   │   ├── StatusLabelsController.cs
│   │   │   ├── CustomFieldsController.cs
│   │   │   ├── ReportsController.cs
│   │   │   ├── DashboardController.cs
│   │   │   └── ImportExportController.cs
│   │   ├── Middleware/
│   │   │   ├── ApiExceptionMiddleware.cs
│   │   │   └── RequestLoggingMiddleware.cs
│   │   ├── Filters/
│   │   │   └── CompanyScopeFilter.cs
│   │   └── Mapping/
│   │       └── ApiResponseMappingProfile.cs
│   │
│   ├── Program.cs                        # Entry point
│   ├── appsettings.json
│   ├── appsettings.Development.json
│   └── aspire-react.Server.csproj
│
└── frontend/                             # React + TypeScript + Ant Design
    ├── package.json
    ├── vite.config.ts
    ├── tsconfig.json
    ├── index.html
    └── src/
        ├── main.tsx
        ├── App.tsx
        ├── router.tsx                     # React Router configuration
        ├── services/                      # API Client
        │   ├── api-client.ts             # Axios instance + interceptors
        │   ├── assets.service.ts
        │   ├── consumables.service.ts
        │   ├── users.service.ts
        │   ├── auth.service.ts
        │   ├── dashboard.service.ts
        │   ├── reports.service.ts
        │   └── import-export.service.ts
        ├── hooks/                         # Custom hooks
        │   ├── useAssets.ts
        │   ├── useConsumables.ts
        │   ├── useAuth.ts
        │   ├── usePermission.ts
        │   ├── usePagination.ts
        │   └── useDebounce.ts
        ├── stores/                        # Zustand stores
        │   ├── auth.store.ts
        │   ├── asset.store.ts
        │   ├── app.store.ts
        │   └── notification.store.ts
        ├── types/                         # TypeScript type definitions
        │   ├── asset.ts
        │   ├── consumable.ts
        │   ├── user.ts
        │   ├── api-response.ts
        │   ├── dashboard.ts
        │   └── report.ts
        ├── components/                    # Shared UI components
        │   ├── layout/
        │   │   ├── AppLayout.tsx
        │   │   ├── Sidebar.tsx
        │   │   └── Header.tsx
        │   ├── common/
        │   │   ├── ProtectedRoute.tsx
        │   │   ├── PermissionButton.tsx
        │   │   ├── ApiErrorAlert.tsx
        │   │   ├── ConfirmDialog.tsx
        │   │   ├── LoadingSpinner.tsx
        │   │   ├── EmptyState.tsx
        │   │   └── PageHeader.tsx
        │   ├── assets/
        │   │   ├── AssetTree.tsx
        │   │   ├── AssetTable.tsx
        │   │   ├── CheckoutDialog.tsx
        │   │   ├── CheckinDialog.tsx
        │   │   └── ActionLogTimeline.tsx
        │   ├── consumables/
        │   │   ├── StockAlertBadge.tsx
        │   │   └── CheckoutConsumableDialog.tsx
        │   ├── dashboard/
        │   │   ├── SummaryCard.tsx
        │   │   ├── AssetByStatusChart.tsx
        │   │   ├── AssetByCategoryChart.tsx
        │   │   ├── RecentActivityWidget.tsx
        │   │   └── LowStockAlert.tsx
        │   ├── reports/
        │   │   └── ReportFilterForm.tsx
        │   └── custom-fields/
        │       ├── DynamicFormRenderer.tsx
        │       └── FieldsetBuilder.tsx
        ├── pages/
        │   ├── auth/
        │   │   └── LoginPage.tsx
        │   ├── dashboard/
        │   │   └── DashboardPage.tsx
        │   ├── assets/
        │   │   ├── AssetListPage.tsx
        │   │   ├── AssetDetailPage.tsx
        │   │   ├── AssetFormPage.tsx
        │   │   └── AssetTreePage.tsx
        │   ├── consumables/
        │   │   └── ConsumableListPage.tsx
        │   ├── users/
        │   │   └── UserManagementPage.tsx
        │   ├── admin/
        │   │   ├── CategoryListPage.tsx
        │   │   ├── LocationTreePage.tsx
        │   │   ├── ModelListPage.tsx
        │   │   ├── CustomFieldListPage.tsx
        │   │   └── PermissionMatrixPage.tsx
        │   ├── reports/
        │   │   └── ReportsPage.tsx
        │   └── import-export/
        │       ├── ImportPage.tsx
        │       └── ExportPage.tsx
        └── styles/
            └── theme.ts                   # Ant Design theme customization
```

#### 3.1.4 Chuẩn hóa API Response

Mọi API endpoint trong hệ thống phải tuân thủ chuẩn response format dưới đây. Nguyên tắc cốt lõi: **không bao giờ trả về raw ID kỹ thuật** — luôn kèm theo descriptive names (`{id, name}`) cho mọi foreign key.

##### A. Success Response (Action CRUD / Checkout / Checkin)

```json
{
  "status": "success",
  "message": "Asset checked out successfully.",
  "data": {
    "asset": {
      "id": 123,
      "asset_tag": "MAC-0042",
      "name": "MacBook Pro 16-inch",
      "current_assignment": {
        "id": 567,
        "assigned_to": {
          "id": 42,
          "type": "user",
          "name": "Nguyen Van A",
          "username": "nguyenvana",
          "email": "nguyenvana@company.com",
          "employee_number": "NV0042",
          "jobtitle": "Kỹ sư phần mềm"
        },
        "assigned_by": {
          "id": 1,
          "name": "Admin"
        },
        "assigned_at": "2026-08-05T09:30:00Z",
        "note": "Bàn giao máy tính cho nhân viên mới"
      }
    }
  },
  "timestamp": "2026-08-05T09:30:00Z"
}
```

##### B. Error Response (Business Logic)

```json
{
  "status": "error",
  "message": "Asset MAC-0042 is not available for checkout.",
  "error_code": "ASSET_NOT_AVAILABLE",
  "details": {
    "asset_id": 123,
    "asset_tag": "MAC-0042",
    "reason": "Asset is already assigned to User #42"
  },
  "timestamp": "2026-08-05T09:30:00Z"
}
```

##### C. Validation Error Response

```json
{
  "status": "error",
  "message": "Validation failed.",
  "error_code": "VALIDATION_ERROR",
  "errors": [
    {
      "field": "checkout_to_type",
      "message": "The checkout_to_type field is required.",
      "code": "required"
    },
    {
      "field": "assigned_user",
      "message": "At least one target (user, asset, or location) must be provided.",
      "code": "required_without_all"
    }
  ],
  "timestamp": "2026-08-05T09:30:00Z"
}
```

##### D. Paginated List Response

```json
{
  "status": "success",
  "data": [
    {
      "id": 123,
      "name": "MacBook Pro 16-inch",
      "asset_tag": "MAC-0042",
      "serial": "C02ZX1234ABCD",
      "model": { "id": 5, "name": "MacBook Pro 16\" 2023" },
      "category": { "id": 3, "name": "Laptops", "tag_color": "#3498db" },
      "manufacturer": { "id": 1, "name": "Apple Inc." },
      "status_label": { "id": 2, "name": "Ready to Deploy", "status_type": "deployable", "color": "green" },
      "location": { "id": 7, "name": "Tầng 2 - VP Hà Nội" },
      "assigned_to": null,
      "purchase_date": { "formatted": "2023-06-15" },
      "purchase_cost": "$2,499.00",
      "warranty_expires": { "formatted": "2026-06-15" },
      "available_actions": {
        "checkout": true,
        "checkin": false,
        "update": true,
        "delete": false,
        "audit": true,
        "clone": true
      }
    }
  ],
  "pagination": {
    "page": 1,
    "page_size": 20,
    "total_items": 1500,
    "total_pages": 75,
    "has_next_page": true,
    "has_previous_page": false
  },
  "timestamp": "2026-08-05T09:30:00Z"
}
```

##### E. API Response Wrapper trong C#

```csharp
// Application/Common/Models/ApiResponse.cs
public class ApiResponse<T>
{
    public string Status { get; set; } = "success";
    public string Message { get; set; } = string.Empty;
    public T? Data { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    public static ApiResponse<T> Success(T data, string message = "Operation successful.")
        => new() { Status = "success", Data = data, Message = message };

    public static ApiResponse<T> Error(string message, string errorCode, object? details = null)
        => new() { Status = "error", Message = message };
}

public class PaginatedResponse<T>
{
    public string Status { get; set; } = "success";
    public List<T> Data { get; set; } = new();
    public PaginationMetadata Pagination { get; set; } = new();
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

public class PaginationMetadata
{
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalItems { get; set; }
    public int TotalPages { get; set; }
    public bool HasNextPage { get; set; }
    public bool HasPreviousPage { get; set; }
}
```

##### F. Descriptive Names Mapping Rule

Quy tắc bắt buộc cho mọi Foreign Key trong response:

| FK trong DB | Trả về | Ví dụ |
|-------------|--------|-------|
| `model_id` | `model: { id, name }` | `{ "id": 5, "name": "MacBook Pro 16\" 2023" }` |
| `category_id` | `category: { id, name, tag_color }` | `{ "id": 3, "name": "Laptops", "tag_color": "#3498db" }` |
| `manufacturer_id` | `manufacturer: { id, name }` | `{ "id": 1, "name": "Apple Inc." }` |
| `status_id` | `status_label: { id, name, status_type, color }` | `{ "id": 2, "name": "Ready to Deploy", "status_type": "deployable", "color": "green" }` |
| `location_id` | `location: { id, name }` | `{ "id": 7, "name": "Tầng 2 - VP Hà Nội" }` |
| `supplier_id` | `supplier: { id, name }` | `{ "id": 10, "name": "Apple Reseller" }` |
| `company_id` | `company: { id, name }` | `{ "id": 1, "name": "Công ty TNHH ABC" }` |
| `current_assignment_id` | `current_assignment: { id, assigned_to: {...}, assigned_by: {...} }` | Polymorphic resolution |
| `created_by` | `created_by: { id, name }` | `{ "id": 1, "name": "Admin" }` |

### 3.2 Sơ đồ thành phần .NET Aspire

```
┌──────────────────────────────────────────────────────────────────────────┐
│                     aspire-react.AppHost (Orchestrator)                   │
├──────────────────────────────────────────────────────────────────────────┤
│                                                                          │
│  ┌──────────────┐   ┌──────────────┐   ┌──────────────┐                 │
│  │ postgres     │   │ redis        │   │ keycloak     │                 │
│  │ (Container)  │   │ (Container)  │   │ (Container)  │                 │
│  │ PostgreSQL 16│   │ Redis 7      │   │ Keycloak 26  │                 │
│  │ Port: 5432   │   │ Port: 6379   │   │ Port: 8080   │                 │
│  └──────┬───────┘   └──────┬───────┘   └──────┬───────┘                 │
│         │                  │                  │                          │
│  ┌──────┴──────────────────┴──────────────────┴───────┐                 │
│  │                  server (ASP.NET Core)              │                 │
│  │                  Port: 5000 (HTTP)                  │                 │
│  │                  /health endpoint                   │                 │
│  └────────────────────────┬───────────────────────────┘                 │
│                           │                                              │
│  ┌────────────────────────┴───────────────────────────┐                 │
│  │               webfrontend (Vite + React)            │                 │
│  │               Port: 5173 (dev), static in prod      │                 │
│  │               Proxy: /api → server:5000             │                 │
│  └────────────────────────────────────────────────────┘                 │
│                                                                          │
│  ┌────────────────────────────────────────────────────┐                 │
│  │        OpenTelemetry Collector + Grafana            │                 │
│  │        (Monitoring stack — optional container)      │                 │
│  └────────────────────────────────────────────────────┘                 │
│                                                                          │
└──────────────────────────────────────────────────────────────────────────┘
```

**AppHost.cs mở rộng cho hệ thống đích**:

```csharp
var builder = DistributedApplication.CreateBuilder(args);

// Infrastructure
var postgres = builder.AddPostgres("postgres", port: 5432)
    .WithPgAdmin()
    .AddDatabase("aspire-react-db");

var redis = builder.AddRedis("cache", port: 6379);

var keycloak = builder.AddKeycloak("keycloak", port: 8080)
    .WithRealm("aspire-react")
    .WithClient("frontend", client =>
    {
        client.WithRedirectUri("http://localhost:5173/*");
        client.WithPostLogoutRedirectUri("http://localhost:5173/");
    });

// Backend
var server = builder.AddProject<Projects.aspire_react_Server>("server")
    .WithReference(postgres)
    .WithReference(redis)
    .WithReference(keycloak)
    .WaitFor(postgres)
    .WaitFor(redis)
    .WaitFor(keycloak)
    .WithHttpHealthCheck("/health")
    .WithExternalHttpEndpoints();

// Frontend
var webfrontend = builder.AddViteApp("webfrontend", "../frontend")
    .WithReference(server)
    .WaitFor(server);

server.PublishWithContainerFiles(webfrontend, "wwwroot");

builder.Build().Run();
```

### 3.3 Thiết kế Database chi tiết

#### 3.3.1 PostgreSQL Schema (các bảng chính)

| Bảng | Mô tả | Cột quan trọng |
|------|-------|---------------|
| `assets` | Tài sản (cá thể) | `asset_tag`, `serial`, `model_id`, `status_id`, `location_id` (cache), `rtd_location_id`, `current_assignment_id`, `company_id`, `purchase_cost`, `warranty_months`, `expected_checkin`, `last_audit_date`, `next_audit_date`, `checkin_counter`, `checkout_counter`, `physical`, `requestable`, `archived`, `custom_fields` (JSONB — MVP) |
| `assignments` | Gán tài sản (thay thế polymorphic) | `asset_id`, `target_type` (enum), `target_id`, `assigned_by_id`, `assigned_at`, `note` |
| `models` | Model/loại tài sản | `name`, `model_number`, `manufacturer_id`, `category_id`, `depreciation_id`, `fieldset_id`, `eol` |
| `categories` | Danh mục (đa năng) | `name`, `category_type` (asset/consumable/accessory/component/license), `tag_color`, `use_default_eula`, `require_acceptance`, `checkin_email` |
| `manufacturers` | Nhà sản xuất | `name`, `url`, `support_url`, `support_email` |
| `suppliers` | Nhà cung cấp | `name`, `url`, `contact_name`, `contact_email` |
| `locations` | Địa điểm (phân cấp) | `name`, `parent_id` (self-referencing), `manager_id`, `address`, `city`, `state`, `country`, `zip` |
| `status_labels` | Nhãn trạng thái | `name`, `deployable`, `pending`, `archived`, `status_type`, `color` |
| `consumables` | Vật tư tiêu hao (theo dòng) | `name`, `item_no`, `qty`, `min_amt`, `category_id`, `manufacturer_id`, `location_id`, `company_id` |
| `consumable_checkouts` | Lịch sử cấp phát vật tư | `consumable_id`, `user_id`, `assigned_to`, `quantity`, `note` |
| `components` | Linh kiện (theo dòng) | `name`, `serial`, `qty`, `min_amt`, `category_id`, `location_id`, `company_id` |
| `component_assignments` | Gán linh kiện vào asset | `component_id`, `asset_id`, `assigned_qty`, `note` |
| `accessories` | Phụ kiện (theo dòng) | `name`, `qty`, `min_amt`, `category_id`, `location_id`, `company_id` |
| `accessory_checkouts` | Lịch sử cấp phát phụ kiện | `accessory_id`, `assigned_to`, `target_type`, `quantity`, `note` |
| `licenses` | Bản quyền phần mềm | `name`, `serial`, `seats`, `expiration_date`, `manufacturer_id`, `category_id`, `company_id` |
| `license_seats` | Ghế bản quyền | `license_id`, `asset_id` (nullable) |
| `action_logs` | Nhật ký hành động | `item_type` (enum), `item_id`, `target_type` (enum), `target_id`, `action_type` (enum), `created_by`, `location_id`, `company_id`, `note`, `log_meta` (JSONB), `action_date`, `remote_ip`, `user_agent`, `action_source` (enum) |
| `users` | Người dùng | `username`, `first_name`, `last_name`, `email`, `employee_number`, `jobtitle`, `location_id`, `department_id`, `company_id`, `is_superuser`, `is_active` |
| `permission_groups` | Nhóm quyền | `name`, `description` |
| `user_permissions` | Quyền cá nhân | `user_id`, `permission_key`, `value` (Grant/Deny/NotSet) |
| `group_permissions` | Quyền nhóm | `group_id`, `permission_key`, `value` |
| `user_groups` | User-Group pivot | `user_id`, `group_id` |
| `companies` | Công ty (multi-tenant) | `name`, `parent_id` |
| `user_companies` | User-Company pivot | `user_id`, `company_id` |
| `departments` | Phòng ban | `name`, `company_id`, `location_id`, `manager_id` |
| `depreciations` | Phương pháp khấu hao | `name`, `months` |
| `custom_fields` | Metadata trường tùy chỉnh | `name`, `slug`, `format`, `element`, `field_values`, `field_encrypted`, `help_text`, `show_in_email`, `is_unique` |
| `custom_fieldsets` | Bộ trường tùy chỉnh | `name` |
| `custom_field_fieldsets` | Field-Fieldset pivot | `fieldset_id`, `field_id`, `required`, `order` |
| `asset_maintenances` | Lịch sử bảo trì | `asset_id`, `maintenance_type`, `title`, `start_date`, `completion_date`, `cost`, `supplier_id`, `is_warranty` |

#### 3.3.2 Chuyển đổi Polymorphic → Bảng Assignments

**Trước (Snipe-IT MySQL)**:

```sql
-- assets table
assigned_to   INT NULL
assigned_type VARCHAR(255) NULL  -- 'App\Models\User', 'App\Models\Location', 'App\Models\Asset'
```

**Sau (.NET PostgreSQL)**:

```sql
-- assignments table (bảng mới)
id               SERIAL PRIMARY KEY
asset_id         INT NOT NULL REFERENCES assets(id) ON DELETE CASCADE
target_type      SMALLINT NOT NULL  -- 1=User, 2=Location, 3=Asset
target_id        INT NOT NULL
assigned_by_id   INT NOT NULL REFERENCES users(id)
assigned_at      TIMESTAMPTZ NOT NULL DEFAULT NOW()
note             TEXT

-- assets table
current_assignment_id INT NULL REFERENCES assignments(id) ON DELETE SET NULL
```

#### 3.3.3 Recursive Location Resolution trong PostgreSQL

```sql
-- Recursive CTE để tìm vị trí thực tế của asset trong chuỗi Parent-Child
WITH RECURSIVE asset_chain AS (
    SELECT a.id, a.current_assignment_id, a.rtd_location_id, 1 AS depth
    FROM assets a
    WHERE a.id = @assetId

    UNION ALL

    SELECT p.id, p.current_assignment_id, p.rtd_location_id, ac.depth + 1
    FROM assets p
    INNER JOIN assignments asgn ON asgn.asset_id = ac.id
    INNER JOIN asset_chain ac ON asgn.target_type = 3 AND asgn.target_id = ac.id
    WHERE ac.depth < 10  -- Giới hạn 10 cấp (chống circular reference)
)
SELECT
    CASE
        WHEN asgn_u.target_type = 1 THEN u_loc.id
        WHEN asgn_l.target_type = 2 THEN asgn_l.target_id
        ELSE a.rtd_location_id
    END AS resolved_location_id
FROM asset_chain a
LEFT JOIN assignments asgn_u ON a.current_assignment_id = asgn_u.id AND asgn_u.target_type = 1
LEFT JOIN users u ON asgn_u.target_id = u.id
LEFT JOIN locations u_loc ON u.location_id = u_loc.id
LEFT JOIN assignments asgn_l ON a.current_assignment_id = asgn_l.id AND asgn_l.target_type = 2
ORDER BY a.depth DESC
LIMIT 1;
```

#### 3.3.4 Global Query Filters (FMCS) — PostgreSQL Row-Level Security (tuỳ chọn nâng cao)

```sql
ALTER TABLE assets ENABLE ROW LEVEL SECURITY;

CREATE POLICY company_isolation ON assets
    FOR SELECT
    USING (
        company_id IN (
            SELECT uc.company_id
            FROM user_companies uc
            WHERE uc.user_id = current_setting('app.current_user_id')::int
        )
        OR company_id IS NULL
    );

CREATE POLICY superuser_bypass ON assets
    FOR ALL
    USING (
        EXISTS (
            SELECT 1 FROM users
            WHERE users.id = current_setting('app.current_user_id')::int
            AND users.is_superuser = true
        )
    );
```

#### 3.3.5 PostgreSQL JSONB cho Audit Trail

Bảng `action_logs` sử dụng `log_meta JSONB` để lưu snapshot old/new values:

```sql
action_logs:
  log_meta JSONB  -- {"old": {"status_id": 1, "location_id": 5},
                   --  "new": {"status_id": 2, "location_id": 7}}
```

Cho phép query: `SELECT * FROM action_logs WHERE log_meta->'new'->>'status_id' = '2'`

#### 3.3.6 Giải pháp lai cho Custom Fields: EAV vs JSONB vs ALTER TABLE

##### Phân tích so sánh

| Tiêu chí | EAV (Entity-Attribute-Value) | ALTER TABLE (Snipe-IT approach) | **JSONB (PostgreSQL) — Khuyến nghị MVP** |
|----------|------------------------------|----------------------------------|------------------------------------------|
| **An toàn production** | ✅ Cao — không thay đổi schema | ❌ Thấp — ALTER TABLE trực tiếp, rủi ro lock table | ✅ Cao — chỉ UPDATE dữ liệu |
| **Tốc độ query** | ❌ Chậm — cần JOIN/PIVOT phức tạp | ✅ Nhanh — native column, index được | ✅ Khá nhanh — GIN index + JSONB operators |
| **Tính linh hoạt** | ✅ Cao — thêm field không giới hạn | ❌ Thấp — format cứng (DATE → DATE column) | ✅ Cao — schema-less, dễ thay đổi |
| **Validation** | ❌ Khó — không có type checking ở DB | ✅ Tốt — type checking native | ✅ Tốt — JSON Schema validation hoặc application-level |
| **Migration dữ liệu** | ✅ Dễ — chỉ là rows mới | ❌ Khó — cần ALTER TABLE + backfill data | ✅ Dễ — chỉ UPDATE JSON |
| **Sort/Filter** | ❌ Rất chậm — cần pivot | ✅ Nhanh — `ORDER BY native_column` | ✅ Khá nhanh — `ORDER BY custom_fields->>'color'` với GIN index |
| **Encrypted fields** | ⚠️ Phức tạp | ⚠️ Cần custom Rule Objects | ⚠️ Encrypt value trước khi lưu vào JSON |
| **Phù hợp giai đoạn** | Production ổn định | Production ổn định + ít thay đổi | **MVP / Phát triển nhanh** |

##### Khuyến nghị: Chiến lược 2 giai đoạn

**Giai đoạn 1 — MVP (JSONB)**: Sử dụng cột `custom_fields JSONB` trên bảng `assets` để lưu tất cả custom field values. Đây là giải pháp an toàn, linh hoạt, cho phép team phát triển nhanh mà không lo rủi ro ALTER TABLE.

**Giai đoạn 2 — Production (Hybrid)**: Khi hệ thống ổn định:
- Các field có tần suất query/sort/filter cao → migrate ra native column qua managed migration
- Các field ít dùng hoặc thay đổi thường xuyên → giữ nguyên trong JSONB

##### Implementation JSONB cho MVP

**Schema**:

```sql
-- assets table
ALTER TABLE assets ADD COLUMN custom_fields JSONB DEFAULT '{}';

-- GIN index cho tìm kiếm và sort trên JSONB
CREATE INDEX idx_assets_custom_fields ON assets USING GIN (custom_fields jsonb_path_ops);

-- Index cho một key cụ thể nếu query thường xuyên
CREATE INDEX idx_assets_custom_color ON assets ((custom_fields->>'color'));
```

**Entity**:

```csharp
public class Asset
{
    // ... other fields

    // Custom fields stored as JSONB
    [Column(TypeName = "jsonb")]
    public string CustomFieldsJson { get; set; } = "{}";

    [NotMapped]
    public Dictionary<string, object?> CustomFields
    {
        get => JsonSerializer.Deserialize<Dictionary<string, object?>>(CustomFieldsJson) ?? new();
        set => CustomFieldsJson = JsonSerializer.Serialize(value);
    }

    // Typed accessor cho field cụ thể
    [NotMapped]
    public string? Color
    {
        get => CustomFields.TryGetValue("color", out var val) ? val?.ToString() : null;
        set => CustomFields["color"] = value;
    }
}
```

**EF Core Configuration**:

```csharp
modelBuilder.Entity<Asset>(entity =>
{
    entity.Property(e => e.CustomFieldsJson)
          .HasColumnType("jsonb")
          .HasDefaultValue("{}");

    // GIN index
    entity.HasIndex(e => e.CustomFieldsJson)
          .HasMethod("gin")
          .HasOperators("jsonb_path_ops");
});
```

**Query với JSONB — LINQ**:

```csharp
// Lọc asset có custom field "color" = "Space Gray"
var assets = await _context.Assets
    .Where(a => EF.Functions.JsonContains(
        a.CustomFieldsJson,
        "{\"color\": \"Space Gray\"}"))
    .ToListAsync();

// Sort theo custom field
var sorted = await _context.Assets
    .OrderBy(a => a.CustomFieldsJson.RootElement.GetProperty("color").GetString())
    .ToListAsync();

// Raw SQL cho performance tối ưu
var sql = @"SELECT * FROM assets
            WHERE custom_fields @> '{\"color\": \"Space Gray\"}'
            ORDER BY custom_fields->>'color'";
```

**Validation**:

```csharp
public class CustomFieldValidator
{
    public ValidationResult Validate(Dictionary<string, object?> values, List<CustomField> fieldDefs)
    {
        var errors = new List<ValidationError>();

        foreach (var field in fieldDefs)
        {
            var value = values.GetValueOrDefault(field.Slug);

            // Required check
            if (field.IsRequired && value == null)
            {
                errors.Add(new ValidationError(field.Slug, $"{field.Name} is required."));
                continue;
            }

            if (value == null) continue;

            // Type check
            switch (field.Format)
            {
                case CustomFieldFormat.Numeric:
                    if (!decimal.TryParse(value.ToString(), out _))
                        errors.Add(new ValidationError(field.Slug, $"{field.Name} must be a number."));
                    break;
                case CustomFieldFormat.Email:
                    if (!IsValidEmail(value.ToString()))
                        errors.Add(new ValidationError(field.Slug, $"{field.Name} must be a valid email."));
                    break;
                case CustomFieldFormat.Date:
                    if (!DateTime.TryParse(value.ToString(), out _))
                        errors.Add(new ValidationError(field.Slug, $"{field.Name} must be a valid date."));
                    break;
                // ... other formats
            }

            // Unique check
            if (field.IsUnique)
            {
                var exists = _context.Assets.Any(a =>
                    a.CustomFieldsJson.Contains($"\"{field.Slug}\": \"{value}\""));
                if (exists)
                    errors.Add(new ValidationError(field.Slug, $"{field.Name} must be unique."));
            }
        }

        return new ValidationResult(errors);
    }
}
```

**Migration JSONB → Native Columns (Giai đoạn 2)**:

Khi cần migrate một custom field từ JSONB ra native column:

```sql
-- 1. Thêm cột native
ALTER TABLE assets ADD COLUMN color TEXT;

-- 2. Backfill data từ JSONB
UPDATE assets SET color = custom_fields->>'color'
WHERE custom_fields ? 'color';

-- 3. Tạo index
CREATE INDEX idx_assets_color ON assets (color);

-- 4. Xóa key khỏi JSONB (sau khi verify)
UPDATE assets SET custom_fields = custom_fields - 'color';
```

---

### 3.4 Frontend Architecture

#### 3.4.1 React Router — Cấu trúc điều hướng

```typescript
// src/router.tsx
import { createBrowserRouter, Navigate } from 'react-router-dom';
import AppLayout from './components/layout/AppLayout';
import ProtectedRoute from './components/common/ProtectedRoute';
import DashboardPage from './pages/dashboard/DashboardPage';
import AssetListPage from './pages/assets/AssetListPage';
import AssetDetailPage from './pages/assets/AssetDetailPage';
import AssetTreePage from './pages/assets/AssetTreePage';
import ConsumableListPage from './pages/consumables/ConsumableListPage';
import UserManagementPage from './pages/users/UserManagementPage';
import ReportsPage from './pages/reports/ReportsPage';
import ImportPage from './pages/import-export/ImportPage';
import LoginPage from './pages/auth/LoginPage';

export const router = createBrowserRouter([
  {
    path: '/login',
    element: <LoginPage />,
  },
  {
    path: '/',
    element: (
      <ProtectedRoute>
        <AppLayout />
      </ProtectedRoute>
    ),
    children: [
      { index: true, element: <Navigate to="/dashboard" replace /> },
      { path: 'dashboard', element: <DashboardPage /> },
      { path: 'assets', element: <AssetListPage /> },
      { path: 'assets/tree', element: <AssetTreePage /> },
      { path: 'assets/:id', element: <AssetDetailPage /> },
      { path: 'consumables', element: <ConsumableListPage /> },
      { path: 'users', element: <UserManagementPage /> },
      { path: 'reports', element: <ReportsPage /> },
      { path: 'import', element: <ImportPage /> },
      // ... admin routes (lazy-loaded)
      {
        path: 'admin',
        lazy: () => import('./pages/admin/AdminRoutes'),
      },
    ],
  },
]);
```

#### 3.4.2 State Management với Zustand

```typescript
// src/stores/auth.store.ts
import { create } from 'zustand';
import { AuthState, User } from '../types/user';

interface AuthStore extends AuthState {
  setUser: (user: User | null) => void;
  setToken: (token: string | null) => void;
  setPermissions: (permissions: Record<string, number>) => void;
  hasPermission: (key: string) => boolean;
  logout: () => void;
}

export const useAuthStore = create<AuthStore>((set, get) => ({
  user: null,
  token: null,
  permissions: {},
  isAuthenticated: false,

  setUser: (user) => set({ user, isAuthenticated: !!user }),
  setToken: (token) => set({ token }),
  setPermissions: (permissions) => set({ permissions }),

  hasPermission: (key) => {
    const { permissions } = get();
    if (permissions['superuser'] === 1) return true;
    if (permissions['admin'] === 1) return true;
    if (permissions[key] === -1) return false;
    if (permissions[key] === 1) return true;
    // Check group grants
    return permissions[`group:${key}`] === 1;
  },

  logout: () => set({
    user: null,
    token: null,
    permissions: {},
    isAuthenticated: false,
  }),
}));
```

```typescript
// src/stores/asset.store.ts
import { create } from 'zustand';
import { Asset, AssetFilters } from '../types/asset';
import { assetsService } from '../services/assets.service';

interface AssetStore {
  assets: Asset[];
  total: number;
  filters: AssetFilters;
  loading: boolean;
  error: string | null;

  fetchAssets: () => Promise<void>;
  setFilters: (filters: Partial<AssetFilters>) => void;
}

export const useAssetStore = create<AssetStore>((set, get) => ({
  assets: [],
  total: 0,
  filters: { page: 1, pageSize: 20 },
  loading: false,
  error: null,

  fetchAssets: async () => {
    set({ loading: true, error: null });
    try {
      const response = await assetsService.getList(get().filters);
      set({ assets: response.data, total: response.pagination.totalItems });
    } catch (err: any) {
      set({ error: err.message });
    } finally {
      set({ loading: false });
    }
  },

  setFilters: (newFilters) => {
    set((state) => ({ filters: { ...state.filters, ...newFilters } }));
    get().fetchAssets();
  },
}));
```

#### 3.4.3 Luồng tích hợp Keycloak (OIDC)

```typescript
// src/services/auth.service.ts
import Keycloak from 'keycloak-js';
import { useAuthStore } from '../stores/auth.store';

const keycloakConfig = {
  url: import.meta.env.VITE_KEYCLOAK_URL || 'http://localhost:8080',
  realm: 'aspire-react',
  clientId: 'frontend',
};

const keycloak = new Keycloak(keycloakConfig);

export const initKeycloak = async (): Promise<boolean> => {
  try {
    const authenticated = await keycloak.init({
      onLoad: 'check-sso',
      silentCheckSsoRedirectUri: window.location.origin + '/silent-check-sso.html',
      pkceMethod: 'S256',
    });

    if (authenticated) {
      const { setUser, setToken, setPermissions } = useAuthStore.getState();

      setToken(keycloak.token!);
      setUser({
        id: keycloak.tokenParsed?.sub!,
        username: keycloak.tokenParsed?.preferred_username,
        email: keycloak.tokenParsed?.email,
        firstName: keycloak.tokenParsed?.given_name,
        lastName: keycloak.tokenParsed?.family_name,
      });

      // Fetch permissions từ API
      const permissionsResponse = await apiClient.get('/api/v1/permissions/check');
      setPermissions(permissionsResponse.data.permissions);
    }

    // Auto-refresh token (30s trước khi hết hạn)
    keycloak.onTokenExpired = () => {
      keycloak.updateToken(30).catch(() => {
        useAuthStore.getState().logout();
      });
    };

    return authenticated;
  } catch (error) {
    console.error('Keycloak initialization failed:', error);
    return false;
  }
};

export const login = () => keycloak.login();
export const logout = () => keycloak.logout();
export const getToken = () => keycloak.token;
```

#### 3.4.4 Axios Interceptor — Tự động attach token & xử lý lỗi

```typescript
// src/services/api-client.ts
import axios, { AxiosError, InternalAxiosRequestConfig } from 'axios';
import { getToken, logout } from './auth.service';
import { notification } from 'antd';

const apiClient = axios.create({
  baseURL: import.meta.env.VITE_API_URL || '/api/v1',
  timeout: 30000,
  headers: { 'Content-Type': 'application/json' },
});

// Request interceptor — attach JWT token
apiClient.interceptors.request.use(
  (config: InternalAxiosRequestConfig) => {
    const token = getToken();
    if (token && config.headers) {
      config.headers.Authorization = `Bearer ${token}`;
    }
    return config;
  },
  (error) => Promise.reject(error)
);

// Response interceptor — centralized error handling
apiClient.interceptors.response.use(
  (response) => response,
  (error: AxiosError) => {
    if (error.response?.status === 401) {
      // Token expired or invalid → trigger logout
      logout();
      window.location.href = '/login';
      return Promise.reject(error);
    }

    if (error.response?.status === 403) {
      notification.error({
        message: 'Access Denied',
        description: 'You do not have permission to perform this action.',
      });
      return Promise.reject(error);
    }

    if (error.response?.status === 429) {
      notification.warning({
        message: 'Rate Limited',
        description: 'Too many requests. Please try again later.',
      });
      return Promise.reject(error);
    }

    // Business logic errors (HTTP 200 với status "error")
    const data = error.response?.data as any;
    if (data?.status === 'error') {
      notification.error({
        message: 'Operation Failed',
        description: data.message || 'An unexpected error occurred.',
      });
    }

    return Promise.reject(error);
  }
);

export default apiClient;
```

#### 3.4.5 Component Structure — Permission-aware UI

```typescript
// src/components/common/PermissionButton.tsx
import { Button, ButtonProps } from 'antd';
import { useAuthStore } from '../../stores/auth.store';

interface PermissionButtonProps extends ButtonProps {
  permission: string;
  children: React.ReactNode;
}

export const PermissionButton: React.FC<PermissionButtonProps> = ({
  permission,
  children,
  ...buttonProps
}) => {
  const hasPermission = useAuthStore((state) => state.hasPermission);

  if (!hasPermission(permission)) {
    return null; // Ẩn hoàn toàn nếu không có quyền
  }

  return <Button {...buttonProps}>{children}</Button>;
};

// Usage
<PermissionButton
  permission="assets.checkout"
  type="primary"
  onClick={() => setCheckoutOpen(true)}
>
  Checkout
</PermissionButton>
```

---

## 4. Kế hoạch triển khai — Vertical Slices

### 4.0 Bảng tổng thời gian & Sơ đồ phụ thuộc

#### Phân bổ thời gian

| Phase | Tên | Thời lượng | Tuần | Phụ thuộc |
|-------|-----|-----------|------|-----------|
| **Phase 0** | Foundation & CI/CD | 2 tuần | 1-2 | — |
| **Phase 1** | Auth, SSO & Permission | 3 tuần | 3-5 | Phase 0 |
| **Phase 2** | Core Asset Management | 4 tuần | 6-9 | Phase 1 |
| **Phase 3** | Asset Lifecycle (Checkout/Checkin) | 4 tuần | 10-13 | Phase 2 |
| **Phase 4** | Consumables & Stock | 2 tuần | 14-15 | Phase 2 (có thể song song Phase 3) |
| **Phase 5** | Multi-tenant & Custom Fields | 3 tuần | 16-18 | Phase 2, Phase 4 |
| **Phase 6** | Dashboard, Reports, Import/Export | 2 tuần | 19-20 | Phase 3, Phase 4, Phase 5 |
| **Buffer** | Dự phòng + Hardening + Fix bugs | 6 tuần | 21-26 | Tất cả phases |
| **Tổng** | | **26 tuần** | 1-26 | |

#### Sơ đồ phụ thuộc — Mermaid Gantt Chart

```mermaid
gantt
    title AspireReact Migration Timeline (26 Weeks)
    dateFormat  YYYY-MM-DD
    axisFormat  Week %W

    section Foundation
    Phase 0: Foundation & CI/CD           :p0, 2026-08-10, 14d

    section Auth & Permission
    Phase 1: Auth, SSO & Permission       :p1, after p0, 21d

    section Asset Core
    Phase 2: Core Asset Management        :p2, after p1, 28d

    section Lifecycle
    Phase 3: Checkout/Checkin & Audit     :p3, after p2, 28d

    section Inventory
    Phase 4: Consumables & Stock          :p4, after p2, 14d

    section Multi-tenant
    Phase 5: FMCS & Custom Fields         :p5, after p2, 21d

    section Reports
    Phase 6: Dashboard & Reports          :p6, after p3, 14d

    section Buffer
    Buffer: Hardening & Bug Fixes         :buf, after p6, 42d
```

> **Ghi chú**: Phase 4 và Phase 5 có thể chạy song song một phần với Phase 3 vì không phụ thuộc trực tiếp vào Checkout/Checkin flow. Phase 6 phụ thuộc Phase 3 (action logs cho dashboard) và Phase 5 (FMCS cho reports).

---

Mỗi phase được triển khai theo **Vertical Slice Architecture**, bao gồm đồng thời: Entity → Service → API → React Component. Backend và Frontend phát triển song song trong mỗi phase.

### Phase 0: Foundation & CI/CD (Tuần 1-2)

**Mục tiêu**: Thiết lập hạ tầng phát triển, CI/CD, cấu trúc dự án cơ bản.

#### API Endpoints

| Method | Route | Mô tả |
|--------|-------|-------|
| `GET` | `/health` | Health check endpoint |
| `GET` | `/api/health/db` | Database connectivity check |
| `GET` | `/api/health/redis` | Redis connectivity check |

#### Entities cốt lõi

| Entity | Bảng |
|--------|------|
| `Company` | `companies` |
| `User` | `users` (basic: id, username, email từ Keycloak) |

#### React Components

| Component | File | Mô tả |
|-----------|------|-------|
| `AppLayout` | `components/layout/AppLayout.tsx` | Layout chính: Ant Design Layout + Sider + Header |
| `Sidebar` | `components/layout/Sidebar.tsx` | Menu điều hướng (Ant Design Menu) |
| `Header` | `components/layout/Header.tsx` | Header với user avatar, notifications |
| `LoadingSpinner` | `components/common/LoadingSpinner.tsx` | Global loading indicator |
| `EmptyState` | `components/common/EmptyState.tsx` | Hiển thị khi không có dữ liệu |
| `PageHeader` | `components/common/PageHeader.tsx` | Breadcrumb + tiêu đề trang |

#### Services

| Service | File | Mô tả |
|---------|------|-------|
| `api-client.ts` | `services/api-client.ts` | Axios instance với interceptors (auth token, error handling) |
| `auth.service.ts` | `services/auth.service.ts` | Keycloak JS adapter, token refresh |

#### Infrastructure

- Scaffold solution: AppHost + ServiceDefaults + Server + Frontend
- PostgreSQL container trong Aspire
- Redis container trong Aspire
- Keycloak container trong Aspire
- EF Core initial migration (chỉ Company, User, PermissionGroup)
- GitHub Actions CI/CD pipeline: build, test, lint
- Docker Compose cho local development fallback
- Shared NuGet package cho DTOs/Enums giữa Server và Frontend

#### Tiêu chí hoàn thành

- [ ] `aspire start` chạy thành công toàn bộ stack (AppHost + Server + Frontend + PostgreSQL + Redis + Keycloak)
- [ ] `/health` trả về 200 OK
- [ ] Frontend hiển thị layout Ant Design cơ bản với sidebar navigation
- [ ] CI/CD pipeline chạy build thành công
- [ ] ESLint + Prettier configured

---

### Phase 1: Auth, SSO (Keycloak) & Permission System (Tuần 3-5)

**Mục tiêu**: Xác thực qua Keycloak, phân quyền chi tiết, quản lý User/Group.

#### API Endpoints

| Method | Route | Auth | Mô tả |
|--------|-------|------|-------|
| `GET` | `/api/v1/users` | `users.view` | Danh sách user (filter, search, pagination) |
| `GET` | `/api/v1/users/{id}` | `users.view` | Chi tiết user |
| `POST` | `/api/v1/users` | `users.create` | Tạo user mới (sync từ Keycloak hoặc tạo local) |
| `PUT` | `/api/v1/users/{id}` | `users.edit` | Cập nhật user |
| `DELETE` | `/api/v1/users/{id}` | `users.delete` | Soft-delete user |
| `GET` | `/api/v1/groups` | `admin` | Danh sách permission groups |
| `POST` | `/api/v1/groups` | `admin` | Tạo group mới |
| `PUT` | `/api/v1/groups/{id}` | `admin` | Cập nhật group |
| `DELETE` | `/api/v1/groups/{id}` | `admin` | Xóa group |
| `GET` | `/api/v1/permissions/check` | (none) | Kiểm tra permission của user hiện tại |
| `GET` | `/api/v1/permissions/matrix` | `admin` | Ma trận phân quyền (tất cả user × permission) |
| `PUT` | `/api/v1/users/{id}/permissions` | `admin` | Cập nhật permission cá nhân |
| `PUT` | `/api/v1/groups/{id}/permissions` | `admin` | Cập nhật permission group |

#### Entities

| Entity | Mô tả |
|--------|-------|
| `User` | Mở rộng: `username`, `first_name`, `last_name`, `email`, `employee_number`, `jobtitle`, `location_id`, `department_id`, `company_id`, `is_superuser`, `is_active` |
| `PermissionGroup` | `name`, `description` |
| `UserPermission` | `user_id`, `permission_key`, `value` |
| `GroupPermission` | `group_id`, `permission_key`, `value` |
| `UserGroup` | `user_id`, `group_id` (pivot) |
| `UserCompany` | `user_id`, `company_id` (pivot) |

#### Services

| Service | Mô tả |
|---------|-------|
| `PermissionHandler` | Authorization Handler cho Policy-based Authorization |
| `CurrentUserService` | Resolve user hiện tại từ JWT claims |
| `KeycloakConfiguration` | Cấu hình OpenID Connect, token validation |

#### React Components

| Component | Mô tả |
|-----------|-------|
| `LoginPage` | Redirect đến Keycloak login (hoặc SSO button) |
| `ProtectedRoute` | Auth guard — redirect nếu chưa login |
| `PermissionButton` | Button tự động ẩn/hiện dựa trên permission |
| `UserListPage` | Bảng user với Ant Design Table |
| `UserFormPage` | Form tạo/sửa user (Ant Design Form) |
| `GroupListPage` | Danh sách permission groups |
| `GroupFormPage` | Form tạo/sửa group |
| `PermissionMatrix` | Ma trận phân quyền (Checkbox grid: User × Permission) |

#### Tiêu chí hoàn thành

- [ ] SSO với Keycloak hoạt động (login, logout, token refresh)
- [ ] Permission matrix hoạt động chính xác (Superuser → Admin → User Grant → User Deny → Group Grant → Default Deny)
- [ ] UI hiển thị/ẩn các nút dựa trên permission
- [ ] User CRUD đầy đủ

---

### Phase 2: Core Asset Management (Tuần 6-9)

**Mục tiêu**: CRUD Asset, Model, Category, Manufacturer, Supplier, Location, StatusLabel. TreeView cho Asset Parent-Child.

#### API Endpoints

| Method | Route | Auth | Mô tả |
|--------|-------|------|-------|
| `GET` | `/api/v1/assets` | `assets.view` | Danh sách assets (filter: status, location, category, model, search, sort, pagination) |
| `GET` | `/api/v1/assets/{id}` | `assets.view` | Chi tiết asset (kèm custom fields, action logs gần nhất) |
| `POST` | `/api/v1/assets` | `assets.create` | Tạo asset mới |
| `PATCH` | `/api/v1/assets/{id}` | `assets.edit` | Cập nhật asset |
| `DELETE` | `/api/v1/assets/{id}` | `assets.delete` | Soft-delete asset |
| `GET` | `/api/v1/assets/selectlist` | `view.selectlists` | Select2-style dropdown (search by name/asset_tag) |
| `GET` | `/api/v1/assets/tree` | `assets.view` | Cây phân cấp Parent-Child (recursive) |
| `GET` | `/api/v1/assets/{id}/children` | `assets.view` | Danh sách asset con trực tiếp |
| `GET` | `/api/v1/models` | `models.view` | Danh sách models |
| `POST` | `/api/v1/models` | `models.create` | Tạo model |
| `PUT` | `/api/v1/models/{id}` | `models.edit` | Cập nhật model |
| `DELETE` | `/api/v1/models/{id}` | `models.delete` | Xóa model |
| `GET` | `/api/v1/categories` | `categories.view` | Danh sách categories |
| `POST` | `/api/v1/categories` | `categories.create` | Tạo category |
| `PUT` | `/api/v1/categories/{id}` | `categories.edit` | Cập nhật category |
| `DELETE` | `/api/v1/categories/{id}` | `categories.delete` | Xóa category |
| `GET` | `/api/v1/manufacturers` | `manufacturers.view` | Danh sách manufacturers |
| `POST` | `/api/v1/manufacturers` | `manufacturers.create` | Tạo manufacturer |
| `PUT` | `/api/v1/manufacturers/{id}` | `manufacturers.edit` | Cập nhật manufacturer |
| `GET` | `/api/v1/suppliers` | `suppliers.view` | Danh sách suppliers |
| `POST` | `/api/v1/suppliers` | `suppliers.create` | Tạo supplier |
| `GET` | `/api/v1/locations` | `locations.view` | Danh sách locations (phân cấp) |
| `POST` | `/api/v1/locations` | `locations.create` | Tạo location |
| `PUT` | `/api/v1/locations/{id}` | `locations.edit` | Cập nhật location |
| `GET` | `/api/v1/statuslabels` | `statuslabels.view` | Danh sách status labels |
| `POST` | `/api/v1/statuslabels` | `statuslabels.create` | Tạo status label |

#### Entities

| Entity | Điểm đặc biệt |
|--------|-------------|
| `Asset` | `current_assignment_id` → `Assignment`; computed: `warrantyExpires`, `eolDate`, `age`; JSONB `custom_fields` |
| `Assignment` | Discriminator `target_type` → User/Location/Asset |
| `AssetModel` | `fieldset_id` → `CustomFieldset`; `depreciation_id` → `Depreciation` |
| `Category` | `category_type` enum: Asset/Consumable/Accessory/Component/License |
| `Manufacturer` | Standard CRUD |
| `Supplier` | Standard CRUD |
| `Location` | Self-referencing `parent_id` cho phân cấp |
| `StatusLabel` | `deployable`, `pending`, `archived` flags |

#### React Components

| Component | Mô tả |
|-----------|-------|
| `AssetListPage` | Bảng assets với filter, search, sort. Ant Design Table + server-side pagination |
| `AssetDetailPage` | Chi tiết asset: Descriptions + Timeline action logs + Children list |
| `AssetFormPage` | Form tạo/sửa asset (Ant Design Form + DynamicFormRenderer cho custom fields) |
| `AssetTreePage` | Split view: Tree bên trái (Ant Design Tree), Table bên phải |
| `AssetTree` | Ant Design Tree component hiển thị Parent-Child (load on expand) |
| `AssetTable` | Bảng assets với columns configurable |
| `ModelListPage` | CRUD models |
| `CategoryListPage` | CRUD categories |
| `LocationTreePage` | Quản lý locations dạng Tree |
| `ManufacturerListPage` | CRUD manufacturers |
| `SupplierListPage` | CRUD suppliers |
| `StatusLabelListPage` | CRUD status labels |

#### Tiêu chí hoàn thành

- [ ] Asset CRUD đầy đủ với validation
- [ ] Asset Tree hiển thị đúng phân cấp Parent-Child (Ant Design Tree)
- [ ] Tất cả FK trong API response là object `{id, name}`, không raw ID
- [ ] Filter/search/pagination hoạt động trên `GET /api/v1/assets`
- [ ] Model/Category/Manufacturer/Supplier/Location/StatusLabel CRUD đầy đủ

---

### Phase 3: Vòng đời Tài sản — Checkout/Checkin & Audit Trail (Tuần 10-13)

**Mục tiêu**: Checkout/Checkin với concurrency lock, Action Logs, bulk operations, EULA acceptance.

#### API Endpoints

| Method | Route | Auth | Mô tả |
|--------|-------|------|-------|
| `POST` | `/api/v1/assets/{id}/checkout` | `assets.checkout` | Checkout asset cho user/location/asset |
| `POST` | `/api/v1/assets/{id}/checkin` | `assets.checkin` | Checkin asset về kho |
| `POST` | `/api/v1/assets/bytag/{tag}/checkout` | `assets.checkout` | Checkout bằng asset tag |
| `POST` | `/api/v1/assets/bytag/{tag}/checkin` | `assets.checkin` | Checkin bằng asset tag |
| `POST` | `/api/v1/assets/{id}/audit` | `assets.audit` | Kiểm kê asset (cập nhật last_audit_date) |
| `POST` | `/api/v1/assets/bulk` | `assets.edit` | Cập nhật hàng loạt (status, location) |
| `POST` | `/api/v1/assets/audit/bulk` | `assets.audit` | Kiểm kê hàng loạt |
| `GET` | `/api/v1/assets/{id}/history` | `assets.view` | Lịch sử action log của asset |
| `POST` | `/api/v1/assets/{id}/accept` | (none) | User chấp nhận EULA |
| `POST` | `/api/v1/assets/{id}/decline` | (none) | User từ chối EULA |
| `GET` | `/api/v1/assets/due-audit` | `assets.audit` | Assets đến hạn kiểm kê |
| `GET` | `/api/v1/assets/due-checkin` | `assets.checkin` | Assets đến hạn checkin |

#### Entities

| Entity | Mô tả |
|--------|-------|
| `ActionLog` | `item_type` (enum), `item_id`, `target_type` (enum), `target_id`, `action_type` (enum), `created_by`, `location_id`, `company_id`, `note`, `log_meta` (JSONB), `action_date`, `remote_ip`, `user_agent`, `action_source` (enum) |
| `CheckoutAcceptance` | `checkoutable_type`, `checkoutable_id`, `user_id`, `accepted_at`, `declined_at`, `signature` |

#### Checkout Flow (chi tiết)

```
Controller::CheckoutAsync(assetId, request)
│
├── 1. Validate Request (FluentValidation)
│   ├── checkout_to_type: required, must be user|location|asset
│   ├── assigned_user/asset/location: required_without_all
│   ├── status_id (if provided): must be deployable
│   └── note: required nếu setting bắt buộc
│
├── 2. Authorization
│   └── [Authorize(Policy = "assets.checkout")]
│
├── 3. Business Rule Check (advisory)
│   ├── Asset tồn tại?
│   ├── Asset không bị archived?
│   ├── Asset status is deployable?
│   ├── Asset chưa được checkout? (CurrentAssignment == null)
│   └── Nếu không → 200 {"status":"error","message":"Asset not available"}
│
├── 4. Target Resolution (bypass Global Query Filter)
│   ├── User/Location/Asset::IgnoreQueryFilters().FindAsync(targetId)
│   ├── Target không tồn tại? → 200 error
│   └── Target soft-deleted? → 200 error
│
├── 5. FMCS Company Check
│   ├── Asset.CompanyId == Target.CompanyId? (nếu FMCS bật)
│   └── Không match → 200 error "company mismatch"
│
├── 6. Concurrency Guard (Database Transaction + Row Lock)
│   └── BEGIN TRANSACTION
│       ├── Asset tìm lại với SELECT ... FOR UPDATE
│       ├── Re-check available (authoritative)
│       ├── Nếu không còn available → ROLLBACK, return error
│       ├── Tạo Assignment record
│       ├── Cập nhật Asset: CurrentAssignmentId, location_id (cache)
│       ├── Cập nhật Asset: last_checkout, checkout_counter++
│       ├── Tạo ActionLog record
│       ├── COMMIT
│       └── Publish domain event: AssetCheckedOut
│
├── 7. Post-Commit (outbox/external)
│   ├── Gửi email notification (nếu category yêu cầu)
│   ├── Gửi webhook (nếu cấu hình)
│   └── SignalR real-time update (nếu có frontend đang xem)
│
└── Return 200 {"status":"success","message":"Asset checked out","data":{...}}
```

#### Services

| Service | Mô tả |
|---------|-------|
| `CheckoutService` | Xử lý logic checkout + concurrency lock |
| `CheckinService` | Xử lý logic checkin + sync location |
| `AuditService` | Kiểm kê tài sản |
| `ActionLogService` | Query và filter action logs |
| `BulkOperationService` | Cập nhật hàng loạt, kiểm kê hàng loạt |

#### React Components

| Component | Mô tả |
|-----------|-------|
| `CheckoutDialog` | Modal checkout: select target type → search target → date → note |
| `CheckinDialog` | Modal checkin: xác nhận + note + optional date |
| `ActionLogTimeline` | Ant Design Timeline hiển thị lịch sử thay đổi |
| `AuditPage` | Kiểm kê asset (scan QR/barcode hoặc manual) |
| `BulkUpdateDrawer` | Drawer cập nhật hàng loạt: chọn assets → set status/location |
| `DueBadge` | Ant Design Badge cảnh báo overdue checkin/audit |

#### Tiêu chí hoàn thành

- [ ] Checkout flow hoàn chỉnh với concurrency lock chống race condition
- [ ] Checkin flow hoàn chỉnh với cập nhật location denormalized
- [ ] Action log ghi đầy đủ mọi checkout/checkin/audit
- [ ] Bulk operations (update, audit) hoạt động
- [ ] UI Timeline hiển thị lịch sử rõ ràng
- [ ] Cảnh báo overdue checkin/audit

---

### Phase 4: Vật tư tiêu hao & Dynamic Stock Calculation (Tuần 14-15)

**Mục tiêu**: Quản lý Consumables, Components, Accessories với tính toán số lượng động và cảnh báo tồn kho.

#### API Endpoints

| Method | Route | Auth | Mô tả |
|--------|-------|------|-------|
| `GET` | `/api/v1/consumables` | `consumables.view` | Danh sách consumables (filter, search, sort) |
| `GET` | `/api/v1/consumables/{id}` | `consumables.view` | Chi tiết consumable |
| `POST` | `/api/v1/consumables` | `consumables.create` | Tạo consumable mới |
| `PUT` | `/api/v1/consumables/{id}` | `consumables.edit` | Cập nhật consumable |
| `DELETE` | `/api/v1/consumables/{id}` | `consumables.delete` | Xóa consumable |
| `POST` | `/api/v1/consumables/{id}/checkout` | `consumables.checkout` | Cấp phát consumable cho user |
| `GET` | `/api/v1/consumables/low-stock` | `consumables.view` | Danh sách vật tư dưới ngưỡng min_amt |
| `GET` | `/api/v1/consumables/{id}/checkouts` | `consumables.view` | Lịch sử cấp phát |
| `GET` | `/api/v1/components` | `components.view` | Danh sách components |
| `POST` | `/api/v1/components/{id}/assign` | `components.checkout` | Gán component vào asset |
| `GET` | `/api/v1/accessories` | `accessories.view` | Danh sách accessories |
| `POST` | `/api/v1/accessories/{id}/checkout` | `accessories.checkout` | Cấp phát accessory |
| `GET` | `/api/v1/accessories/low-stock` | `accessories.view` | Accessories dưới ngưỡng |
| `GET` | `/api/v1/licenses` | `licenses.view` | Danh sách licenses |
| `POST` | `/api/v1/licenses/{id}/checkout` | `licenses.checkout` | Cấp phát license seat |

#### React Components

| Component | Mô tả |
|-----------|-------|
| `ConsumableListPage` | Bảng consumables với cột Remaining/PercentRemaining |
| `ConsumableFormPage` | Form tạo/sửa consumable |
| `CheckoutConsumableDialog` | Modal cấp phát: select user + quantity |
| `StockAlertWidget` | Widget trên Dashboard: danh sách vật tư sắp hết |
| `StockAlertBadge` | Ant Design Badge hiển thị số lượng vật tư dưới ngưỡng |
| `ComponentListPage` | Quản lý components |
| `AssignComponentDialog` | Gán component vào asset |
| `AccessoryListPage` | Quản lý accessories |
| `LicenseListPage` | Quản lý licenses |

#### Tiêu chí hoàn thành

- [ ] Consumable CRUD + checkout với lock chống race condition
- [ ] Stock calculation chính xác (Remaining = Qty - CheckoutCount)
- [ ] Cảnh báo tồn kho hiển thị trên Dashboard và list page
- [ ] Component/Accessory/License CRUD cơ bản

---

### Phase 5: Multi-tenant (FMCS Scoping) & Custom Fields (Tuần 16-18)

**Mục tiêu**: Hoàn thiện multi-tenant với Global Query Filter và hệ thống Custom Fields JSONB.

#### API Endpoints

| Method | Route | Auth | Mô tả |
|--------|-------|------|-------|
| `GET` | `/api/v1/companies` | `companies.view` | Danh sách companies (phân cấp) |
| `POST` | `/api/v1/companies` | `companies.create` | Tạo company |
| `PUT` | `/api/v1/companies/{id}` | `companies.edit` | Cập nhật company |
| `GET` | `/api/v1/companies/{id}/users` | `companies.view` | Users thuộc company |
| `PUT` | `/api/v1/companies/{id}/users` | `companies.edit` | Gán user vào company |
| `GET` | `/api/v1/custom-fields` | `customfields.view` | Danh sách custom fields |
| `POST` | `/api/v1/custom-fields` | `customfields.create` | Tạo custom field mới (thêm key vào JSONB schema) |
| `PUT` | `/api/v1/custom-fields/{id}` | `customfields.edit` | Cập nhật custom field |
| `DELETE` | `/api/v1/custom-fields/{id}` | `customfields.delete` | Xóa custom field |
| `GET` | `/api/v1/custom-fieldsets` | `customfields.view` | Danh sách fieldsets |
| `POST` | `/api/v1/custom-fieldsets` | `customfields.create` | Tạo fieldset |
| `PUT` | `/api/v1/custom-fieldsets/{id}` | `customfields.edit` | Cập nhật fieldset (gán fields) |
| `PUT` | `/api/v1/models/{id}/fieldset` | `models.edit` | Gán fieldset vào model |

#### React Components

| Component | Mô tả |
|-----------|-------|
| `CompanyListPage` | Quản lý companies (cây phân cấp) |
| `CompanyUserAssignment` | Gán user vào company |
| `CustomFieldListPage` | Danh sách custom fields |
| `CustomFieldForm` | Form tạo/sửa custom field |
| `FieldsetBuilder` | Drag-and-drop builder để tạo fieldset từ các field |
| `DynamicFormRenderer` | Component render form động từ fieldset |
| `ModelFieldsetAssignment` | Gán fieldset vào model |

#### Tiêu chí hoàn thành

- [ ] FMCS Global Query Filter hoạt động chính xác (user chỉ thấy dữ liệu công ty mình)
- [ ] Superuser thấy toàn bộ dữ liệu bất kể company
- [ ] Floater mode hoạt động (null company_id = system-wide)
- [ ] Tạo custom field → lưu schema → hiển thị trong DynamicFormRenderer
- [ ] Validation custom field hoạt động (required, format, unique)
- [ ] Encrypted fields: encrypt khi lưu vào JSONB, decrypt khi đọc (với gate check)

---

### Phase 6: Dashboard, Reports, Import/Export (Tuần 19-20)

**Mục tiêu**: Trang tổng quan, báo cáo, xuất nhập dữ liệu.

#### API Endpoints

| Method | Route | Auth | Mô tả |
|--------|-------|------|-------|
| `GET` | `/api/v1/dashboard/summary` | (any) | Tổng quan: total assets, deployed, RTD, pending, overdue checkin, overdue audit, low stock count |
| `GET` | `/api/v1/dashboard/recent-activity` | (any) | 20 action logs gần nhất |
| `GET` | `/api/v1/dashboard/assets-by-status` | (any) | Số lượng asset theo từng status_label |
| `GET` | `/api/v1/dashboard/assets-by-category` | (any) | Số lượng asset theo category |
| `GET` | `/api/v1/dashboard/assets-by-location` | (any) | Số lượng asset theo location |
| `GET` | `/api/v1/dashboard/low-stock` | (any) | Danh sách consumables/accessories dưới ngưỡng min_amt |
| `GET` | `/api/v1/dashboard/monthly-checkout-trend` | (any) | Số lượng checkout theo tháng (12 tháng gần nhất) |
| `GET` | `/api/v1/reports/custom` | `reports.view` | Báo cáo tùy chỉnh (filter: date range, category, location, status, group by) |
| `GET` | `/api/v1/reports/depreciation` | `reports.view` | Báo cáo khấu hao: asset, purchase_cost, current_book_value, months_remaining |
| `GET` | `/api/v1/reports/audit` | `reports.view` | Báo cáo kiểm kê: assets đã audit, chưa audit, overdue audit |
| `GET` | `/api/v1/reports/checkout-history` | `reports.view` | Lịch sử checkout trong khoảng thời gian |
| `GET` | `/api/v1/reports/asset-by-age` | `reports.view` | Phân bố asset theo tuổi (<1 năm, 1-3 năm, 3-5 năm, >5 năm) |
| `POST` | `/api/v1/import/assets` | `import` | Import assets từ CSV/Excel (multipart upload) |
| `POST` | `/api/v1/import/consumables` | `import` | Import consumables từ CSV |
| `POST` | `/api/v1/import/users` | `import` | Import users từ CSV |
| `GET` | `/api/v1/import/templates/assets` | `import` | Tải template CSV cho import assets |
| `GET` | `/api/v1/export/assets` | `assets.view` | Export assets ra CSV/Excel |
| `GET` | `/api/v1/export/consumables` | `consumables.view` | Export consumables |
| `GET` | `/api/v1/export/action-logs` | `reports.view` | Export action logs |
| `POST` | `/api/v1/assets/labels` | `assets.view` | Generate QR/Barcode labels (trả về PDF) |

#### Entities mới

| Entity | Mô tả |
|--------|-------|
| `ImportJob` | Theo dõi trạng thái import: `file_name`, `import_type`, `status` (pending/processing/completed/failed), `total_rows`, `success_rows`, `error_rows`, `error_log` (JSONB), `created_by`, `created_at` |
| `ExportJob` | Theo dõi export lớn: `export_type`, `filters` (JSONB), `status`, `file_path`, `created_by`, `created_at` |

#### DTOs cho Dashboard

```csharp
public class DashboardSummaryDto
{
    public int TotalAssets { get; set; }
    public int DeployedAssets { get; set; }       // Có current_assignment
    public int RtdAssets { get; set; }             // Ready to Deploy
    public int PendingAssets { get; set; }
    public int ArchivedAssets { get; set; }
    public int OverdueCheckinCount { get; set; }   // expected_checkin < now
    public int OverdueAuditCount { get; set; }     // next_audit_date < now
    public int LowStockCount { get; set; }         // consumables + accessories
    public decimal TotalAssetValue { get; set; }   // SUM(purchase_cost)
    public decimal TotalDepreciatedValue { get; set; }
}

public class MonthlyCheckoutTrendDto
{
    public string Month { get; set; }              // "2026-01"
    public int CheckoutCount { get; set; }
    public int CheckinCount { get; set; }
}

public class ReportRequestDto
{
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public int? CategoryId { get; set; }
    public int? LocationId { get; set; }
    public int? StatusId { get; set; }
    public int? CompanyId { get; set; }
    public string? GroupBy { get; set; }            // "category", "location", "status", "model"
    public string? SortBy { get; set; }
    public string? Format { get; set; }             // "json", "csv", "xlsx"
}
```

#### Services

| Service | Mô tả |
|---------|-------|
| `DashboardService` | Aggregate queries cho dashboard widgets |
| `ReportService` | Dynamic report generation với filter/group by |
| `ImportService` | CSV/Excel parsing, validation, batch insert |
| `ExportService` | CSV/Excel generation với ClosedXML |
| `LabelService` | QR/Barcode generation (ZXing.NET hoặc QRCoder) |

#### Checkout Trend với PostgreSQL Window Functions

```sql
-- Monthly checkout trend
SELECT
    TO_CHAR(action_date, 'YYYY-MM') AS month,
    COUNT(*) FILTER (WHERE action_type = 1) AS checkout_count,  -- ActionType.Checkout
    COUNT(*) FILTER (WHERE action_type = 2) AS checkin_count    -- ActionType.Checkin
FROM action_logs
WHERE item_type = 1  -- ItemType.Asset
  AND action_date >= NOW() - INTERVAL '12 months'
GROUP BY TO_CHAR(action_date, 'YYYY-MM')
ORDER BY month;
```

#### Import Service Architecture

```csharp
public class ImportService
{
    private readonly AppDbContext _context;
    private readonly IValidator<ImportAssetDto> _validator;

    public async Task<ImportResult> ImportAssetsAsync(Stream fileStream, string fileName, int userId)
    {
        var job = new ImportJob
        {
            FileName = fileName,
            ImportType = "assets",
            Status = ImportStatus.Processing,
            CreatedBy = userId,
            CreatedAt = DateTime.UtcNow
        };
        _context.ImportJobs.Add(job);
        await _context.SaveChangesAsync();

        var result = new ImportResult();
        var errors = new List<ImportError>();

        using var reader = new StreamReader(fileStream);
        using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);

        var records = csv.GetRecords<ImportAssetDto>().ToList();
        job.TotalRows = records.Count;
        result.TotalRows = records.Count;

        var batch = new List<Asset>();
        foreach (var record in records)
        {
            var validationResult = await _validator.ValidateAsync(record);
            if (!validationResult.IsValid)
            {
                errors.Add(new ImportError
                {
                    Row = result.SuccessRows + errors.Count + 1,
                    Message = string.Join("; ", validationResult.Errors.Select(e => e.ErrorMessage))
                });
                continue;
            }

            batch.Add(new Asset
            {
                AssetTag = record.AssetTag,
                Name = record.Name,
                Serial = record.Serial,
                ModelId = record.ModelId,
                StatusId = record.StatusId,
                LocationId = record.LocationId,
                CompanyId = record.CompanyId,
                PurchaseCost = record.PurchaseCost,
                PurchaseDate = record.PurchaseDate,
                CreatedAt = DateTime.UtcNow,
            });

            if (batch.Count >= 100) // Batch insert 100 records
            {
                _context.Assets.AddRange(batch);
                await _context.SaveChangesAsync();
                result.SuccessRows += batch.Count;
                batch.Clear();
            }
        }

        if (batch.Any())
        {
            _context.Assets.AddRange(batch);
            await _context.SaveChangesAsync();
            result.SuccessRows += batch.Count;
        }

        job.Status = errors.Any() ? ImportStatus.CompletedWithErrors : ImportStatus.Completed;
        job.SuccessRows = result.SuccessRows;
        job.ErrorRows = errors.Count;
        job.ErrorLog = JsonSerializer.Serialize(errors);
        await _context.SaveChangesAsync();

        result.Errors = errors;
        return result;
    }
}
```

#### React Components

| Component | Mô tả |
|-----------|-------|
| `DashboardPage` | Tổng quan: Ant Design Statistic cards + Charts (Recharts) |
| `SummaryCard` | Card thống kê: total assets, deployed, RTD với Ant Design Statistic |
| `AssetByStatusChart` | Pie/Donut chart assets theo status |
| `AssetByCategoryChart` | Bar chart assets theo category |
| `AssetByLocationChart` | Horizontal bar chart assets theo location |
| `MonthlyCheckoutTrendChart` | Line chart checkout/checkin theo tháng |
| `RecentActivityWidget` | Timeline 20 hành động gần nhất |
| `LowStockAlert` | Danh sách vật tư sắp hết với progress bar |
| `ReportsPage` | Báo cáo tùy chỉnh: filter form + table kết quả + export button |
| `ReportFilterForm` | Form filter báo cáo: date range picker, category/location/status select, group by |
| `ImportWizard` | Multi-step wizard: upload file → preview & map columns → validate → import |
| `ImportPage` | Trang import với history các lần import trước |
| `ExportButton` | Nút export với format selector (CSV/Excel) |
| `LabelGenerator` | Tạo và in nhãn QR/Barcode (chọn assets → preview → in PDF) |

#### Tiêu chí hoàn thành

- [ ] Dashboard hiển thị số liệu tổng quan chính xác (refresh real-time hoặc theo interval)
- [ ] Tất cả biểu đồ hiển thị đúng dữ liệu theo company scope (FMCS)
- [ ] Báo cáo tùy chỉnh với filter/group by hoạt động
- [ ] Import CSV/Excel với validation, error reporting, và batch insert
- [ ] Export danh sách ra CSV/Excel với ClosedXML
- [ ] QR code generation cho asset labels
- [ ] Export report dạng PDF (tuỳ chọn)

---

## 5. Rủi ro & Giảm thiểu

| # | Rủi ro | Mức độ | Tác động | Giảm thiểu |
|---|--------|--------|---------|-----------|
| 1 | **Race condition khi concurrent checkout** | **Rất cao** | Hai admin checkout cùng một asset → cả hai thành công → mất dữ liệu | PostgreSQL `SELECT ... FOR UPDATE` row lock trong transaction. Advisory check → lock → authoritative re-check. Integration test với concurrent requests (Task.WhenAll). |
| 2 | **Performance đệ quy Parent-Child** (N+1 queries) | **Cao** | Asset tree >1000 nodes gây timeout | Recursive CTE trong PostgreSQL. Cache `location_id` denormalized. Asset Tree API trả về toàn bộ cây (depth=full) hoặc lazy-load từng cấp. Index trên `assignments.target_type, target_id`. |
| 3 | **JSON Permissions migration sai** | **Cao** | User mất quyền hoặc thừa quyền sau migration | Script chuyển đổi tự động: JSON key → record. Audit mapping trước khi chạy production. Dry-run mode. Rollback plan. |
| 4 | **ALTER TABLE trong production** (Custom Fields) | **Cao** | Lock table, downtime | **Giải pháp JSONB cho MVP** — không cần ALTER TABLE. Khi migrate sang native columns, dùng application lock + thực hiện trong maintenance window. |
| 5 | **Data integrity khi migration MySQL → PostgreSQL** | **Trung bình** | Dữ liệu lệch, mất quan hệ | Dùng `pgloader`. Verify checksum (COUNT, SUM) trước/sau migration. Chạy thử trên staging trước. |
| 6 | **FMCS bỏ sót dữ liệu** | **Trung bình** | User thấy dữ liệu công ty khác (security breach) | Integration test: tạo user thuộc company A, verify không thấy dữ liệu company B. Audit Global Query Filter hoạt động trên tất cả entity `ICompanyable`. |
| 7 | **Lỗi mapping Polymorphic → Assignment** | **Trung bình** | Asset mất quan hệ checkout/Parent-Child | Script ETL map `assigned_to` + `assigned_type` → bảng `Assignments`. Verify số lượng assignment sau migration = số lượng asset có `assigned_to IS NOT NULL`. |
| 8 | **Keycloak downtime** | **Thấp** | User không đăng nhập được | Keycloak cluster (nếu production). Token cache phía Server. Fallback: local JWT validation khi Keycloak tạm thời không reachable. |
| 9 | **Hiệu năng Ant Design Table với >10,000 rows** | **Thấp** | UI lag, scroll chậm | Server-side pagination, filter, sort. Virtual scrolling (Ant Design Table `virtual` prop). Chỉ render columns cần thiết. |
| 10 | **Stock calculation sai khi checkout đồng thời** | **Trung bình** | Cấp phát vượt tồn kho | Cùng cơ chế lock như asset checkout (SELECT FOR UPDATE). Check `numRemaining() >= checkout_qty` sau lock. |

---

## 6. Chiến lược di trú dữ liệu

### 6.1 Công cụ & Phương pháp

| Bước | Công cụ | Mô tả |
|------|---------|-------|
| 1. Export schema | `pgloader` | Chuyển đổi MySQL schema → PostgreSQL, tự động map data types |
| 2. Bulk data migration | `pgloader` | Copy toàn bộ data từ MySQL sang PostgreSQL tables tạm |
| 3. Transform data | Custom .NET Console App (hoặc SQL scripts) | Map schema cũ → schema mới (polymorphic → assignments, JSON permissions → claims) |
| 4. Verify | SQL checksum scripts | COUNT(*), SUM(qty) comparison |
| 5. Cutover | Blue-Green deployment | Chạy song song 2 hệ thống 1 tuần, verify trước khi switch DNS |

### 6.2 Schema Mapping chi tiết

| MySQL (Snipe-IT) | PostgreSQL (AspireReact) | Ghi chú |
|------------------|--------------------------|---------|
| `models` | `models` | Direct mapping |
| `categories` | `categories` | Thêm enum `category_type` |
| `manufacturers` | `manufacturers` | Direct mapping |
| `suppliers` | `suppliers` | Direct mapping |
| `locations` | `locations` | Direct mapping |
| `status_labels` | `status_labels` | Direct mapping |
| `depreciations` | `depreciations` | Direct mapping |
| `departments` | `departments` | Direct mapping |
| `companies` | `companies` | Direct mapping |
| `custom_fields` | `custom_fields` | Direct mapping (metadata) |
| `custom_fieldsets` | `custom_fieldsets` | Direct mapping |
| `asset_maintenances` | `asset_maintenances` | Direct mapping |
| `users` | `users` + `user_permissions` | Tách cột `permissions` JSON → bảng `user_permissions` |
| `permission_groups` | `permission_groups` + `group_permissions` | Tách cột `permissions` JSON |
| `assets` | `assets` + `assignments` | Tách `assigned_to`/`assigned_type` → bảng `assignments`. Custom field values → `custom_fields` JSONB |
| `action_logs` | `action_logs` | Map `item_type`/`target_type`/`action_type` string → enum. `log_meta` TEXT → JSONB |
| `consumables_users` | `consumable_checkouts` | Đổi tên bảng, thêm cột `quantity` |
| `components_assets` | `component_assignments` | Đổi tên bảng |
| `accessories_checkout` | `accessory_checkouts` | Map polymorphic → discriminator enum |

### 6.3 Quy trình migration

```
Week -2: Setup môi trường staging (PostgreSQL) + chạy migration thử
Week -1: Verify dữ liệu trên staging (checksum, business rule check)
Day 0 (Cutover):
  08:00 - Thông báo bảo trì
  09:00 - Dừng Snipe-IT (read-only mode)
  09:30 - Export MySQL dump cuối cùng
  10:00 - Chạy pgloader (bulk copy) — ~30 phút cho 1M records
  10:30 - Chạy transform scripts
  11:00 - Verify checksum
  11:30 - Switch DNS → hệ thống mới
  12:00 - Smoke test (login, xem asset, checkout)
  13:00 - Go live
  14:00 - Monitoring (2h đầu)
  Day 1-7: Hỗ trợ + fix bugs
```

---

## 7. Tiêu chí nghiệm thu

### 7.1 Unit Testing

| Tiêu chí | Mục tiêu | Công cụ |
|----------|---------|---------|
| Coverage Domain layer | ≥ 90% | xUnit + Moq/NSubstitute |
| Coverage Application layer | ≥ 85% | xUnit + Moq |
| Coverage Infrastructure | ≥ 70% | Testcontainers (PostgreSQL, Redis) |
| Permission Handler | 100% test cases (all 6 priority paths) | xUnit |
| Stock Calculation | 100% test cases (boundary: 0, negative, overflow) | xUnit |
| Checkout/Checkin Logic | All edge cases (concurrent, already checked out, soft-deleted target) | xUnit + Task.WhenAll |
| Custom Field Validation | All format types + required/unique checks | xUnit |
| Import Validation | All error cases (missing required, invalid format, duplicate) | xUnit |

### 7.2 API Integration Testing

| Tiêu chí | Mô tả |
|----------|-------|
| Tất cả endpoints có test | Mỗi endpoint ít nhất: success case, validation fail, unauthorized, not found |
| Concurrency test | Gửi 10 concurrent checkout requests, chỉ 1 thành công |
| Performance test | GET /api/v1/assets (10,000 records) < 2s |
| FMCS isolation test | User company A không thấy dữ liệu company B |
| Token refresh flow | Access token hết hạn → refresh → retry thành công |

### 7.3 UI/UX (Ant Design)

| Tiêu chí | Mô tả |
|----------|-------|
| Design consistency | Tuân thủ Ant Design Design Language (spacing, color, typography, border-radius) |
| Responsive | Layout hoạt động trên Desktop (1920px, 1366px) và Tablet (1024px) |
| Accessibility | WCAG 2.1 AA: contrast ratio ≥ 4.5:1, keyboard navigation, ARIA labels |
| Loading states | Tất cả data fetch có Skeleton/Spinner loading |
| Error states | Hiển thị lỗi rõ ràng (Alert, Message, notification) |
| Empty states | Hiển thị Empty component khi không có dữ liệu |
| Form validation | Real-time validation với error message tiếng Việt |
| Table | Server-side sort, filter, pagination. Responsive columns. |
| Tree | Lazy-load children. Expand/collapse animation. |

### 7.4 Hiệu năng

| Chỉ số | Mục tiêu | Cách đo |
|--------|---------|---------|
| Asset list API (10,000 records) | < 2 giây | K6 load test |
| Asset detail API | < 500ms | K6 + Application Insights |
| Asset Tree (1000 nodes) | < 1 giây | Manual test + tracing |
| Checkout operation | < 1 giây (bao gồm lock) | K6 |
| Dashboard page load | < 3 giây (LCP) | Lighthouse |
| Frontend bundle size | < 500KB (gzipped) | Vite build analysis |
| DB query time (top 10 queries) | < 100ms | PostgreSQL `pg_stat_statements` |
| JSONB GIN index query | < 200ms cho 100K records | K6 |

### 7.5 Bảo mật

| Tiêu chí | Mô tả |
|----------|-------|
| OWASP Top 10 scan | Không critical/high vulnerabilities |
| Authentication | Tất cả API endpoints (trừ health) yêu cầu JWT Bearer token |
| Authorization | Mọi endpoint kiểm tra Policy-based permission |
| FMCS isolation | User không thể truy cập dữ liệu công ty khác qua API |
| Data encryption | Custom field encrypted values dùng AES-256 |
| SQL injection | Sử dụng parameterized queries (EF Core / Npgsql) |
| XSS prevention | React auto-escape + Content-Security-Policy header |
| Rate limiting | API rate limit: 60 req/min cho user thường, 300 req/min cho admin |
| Audit trail | Mọi hành động checkout/checkin/update/delete đều ghi vào `action_logs` |

### 7.6 Deployment & Operations

| Tiêu chí | Mô tả |
|----------|-------|
| Container health check | Tất cả containers phải pass health check trước khi nhận traffic |
| Zero-downtime deployment | Rolling update với Kubernetes/Docker Swarm, hoặc Blue-Green deployment |
| Graceful shutdown | ASP.NET Core xử lý hết request đang chạy trước khi shutdown (30s timeout) |
| Database migration | EF Core migration chạy tự động khi deploy, rollback nếu fail |
| Log aggregation | Tất cả logs được collect về OpenTelemetry → Grafana Loki |
| Alerting | Grafana alert khi: error rate > 1%, response time > 2s, DB connection pool exhausted |
| Backup | PostgreSQL automated backup mỗi 6h, retention 30 ngày |
| Disaster recovery | RPO < 6h, RTO < 4h |

### 7.7 Change Management

| Tiêu chí | Mô tả |
|----------|-------|
| Tài liệu đầy đủ | API Docs (Scalar UI), DB Schema Diagram, Deployment Guide, User Manual, Admin Guide |
| Training hoàn tất | 100% admin được training; 80% end-user được training trước go-live |
| Rollback plan | Quy trình rollback về Snipe-IT cũ trong vòng 2h nếu có critical issue |
| User acceptance test | 10 power users test toàn bộ flow trước go-live 1 tuần |
| Support plan | Đội hỗ trợ 24/7 trong tuần đầu go-live |

### 7.8 Tài liệu

| Tài liệu | Mô tả |
|----------|-------|
| API Documentation | Swagger/OpenAPI (Scalar UI) — tất cả endpoints có mô tả, example request/response |
| Database Schema Diagram | ERD export từ PostgreSQL |
| Deployment Guide | Hướng dẫn deploy với Aspire + Docker |
| User Manual | Hướng dẫn sử dụng cho end-user (tiếng Việt) |
| Admin Guide | Hướng dẫn quản trị hệ thống |

---

## 8. Triển khai & Vận hành (DevOps)

### 8.1 Cấu trúc môi trường

| Môi trường | Mục đích | Infrastructure | Database | Cập nhật |
|------------|---------|---------------|----------|----------|
| **Development** | Phát triển local | .NET Aspire local orchestration | PostgreSQL container, auto-seeded | Mỗi lần `aspire start` |
| **Staging** | Test tích hợp, UAT | Docker Compose / Kubernetes cluster nhỏ | PostgreSQL managed (nhỏ), data anonymized từ production | Mỗi merge vào `main` branch |
| **Production** | Live system | Kubernetes (AKS/EKS/GKE) hoặc Docker Swarm | PostgreSQL managed (HA, auto-backup) | Release approved, scheduled deployment |

### 8.2 CI/CD Pipeline (GitHub Actions)

```yaml
# .github/workflows/ci-cd.yml
name: CI/CD Pipeline

on:
  push:
    branches: [main, develop]
  pull_request:
    branches: [main]

env:
  DOTNET_VERSION: '9.0.x'
  NODE_VERSION: '22.x'

jobs:
  build-and-test:
    runs-on: ubuntu-latest
    services:
      postgres:
        image: postgres:16
        env:
          POSTGRES_PASSWORD: testpass
        ports:
          - 5432:5432
        options: >-
          --health-cmd pg_isready
          --health-interval 10s
          --health-timeout 5s
          --health-retries 5

    steps:
      - uses: actions/checkout@v4

      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: ${{ env.DOTNET_VERSION }}

      - name: Setup Node
        uses: actions/setup-node@v4
        with:
          node-version: ${{ env.NODE_VERSION }}

      # Backend
      - name: Restore .NET dependencies
        run: dotnet restore aspire-react/aspire-react.sln

      - name: Build .NET
        run: dotnet build aspire-react/aspire-react.sln --configuration Release --no-restore

      - name: Run .NET Tests
        run: dotnet test aspire-react/aspire-react.sln --configuration Release --no-build --collect:"XPlat Code Coverage"

      # Frontend
      - name: Install Frontend Dependencies
        run: npm ci
        working-directory: aspire-react/frontend

      - name: Lint Frontend
        run: npm run lint
        working-directory: aspire-react/frontend

      - name: Build Frontend
        run: npm run build
        working-directory: aspire-react/frontend

      # Security scan
      - name: OWASP Dependency Check
        run: dotnet list aspire-react/aspire-react.sln package --vulnerable

  deploy-staging:
    needs: build-and-test
    if: github.ref == 'refs/heads/main'
    runs-on: ubuntu-latest
    steps:
      - name: Deploy to Staging
        run: |
          # Docker build + push + deploy to staging
          echo "Deploying to staging..."

  deploy-production:
    needs: deploy-staging
    if: github.ref == 'refs/heads/main' && github.event_name == 'push' && contains(github.event.head_commit.message, '[deploy]')
    runs-on: ubuntu-latest
    environment: production
    steps:
      - name: Deploy to Production
        run: |
          # Docker build + push + rolling update
          echo "Deploying to production..."
```

### 8.3 Monitoring & Observability

#### Stack giám sát

```
┌─────────────────────────────────────────────────────────────────────┐
│                        Monitoring Stack                              │
├─────────────────────────────────────────────────────────────────────┤
│                                                                     │
│  Application (ASP.NET Core + React)                                 │
│       │                                                             │
│       ├──► OpenTelemetry SDK (traces, metrics, logs)                │
│       │        │                                                    │
│       │        └──► OpenTelemetry Collector                         │
│       │                 │                                           │
│       │                 ├──► Grafana Tempo (Traces)                 │
│       │                 ├──► Grafana Mimir / Prometheus (Metrics)   │
│       │                 └──► Grafana Loki (Logs)                    │
│       │                                                             │
│       └──► Health Checks (/health, /health/db, /health/redis)       │
│                                                                     │
│  Infrastructure                                                     │
│       │                                                             │
│       ├──► PostgreSQL Exporter → Prometheus                         │
│       ├──► Redis Exporter → Prometheus                              │
│       └──► Keycloak Metrics → Prometheus                            │
│                                                                     │
│  Visualization: Grafana Dashboards                                  │
│       ├──► ASP.NET Core Dashboard (requests/sec, latency, errors)   │
│       ├──► PostgreSQL Dashboard (connections, query time, locks)    │
│       ├──► Business Dashboard (checkout rate, inventory alerts)     │
│       └──► SLO Dashboard (uptime, error budget)                     │
│                                                                     │
│  Alerting: Grafana Alertmanager                                     │
│       ├──► Error rate > 1% in 5 min → PagerDuty/Slack              │
│       ├──► P95 latency > 2s → Slack                                 │
│       ├──► DB connection pool > 80% → Slack                        │
│       └──► Certificate expiry < 30 days → Email                     │
│                                                                     │
└─────────────────────────────────────────────────────────────────────┘
```

#### Cấu hình OpenTelemetry trong .NET

```csharp
// Program.cs
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using OpenTelemetry.Metrics;
using OpenTelemetry.Logs;

builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("aspire-react-server"))
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddEntityFrameworkCoreInstrumentation()
        .AddNpgsql()
        .AddRedisInstrumentation()
        .AddHttpClientInstrumentation()
        .AddOtlpExporter())
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()
        .AddRuntimeInstrumentation()
        .AddProcessInstrumentation()
        .AddOtlpExporter());

builder.Logging.AddOpenTelemetry(logging => logging.AddOtlpExporter());
```

### 8.4 Chiến lược Backup & Restore

| Thành phần | Tần suất | Retention | Công cụ |
|------------|---------|-----------|---------|
| PostgreSQL (Full backup) | Mỗi 6 giờ | 30 ngày | `pg_dump` + WAL archiving |
| PostgreSQL (WAL) | Continuous | 7 ngày | PostgreSQL WAL-G |
| File uploads (images, labels) | Mỗi 24 giờ | 90 ngày | Object storage versioning (S3/Azure Blob) |
| Configuration (appsettings) | Mỗi lần deploy | Vĩnh viễn | Git + Infrastructure as Code |

#### Disaster Recovery Procedure

```
1. Phát hiện sự cố (alert từ Grafana hoặc user report)
2. Xác định nguyên nhân:
   a. Database corrupted → Restore from latest backup + WAL replay
   b. Application bug → Rollback deployment
   c. Infrastructure failure → Failover to standby region
3. Restore:
   - RPO (Recovery Point Objective): < 6 giờ
   - RTO (Recovery Time Objective): < 4 giờ
4. Verify: smoke test toàn bộ flow chính
5. Communication: thông báo cho stakeholders
```

### 8.5 Quy trình chạy song song & Chuyển đổi

#### Tuần -2 đến Tuần 0: Go-Live Preparation

```
Week -2: ──── Staging environment ready ────
           ├── Database migrated (anonymized production data)
           ├── Smoke test all flows
           └── Performance test (K6 load test)

Week -1: ──── User Acceptance Testing ────
           ├── 10 power users test staging
           ├── Bug fixes (critical only)
           ├── Training sessions (2 sessions/day)
           └── Final data migration dry-run

Day -1: ──── Pre-Cutover ────
           ├── Final backup of MySQL
           ├── Communication: email to all users
           └── Rollback plan printed + distributed to team

Day 0: ──── CUTOVER ────
  08:00   ├── Maintenance mode ON (Snipe-IT read-only)
  09:00   ├── Final MySQL dump
  10:00   ├── pgloader bulk migration
  10:30   ├── Transform scripts
  11:00   ├── Data verification (checksum)
  11:30   ├── DNS switch to new system
  12:00   ├── Smoke test
  13:00   ├── GO LIVE
  14:00   ├── Intensive monitoring (2h)
  16:00   └── Stand down (if stable)
```

#### Tuần 1-2 Sau Go-Live: Hyper-care

```
- Daily standup: review issues, prioritize fixes
- Hotfix process: critical bugs → fix → deploy same day
- Support: dedicated Slack channel + hotline
- Performance monitoring: compare response times vs baseline
- Daily backup verification
```

#### Song song & Rollback

Trong 2 tuần đầu, Snipe-IT được giữ ở chế độ read-only. Nếu có critical issue:

1. **Rollback trigger**: P0 bug (data loss, security breach, system unavailable >30 phút)
2. **Rollback process**: Switch DNS về Snipe-IT (vẫn đang chạy read-only trong 2 tuần) → bật write mode → thông báo user
3. **Time to rollback**: < 30 phút
4. **Data sync**: Sau rollback, sync dữ liệu mới từ AspireReact về Snipe-IT (nếu có)

---

> **Kết thúc Migration Plan v1.1.**
>
> **Các cập nhật chính từ v1.0:**
> - Hoàn thiện Phase 6 với 20 API endpoints, DTOs, services, và Import architecture
> - Thêm Bảng tổng thời gian 26 tuần + Mermaid Gantt chart
> - Thêm Mục 3.1.4 Chuẩn hóa API Response (5 format: Success, Error, Validation, Pagination, Descriptive Names)
> - Thêm Mục 3.3.6 Giải pháp lai Custom Fields: phân tích EAV vs JSONB vs ALTER TABLE + khuyến nghị JSONB cho MVP
> - Thêm Mục 3.4 Frontend Architecture (Router, Zustand, Keycloak, Axios interceptor, PermissionButton)
> - Thêm Mục 8 DevOps (Environments, CI/CD, Monitoring, Backup/Restore, Cutover procedure)
> - Cập nhật Mục 7 Tiêu chí nghiệm thu (thêm Deployment, Change Management, Training)
> - Rà soát toàn bộ code C#, SQL, TypeScript
>
> **Tài liệu tham khảo:**
> - `docs/giai-doan-1-data-va-business-logic-loi.md` — Data & Business Logic
> - `docs/giai-doan-2-api-va-validation.md` — API & Validation
> - `docs/giai-doan-3-security-va-dynamic-fields.md` — Security & Dynamic Fields