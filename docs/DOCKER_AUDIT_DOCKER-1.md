# DOCKER-1 Audit — Hard-code, Biến Môi Trường & Thiết Kế Triển Khai Docker

> **Phạm vi:** AUDIT + THIẾT KẾ. Không code, không xóa dữ liệu trong lượt này. Báo cáo làm cơ sở để duyệt các task Docker-2..N.

- Ngày audit: 2026-08-18
- Stack kiểm tra: .NET 10 + Aspire 13.4.6, React 19 + Vite, Keycloak 26.6, Postgres 18.3, Redis 8
- Các file đã quét: `aspire-react.AppHost/**`, `aspire-react.Server/**` (Program.cs, appsettings*.json, `Infrastructure/**/*ServiceCollectionExtensions.cs`, `Infrastructure/Persistence/StartupDataSeeder.cs`), `frontend/**` (vite.config.ts, `src/services/keycloak.ts`, `src/services/api-client.ts`), `aspire-react-realm.json`, `aspire-react.Server/Dockerfile`, `docs/*.md`, `scripts/*`

---

## 1. Kiểm kê toàn bộ giá trị hard-code cần chuyển thành biến môi trường

### 1.1 Backend — `aspire-react.Server/appsettings.json:9-16`
```json
"Keycloak": {
  "ServerUrl": "https://localhost:8080",          // ← hard-code host/port + scheme
  "Realm": "aspire-react",                         // ← hard-code realm name
  "ClientId": "backend-service",                   // ← hard-code confidential client id
  "ClientSecret": "a1b2c3d4-e5f6-7890-abcd-ef1234567890", // ← SECRET hard-code
  "SuperUserGroupName": "superuser",
  "TimeoutSeconds": 30
}
```
- `appsettings.Development.json` hiện rỗng (không override) — mọi giá trị trên là default cho cả dev lẫn prod nếu không set env.
- Không có `Jwt:Issuer/Audience/SigningKey` riêng — backend dùng Keycloak JWT bearer (`AuthenticationServiceCollectionExtensions.cs:19-20` đọc `Keycloak:Authority ?? https://localhost:8080/realms/aspire-react`). Authority fallback cũng hard-code.

### 1.2 Backend — `aspire-react.Server/Program.cs:36-42`
```csharp
policy.WithOrigins("http://localhost:5173")
      .AllowAnyHeader()
      .AllowAnyMethod();
```
- CORS `WithOrigins` hard-code `http://localhost:5173`. Thiếu `https://localhost:5173` và mọi origin prod.
- `builder.AddServiceDefaults()`, `AddPersistence()`, `AddRedisCaching()` đọc connection string qua `GetConnectionString("aspire-react-db")` / `"cache"` — đúng pattern (không hard-code string), nhưng tên connection (`aspire-react-db`, `cache`) là hằng trong code.

### 1.3 Backend — `Infrastructure/Authentication/AuthenticationServiceCollectionExtensions.cs:19-20`
```csharp
var keycloakUrl = configuration["Keycloak:Authority"]
    ?? "https://localhost:8080/realms/aspire-react";
options.Authority = keycloakUrl;
options.RequireHttpsMetadata = false; // ← dev-only, phải bật true ở prod
```
- Fallback Authority hard-code `https://localhost:8080/realms/aspire-react`.

### 1.4 Backend — `Infrastructure/Persistence/PersistenceServiceCollectionExtensions.cs:15`
```csharp
builder.AddNpgsqlDbContext<AppDbContext>("aspire-react-db");
var dbConnectionString = builder.Configuration.GetConnectionString("aspire-react-db") ?? string.Empty;
```
- Tên DB key `aspire-react-db` hard-code (đúng Aspire convention, nhưng cần document thành env `ConnectionStrings__aspire-react-db`).

### 1.5 Backend — `Infrastructure/Caching/CachingServiceCollectionExtensions.cs:20`
```csharp
builder.AddRedisOutputCache(connectionName: "cache");
```
- Tên Redis key `cache` hard-code → env `ConnectionStrings__cache`.

### 1.6 Backend — `aspire-react.Server/Properties/launchSettings.json`
```json
"applicationUrl": "http://localhost:5428"           // http profile
"applicationUrl": "https://localhost:7314;http://localhost:5428" // https profile
```
- Port API dev hard-code `5428` / `7314`. Dockerfile lại hard-code `ASPNETCORE_URLS=http://+:5000` + `EXPOSE 5000` (mismatch).

### 1.7 AppHost — `aspire-react.AppHost/AppHost.cs`
```csharp
var postgres = builder.AddPostgres("postgres")       // resource name hard-code
    .WithDataVolume("postgres-data")                  // volume name hard-code
    .WithPgAdmin()
    .AddDatabase("aspire-react-db");
var cache = builder.AddRedis("cache");
var keycloak = builder.AddKeycloak("keycloak", 8080)  // port 8080 hard-code
    .WithDataVolume("keycloak-data")
    .WithRealmImport("../aspire-react-realm.json")
    .WithEnvironment("KC_BOOTSTRAP_ADMIN_USERNAME", "admin")         // ← hard-code
    .WithEnvironment("KC_BOOTSTRAP_ADMIN_PASSWORD", "Admin123!");    // ← SECRET hard-code
var webfrontend = builder.AddViteApp("webfrontend", "../frontend")
    .WithEndpoint("http", e => { e.Port = 5173; e.IsProxied = false; }); // port 5173 hard-code
```
- Toàn bộ tên resource/volume/port/credential của stack dev nằm đây. Không dùng được cho prod.

### 1.8 Frontend — `frontend/src/services/keycloak.ts:3-7`
```ts
const keycloakConfig = {
  url: import.meta.env.VITE_KEYCLOAK_URL || 'https://localhost:8080',
  realm: 'aspire-react',      // ← hard-code
  clientId: 'frontend',       // ← hard-code
};
```
- `realm` + `clientId` không đọc từ env. `url` có fallback localhost.

### 1.9 Frontend — `frontend/src/services/api-client.ts:7`
```ts
const API_BASE = (import.meta as any).env?.VITE_API_BASE_URL ?? 'http://localhost:5428';
baseURL: `${API_BASE}/api/v1`,
```
- Fallback API base `http://localhost:5428` hard-code.

### 1.10 Frontend — `frontend/vite.config.ts:7-10`
```ts
const backendUrl = env.VITE_API_BASE_URL
  || process.env.services__server__https__0
  || process.env.services__server__http__0
  || (mode === 'production' ? '/api/v1' : 'http://localhost:5428');
proxy: { '/api': { target: backendUrl, changeOrigin: true, secure: false } }
```
- Fallback `http://localhost:5428` + proxy `/api` dev-only. `secure: false` dev-only.

### 1.11 Keycloak Realm — `aspire-react-realm.json`
```json
"realm": "aspire-react",
"clients": [
  { "clientId": "frontend", "redirectUris": ["http://localhost:5173/*", "https://localhost:5173/*"], "webOrigins": ["http://localhost:5173", "https://localhost:5173"] },
  { "clientId": "backend-service", "secret": "a1b2c3d4-e5f6-7890-abcd-ef1234567890" }
],
"users": [
  { "username": "admin", "email": "admin@aspire-react.local", "credentials": [{ "value": "Admin123!" }], "realmRoles": ["admin"] }
]
```
- Chứa **cả 2 secret**: `backend-service` secret + user `admin` password `Admin123!`. Đây là file import realm lần đầu — nếu giữ nguyên sẽ hard-code credential vào image/volume.

### 1.12 Tổng hợp: mật khẩu `Admin123!` — vị trí chính xác
| File | Dòng | Vai trò |
|------|------|---------|
| `aspire-react.AppHost/AppHost.cs:18` | `KC_BOOTSTRAP_ADMIN_PASSWORD = "Admin123!"` | **Keycloak master admin** (realm `master`) — bootstrap lần đầu, lưu vào volume `keycloak-data` (H2). Sau lần đầu, đổi env không đổi password trong DB. |
| `aspire-react-realm.json:52` | `credentials[0].value = "Admin123!"` cho user `admin` | **Admin ứng dụng** trong realm `aspire-react` (realm role `admin`, dùng để đăng nhập app). Import 1 lần; sau đó lưu trong DB Keycloak. |
| `aspire-react.Tests/ConcurrencyRaceAuditTests.cs:48` | `["password"] = "Admin123!"` | Test code — không ảnh hưởng prod, nhưng là tham chiếu thứ 3. |
| `docs/*` (DEVELOPMENT_WORKFLOW.md, HANDOFF_LATEST.md, DEPLOYMENT.md, ASSET_MODULE_HANDOFF.md) | nhiều dòng | Tài liệu — ghi chú `admin/Admin123!` để dev đăng nhập. Không phải hard-code runtime. |

**Kết luận:** `Admin123!` xuất hiện ở **đúng 2 nơi runtime** (AppHost bootstrap + realm JSON user). Cả 2 đều cần tách ra biến môi trường cho triển khai thật. Docs là tài liệu, giữ nguyên nhưng cần ghi chú "chỉ dùng cho dev".

### 1.13 Các giá trị còn lại cần env hóa (đã liệt kê) + không có secret ẩn khác
- Không có JWT signing key riêng (dùng Keycloak).
- Không có API key bên thứ 3.
- SuperUserGroupName `superuser` là hằng role name — nên env hóa nhưng có default an toàn.
- Redis hiện không có password (Aspire dev không set) — prod cần `REDIS_PASSWORD` nếu dùng Redis có auth/TLS.

---

## 2. Đánh giá hạ tầng triển khai hiện tại

### 2.1 Aspire AppHost là dev-time orchestrator — KHÔNG dùng trực tiếp cho prod
- File `AppHost.cs` dùng `DistributedApplication.CreateBuilder`, `AddPostgres/WithDataVolume/WithPgAdmin/AddKeycloak/WithRealmImport/AddViteApp/WithReference/WaitFor/PublishWithContainerFiles` — toàn bộ là API dev orchestration của Aspire. `builder.Build().Run()` khởi DCP + Dashboard, quản lý vòng đời container qua Docker API, inject service discovery (`services__server__http__0`...), OTel, health check.
- Ranh giới: Aspire **không sinh** `docker-compose.yml`/`Dockerfile` mặc định; muốn prod phải `dotnet publish` với publisher `manifest`/`docker-compose` hoặc tự viết compose. AppHost không chạy trong container prod.
- Xác nhận: `aspire.config.json` chỉ trỏ `appHost.path`, không cấu hình publish.

### 2.2 Dockerfile / docker-compose hiện có
- `aspire-react.Server/Dockerfile` — **đã có** (15 dòng, multi-stage `sdk:10.0` → `aspnet:10.0`, `ASPNETCORE_URLS=http://+:5000`, `EXPOSE 5000`). Tuổi cũ, hard-code URL/port, không nhận build arg cho env.
- `frontend/Dockerfile` — **CHƯA có** (repo không có file nào).
- `docker-compose.yml` / `compose.yml` ở repo root — **CHƯA có**. `docs/DEPLOYMENT.md:24-88` có **mẫu** compose trong tài liệu (postgres/redis/keycloak/server/frontend + network/volumes), nhưng đó chỉ là ví dụ trong docs, **không phải file thực tế** trong repo và vẫn chứa hard-code/fallback (`${DB_PASSWORD:-postgres}`, `admin`).
- Kết luận: **Cần tạo mới từ đầu** `docker-compose.yml` (prod) + `frontend/Dockerfile` (multi-stage Node build → Nginx/Static), và cập nhật `Server/Dockerfile` để nhận env.

### 2.3 Keycloak `aspire-react-realm.json` — hard-code hay skeleton?
- File hiện là **realm import đầy đủ kèm credential thật** (secret + password). Phần **cấu trúc realm** (roles, clients skeleton: `frontend` public + `backend-service` confidential, `sslRequired: none`, `bruteForceProtected: true`) là skeleton an toàn — không chứa secret.
- Phần **chứa secret**: `clients[1].secret` và `users[0].credentials[0].value` — 2 giá trị trên — **phải tách ra biến môi trường** cho prod. Đề xuất: giữ 1 bản `aspire-react-realm.json` skeleton (không users, secret placeholder) cho prod, hoặc template biến `${KC_CLIENT_SECRET}` + seed user qua Admin API thay vì import.

---

## 3. Thiết kế schema biến môi trường (`.env.example`)

Nhóm theo service. Quy ước: `BẮT BUỘC` = prod phải set, không có default an toàn; `TÙY CHỌN` = có default dev hợp lý.

### Postgres
| Biến | Bắt buộc | Default (dev) | Mô tả |
|------|----------|---------------|-------|
| `POSTGRES_DB` | Tùy chọn | `aspire-react-db` | Tên DB |
| `POSTGRES_USER` | Tùy chọn | `postgres` | User DB |
| `POSTGRES_PASSWORD` | **BẮT BUỘC (prod)** | *(không default prod)* — dev `postgres` | Password DB. Docs mẫu dùng `${DB_PASSWORD:-postgres}` — prod **không** để fallback. |
| `POSTGRES_PORT` | Tùy chọn | `5432` | Host port map `5432:5432` |

Tương ứng `ConnectionStrings__aspire-react-db` trong backend sẽ là `Host=postgres;Database=${POSTGRES_DB};Username=${POSTGRES_USER};Password=${POSTGRES_PASSWORD}` (compose tự inject).

### Redis
| Biến | Bắt buộc | Default | Mô tả |
|------|----------|---------|-------|
| `REDIS_PASSWORD` | Tùy chọn | *(trống — dev không auth)* | Nếu Redis prod có `--requirepass`, set ở đây. Aspire dev hiện không set. |
| `REDIS_PORT` | Tùy chọn | `6379` |  |

`ConnectionStrings__cache` = `redis:6379` hoặc `redis:6379,password=...` (StackExchange.Redis).

### Keycloak — Realm & Clients
| Biến | Bắt buộc | Default (dev) | Mô tả |
|------|----------|---------------|-------|
| `KEYCLOAK_REALM` | Tùy chọn | `aspire-react` | Tên realm. Đồng bộ với `Keycloak__Realm` backend + `VITE_KEYCLOAK_REALM`. |
| `KEYCLOAK_FRONTEND_CLIENT_ID` | Tùy chọn | `frontend` | Public client id. |
| `KEYCLOAK_BACKEND_CLIENT_ID` | Tùy chọn | `backend-service` | Confidential client id. |
| `KEYCLOAK_BACKEND_CLIENT_SECRET` | **BẮT BUỘC (prod)** | *(không default)* — dev `a1b2c3d4-...-7890` | Secret của `backend-service`. Phải map vào `Keycloak__ClientSecret` backend + realm JSON placeholder. |
| `KEYCLOAK_SERVER_URL` | Tùy chọn | `https://keycloak:8080` (prod nội bộ) / `https://localhost:8080` (dev) | Base URL Keycloak. Backend `Keycloak__Authority = ${KEYCLOAK_SERVER_URL}/realms/${KEYCLOAK_REALM}`. Frontend `VITE_KEYCLOAK_URL = ${KEYCLOAK_SERVER_URL}`. |

`redirectUris`/`webOrigins` trong realm JSON cho `frontend` hiện hard-code `http://localhost:5173` — prod phải là `https://<prod-host>/*` qua biến `FRONTEND_URL` (xem Frontend).

### Backend API
| Biến | Bắt buộc | Default | Mô tả |
|------|----------|---------|-------|
| `ASPNETCORE_URLS` | Tùy chọn | `http://+:5000` | Kestrel listen. Dockerfile đã hard-code, nên để env override. |
| `ASPNETCORE_ENVIRONMENT` | Tùy chọn | `Production` | `Development` chỉ cho dev. |
| `ConnectionStrings__aspire-react-db` | **BẮT BUỘC** (compose tự set) | — | Postgres connection string. |
| `ConnectionStrings__cache` | **BẮT BUỘC** (compose tự set) | — | Redis connection string. |
| `Keycloak__Authority` | Suy ra từ `KEYCLOAK_SERVER_URL/REALM` | `https://keycloak:8080/realms/aspire-react` | Authority JWT. |
| `Keycloak__Realm` | Suy ra | `aspire-react` |  |
| `Keycloak__ClientId` | Suy ra | `backend-service` |  |
| `Keycloak__ClientSecret` | **BẮT BUỘC** (map từ `KEYCLOAK_BACKEND_CLIENT_SECRET`) | — |  |
| `CORS_ALLOWED_ORIGINS` | **BẮT BUỘC (prod)** | `http://localhost:5173` (dev) | Thay thế `WithOrigins("http://localhost:5173")` trong Program.cs. Dạng CSV: `https://app.example.com,https://admin.example.com`. |
| `FRONTEND_URL` | Tùy chọn | `http://localhost:5173` | Dùng để sinh redirectUris/webOrigins nếu template realm. |

### Frontend (build-time — Vite `VITE_*`)
| Biến | Bắt buộc | Default | Mô tả |
|------|----------|---------|-------|
| `VITE_API_BASE_URL` | Tùy chọn | `/api/v1` (prod, cùng origin) / `http://localhost:5428` (dev) | Base URL API. Prod nên là `/api/v1` qua Nginx proxy, không hard-code host. |
| `VITE_KEYCLOAK_URL` | Suy ra | `https://keycloak:8080` (dev) | Keycloak URL cho `keycloak-js`. |
| `VITE_KEYCLOAK_REALM` | Suy ra | `aspire-react` | Đồng bộ `KEYCLOAK_REALM`. |
| `VITE_KEYCLOAK_CLIENT_ID` | Suy ra | `frontend` |  |

Lưu ý: `VITE_*` được bake vào bundle lúc `npm run build` — compose phải truyền `args` cho `frontend/Dockerfile` build stage, không phải `environment` runtime.

### Cổng lắng nghe (host → container)
| Service | Container | Host default (dev) | Biến |
|---------|-----------|-------------------|------|
| Postgres | 5432 | 5432 | `POSTGRES_PORT` |
| Redis | 6379 | 6379 | `REDIS_PORT` |
| Keycloak | 8080 (http) | 8080 | `KEYCLOAK_PORT` |
| Backend API | 5000 | 5000 (prod) / 5428 (dev Aspire) | `BACKEND_PORT` |
| Frontend | 80 (Nginx) | 80 / 5173 (dev Vite) | `FRONTEND_PORT` |

---

## 4. Kế hoạch xóa dữ liệu hiện có (audit trước, chưa thực thi)

### 4.1 "Xóa toàn bộ dữ liệu" nghĩa là gì
- **Reset schema + data (khuyến nghị cho "sạch hoàn toàn"):** `docker compose down -v` xóa volumes → xóa **cả schema (migrations) lẫn data**. Lần `up` tiếp theo: Postgres volume rỗng → EF Core `db.Database.Migrate()` trong `StartupDataSeeder.Seed` tự tạo schema từ migrations + seed nhóm hệ thống. Keycloak volume rỗng → import lại realm JSON (hoặc tạo realm mới qua Admin API).
- **Chỉ xóa data, giữ schema:** `TRUNCATE`/`DELETE` các bảng hoặc `dotnet ef database update 0` rồi `update` lại — giữ volume nhưng làm trống data. Phức tạp hơn, ít khi cần; `down -v` đơn giản và đảm bảo sạch.

### 4.2 Volume cần xóa để "sạch hoàn toàn"
- `postgres-data` — chứa DB `aspire-react-db` (tạo 2026-08-07).
- `keycloak-data` — chứa H2 DB của Keycloak (realm `aspire-react` + user `admin`, tạo 2026-08-08). Sau khi xóa, credential trong DB mất — lần import tiếp theo mới nhận env mới.
- Không cần xóa volume Redis (cache, không persistent quan trọng) hay `pgadmin` nếu có.
- Kiểm tra hiện tại: `docker volume ls` thấy `postgres-data`, `keycloak-data` + nhiều volume `13cf72...` (Aspire ephemeral). `docker volume inspect` xác nhận mountpoint `/var/lib/docker/volumes/<name>/_data`.

### 4.3 Đề xuất: không tự xóa dev data ngay — cung cấp cơ chế
- **KHÔNG** chạy `docker compose down -v` hay `TRUNCATE` trong lượt audit này. Dữ liệu dev hiện tại (Aspire stack đang chạy, user `admin`/`ndkien`/`st1verify`...) vẫn cần để tiếp tục phát triển.
- Cung cấp script/lệnh reset để người dùng tự làm sạch khi triển khai thật:
  ```bash
  # From repo root, after docker-compose.yml exists:
  docker compose down -v          # xóa containers + volumes (postgres-data, keycloak-data)
  docker volume rm postgres-data keycloak-data  # nếu down -v không xóa hết
  docker compose up -d            # khởi lại sạch, Migrate + seed lại
  ```
- Hoặc script `scripts/docker-reset.ps1` / `scripts/docker-reset.sh` bọc lệnh trên + xác nhận `y/N`.

### 4.4 Câu hỏi cần xác nhận (không tự quyết định)
> **Bạn muốn xóa dữ liệu dev hiện tại NGAY trong đợt này, hay chỉ cần cơ chế để làm được việc đó khi triển khai thật, còn dữ liệu dev hiện tại vẫn giữ để tiếp tục làm việc?**
- Đề xuất: **GIỮ** dữ liệu dev hiện tại. Chỉ cung cấp `docker compose down -v` + hướng dẫn trong `docs/DEPLOYMENT.md` và `.env.example` comment.

---

## 5. Seed tài khoản khởi tạo lần đầu — 2 loại tài khoản RIÊNG BIỆT

### 5a. Admin ứng dụng (`IsSuperUser=true` — dùng để đăng nhập app Mirats)

**Yêu cầu:** khi khởi tạo lần đầu, seed 1 User `IsSuperUser=true`. Dữ liệu lấy từ biến môi trường **bắt buộc, không default**:
- `INITIAL_ADMIN_USERNAME` — username đăng nhập (VD `admin`)
- `INITIAL_ADMIN_EMAIL` — email (VD `admin@example.com`)
- `INITIAL_ADMIN_PASSWORD` — mật khẩu (secret, không default, không commit)

**Cơ chế hiện tại (đã thiết lập từ đầu dự án):**
- Hệ thống dùng Keycloak làm IdP. Khi user đăng nhập, `OnTokenValidated` trong `AuthenticationServiceCollectionExtensions.cs` gọi `IJitUserProvisioningService.ProvisionAsync(principal)` — JIT tạo bản ghi `User` local nếu chưa có, stamp claim `local_user_id` (KHÔNG dùng Keycloak `sub` làm FK). `ICurrentUserService.GetLocalUserId()` / `IActionLogService.GetCurrentUserIdAsync()` đọc `local_user_id`.
- `StartupDataSeeder.Seed` hiện **không tạo user** — chỉ seed nhóm `Superuser`/`Admin` + gán legacy `IsSuperUser` vào nhóm. User `admin` hiện có là do `aspire-react-realm.json` import + JIT lần đầu.

**Đề xuất seed — 2 phương án:**

| Phương án | Cách làm | Ưu | Nhược |
|-----------|----------|----|-------|
| **A. Seed qua Keycloak Admin API (KHUYẾN NGHỊ)** | Script `init` container (hoặc entrypoint `server`) gọi Keycloak Admin API (`POST /admin/realms/aspire-react/users` + `PUT .../reset-password` + gán realm role `admin`/`superuser`) với `INITIAL_ADMIN_*` từ env. Sau đó user đăng nhập lần đầu → JIT tự tạo bản ghi local `User` (với `IsSuperUser` suy từ realm role, hoặc gán vào nhóm Superuser qua `PermissionMigration`). | Đúng quy ước `local_user_id` (local FK không phải `sub`); không cần tạo local user thủ công; đồng bộ 1 nguồn (Keycloak là source of truth). | Cần chờ Keycloak ready + admin token (`admin-cli` + `KC_BOOTSTRAP_*`). |
| **B. Seed cả 2 nơi ngay trong script** | Script tạo user trong Keycloak **và** `INSERT` vào `users` table (với `IsSuperUser=true` hoặc gán `GroupPermissions`). | User tồn tại ngay cả trước lần đăng nhập đầu; không phụ thuộc JIT. | Phải tự sinh `local_user_id` UUID, đồng bộ password 2 nơi, rủi ro lệch `sub` vs local id; vi phạm nguyên tắc "JIT là nguồn tạo local user". |

**Khuyến nghị: Phương án A** — seed script tạo user **trực tiếp qua Keycloak Admin API** lúc `docker compose up` (dùng service `keycloak-init` one-shot hoặc `server` entrypoint `seed-initial-admin.sh`). Để JIT tự tạo local `User` khi `INITIAL_ADMIN_USERNAME` đăng nhập lần đầu. Nếu cần local `IsSuperUser` ngay, script có thể gọi thêm `POST /api/v1/users` (đã có) sau khi Keycloak user tồn tại, nhưng không bắt buộc nếu realm role `superuser` đã đủ cho `ICompanyScopeService.IsSuperUser()`.

**Biến môi trường cho 5a (bắt buộc, không default):**
```
INITIAL_ADMIN_USERNAME=        # required, e.g. admin
INITIAL_ADMIN_EMAIL=           # required, e.g. admin@example.com
INITIAL_ADMIN_PASSWORD=        # required, secret — no default, no fallback like "Admin123!"
```

### 5b. Keycloak master admin (quản trị Keycloak Admin Console — realm `master`)

**Đây là tài khoản HOÀN TOÀN ĐỘC LẬP với 5a** — dùng để đăng nhập `https://keycloak:8080/admin` quản trị realm/client/user. Không phải admin ứng dụng Mirats.

**Image Keycloak chính thức đã hỗ trợ sẵn biến môi trường chuẩn:**
- Keycloak 26.6 (dự án đang dùng `quay.io/keycloak/keycloak:26.6`) dùng **`KC_BOOTSTRAP_ADMIN_USERNAME` / `KC_BOOTSTRAP_ADMIN_PASSWORD`** (thay thế `KEYCLOAK_ADMIN` / `KEYCLOAK_ADMIN_PASSWORD` cũ từ Keycloak < 20). AppHost hiện đã dùng đúng cặp này (`AppHost.cs:17-18`), và `docs/DEPLOYMENT.md:59` cũng ghi `KC_BOOTSTRAP_ADMIN_USERNAME/PASSWORD`.
- Hành vi: chỉ có tác dụng **lần đầu** khi volume `keycloak-data` rỗng; sau đó credential lưu trong H2 DB, đổi env không đổi password trong DB (phải xóa volume hoặc reset qua Admin API).
- **Không cần tự viết seed logic** cho 5b — chỉ cần map đúng biến trong `docker-compose.yml`:
  ```yaml
  keycloak:
    image: quay.io/keycloak/keycloak:26.6
    environment:
      KC_BOOTSTRAP_ADMIN_USERNAME: ${KC_BOOTSTRAP_ADMIN_USERNAME:?required}
      KC_BOOTSTRAP_ADMIN_PASSWORD: ${KC_BOOTSTRAP_ADMIN_PASSWORD:?required}
  ```

**Biến môi trường cho 5b (bắt buộc, không default, độc lập với 5a):**
```
KC_BOOTSTRAP_ADMIN_USERNAME=   # required, e.g. kcadmin  (KHÁC INITIAL_ADMIN_USERNAME)
KC_BOOTSTRAP_ADMIN_PASSWORD=   # required, secret        (KHÁC INITIAL_ADMIN_PASSWORD)
```

**Tuyệt đối không dùng chung 1 bộ biến** cho 5a và 5b. Trong `.env.example` phải ghi chú rõ:
```
# 5a — Admin ỨNG DỤNG (đăng nhập Mirats, IsSuperUser)
INITIAL_ADMIN_USERNAME=
INITIAL_ADMIN_EMAIL=
INITIAL_ADMIN_PASSWORD=

# 5b — Admin KEYCLOAK MASTER (đăng nhập /admin console) — ĐỘC LẬP, không trùng 5a
KC_BOOTSTRAP_ADMIN_USERNAME=
KC_BOOTSTRAP_ADMIN_PASSWORD=
```

---

## 6. Đánh giá: cần tạo Dockerfile + docker-compose từ đầu hay đã có sẵn

| Thành phần | Trạng thái | Hành động |
|------------|------------|-----------|
| `aspire-react.Server/Dockerfile` | **Đã có** (sdk:10.0 → aspnet:10.0, 15 dòng) | Cập nhật: nhận `ASPNETCORE_URLS` từ env, không hard-code `5000`; thêm `HEALTHCHECK`; ARG cho `ConnectionStrings` nếu cần. |
| `frontend/Dockerfile` | **CHƯA có** | Tạo mới: multi-stage `node:20-alpine` (npm ci + build với `VITE_*` args) → `nginx:alpine` serve `dist/` + proxy `/api` → backend. |
| `docker-compose.yml` (repo root) | **CHƯA có** (chỉ mẫu trong `docs/DEPLOYMENT.md`) | Tạo mới từ đầu: 5 services (postgres, redis, keycloak, server, frontend) + 2 volumes + network. Dùng `.env` + `env_file`. |
| `.env` / `.env.example` | **CHƯA có** ở cả root lẫn frontend | Tạo mới `.env.example` theo schema §3 (đã thiết kế). `.env` gitignore. |
| Realm import | `aspire-react-realm.json` có sẵn (kèm secret) | Tạo bản skeleton cho prod (không users/secret) hoặc template `${...}` + seed script. |

---

## 7. Đề xuất xử lý mật khẩu admin mặc định an toàn

- **Xóa `Admin123!` khỏi mọi file seed/realm cho triển khai thật.** Trong `aspire-react-realm.json` prod: bỏ block `users` (hoặc để `users: []`), thay `secret: "a1b2c3d4-..."` bằng placeholder `${KEYCLOAK_BACKEND_CLIENT_SECRET}` (Keycloak hỗ trợ thay biến khi import nếu dùng `KC_SPI_...` hoặc seed qua Admin API).
- **Không hard-code fallback** như `${DB_PASSWORD:-postgres}` hay `${KC_ADMIN_PASSWORD:-admin}` trong `docker-compose.yml` prod — dùng `${VAR:?required}` để compose fail nếu thiếu, buộc người dùng set.
- **Dev fallback** chỉ cho `docker-compose.override.yml` hoặc `.env.development` (không commit prod).
- **Tài liệu:** `docs/DEPLOYMENT.md` cần cập nhật: hướng dẫn `cp .env.example .env` rồi **bắt buộc** điền `INITIAL_ADMIN_*` + `KC_BOOTSTRAP_*` + `KEYCLOAK_BACKEND_CLIENT_SECRET` + `POSTGRES_PASSWORD` trước `docker compose up`.

---

## 8. Câu hỏi cần bạn xác nhận trước khi thực thi (đặc biệt xóa dữ liệu)

1. **Xóa dữ liệu dev hiện tại?** Bạn muốn `docker compose down -v` (xóa `postgres-data` + `keycloak-data`) **ngay bây giờ**, hay chỉ cần cơ chế/script để làm khi triển khai thật, **giữ dữ liệu dev hiện tại** để tiếp tục làm việc? (Đề xuất: **GIỮ**.)
2. **Tên host/prod domain cho Keycloak redirect?** `redirectUris`/`webOrigins` trong realm hiện `localhost:5173` — prod sẽ là gì (để điền `FRONTEND_URL`/`CORS_ALLOWED_ORIGINS`)? Tạm để placeholder `https://app.example.com`?
3. **Redis có cần password ở prod?** Hiện dev không auth — prod bật `REDIS_PASSWORD` hay giữ không password (nội bộ network)?
4. **Frontend prod serve:** Nginx (khuyến nghị) hay `node serve`? Nginx cho phép proxy `/api` → backend không cần CORS prod phức tạp.
5. **Có cần giữ `pgadmin` trong compose prod?** Hiện AppHost `WithPgAdmin()` chỉ dev — prod nên bỏ hay giữ service `pgadmin` tùy chọn?

---

## 9. Chia task nhỏ để duyệt từng bước (đề xuất)

| Task | Mô tả | Phụ thuộc |
|------|-------|-----------|
| **DOCKER-2** | Tạo `.env.example` + `.gitignore` cho `.env` (schema §3, không chứa secret thật) | DOCKER-1 (audit này) |
| **DOCKER-3** | Tạo `frontend/Dockerfile` (multi-stage Node → Nginx) + cập nhật `Server/Dockerfile` (healthcheck, env) | DOCKER-2 |
| **DOCKER-4** | Tạo `docker-compose.yml` prod (5 services, volumes, network, `env_file: .env`, `KC_BOOTSTRAP_*`, `INITIAL_ADMIN_*` placeholder, không fallback secret) | DOCKER-2, DOCKER-3 |
| **DOCKER-5** | Tách `aspire-react-realm.json` thành skeleton prod (không users/secret) + script `scripts/seed-initial-admin.sh` (Keycloak Admin API, phương án A) | DOCKER-4 |
| **DOCKER-6** | Env hóa `Program.cs` CORS + `keycloak.ts` realm/clientId + `api-client.ts`/`vite.config.ts` fallback (đọc từ `CORS_ALLOWED_ORIGINS`/`VITE_*`) | DOCKER-4 |
| **DOCKER-7** | Cập nhật `docs/DEPLOYMENT.md` + tạo `scripts/docker-reset.ps1|.sh` (down -v + hướng dẫn) | DOCKER-4 |
| **DOCKER-8** | Test e2e: `cp .env.example .env` → điền `INITIAL_ADMIN_*`/`KC_BOOTSTRAP_*`/secret → `docker compose up -d --build` → verify login admin mới + JIT local user + health checks | DOCKER-2..7 |

> Mỗi task sẽ được thực hiện riêng, bạn duyệt trước khi sang task tiếp theo. Lượt này **chỉ audit** — không code/xóa dữ liệu.

---

## 10. Phụ lục — Danh sách file:line hard-code đầy đủ (để grep)

```
aspire-react.Server/appsettings.json:10  Keycloak:ServerUrl = https://localhost:8080
aspire-react.Server/appsettings.json:11  Keycloak:Realm = aspire-react
aspire-react.Server/appsettings.json:12  Keycloak:ClientId = backend-service
aspire-react.Server/appsettings.json:13  Keycloak:ClientSecret = a1b2c3d4-e5f6-7890-abcd-ef1234567890
aspire-react.Server/appsettings.json:14  Keycloak:SuperUserGroupName = superuser
aspire-react.AppHost/AppHost.cs:14       AddKeycloak("keycloak", 8080)
aspire-react.AppHost/AppHost.cs:17       KC_BOOTSTRAP_ADMIN_USERNAME = admin
aspire-react.AppHost/AppHost.cs:18       KC_BOOTSTRAP_ADMIN_PASSWORD = Admin123!
aspire-react.AppHost/AppHost.cs:38       webfrontend Port = 5173
aspire-react.Server/Program.cs:39        WithOrigins("http://localhost:5173")
aspire-react.Server/Properties/launchSettings.json: http  http://localhost:5428
aspire-react.Server/Properties/launchSettings.json: https https://localhost:7314;http://localhost:5428
aspire-react.Server/Dockerfile:13        ASPNETCORE_URLS=http://+:5000 / EXPOSE 5000
aspire-react.Server/Infrastructure/Authentication/AuthenticationServiceCollectionExtensions.cs:19  Authority ?? https://localhost:8080/realms/aspire-react
frontend/src/services/keycloak.ts:4      VITE_KEYCLOAK_URL || https://localhost:8080
frontend/src/services/keycloak.ts:5      realm: 'aspire-react'
frontend/src/services/keycloak.ts:6      clientId: 'frontend'
frontend/src/services/api-client.ts:7    VITE_API_BASE_URL ?? http://localhost:5428
frontend/vite.config.ts:10               mode production ? /api/v1 : http://localhost:5428
aspire-react-realm.json:2                realm aspire-react
aspire-react-realm.json:13               clientId frontend + redirectUris http://localhost:5173/*
aspire-react-realm.json:31               clientId backend-service + secret a1b2c3d4-...
aspire-react-realm.json:43               users[0].username admin / credentials Admin123!
```

*Hết báo cáo DOCKER-1.*

---

## PHỤ LỤC — DOCKER-4/DOCKER-5 fix đã thực hiện (2026-08-18)

### Fix Authority mismatch — tách 2 URL Keycloak
- **Vấn đề:** `KEYCLOAK_SERVER_URL` mặc định `https://keycloak:8080` dùng chung cho cả
  server→keycloak (Docker network) VÀ browser→keycloak (host expose). Keycloak chạy
  `start-dev` chỉ HTTP → backend fetch JWKS qua HTTPS fail → JWT 401.
- **Fix:** `.env.example` + `docker-compose.yml` tách 2 biến:
  - `KEYCLOAK_INTERNAL_URL=http://keycloak:8080` — server↔keycloak nội bộ (http, đúng start-dev).
  - `KEYCLOAK_PUBLIC_URL=http://localhost:8080` — browser↔keycloak qua host expose.
  - `Keycloak__Authority` = `${KEYCLOAK_INTERNAL_URL}/realms/...`; `VITE_KEYCLOAK_URL` = `${KEYCLOAK_PUBLIC_URL}`.
  - Seed scripts (`.ps1`/`.sh`) ưu tiên `KEYCLOAK_PUBLIC_URL` rồi `KEYCLOAK_URL`/`KEYCLOAK_SERVER_URL` cũ.

### Fix healthchecks (cold-start DOCKER-4)
- **Postgres 18:** volume mount `/var/lib/postgresql/data` → `/var/lib/postgresql` (PG 18 yêu cầu mount parent).
- **Redis:** thiếu `env_file: [.env]` → `REDIS_PASSWORD` rỗng → healthcheck `REDISCLI_AUTH="$REDIS_PASSWORD" redis-cli ping`.
- **Keycloak 26.6 (RHEL UBI):** không có wget/curl → healthcheck `bash -c 'exec 3<>/dev/tcp/127.0.0.1/9000'`.
- **Server (aspnet:10.0):** không có wget/curl → healthcheck `bash -c 'exec 3<>/dev/tcp/127.0.0.1/5000'`.
- **Server Dockerfile:** `COPY nuget.config ./` + `.dockerignore` (bin/obj) + `rm -rf obj/bin` trước publish (fix lỗi fallback package folder Windows trên Linux build).

### Realm skeleton PROD (DOCKER-5)
- `aspire-react-realm.json`: xóa block `users` (không còn `admin/Admin123!`), secret `backend-service`
  → placeholder `${KEYCLOAK_BACKEND_CLIENT_SECRET}`, thêm `roles.realm: [admin, superuser]`
  (cần cho `RealmAccessHelper.IsSuperUser` exact match).

### Seed initial admin (DOCKER-5, Phương án A)
- `scripts/seed-initial-admin.ps1` + `.sh`: tạo user qua Keycloak Admin API (`INITIAL_ADMIN_*`),
  set password, gán realm role `admin` → JIT tạo local User `IsSuperUser=true` lần đăng nhập đầu.
- Fail rõ nếu thiếu `INITIAL_ADMIN_*`/`KC_BOOTSTRAP_*` hoặc Keycloak chưa ready (12 retry 5s).
- **Verify thật (stack compose cold-start):**
  - Seed `seedadmin` → role `admin` assigned → `login ok`, token roles `['offline_access','admin',...]` → IsSuperUser=True.
  - `GET /api/v1/dashboard/summary` với token → **HTTP 200** (JIT OnTokenValidated chạy).
  - DB: `SELECT ... WHERE Username='seedadmin'` → **1 row, IsSuperUser=t, IsActive=t**.

### LƯU Ý RIÊNG — Keycloak dev-mode HTTP
- `docker-compose.yml` dùng `command: ["start-dev", "--import-realm"]` → Keycloak CHỈ HTTP nội bộ
  (`http://0.0.0.0:8080`). Đây là đặc điểm của `start-dev`, KHÔNG phải giới hạn compose.
- **Prod thật:** cần Keycloak chạy HTTPS thật (`start --optimized` + cert/TLS termination qua
  reverse proxy Nginx/Ingress), và đổi `KEYCLOAK_INTERNAL_URL`/`KEYCLOAK_PUBLIC_URL` sang `https`.
  → **BACKLOG RIÊNG** (ngoài phạm vi DOCKER-4/DOCKER-5): bật production mode Keycloak + TLS.
- Không tự ý đổi Keycloak sang prod mode trong các task này — là quyết định lớn, cần bàn riêng.

### DOCKER-6 — Env hóa CORS/Keycloak trong code (2026-08-18)
- **Program.cs CORS:** `WithOrigins("http://localhost:5173")` hard-code → đọc `CORS_ALLOWED_ORIGINS`
  (CSV split, trim), fallback dev `http://localhost:5173` (an toàn, không secret).
- **keycloak.ts:** `realm: 'aspire-react'` / `clientId: 'frontend'` hard-code → đọc
  `import.meta.env.VITE_KEYCLOAK_REALM` / `VITE_KEYCLOAK_CLIENT_ID`, fallback dev giữ nguyên.
- **Không cần sửa (đã đúng):**
  - Auth Authority fallback (`AuthenticationServiceCollectionExtensions.cs:20`) — đã đọc `Keycloak:Authority` config (compose set), fallback `https://localhost:8080/...` chỉ khi không config, dev an toàn.
  - api-client.ts `VITE_API_BASE_URL ?? localhost:5428` — đã đọc ENV, fallback dev an toàn.
  - vite.config.ts fallback — đã đọc `VITE_API_BASE_URL`, fallback dev/prod an toàn.
- **Verify thật:**
  - CORS: set `CORS_ALLOWED_ORIGINS=https://test-cors.example.com` → `Origin: https://test-cors.example.com` trả `Access-Control-Allow-Origin: https://test-cors.example.com` (200); `Origin: http://localhost:5173` KHÔNG có ACAO (origin cũ đã loại).
  - Frontend: build với `KEYCLOAK_REALM=test-realm` → bundle chứa `test-realm` (1 occurrence), `aspire-react` count 0.
  - Dev thường (không Docker): `npm run build` local 0 lỗi, `dotnet test` 283/283 PASS.
- Dọn sạch: `.env` tmp + `mirats-*` volumes removed, Aspire dev volumes còn.

### DOCKER-7 — docs/DEPLOYMENT.md + scripts reset dữ liệu (2026-08-18)
- **docs/DEPLOYMENT.md** — viết lại hoàn chỉnh theo thực tế đã build:
  - Yêu cầu hệ thống (Docker 24+, Compose v2, không cần .NET/Node — build trong image).
  - Quy trình 5 bước: `cp .env.example .env` → điền 9 biến BẮT BUỘC (bảng kèm ví dụ)
    → `docker compose up -d --build` → `scripts/seed-initial-admin.*` → xác nhận login.
  - Giải thích rõ 2 loại admin (5a ứng dụng INITIAL_ADMIN_* vs 5b Keycloak master KC_BOOTSTRAP_*),
    bảng so sánh + quy tắc vàng (không dùng chung, không fallback Admin123!).
  - Mục riêng KEYCLOAK_INTERNAL_URL vs KEYCLOAK_PUBLIC_URL (bài học Authority mismatch DOCKER-5),
    cảnh báo "đừng gộp 2 biến về 1".
  - Debug/Dev: pgAdmin `--profile debug`, docker-compose.override.yml expose Postgres/Redis (không commit).
  - Giới hạn hiện tại: Keycloak start-dev chỉ HTTP nội bộ — CHƯA an toàn internet-facing,
    backlog: start --optimized + TLS + sslRequired external + backup.
  - Troubleshooting 4 lỗi thật đã gặp: PG18 mount path, wget/curl thiếu, Authority mismatch,
    `${VAR:?required}` khi down -v.
  - Bảng biến môi trường đầy đủ (bắt buộc/tùy chọn), health checks, lưu ý VITE_* build-time.
- **scripts/docker-reset.sh + docker-reset.ps1** — reset stack production:
  - Chỉ xóa volume `mirats-*` (mirats-postgres-data/redis-data/keycloak-data); KHÔNG đụng
    postgres-data/keycloak-data (Aspire dev).
  - Hỏi xác nhận `yes` trước khi xóa (irreversible) — nếu không thoát, không xóa gì.
  - `docker compose down -v`; nếu compose fail (thiếu .env/biến BẮT BUỘC) → fallback dọn
    container theo label project + xóa volume thủ công. In hướng dẫn rebuild + seed.
  - PS 5.1-compatible: plain ASCII (không BOM/em-dash — test thật bắt parser error khi có
    ký tự UTF-8, đã fix), không `??`, không EAP=Stop (native stderr redirect 5.1 lỗi).
- **Test thật (volume TEST mirats-*):**
  - Path "no": hiển thị danh sách volume sẽ xóa + cảnh báo dev volumes NOT touched → Aborted, không xóa.
  - Path "yes" (không có .env): compose down -v fail → fallback dọn container + xóa mirats-test-a/b → exit 0.
  - Path "yes" (có .env tạm đủ biến): compose down -v chạy → xóa volume → exit 0.
  - Sau mọi path: `docker volume ls` → mirats-* rỗng; `postgres-data` + `keycloak-data` CÒN NGUYÊN.
  - .sh: bash -n exit 0 (không thể chạy live từ WSL — WSL không tới docker engine Windows; đã dừng đúng).
  - Dọn: volume test + .env tạm đã xóa.

### DOCKER-8 — Test E2E cuối cùng (mô phỏng người dùng thật theo docs) — HOÀN THÀNH (2026-08-18)
- Mô phỏng 1 người dùng mới đi theo docs/DEPLOYMENT.md từ đầu: reset → cp .env → điền BẮT BUỘC
  → up -d --build → seed → login → nghiệp vụ → Keycloak admin → pgAdmin. Phát hiện + fix 4 BUG THẬT
  mà docs KHÔNG nói (người dùng mới sẽ kẹt):
  1. **Realm redirect_uri:** realm skeleton `frontend` client chỉ cho `localhost:5173/*` → app serve ở
     `http://localhost` (port 80) → Keycloak 400 "Invalid parameter: redirect_uri", KHÔNG tới được login.
     → Fix: thêm `http://localhost/*`, `https://localhost/*`, `https://app.example.com/*` vào redirectUris
     + webOrigins của `aspire-react-realm.json`; realm đã import phải sửa client qua Admin API.
  2. **Double `/api/v1/api/v1`:** `api-client.ts` nối `baseURL = ${VITE_API_BASE_URL}/api/v1`; prod
     `VITE_API_BASE_URL=/api/v1` → mọi API 404. → Fix: `api-client.ts` kiểm tra `endsWith('/api/v1')`
     trước khi nối. Docs §4.4 + .env.example ghi rõ ngữ nghĩa.
  3. **JIT provisioning race:** login đầu fire 5 request song song cùng `ProvisionAsync` → cùng thấy
     "user chưa tồn tại" → cùng insert → `23505 duplicate key IX_users_Email` → dashboard 500.
     → Fix: `JitUserProvisioningService.ProvisionAsync` bắt `DbUpdateException` + `PostgresErrorCodes.UniqueViolation`,
     `ChangeTracker.Clear()` + đọc lại user đã thắng (idempotent). Verify: fresh login 0 lỗi.
  4. **Health endpoint sai:** docs + nginx.conf trỏ `/health` (ServiceDefaults chỉ map khi Development);
     prod trả 404. → Fix: docs ghi `/api/v1/health` (prod), nginx.conf proxy `/health → server:5000/api/v1/health`.
- **Seed script 5 lỗi PS 5.1 đã fix:** (a) không tự đọc .env → thêm auto-load .env từ repo root;
  (b) `Invoke-WebRequest` 201-empty-body throw "NonInteractive" → dùng `Invoke-RestMethod`;
  (c) role-mapping `Invoke-RestMethod` gửi JSON array sai → Keycloak 400 "Cannot parse the JSON" →
  dùng `curl.exe --data-binary @file` (204 OK); (d) `@(Invoke-RestMethod)` double-wrap JSON array →
  `$_.name` match cả array → role body sai → fix flatten `ForEach-Object { $_ }`;
  (e) `& curl | Out-Null` làm $LASTEXITCODE mất → fix `curl -f` + `-o NUL`.
- **Kết quả verify CUỐI (clean cold-start, mọi fix đã bake):**
  - `up -d` → 5 services healthy: 56s. Seed: +5s. Login app thành công (~1-1.5 phút total từ up).
  - Dashboard đầy đủ (Tổng tài sản/Đã cấp phát/Sẵn sàng/Tổng giá trị + bảng charts), 0 console error.
  - Sidebar: nhóm QUẢN TRỊ hiển thị (superuser đúng). Tạo Category "E2E Final Category" → DB row +
    ActionLog ActionType=1 → chuỗi frontend→CORS→backend→DB hoạt động thật.
  - Keycloak Admin Console login `e2ekcadmin` (master) OK, độc lập hoàn toàn với app admin `e2eadmin`.
  - pgAdmin KHÔNG tự bật (không có --profile debug); `docker compose --profile debug up -d pgadmin` bật OK (5050).
  - `dotnet test --filter Category!=Concurrency`: 279/279 PASS (4 ConcurrencyRaceAuditTests cần Aspire
    stack dev https://localhost:8080 đang tắt — KHÔNG phải regression).
  - npm run build: OK.
- **Docs đã cập nhật (sửa ngay, không để dành):** §3 Bước 5 + §10 health = /api/v1/health;
  §4.4 VITE_API_BASE_URL ngữ nghĩa + cảnh báo double-path; §7 backlog item 3 redirectUris;
  §8.5 redirect_uri; §8.6 duplicate-key JIT; nginx.conf /health proxy; .env.example comment.
- Dọn sạch sau test: mirats-* volumes removed, .env test removed, containers down,
  postgres-data/keycloak-data (dev) CÒN NGUYÊN.

## ✅ KẾT LUẬN: TOÀN BỘ DOCKER-1..8 HOÀN THÀNH
Một người mới đọc docs/DEPLOYMENT.md có thể đi từ `.env.example` trống → stack chạy đầy đủ →
đăng nhập → thao tác nghiệp vụ thành công, CHỈ bằng docs. Mục tiêu kế hoạch Docker đạt.

### DOCKER-8 rà soát bổ sung — Server HEALTHCHECK (sau review người dùng) (2026-08-18)
- **Câu hỏi:** compose healthcheck của `server` gọi `/health` hay `/api/v1/health`?
  Nếu `/health` thì sẽ FAIL ở Production (endpoint không tồn tại) → frontend không bao giờ khởi động.
- **Trả lời (xác minh bằng đọc file + thực nghiệm):**
  - `docker-compose.yml:116` healthcheck server = **TCP-connect**: `bash -c 'exec 3<>/dev/tcp/127.0.0.1/5000'`
    → KHÔNG dùng HTTP `/health` hay `/api/v1/health` → KHÔNG phụ thuộc ASPNETCORE_ENVIRONMENT.
    Chỉ cần Kestrel lắng nghe :5000 (log xác nhận). frontend vẫn khởi động được qua service_healthy.
  - Xác nhận thực nghiệm: `mcr.microsoft.com/dotnet/aspnet:10.0` CÓ `/usr/bin/bash` (TCP check chạy được),
    KHÔNG có wget (đúng lý do DOCKER-4 phải dùng TCP, không phải /health HTTP).
  - **Không có khoảng trống "ngầm qua nhờ Development"**: DOCKER-4 chạy 3 lần với
    ASPNETCORE_ENVIRONMENT=Production (mặc định .env.example) vẫn Healthy vì TCP check không dính /health.
- **Khoảng trống THẬT tìm thấy khi rà soát:** `aspire-react.Server/Dockerfile:32-33` HEALTHCHECK vẫn dùng
  `wget -qO- http://127.0.0.1:5000/health` → SAI 2 lần: (1) aspnet:10.0 không có wget;
  (2) /health chỉ tồn tại ở Development. Lỗi bị che giấu vì compose override bằng TCP check,
  nhưng `docker run` image độc lập (không qua compose) sẽ fail vĩnh viễn → Unhealthy.
- **Fix:** Dockerfile đổi HEALTHCHECK sang `bash -c 'exec 3<>/dev/tcp/127.0.0.1/5000'` (khớp compose).
  Verify: rebuild image → `docker image inspect` Healthcheck.Test = `["CMD-SHELL","bash -c 'exec 3<>/dev/tcp/127.0.0.1/5000' || exit 1"]` ✓.
- **Docs:** DEPLOYMENT.md §8.2 + §10 ghi rõ CẢ compose LẪN Dockerfile dùng TCP /dev/tcp.
- Dọn: temp .env + test container hc-test + aspireproject-pgadmin-1 đã xóa.
