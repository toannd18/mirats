# AspireReact API Reference

> Base URL: `http://localhost:5428/api/v1` (HTTP) hoặc `https://localhost:7314/api/v1` (HTTPS)
> Auth: JWT Bearer token từ Keycloak

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
| `PUT` | `/api/v1/users/{id:guid}` | `users.edit` |
| `DELETE` | `/api/v1/users/{id:guid}` | `users.delete` |
| `PUT` | `/api/v1/users/{id:guid}/groups` | `admin` | Gán nhóm cho user (thay thế toàn bộ); **chống self-lockout** (`SELF_LOCKOUT`) + ghi ActionLog |

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
| Status Labels | `GET/POST` `/api/v1/statuslabels`, `GET/PUT/DELETE` `/api/v1/statuslabels/{id}` |
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