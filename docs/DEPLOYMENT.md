# Hướng dẫn Triển khai — Mirats / AspireReact (Docker Compose)

> **Phiên bản triển khai:** stack Docker Compose production, **auth local** (JWT tự ký HS256 + passkey).
> **Dành cho:** người vận hành triển khai sản phẩm Mirats lên máy chủ (VPS / máy có Docker).
> **Khác với dev:** khi phát triển hàng ngày dùng `.NET Aspire` (`aspire-react/aspire-react.AppHost`),
> xem [docs/DEVELOPMENT_WORKFLOW.md](DEVELOPMENT_WORKFLOW.md). Stack compose này là **production path**
> riêng, không dùng AppHost, không phụ thuộc Aspire Dashboard.
>
> **[FIX-DEPLOY 2026-10-02]** Keycloak đã bị **XÓA HOÀN TOÀN** ở AUTH Phase 5. Tài liệu này đã được
> viết lại theo auth local: không còn service `keycloak`, không còn biến `KEYCLOAK_*`/`KC_BOOTSTRAP_*`,
> không còn bước chạy script seed thủ công (admin đầu tiên do server tự tạo lúc khởi động).

---

## 1. Yêu cầu hệ thống

| Thành phần | Yêu cầu tối thiểu |
|------------|-------------------|
| **Docker Engine** | 24.0+ (khuyến nghị mới nhất) |
| **Docker Compose** | v2 (đi kèm Docker Desktop / plugin compose của Docker) |
| **HĐH** | Windows (Docker Desktop) hoặc Linux/macOS. Các lệnh `bash scripts/*.sh` cần `bash`; Windows dùng `scripts/*.ps1`. |
| **Tài nguyên** | 4 GB RAM khả dụng trở lên (stack gồm Postgres + Redis + .NET API + Nginx). |
| **Port trống** | `5000` (API, đổi được qua `BACKEND_PORT`), `80` (Frontend, đổi qua `FRONTEND_PORT`), `5050` chỉ khi bật profile debug. |
| **TLS (bắt buộc cho domain thật)** | Xem §4.5 — refresh cookie là `Secure`, và passkey/WebAuthn yêu cầu secure context. |

**Không bắt buộc:** .NET SDK, Node.js. Mọi thứ build trong image (multi-stage). Bạn chỉ cần Docker.

---

## 2. Cấu trúc thư mục liên quan

```
<repo root>/
├── .env.example                    # template biến môi trường (bắt buộc copy → .env)
├── docker-compose.yml              # stack production (4 services + pgadmin profile debug)
├── scripts/
│   ├── docker-reset.sh             # reset dữ liệu stack (Linux/macOS)
│   └── docker-reset.ps1            # reset dữ liệu stack (Windows)
├── aspire-react/
│   ├── aspire-react.Server/Dockerfile   # image backend (.NET 10 → aspnet:10.0)
│   └── frontend/
│       ├── Dockerfile                   # image frontend (Node build → Nginx)
│       └── nginx.conf                   # serve SPA + proxy /api → server:5000
```

---

## 3. Quy trình triển khai từng bước

### Bước 1 — Tạo `.env` từ template

```bash
cd <repo root>
cp .env.example .env
```

### Bước 2 — Điền ĐẦY ĐỦ các biến BẮT BUỘC

`docker compose up` sẽ **từ chối chạy** (lỗi interpolation `${VAR:?required}`) nếu thiếu bất kỳ biến BẮT BUỘC nào. Danh sách bắt buộc:

| Biến | Ý nghĩa | Ví dụ |
|------|---------|-------|
| `POSTGRES_PASSWORD` | Mật khẩu DB Postgres | mật khẩu mạnh, không dùng chung nơi khác |
| `REDIS_PASSWORD` | Mật khẩu Redis (prod bật auth) | mật khẩu mạnh |
| `AUTH_SIGNING_KEY` | Khóa ký JWT HS256 — **≥ 32 byte UTF-8** (server fail-fast nếu ngắn hơn) | `openssl rand -base64 48` |
| `AUTH_BOOTSTRAP_ADMIN_PASSWORD` | Mật khẩu **admin đầu tiên** (KHÔNG để `Admin123!`). Khi tạo admin trên DB trắng: **≥ 12 ký tự** và **khác `INITIAL_ADMIN_USERNAME`** (server fail-fast nếu không đạt) | mật khẩu mạnh |
| `CORS_ALLOWED_ORIGINS` | Origin được phép gọi API (CSV). Prod là domain người dùng vào | `https://app.example.com` |

Các biến TÙY CHỌN đã có default hợp lý: `AUTH_ISSUER` (`aspire-react`), `AUTH_AUDIENCE` (`aspire-react-api`), `INITIAL_ADMIN_USERNAME` (`admin`), `INITIAL_ADMIN_EMAIL` (`admin@localhost`), `VITE_API_BASE_URL` (`/api/v1`), `BACKEND_PORT` (`5000`), `FRONTEND_PORT` (`80`), `ASPNETCORE_URLS`, `ASPNETCORE_ENVIRONMENT`, `POSTGRES_DB/USER/PORT`, `REDIS_PORT`.

> ⚠️ `PGADMIN_PASSWORD` **không còn default** (`pgadmin`): compose đọc/validate toàn bộ file trước khi lọc profile,
> nên biến này phải có giá trị ngay cả khi **không** bật `--profile debug` — nếu thiếu, `docker compose` báo lỗi
> interpolation. Chỉ dùng khi bật pgAdmin (§5.1).

> ⚠️ **Không commit `.env`.** File này chứa secret thật và đã có trong `.gitignore`.

### Bước 3 — Build & khởi động stack

```bash
docker compose up -d --build
```

Lần đầu sẽ build 2 image (`server`, `frontend`) — mất vài phút. Sau đó container khởi động theo `depends_on` + healthcheck:

```
Postgres → Redis → Server (migrate + seed + tạo admin đầu tiên) → Frontend
```

Kiểm tra trạng thái:

```bash
docker compose ps            # tất cả "healthy"
docker compose logs -f server
```

Khi 4 services đều **healthy** (server ~30s, frontend ~15s sau khi server healthy), tiếp tục Bước 4.

### Bước 4 — Admin đầu tiên (TỰ ĐỘNG, không cần script)

Ở lần khởi động đầu tiên trên database trắng, `StartupDataSeeder` **tự tạo** user admin:

- username = `INITIAL_ADMIN_USERNAME` (mặc định `admin`), email = `INITIAL_ADMIN_EMAIL`;
- mật khẩu = `AUTH_BOOTSTRAP_ADMIN_PASSWORD` (đã hash PBKDF2), `IsSuperUser = true`, `IsActive = true`;
- **idempotent**: nếu user đã tồn tại thì KHÔNG tạo lại và KHÔNG ghi đè mật khẩu; nếu user tồn tại nhưng chưa có hash thì hash được seed từ biến này (một lần).

Log xác nhận (xem `docker compose logs server`):

```
Bootstrap admin 'admin' created (local auth).
```

> **Không còn script `seed-initial-admin.*`.** Hai script cũ (`.ps1`/`.sh`) gọi Keycloak Admin API và
> đã bị xóa cùng Keycloak ở AUTH Phase 5 — chúng không thể chạy khi không còn Keycloak, nên giữ lại
> chỉ tạo ra artifact "sống nhưng luôn fail". Cơ chế thay thế nằm ngay trong server (mục trên),
> chạy tự động mỗi lần khởi động nên cũng bền hơn khi container bị recreate.
>
> ⚠️ Nếu `INITIAL_ADMIN_EMAIL` đã bị một user khác dùng, seeder **bỏ qua** việc tạo admin và ghi
> warning (`Bootstrap admin not created: email '...' is already used by another user.`) — khi đó đổi
> `INITIAL_ADMIN_EMAIL` sang địa chỉ trống rồi `docker compose restart server`.

### Bước 5 — Xác nhận đăng nhập

1. Mở trình duyệt **http://localhost** (hoặc `http://<server-ip>:<FRONTEND_PORT>`).
2. Đăng nhập bằng **admin đầu tiên** (`INITIAL_ADMIN_USERNAME` / `AUTH_BOOTSTRAP_ADMIN_PASSWORD`).
3. Vào trang Dashboard → phải hiển thị dữ liệu (migrate + seed đã tự chạy lúc server khởi động).
4. **(Khuyến nghị)** vào **Hồ sơ tài khoản → Đổi mật khẩu** để thay mật khẩu bootstrap bằng mật khẩu riêng.

> **Xác minh nhanh API:** `GET http://localhost:5000/api/v1/health` → `200`. Với token hợp lệ (đăng nhập
> qua `POST /api/v1/auth/login`), `GET http://localhost:5000/api/v1/dashboard/summary` → `200`.
>
> ⚠️ **Lưu ý `/health` vs `/api/v1/health`:** `GET /health` và `GET /alive` (từ ServiceDefaults `MapDefaultEndpoints`) **chỉ được map khi `ASPNETCORE_ENVIRONMENT=Development`**. Trong compose prod (`Production`) 2 endpoint này KHÔNG tồn tại → truy cập `/health` trả **404**. Endpoint health đúng của prod là **`/api/v1/health`** (anonymous, luôn có). Healthcheck container dùng TCP-connect (không cần endpoint HTTP) nên container vẫn healthy.

---

## 4. Các kiến thức bắt buộc khi triển khai

### 4.1 Mô hình auth — local, không còn Keycloak

| Thành phần | Cơ chế |
|---|---|
| **Đăng nhập** | `POST /api/v1/auth/login` (username + mật khẩu local, hash PBKDF2) hoặc **passkey/WebAuthn** (`/api/v1/auth/passkeys/*`, bật/tắt bằng SystemSetting `auth.passkeys.enabled`) |
| **Access token** | JWT **tự ký HS256** bằng `AUTH_SIGNING_KEY`; TTL 15 phút (10 phút khi đang buộc đổi mật khẩu) |
| **Refresh token** | Cookie **httpOnly + Secure + SameSite=Lax**, path `/api/v1/auth`, TTL 7 ngày; **rotate mỗi lần refresh** + reuse-detection (trình token đã dùng → thu hồi toàn bộ session của user) |
| **Buộc đổi mật khẩu** | User tạo mới/reset bởi admin có `MustChangePassword`; token giới hạn chỉ gọi được `/api/v1/users/me` + `/api/v1/auth/password`, mọi endpoint khác trả **403 `MUST_CHANGE_PASSWORD`** |
| **Chống brute-force** | Theo username: 5 lần sai → khóa 60s, các lần sau tăng dần (2^n, tối đa 15 phút). Theo IP: 30 lần sai/phút |
| **Phân quyền** | Policy `[Authorize(Policy = "<resource>.<action>")]` theo `PermissionCatalog`; superuser (`IsSuperUser`) bypass |

**Hệ quả vận hành quan trọng:**

- **Đổi `AUTH_SIGNING_KEY` = vô hiệu mọi access token đã cấp** (mọi user phải refresh/đăng nhập lại).
  Refresh token vẫn dùng được (lưu dạng hash, không phụ thuộc khóa ký).
  Dùng khi cần "đăng xuất toàn hệ thống" khẩn cấp, hoặc khi rotate khóa định kỳ.
- **Mất `AUTH_SIGNING_KEY` = không ai đăng nhập được** (server throw lúc khởi động nếu thiếu).
  Giữ secret này trong `.env` (không commit) + backup an toàn.
- Đổi `AUTH_BOOTSTRAP_ADMIN_PASSWORD` sau khi admin đã có hash **không** đổi mật khẩu trong DB —
  dùng UI đổi mật khẩu hoặc `POST /api/v1/users/{id}/reset-password` (admin).

### 4.2 Không còn 2 loại admin

Trước đây có 2 loại admin riêng biệt (`INITIAL_ADMIN_*` cho ứng dụng + `KC_BOOTSTRAP_*` cho Keycloak
master). **Keycloak đã bị xóa** → chỉ còn **một** loại: admin ứng dụng (`INITIAL_ADMIN_*` +
`AUTH_BOOTSTRAP_ADMIN_PASSWORD`). Không còn Admin Console để quản trị realm/client.

### 4.3 Không gian mạng & cổng

| Service | Container (internal) | Host (mặc định) | Biến host port |
|---------|----------------------|-----------------|----------------|
| Postgres | 5432 | **không expose** (chỉ trong network) | — |
| Redis | 6379 | **không expose** | — |
| API | 5000 | 5000 | `BACKEND_PORT` |
| Frontend | 80 | 80 | `FRONTEND_PORT` |
| pgAdmin (debug) | 80 | 5050 | `PGADMIN_PORT` |

> Postgres/Redis **không expose ra host** trong compose prod (bảo mật VPS công khai). Muốn truy cập từ host khi debug → xem §5.2.

### 4.4 Vite build-time args

`VITE_*` được **bake vào bundle lúc build** (ARG/ENV trong `frontend/Dockerfile`), không phải runtime env. Compose truyền qua `build.args`:

```
VITE_API_BASE_URL  = ${VITE_API_BASE_URL:-/api/v1}   (base SERVER — frontend tự nối /api/v1, không double)
```

**Ngữ nghĩa `VITE_API_BASE_URL`:** đây là base của **SERVER** (không kèm `/api/v1`).
`api-client.ts` nối thêm `/api/v1` (và đã xử lý trường hợp base đã kết thúc bằng `/api/v1`
để không tạo `/api/v1/api/v1`). Giá trị prod đúng: `/api/v1` (same-origin qua Nginx,
không CORS prod). Giá trị dev: `http://localhost:5428`.

> ⚠️ **Lỗi thực tế đã gặp (DOCKER-8):** nếu `VITE_API_BASE_URL=/api/v1` mà `api-client.ts`
> nối thêm `/api/v1` → request thành `/api/v1/api/v1/...` → toàn bộ dashboard 404.
> Đã fix: `api-client.ts` kiểm tra `endsWith('/api/v1')` trước khi nối.

> ⚠️ **[FIX-DEPLOY 2026-10-02] Dev HTTPS cert KHÔNG được yêu cầu khi build image.** Trước đây
> `vite.config.ts` kiểm tra cert dev ở bước load config cho **mọi** command → `npm run build` trong
> image production (không có `certs-dev/`) **throw** → `docker compose up --build` fail hoàn toàn.
> Nay yêu cầu cert chỉ áp dụng cho `vite serve` (dev server vẫn fail loud nếu thiếu cert — chính sách
> AUTH §4.2 giữ nguyên).

### 4.5 TLS — bắt buộc cho mọi triển khai không phải localhost

Frontend container chỉ serve **HTTP** (Nginx). Nhưng:

1. **Refresh cookie có cờ `Secure`** → trình duyệt chỉ lưu khi kết nối HTTPS. Trên `http://localhost`
   các trình duyệt hiện đại vẫn chấp nhận (localhost là secure context), nhưng với domain/IP thật qua
   HTTP cookie sẽ bị **loại bỏ** ⇒ đăng nhập "được" nhưng mất phiên ngay khi access token hết hạn (15 phút).
2. **Passkey/WebAuthn yêu cầu secure context** (HTTPS) — không hoạt động qua HTTP trên domain thật.

**Khuyến nghị:** đặt reverse proxy (Nginx/Caddy/Traefik) hoặc LB terminate TLS trước stack, trỏ về
`FRONTEND_PORT`, và set `CORS_ALLOWED_ORIGINS=https://<domain>`.

**HSTS — đặt tại nơi kết thúc TLS, KHÔNG đặt trong app:** API và frontend Nginx trong repo chỉ chạy HTTP
(bên trong mạng Docker), nên không đủ thông tin về scheme công khai. Cấu hình tại **Nginx Proxy Manager**
(hoặc LB/TLS terminator):

```nginx
add_header Strict-Transport-Security "max-age=15552000; includeSubDomains" always;
```

Kiểm tra bằng `curl -sI https://<domain> | grep -i strict-transport-security`. Khi đổi domain hoặc tạm thời
bỏ HTTPS, nhớ xoá HSTS trên proxy để tránh trình duyệt ép HTTPS trong thời gian dài.

---

## 5. Debug / Development

### 5.1 Bật pgAdmin (profile `debug`)

```bash
docker compose --profile debug up -d pgadmin
# BẮT BUỘC set PGADMIN_PASSWORD trong .env (không còn default "pgadmin")
# truy cập http://localhost:5050  (login: PGADMIN_DEFAULT_EMAIL / PGADMIN_PASSWORD từ .env)
# host DB để kết nối trong pgAdmin: tên service `postgres` (cần map cổng — xem §5.2)
```

### 5.2 Expose Postgres/Redis ra host khi debug (docker-compose.override.yml)

Tạo file `docker-compose.override.yml` ở repo root (**cục bộ, KHÔNG commit**):

```yaml
# docker-compose.override.yml — CHỈ DÙNG CỤC BỘ, KHÔNG COMMIT
services:
  postgres:
    ports:
      - "5432:5432"
  redis:
    ports:
      - "6379:6379"
```

Compose tự đọc override khi chạy `docker compose up -d`. Sau khi hết nhu cầu debug, **xóa file override**
rồi `docker compose up -d` lại. Cách khác: `docker exec -it <postgres-container> psql -U postgres`.

> ⚠️ **Ràng buộc:** `docker-compose.override.yml` phải là file cục bộ tạm thời. Đừng commit — nó vô hiệu
> hóa tính an toàn "không expose DB" của compose prod.

### 5.3 Xem logs / kiểm tra health

```bash
docker compose ps                      # trạng thái + health
docker compose logs -f server          # log backend (migrate/seed/bootstrap admin)
docker compose exec postgres psql -U postgres -d aspire-react-db   # SQL trực tiếp
docker compose exec redis redis-cli -a "$REDIS_PASSWORD" ping       # → PONG
```

---

## 6. Reset toàn bộ dữ liệu (khởi tạo lại từ đầu)

> ⚠️ **Hành động KHÔNG thể hoàn tác.** Script hỏi xác nhận trước khi xóa.

```bash
# Windows (PowerShell)
powershell -File scripts/docker-reset.ps1

# Linux/macOS
bash scripts/docker-reset.sh
```

Script sẽ:
1. Liệt kê các volume `mirats-*` sẽ xóa + hiển thị **cảnh báo** volume dev Aspire (`postgres-data`, `redis-data`) **không đụng tới**.
2. Hỏi gõ `yes` để xác nhận (nếu không, thoát ngay, không xóa gì).
3. `docker compose down -v` → xóa containers + **volume production**: `mirats-postgres-data`, `mirats-redis-data`.
4. In hướng dẫn khởi tạo lại: `cp .env.example .env` → `up -d --build` (admin đầu tiên tự tạo khi server khởi động).

**An toàn:** script chỉ xóa volume có tiền tố `mirats-`. Volume dev Aspire (`postgres-data`/`redis-data`)
**luôn được giữ nguyên**, kể cả khi compose fail.

---

## 7. Giới hạn hiện tại — cần làm trước khi phơi ra internet

| Hạng mục | Trạng thái |
|---|---|
| **TLS** | Stack không tự terminate TLS — **phải** đặt reverse proxy/LB phía trước (§4.5) |
| **Security headers** | API + frontend Nginx đã gửi `X-Content-Type-Options` / `X-Frame-Options` / `Referrer-Policy` / `Permissions-Policy`; **HSTS đặt tại NPM** (nơi kết thúc TLS); CSP để P1 |
| **Backup** | Chưa có backup tự động. Cần `pg_dump` định kỳ + backup secret (`AUTH_SIGNING_KEY`, `.env`) |
| **Rotate khóa ký** | Thủ công: đổi `AUTH_SIGNING_KEY` + `docker compose up -d server` (mọi access token cũ vô hiệu) |
| **Rate limit tầng ngoài** | Chống brute-force trong app là per-username/per-IP process-local; nên thêm rate limit ở reverse proxy |
| **Passkey** | Cần HTTPS (§4.5); bật/tắt bằng SystemSetting `auth.passkeys.enabled` |

---

## 8. Troubleshooting — các lỗi thực tế đã gặp khi xây dựng stack

### 8.1 Postgres mount path sai (PG 18)

- **Triệu chứng:** Postgres container **không healthy**, logs: `PostgreSQL Database directory appears to contain a database; skipping initialization` hoặc `chmod: changing permissions of '/var/lib/postgresql/data': Operation not permitted`; hoặc crash loop.
- **Nguyên nhân:** PG 18 yêu cầu mount **parent** `/var/lib/postgresql` (chứa cả `data` + cấu hình khác), không mount trực tiếp `/var/lib/postgresql/data` như PG cũ.
- **Khắc phục:** volume mount đúng `postgres-data:/var/lib/postgresql` (như trong `docker-compose.yml` hiện tại).

### 8.2 Image base không có `wget`/`curl`

- **Triệu chứng:** healthcheck của Server **fail vĩnh viễn** (`Unhealthy`) dù service hoạt động; log healthcheck `wget: not found`.
- **Nguyên nhân:** `mcr.microsoft.com/dotnet/aspnet:10.0` không cài wget/curl.
- **Khắc phục:** healthcheck dùng **bash `/dev/tcp`**: `bash -c 'exec 3<>/dev/tcp/127.0.0.1/5000'`. Frontend (nginx:alpine) có busybox `wget` nên dùng wget được. Đã áp dụng cho CẢ `docker-compose.yml` LẪN `aspire-react.Server/Dockerfile`.

### 8.3 Build frontend fail: "Dev HTTPS certificates not found"

- **Triệu chứng:** `docker compose up --build` fail ở bước `frontend` với
  `[AUTH §4.2] Dev HTTPS certificates not found in /certs-dev`.
- **Nguyên nhân:** `vite.config.ts` (AUTH Phase 2) yêu cầu cert dev cho MỌI command, kể cả `vite build`.
- **Khắc phục:** đã fix [FIX-DEPLOY 2026-10-02] — yêu cầu cert chỉ còn ở `vite serve`. Nếu gặp ở bản cũ,
  cập nhật `vite.config.ts` và build lại (`docker compose build frontend`).

### 8.4 Mọi API trả 401 ngay sau khi đăng nhập

- **Triệu chứng:** đăng nhập trả token nhưng request sau đó 401.
- **Nguyên nhân thường gặp:** (a) server đang chạy với `AUTH_SIGNING_KEY` khác với khóa đã cấp token
  (VD đổi `.env` mà không restart server); (b) `AUTH_ISSUER`/`AUTH_AUDIENCE` bị đổi lệch giữa cấu hình.
- **Khắc phục:** đảm bảo `AUTH_*` nhất quán rồi `docker compose up -d server` (recreate để nạp env mới);
  đăng nhập lại. Kiểm tra log server khi khởi động — thiếu `AUTH_SIGNING_KEY` sẽ throw ngay.

### 8.5 Đăng nhập "thành công" nhưng bị đăng xuất sau ~15 phút (mất phiên)

- **Triệu chứng:** dùng được một lúc rồi bị đẩy về trang đăng nhập, không tự gia hạn phiên.
- **Nguyên nhân:** refresh cookie có cờ **`Secure`** → không được lưu khi truy cập qua **HTTP domain thật**
  (khác localhost).
- **Khắc phục:** đặt TLS phía trước (§4.5) và truy cập bằng `https://`.

### 8.6 `docker compose down -v` báo "required variable ... is missing"

- **Triệu chứng:** `docker compose` fail ngay khi parse vì thiếu `.env` hoặc biến BẮT BUỘC trống.
- **Nguyên nhân:** `${VAR:?required}` — đúng thiết kế, bảo vệ khỏi dựng stack thiếu secret.
- **Khắc phục:** điền đủ `.env` (§3 Bước 2). Nếu chỉ muốn **reset dữ liệu**, script `docker-reset.*` tự xử lý
  trường hợp này (fallback dọn container + volume không cần compose parse) — xem §6.

### 8.7 Không tạo được admin đầu tiên

- **Triệu chứng:** log server có `Bootstrap admin not created: email '...' is already used by another user.`
  hoặc không thấy dòng `Bootstrap admin '...' created`.
- **Nguyên nhân:** `INITIAL_ADMIN_EMAIL` trùng email của user khác (unique index), hoặc
  `AUTH_BOOTSTRAP_ADMIN_PASSWORD` để trống (seeder bỏ qua khi không có mật khẩu).
- **Khắc phục:** đặt `INITIAL_ADMIN_EMAIL` chưa dùng + `AUTH_BOOTSTRAP_ADMIN_PASSWORD` khác rỗng, rồi
  `docker compose restart server`. Seeder chỉ tạo khi user **chưa** tồn tại và không ghi đè hash đã có.

### 8.8 Trình duyệt báo ERR_TOO_MANY_REDIRECTS sau khi bật tunnel

- **Triệu chứng:** mở domain qua tunnel thì trình duyệt báo lặp chuyển hướng vô hạn.
- **Nguyên nhân:** cloudflared kết nối NPM bằng **HTTP** trong khi proxy host bật
  **Force SSL** — NPM trả `301` về `https://...` (đã đo bằng NPM thật: HTTP kèm cả
  `X-Forwarded-Proto: https` vẫn bị 301, không forward). Trình duyệt theo về
  chính domain qua tunnel, lặp lại từ đầu.
- **Khắc phục:** chuyển cloudflared sang `https://<NPM-host>:443` +
  `originRequest.originServerName = <domain>` (xem §11.3). NPM đã có cert LE hợp
  lệ nên verify chuẩn, không dùng `noTLSVerify`.

---

## 9. Biến môi trường tham chiếu đầy đủ

> Nguồn chính thức: `.env.example` (giữ comment chi tiết). Bảng dưới là tóm tắt.

**BẮT BUỘC (compose fail nếu thiếu):** `POSTGRES_PASSWORD`, `REDIS_PASSWORD`, `AUTH_SIGNING_KEY`,
`AUTH_BOOTSTRAP_ADMIN_PASSWORD`, `CORS_ALLOWED_ORIGINS`, `PGADMIN_PASSWORD` (validate ở bước đọc file — xem
ghi chú §3 Bước 2).

**TÙY CHỌN (có default):**

| Nhóm | Biến | Default |
|------|------|---------|
| Postgres | `POSTGRES_DB`, `POSTGRES_USER`, `POSTGRES_PORT` | `aspire-react-db`, `postgres`, `5432` |
| Redis | `REDIS_PORT` | `6379` |
| Auth | `AUTH_ISSUER`, `AUTH_AUDIENCE`, `INITIAL_ADMIN_USERNAME`, `INITIAL_ADMIN_EMAIL` | `aspire-react`, `aspire-react-api`, `admin`, `admin@localhost` |
| Server | `ASPNETCORE_URLS`, `ASPNETCORE_ENVIRONMENT` | `http://+:5000`, `Production` |
| Frontend | `VITE_API_BASE_URL` | `/api/v1` |
| Ports | `BACKEND_PORT`, `FRONTEND_PORT` | `5000`, `80` |
| pgAdmin (debug) | `PGADMIN_DEFAULT_EMAIL`, `PGADMIN_PASSWORD`, `PGADMIN_PORT` | `pgadmin@example.com`, **(bắt buộc — không default)**, `5050` |
| NPM (chỉ `docker-compose.npm.yml`) | `NPM_NETWORK_NAME`, `PROXY_KNOWN_NETWORKS`, `PROXY_FORWARD_LIMIT`, `WEBAUTHN_RPID`, `WEBAUTHN_ORIGINS` | (không default — bắt buộc, trừ `PROXY_FORWARD_LIMIT=2`) |

> Ghi chú security: `CORS_ALLOWED_ORIGINS` được backend đọc khi khởi động (Program.cs) — đổi xong nhớ
> `docker compose restart server`. `VITE_*` là build-time — đổi xong phải rebuild frontend (§4.4).
>
> **Derived (compose tự suy ra, không cần điền tay):** `Auth__SigningKey`, `Auth__Issuer`, `Auth__Audience`,
> `Auth__BootstrapAdminPassword`, `ConnectionStrings__aspire-react-db`, `ConnectionStrings__cache`,
> `INITIAL_ADMIN_USERNAME/EMAIL` (truyền vào container cho seeder).

---

## 10. Health Checks & Monitoring (tóm tắt)

- **Liveness/readiness:** `/api/v1/health` → 200 (prod). `/health` + `/alive` chỉ tồn tại khi
  `ASPNETCORE_ENVIRONMENT=Development`. Healthcheck compose dùng TCP-connect: Postgres `pg_isready`,
  Redis `redis-cli ping`, Server `/dev/tcp/5000`, Frontend `wget`. `aspire-react.Server/Dockerfile`
  cũng dùng TCP `/dev/tcp/5000` (không dùng HTTP `/health` — image không có wget + `/health` không tồn tại ở Production).
- **Logs:** `docker compose logs -f` (mọi service). OpenTelemetry/traces chỉ hoạt động khi chạy dưới Aspire
  AppHost (dev) — compose prod không có Aspire Dashboard; logs lấy từ stdout container.
- **Security headers:** API (`SecurityHeadersMiddleware`) và frontend Nginx gửi `X-Content-Type-Options: nosniff`,
  `X-Frame-Options: DENY`, `Referrer-Policy: strict-origin-when-cross-origin`, `Permissions-Policy` tối thiểu.
  **HSTS cấu hình tại NPM** (§4.5) — KHÔNG đặt trong app/frontend (chỉ chạy HTTP nội bộ). CSP: P1.
- **Backup (khuyến nghị):** Postgres — `pg_dump` định kỳ; secret — backup `.env` (đặc biệt `AUTH_SIGNING_KEY`).
  Xem §7.

---

## 11. Triển khai sau Nginx Proxy Manager (cấu hình b → c)

> Ba cấu hình truy cập: **(a)** trực tiếp container frontend bằng IP (hiện tại) →
> **(b)** qua NPM bằng domain nội bộ (HTTPS, đích gần) → **(c)** thêm Cloudflare
> Tunnel public (sau này). Backend phân biệt IP client thật nhờ middleware
> trusted-proxy (`Proxy__KnownNetworks` + `Proxy__ForwardLimit`, fail-closed khi
> không cấu hình) — xem `aspire-react.Infrastructure/Networking/`.

### 11.1 Vì sao KHÔNG hỗ trợ truy cập bằng IP (đã xác nhận bằng thực nghiệm)

1. **Cookie refresh có cờ `Secure`** → trình duyệt bỏ qua qua HTTP → hết access
   token (~15 phút) là bị đăng xuất âm thầm (triệu chứng §8.5; đã tái hiện:
   login HTTP 200 + `Set-Cookie: ... secure`, refresh không cookie → 400).
2. **Mật khẩu + dữ liệu đi plaintext** trên mạng nội bộ.
3. **Passkey không hoạt động trên IP** (giới hạn WebAuthn — RP phải là domain HTTPS).
4. **Mọi request backend thấy cùng 1 IP** (IP container frontend — đã tái hiện:
   6 login sai với 2 `X-Forwarded-For` khác nhau đều ghi `::ffff:172.22.0.5`) →
   ngưỡng brute-force theo IP (30 lỗi/phút → `ACCOUNT_LOCKED` toàn bộ user) có
   thể khóa đăng nhập cả hệ thống.

### 11.2 Chuyển từ (a) sang (b) — NPM + domain nội bộ

> **Tuyến khuyến nghị cho (b): NPM → frontend nginx → backend** (đã đo header
> đầy đủ ở bước 6). Không dùng tuyến NPM → backend trực tiếp: tuyến đó đòi
> CORS + phải serve static bundle ở chỗ khác, trong khi frontend nginx đã làm
> sẵn same-origin (`/api` proxy về `server:5000`, giữ `Host`/`XFF`, giữ XFP
> gốc) — một origin duy nhất cho browser, không CORS prod.

1. **Cloudflare API token** (quyền Edit zone DNS) — chỉ dùng để NPM xin chứng chỉ
   Let's Encrypt qua **DNS challenge**, không public gì.
2. **Bản ghi A trên DNS NỘI BỘ** (split DNS, VD Pi-hole/AD DNS) trỏ domain về IP
   NPM. KHÔNG tạo bản ghi trên Cloudflare public DNS.
3. **NPM → Proxy Hosts → Add**: domain + scheme `http`, forward host/IP NPM thấy
   được container frontend (cùng docker network — xem bước 5), port 80; tab
   **Custom locations** thêm location `/api` trỏ cùng upstream (frontend nginx
   proxy `/api` về `server:5000` sẵn); tab **SSL**: DNS challenge Cloudflare
   (credentials = API token bước 1), bật **Force SSL**. Không cần custom Nginx
   config: NPM mặc định ghi đè `X-Forwarded-Proto` thành `http` (đã kiểm chứng
   bằng nginx cùng hành vi), nhưng NPM luôn gửi kèm `X-Forwarded-Scheme: https`
   (scheme nó terminate) — backend tự suy `Scheme=https` từ header này khi
   `X-Forwarded-Proto` vắng/không phải https (đã unit-test). Giữ
   `Host` nguyên (mặc định NPM) — backend dùng `Host` để sinh URL QR Labels.
4. **NPM KHÔNG rewrite Host** (mặc định giữ nguyên) — backend dùng `Host` để sinh
   URL QR Labels; rewrite Host sẽ làm QR sai domain.
5. **Dựng Mirats bằng override**: `docker compose -f docker-compose.yml -f
   docker-compose.npm.yml up -d --build` (frontend + server join network
   external của NPM, gỡ publish port trực tiếp, bơm `Proxy__*` +
   `Auth__WebAuthn__*`). Yêu cầu **Docker Compose ≥ 2.24** (file override dùng
   tag `!reset` để gỡ `ports` — bản cũ hơn không hiểu tag này và fail khi parse).
6. Verify: `https://<domain>/api/v1/health` → 200; login → refresh tự gia hạn;
   brute-force 2 IP khác nhau đếm riêng; ActionLog `RemoteIp` = IP client thật.

### 11.3 Chuyển từ (b) sang (c) — thêm Cloudflare Tunnel

1. Trên dashboard Cloudflare: **Public Hostname** cùng domain, service trỏ về
   **`https://<NPM-host>:443`** (xem bước 2 — tunnel BẮT BUỘC gọi NPM bằng HTTPS).
2. **Force SSL ở NPM GIỮ BẬT** (đúng ở mọi cấu hình — browser và tunnel đều vào
   NPM bằng HTTPS). Đã đo bằng NPM thật: request **HTTP** (kèm cả
   `X-Forwarded-Proto: https` như cloudflared gửi) vào host Force SSL → NPM trả
   **`301` về `https://...`, KHÔNG forward** (logproxy sau NPM không nhận gì) —
   tunnel gọi HTTP sẽ gãy tại đây. Đây là hậu quả khi làm sai, không phải vòng
   lặp redirect (không quan sát thấy loop trong mọi phép đo).
3. **cloudflared → NPM BẮT BUỘC bằng HTTPS** (từ "tăng cường tùy chọn" thành yêu
   cầu cứng): `originRequest: { originServerName: "<domain>" }`, cổng 443.
   NPM đã giữ chứng chỉ Let's Encrypt hợp lệ của domain nên verify chuẩn public
   CA — **KHÔNG dùng `noTLSVerify`**. Đã đo đường HTTPS: request tới backend
   (400 từ app = đã forward), header `XFP=https, XS=https`, Host giữ nguyên.
4. **Khuyến nghị Cloudflare Access** (Zero Trust) trước NPM cho (c).
5. Backend: thêm IP egress của `cloudflared` vào `PROXY_KNOWN_NETWORKS`, nâng
   `PROXY_FORWARD_LIMIT` lên 3, và bật `PROXY_TRUST_CLOUDFLARE=true` để backend
   ưu tiên `CF-Connecting-IP`. **Điều kiện an toàn:** NPM ở (c) CHỈ được nhận
   kết nối qua tunnel (firewall chặn truy cập trực tiếp vào NPM) — nếu NPM vẫn
   reachable trực tiếp, client có thể giả `CF-Connecting-IP` và mạo danh IP bất
   kỳ. Ở (a)/(b) giữ `false` (mặc định): header này bị bỏ qua vô điều kiện.
6. Hành vi header thật của NPM (đã đo bằng image `jc21/nginx-proxy-manager`
   chính thức, proxy host HTTPS với cert tự ký, request browser-like không kèm
   `X-Forwarded-Proto`):

   | Đầu vào NPM | NPM gửi đi: XFP | XS | XFF |
   |---|---|---|---|
   | HTTP + `XFP:https` sẵn có (mô phỏng edge) | `https` (giữ nguyên) | `http` (scheme chặng nó) | append đúng |
   | HTTP, không XFP | `http` (tự đặt) | `http` | append đúng |
   | **HTTPS, không XFP (đúng như browser ở (b))** | **`https`** | **`https`** | append đúng, Host giữ nguyên |

   Hệ quả: ở (b)/(c) cấu hình đúng, backend luôn nhận `X-Forwarded-Proto=https`
   → `Request.Scheme=https` trực tiếp, QR sinh link https. Suy luận dự phòng từ
   `X-Forwarded-Scheme` trong code **không kích hoạt** ở cấu hình đúng — nó chỉ
   còn là **lớp phòng thủ** cho proxy lạ tự ghi đè XFP thành http (không xóa,
   giữ nguyên vì vô hại: chỉ chạy khi XFP vắng/http + peer tin cậy + XS=https).
7. Khi `Proxy__KnownNetworks` để trống ngoài Development, backend log WARNING
   lúc khởi động và bỏ qua mọi header proxy (fail-closed) — brute-force/audit
   sẽ thấy IP container proxy.

### 11.4 Biến môi trường bắt buộc cho từng cấu hình

| Biến | (a) trực tiếp | (b) NPM | (c) + tunnel |
|---|---|---|---|
| `POSTGRES_PASSWORD`, `REDIS_PASSWORD`, `AUTH_SIGNING_KEY`, `AUTH_BOOTSTRAP_ADMIN_PASSWORD`, `CORS_ALLOWED_ORIGINS` | Bắt buộc | Bắt buộc | Bắt buộc |
| `NPM_NETWORK_NAME` | — | Bắt buộc | Bắt buộc |
| `PROXY_KNOWN_NETWORKS` | Khuyến nghị (mặc định loopback = bỏ qua header) | Bắt buộc | Bắt buộc (+ IP cloudflared) |
| `PROXY_FORWARD_LIMIT` | 1 | 2 | 3 |
| `PROXY_TRUST_CLOUDFLARE` | `false` | `false` | `true` (chỉ khi NPM tunnel-only, có firewall) |
| `WEBAUTHN_RPID`, `WEBAUTHN_ORIGINS` | — (passkey không chạy trên IP) | Bắt buộc | Bắt buộc |
| `FRONTEND_PORT`, `BACKEND_PORT` | Mở (truy cập IP) | Bị gỡ bởi override (đóng cửa IP) | Bị gỡ |

*Hết hướng dẫn triển khai.*
