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
| Auth | Keycloak 26 OIDC + JWT Bearer |
| Frontend | React 19 + TypeScript + Vite + Ant Design 6 |
| Cache | Redis 7 (StackExchange.Redis) |

## Component Diagram

```
Browser (localhost:5173)
  │
  ├── HTTPS ─── Keycloak (localhost:8080) ── JWT Token
  │
  └── HTTP ──── Vite Dev Server
                    │
                    │ /api/* proxy or direct
                    ▼
              ASP.NET Core API (localhost:5428 / 7314)
                    │
          ┌─────────┼──────────┐
          ▼         ▼          ▼
     PostgreSQL    Redis    Keycloak
     (5432)       (6379)   (8080)
```

## Request Flow

```
1. User → Keycloak Login → JWT Token
2. Frontend → Backend API with Authorization: Bearer <JWT>
3. Middleware: JwtBearerHandler validates token
4. PermissionHandler checks policies (40+ policies)
5. Controller → MediatR Command/Query → Handler
6. Handler → EF Core → PostgreSQL
7. Response → JSON { status, data, pagination }
```

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