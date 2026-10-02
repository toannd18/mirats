# AspireReact Architecture

## Clean Architecture Overview

```
┌─────────────────────────────────────────────────────────┐
│                      Web Layer                           │
│  Controllers (REST API), Middleware, Filters             │
├─────────────────────────────────────────────────────────┤
│                    Application Layer                     │
│  Commands, Queries, Handlers, Validators, DTOs           │
├─────────────────────────────────────────────────────────┤
│                      Domain Layer                        │
│  Entities, Enums, Value Objects, Interfaces              │
├─────────────────────────────────────────────────────────┤
│                  Infrastructure Layer                    │
│  Persistence (EF Core, PostgreSQL), Auth, Services       │
└─────────────────────────────────────────────────────────┘
```

## Technology Stack

| Layer | Technology |
|-------|-----------|
| Orchestration | .NET Aspire 13.4 |
| API Framework | ASP.NET Core 9 Web API |
| CQRS | MediatR 14 |
| Validation | FluentValidation 12 |
| ORM | Entity Framework Core 9 / Npgsql |
| Auth | Local auth — JWT tự ký HS256 (`TokenService`, 15 phút) + passkey WebAuthn tùy chọn; refresh token 7 ngày trong cookie httpOnly (Keycloak đã xóa hoàn toàn ở AUTH Phase 5) |
| Frontend | React 19 + TypeScript + Vite + Ant Design 6 |
| Cache | Redis 7 (StackExchange.Redis) |

## Component Diagram

```
Browser (localhost:5173)
  │
  ├── POST /api/v1/auth/login (username + password) ──► JWT tự ký HS256 + refresh cookie httpOnly
  │   hoặc POST /api/v1/auth/passkeys/login (WebAuthn, tùy chọn)
  │
  └── HTTP ──── Vite Dev Server
                    │
                    │ /api/* proxy or direct
                    ▼
              ASP.NET Core API (localhost:5428 / 7314)
                    │
          ┌─────────┼──────────┐
          ▼         ▼          ▼
     PostgreSQL    Redis    (không còn IdP ngoài —
     (5432)       (6379)    auth local trong API)
```

## Request Flow

```
1. User → POST /api/v1/auth/login (username + password) hoặc /auth/passkeys/login (WebAuthn) → JWT tự ký HS256 (15 phút) + refresh token 7 ngày (cookie httpOnly)
2. Frontend → Backend API with Authorization: Bearer <JWT>
3. Middleware: JwtBearerHandler validates self-signed token (scheme "App")
4. PasswordChangeGateMiddleware: session mang pwd_change=1 → 403 MUST_CHANGE_PASSWORD (trừ /users/me + /auth/password)
5. PermissionHandler checks policies (40+ policies)
6. Controller → MediatR Command/Query → Handler
7. Handler → EF Core → PostgreSQL
8. Response → JSON { status, data, pagination }
```

Refresh: 401 → `POST /api/v1/auth/refresh` (cookie httpOnly, rotation + reuse-detection) → access token mới.

## Key Design Patterns

### CQRS (MediatR)
- Commands: CheckoutAsset, CheckinAsset, CreateAsset, etc.
- Queries: GetAssets, GetDueAssets, etc.
- Handlers: Single responsibility per use case

### Permission Resolution Chain
```
Superuser (realm_access) → Always Grant
Admin (realm_access)     → Always Grant
Local User IsSuperUser   → Always Grant
UserPermission.Deny      → Fail
UserPermission.Grant     → Succeed
GroupPermission.Grant    → Succeed
Default                  → Deny
```

> `realm_access`/`permission` không còn do Keycloak phát hành: `TokenService` tự gắn hai claim này
> khi user local có `IsSuperUser=true` (giữ nguyên tên claim cũ để `PermissionHandler` +
> `isSuperUser()` phía frontend đọc được không đổi). Nguồn sự thật của quyền là claim `local_user_id`
> → `Users.IsSuperUser` + `UserPermission`/`GroupPermission`.

### Concurrency Lock (Asset Checkout)
```csharp
BEGIN TRANSACTION;
SELECT * FROM assets WHERE id = @id FOR UPDATE;
// Re-check availability
// Create Assignment
// Update Asset
// Create ActionLog
COMMIT;
```

### FMCS Multi-tenant
Global Query Filter in AppDbContext:
```csharp
modelBuilder.Entity<Asset>().HasQueryFilter(a =>
    _companyScope.IsSuperUser() ||
    a.CompanyId == null ||  // Floater
    userCompanyIds.Contains(a.CompanyId.Value));
```

### Dynamic Stock Calculation
```csharp
// LINQ projection in query
Remaining = item.Qty - item.Checkouts.Sum(c => c.Quantity)
IsLowStock = Remaining <= item.MinAmt
```

## Database Schema (13+ tables)

- `companies`, `users`, `permission_groups`, `user_permissions`, `group_permissions`, `user_groups`
- `assets`, `models`, `categories`, `manufacturers`, `suppliers`, `locations`, `depreciations` (`status_labels` đã xóa 2026-09-02 — dead feature, xem BACKLOG)
- `assignments`, `action_logs`
- `consumables`, `consumable_checkouts`, `components`, `component_assignments`, `accessories`, `accessory_checkouts`
- `licenses`, `license_seats`
- `custom_fields`, `custom_fieldsets`, `custom_field_fieldsets`

All primary keys use UUID with `gen_random_uuid()` PostgreSQL function.