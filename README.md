# AspireReact — Hệ thống Quản lý Tài sản IT

> **Snipe-IT** migrated to **.NET 9 + React 19 + Ant Design 6 + .NET Aspire 13.4**

AspireReact là hệ thống quản lý tài sản IT cấp doanh nghiệp, hỗ trợ multi-tenant, quản lý vòng đời tài sản (checkout/checkin/audit), vật tư tiêu hao, linh kiện, phụ kiện, bản quyền, báo cáo và import/export dữ liệu.

## Công nghệ

| Tầng | Công nghệ |
|------|----------|
| **Orchestration** | .NET Aspire 13.4 |
| **Backend** | C# .NET 9, ASP.NET Core Web API, MediatR, FluentValidation |
| **Database** | PostgreSQL 18 (EF Core 9, Npgsql) |
| **Cache** | Redis 7 |
| **Auth** | Local password auth (self-signed JWT + PBKDF2) + WebAuthn Passkeys tùy chọn — [AUTH MIGRATION 2026-09-07: Keycloak đã xóa hoàn toàn] |
| **Frontend** | React 19 + TypeScript 5 + Vite |
| **UI** | Ant Design 6 |
| **Testing** | xUnit, Testcontainers, Playwright |
| **CI/CD** | GitHub Actions, Docker Compose |

## Yêu cầu hệ thống

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Node.js 22.12+](https://nodejs.org/) (dev HTTPS + trust cert cho Vite)
- [Docker Desktop](https://www.docker.com/products/docker-desktop/)

## Cài đặt và chạy

```bash
# Clone repository
git clone <repo-url>
cd "Aspire Project"

# Khởi động toàn bộ stack (PostgreSQL, Redis, Backend, Frontend — auth local)
cd aspire-react/aspire-react.AppHost
dotnet run
```

Truy cập:
- **Frontend (dev HTTPS)**: https://localhost:5173
- **API (HTTP)**: http://localhost:5428
- **API (HTTPS)**: https://localhost:7314
- **Aspire Dashboard**: URL hiển thị trong terminal khi khởi động

### Tài khoản mặc định (dev)

- **Username**: `admin`
- **Password local auth**: được seed 1 lần từ user-secret `Parameters:authBootstrapPassword` (khi admin chưa có hash) — file gitignored `.mirats-test-admin-password` ở repo root chứa cùng giá trị để tiện test. Admin reset password cho user khác qua UI (Users → "Đặt lại mật khẩu"). Hướng dẫn chi tiết xem [CLAUDE.md](CLAUDE.md) (mục "Setup secrets lần đầu") và `docs/AUTH_MIGRATION_PLAYBOOK.md`.

## Cấu trúc thư mục

```
Aspire Project/
├── README.md
├── MIGRATION_PLAN.md
├── PHASE0_COMPLETION_REPORT.md → PHASE7_COMPLETION_REPORT.md
├── docs/
│   ├── API.md
│   ├── ARCHITECTURE.md
│   └── DEPLOYMENT.md
└── aspire-react/
    ├── aspire-react.sln
    ├── aspire.config.json
    ├── aspire-react.AppHost/
    ├── aspire-react.ServiceDefaults/
    ├── aspire-react.Server/
    │   ├── Domain/          # Entities, Enums, Interfaces
    │   ├── Application/     # Commands, Queries, DTOs, Validators
    │   ├── Infrastructure/  # Persistence, Authorization, Services
    │   └── Web/             # Controllers, Middleware
    └── frontend/
        ├── src/
        │   ├── pages/       # Dashboard, Assets, Consumables, ...
        │   ├── components/  # Shared components (ProtectedRoute, Dialogs)
        │   └── services/    # API clients
        └── vite.config.ts
```

## Tính năng

- **Quản lý tài sản**: CRUD, Parent-Child tree, Checkout/Checkin, Audit, Action Logs
- **Vật tư tiêu hao**: Consumables, Components, Accessories với dynamic stock calculation
- **Bản quyền phần mềm**: License + Seat management
- **Multi-tenant**: FMCS Scoping qua Global Query Filter
- **Custom Fields**: JSONB metadata approach
- **Dashboard**: Thống kê, biểu đồ, recent activity, low stock alerts
- **Báo cáo**: Khấu hao, kiểm kê, lịch sử checkout
- **Import/Export**: CSV import/export
- **QR Labels**: QR code generation cho tài sản

## Kiến trúc

Chi tiết xem [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).

## API

Danh sách đầy đủ các endpoint xem [docs/API.md](docs/API.md).

## Triển khai

Hướng dẫn deploy xem [docs/DEPLOYMENT.md](docs/DEPLOYMENT.md).

## License

MIT