# Hướng dẫn Triển khai — Mirats / AspireReact (Docker Compose)

> **Phiên bản triển khai:** stack Docker Compose production (DOCKER-1..7).
> **Dành cho:** người vận hành triển khai sản phẩm Mirats lên máy chủ (VPS / máy có Docker).
> **Khác với dev:** khi phát triển hàng ngày dùng `.NET Aspire` (`aspire-react/aspire-react.AppHost`),
> xem [docs/DEVELOPMENT_WORKFLOW.md](DEVELOPMENT_WORKFLOW.md). Stack compose này là **production path**
> riêng, không dùng AppHost, không phụ thuộc Aspire Dashboard.

---

## 1. Yêu cầu hệ thống

| Thành phần | Yêu cầu tối thiểu |
|------------|-------------------|
| **Docker Engine** | 24.0+ (khuyến nghị mới nhất) |
| **Docker Compose** | v2 (đi kèm Docker Desktop / plugin compose của Docker) |
| **HĐH** | Windows (Docker Desktop) hoặc Linux/macOS. Các lệnh `bash scripts/*.sh` cần `bash`; Windows dùng `scripts/*.ps1`. |
| **Tài nguyên** | 4 GB RAM khả dụng trở lên (stack gồm Postgres + Redis + Keycloak + .NET API + Nginx). |
| **Port trống** | `8080` (Keycloak), `5000` (API), `80` (Frontend), `5432`/`6379`/`5050` chỉ khi bật debug/profile (xem §5). |

**Không bắt buộc:** .NET SDK, Node.js. Mọi thứ build trong image (multi-stage). Bạn chỉ cần Docker.

---

## 2. Cấu trúc thư mục liên quan

```
<repo root>/
├── .env.example                    # template biến môi trường (bắt buộc copy → .env)
├── docker-compose.yml              # stack production (5 services)
├── scripts/
│   ├── docker-reset.sh             # reset dữ liệu stack (Linux/macOS)
│   ├── docker-reset.ps1            # reset dữ liệu stack (Windows)
│   ├── seed-initial-admin.sh       # seed admin ứng dụng (Linux/macOS)
│   └── seed-initial-admin.ps1      # seed admin ứng dụng (Windows)
├── aspire-react/
│   ├── aspire-react.Server/Dockerfile   # image backend (.NET 10 → aspnet:10.0)
│   ├── aspire-react-realm.json          # realm skeleton Keycloak (không users/secret)
│   └── frontend/
│       ├── Dockerfile                   # image frontend (Node build → Nginx)
│       └── nginx.conf                   # serve SPA + proxy /api → server:5000
```

---

## 3. Quy trình triển khai từng bước

> **Đọc kỹ mục 4 trước** — có 2 loại admin riêng biệt, dễ nhầm.

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
| `KEYCLOAK_BACKEND_CLIENT_SECRET` | Secret của confidential client `backend-service` (Keycloak) — **phải trùng** secret trong realm import | UUID/chuỗi ngẫu nhiên dài |
| `CORS_ALLOWED_ORIGINS` | Origin được phép gọi API (CSV). Prod là domain người dùng vào | `https://app.example.com` |
| `INITIAL_ADMIN_USERNAME` | Tên đăng nhập **admin ứng dụng Mirats** (xem 5a) | `admin` |
| `INITIAL_ADMIN_EMAIL` | Email **admin ứng dụng** | `admin@example.com` |
| `INITIAL_ADMIN_PASSWORD` | Mật khẩu **admin ứng dụng** (KHÔNG để `Admin123!`) | mật khẩu mạnh |
| `KC_BOOTSTRAP_ADMIN_USERNAME` | Username **Keycloak master admin** (xem 5b) | `kcadmin` |
| `KC_BOOTSTRAP_ADMIN_PASSWORD` | Password **Keycloak master admin** | mật khẩu mạnh, khác `INITIAL_ADMIN_PASSWORD` |

Các biến TÙY CHỌN đã có default hợp lý (realm name, port, `VITE_API_BASE_URL=/api/v1`, `FRONTEND_URL`, `KEYCLOAK_INTERNAL_URL`/`KEYCLOAK_PUBLIC_URL`). Chỉ đổi khi bạn biết mình đang làm gì (đặc biệt 2 biến URL Keycloak — xem §4.3).

> ⚠️ **Không commit `.env`.** File này chứa secret thật và đã có trong `.gitignore`.

### Bước 3 — Build & khởi động stack

```bash
docker compose up -d --build
```

Lần đầu sẽ build 2 image (`server`, `frontend`) — mất vài phút. Sau đó container khởi động:
Postgres → Redis → Keycloak → Server → Frontend (theo `depends_on` + healthcheck).

Kiểm tra trạng thái:

```bash
docker compose ps            # tất cả "healthy"
docker compose logs -f server
```

Khi 5 services đều **healthy** (server ~24-26s, frontend ~18-20s sau khi keycloak ready), tiếp tục Bước 4.

### Bước 4 — Seed admin ứng dụng lần đầu (BẮT BUỘC 1 lần)

Keycloak tự tạo **master admin** (5b) nhưng **KHÔNG** tự tạo admin ứng dụng (5a) — phải chạy script seed:

```bash
# Linux/macOS
bash scripts/seed-initial-admin.sh

# Windows (PowerShell)
powershell -File scripts/seed-initial-admin.ps1
```

Script đọc `INITIAL_ADMIN_*` + `KC_BOOTSTRAP_*` từ `.env` (nếu bạn đang ở repo root và Docker Compose đã load) — nếu không, export trước:

```bash
export KEYCLOAK_PUBLIC_URL=http://localhost:8080
export INITIAL_ADMIN_USERNAME=admin INITIAL_ADMIN_EMAIL=admin@example.com INITIAL_ADMIN_PASSWORD='...'
bash scripts/seed-initial-admin.sh
```

**Aspire dev (HTTPS self-signed, cần khi đã xóa volume `keycloak-data`):**

```powershell
# Keycloak dev chạy HTTPS self-signed trên port động (xem `docker ps` → 8443/tcp -> 127.0.0.1:<port>).
# Script tự động bypass cert khi -KeycloakUrl là https://localhost|127.0.0.1; thêm -SkipCertCheck
# nếu host khác mà cert vẫn self-signed.

# Windows (PowerShell 5.1/7+)
powershell -File scripts/seed-initial-admin.ps1 -KeycloakUrl https://localhost:<port>
# Ví dụ thực tế: docker port keycloak-twpvcyak → 63096
powershell -File scripts/seed-initial-admin.ps1 -KeycloakUrl https://localhost:63096

# Linux/macOS
KEYCLOAK_PUBLIC_URL=https://localhost:<port> bash scripts/seed-initial-admin.sh
```

> **Lưu ý cert:** Aspire dev dùng `dotnet dev-certs` (self-signed). Trên Windows, PowerShell 5.1 dùng Git `curl` (OpenSSL) để bypass, không dùng `ServicePointManager` (Schannel sẽ lỗi `SEC_E_NO_CREDENTIALS` trong môi trường DSH). Luồng Docker Compose (`http://keycloak:8080` / `http://localhost:8080`) là HTTP nên **không bị ảnh hưởng** — bypass chỉ bật cho `https://localhost`/`127.0.0.1`.

Script **idempotent** (chạy lại an toàn): tạo user → set password → gán realm role `admin`
→ user đăng nhập lần đầu sẽ được JIT-provisioning tạo bản ghi local `User` với `IsSuperUser=true`.

Kết quả mong đợi (dòng cuối):
```
Done. User '<username>' is ready - log in at the app to trigger JIT local provisioning.
```

### Bước 5 — Xác nhận đăng nhập

1. Mở trình duyệt **http://localhost:80** (hoặc `http://<server-ip>:<FRONTEND_PORT>`).
2. Đăng nhập bằng **admin ứng dụng** (`INITIAL_ADMIN_USERNAME` / `INITIAL_ADMIN_PASSWORD`).
3. Vào trang Dashboard → phải hiển thị dữ liệu (migrate + seed hệ thống đã tự chạy lúc server khởi động).
4. (Tùy chọn) Đăng nhập Keycloak Admin Console tại **http://localhost:8080/admin** bằng **master admin** (5b) để quản trị realm/client.

> **Xác minh nhanh API:** `GET http://localhost:5000/api/v1/health` → `200` (endpoint health chính thức của prod — xem ghi chú bên dưới). Với token hợp lệ,
> `GET http://localhost:5000/api/v1/dashboard/summary` → `200`.
>
> ⚠️ **Lưu ý `/health` vs `/api/v1/health`:** `GET /health` và `GET /alive` (từ ServiceDefaults `MapDefaultEndpoints`) **chỉ được map khi `ASPNETCORE_ENVIRONMENT=Development`**. Trong compose prod (`ASPNETCORE_ENVIRONMENT=Production`) 2 endpoint này KHÔNG tồn tại → truy cập `/health` sẽ trả **404**. Endpoint health đúng của prod là **`/api/v1/health`** (anonymous, luôn có). Healthcheck của container server dùng TCP-connect (không cần endpoint HTTP), nên container vẫn healthy.

---

## 4. Các kiến thức bắt buộc khi triển khai

### 4.1 Hai loại tài khoản admin RIÊNG BIỆT — không nhầm lẫn

| | **5a. Admin ỨNG DỤNG Mirats** | **5b. Keycloak MASTER admin** |
|---|---|---|
| **Mục đích** | Đăng nhập ứng dụng Mirats (IsSuperUser=true) | Đăng nhập **Keycloak Admin Console** `/admin` (quản trị realm/client/user) |
| **Biến** | `INITIAL_ADMIN_USERNAME` / `EMAIL` / `PASSWORD` | `KC_BOOTSTRAP_ADMIN_USERNAME` / `PASSWORD` |
| **Ai tạo** | Script `seed-initial-admin.*` qua Keycloak Admin API (Phương án A) | Image Keycloak tự tạo (biến env chuẩn `KC_BOOTSTRAP_*`) |
| **Realm** | `aspire-react` | `master` |
| **Điều kiện** | Chạy **một lần sau** `up -d` | Chỉ có tác dụng **lần đầu** khi volume keycloak còn rỗng |

**Quy tắc vàng:**
- KHÔNG dùng chung 1 bộ username/password cho 2 loại. Không trùng nhau.
- `KC_BOOTSTRAP_*` chỉ có tác dụng lần đầu (volume rỗng). Đổi sau đó không đổi mật khẩu trong DB — muốn đổi phải xóa volume (`docker-reset.*`) hoặc đổi qua Admin Console.
- `INITIAL_ADMIN_*` là **bắt buộc, không default, không fallback `Admin123!`**. Đây là quyết định an toàn từ DOCKER-1.

### 4.2 `KEYCLOAK_INTERNAL_URL` vs `KEYCLOAK_PUBLIC_URL` — tại sao phải tách 2 biến

Keycloak trong stack này chạy `start-dev` — **chỉ HTTP nội bộ** (`http://0.0.0.0:8080`). Nếu dùng 1 biến URL dùng chung cho cả backend lẫn trình duyệt, sẽ dính lỗi **Authority mismatch**:

- **Backend → Keycloak** (server fetch JWKS để xác thực JWT): phải gọi **trong Docker network** bằng tên service: `http://keycloak:8080`. Nếu bạn đưa `https://...` hoặc `http://localhost:8080` vào đây, backend không tới được (hoặc cố gọi HTTPS lên cổng HTTP) → **JWT 401**, đăng nhập vỡ.
- **Trình duyệt → Keycloak** (`keycloak-js` redirect login): phải gọi **qua host**: `http://localhost:8080` (cổng expose). Nếu đưa `http://keycloak:8080` vào đây, trình duyệt của người dùng không phân giải được tên `keycloak`.

Vì vậy compose tách 2 biến:
```
KEYCLOAK_INTERNAL_URL=http://keycloak:8080     → Keycloak__Authority (server)
KEYCLOAK_PUBLIC_URL=http://localhost:8080      → VITE_KEYCLOAK_URL (frontend/browser)
```

**Bài học thực tế (DOCKER-5):** trước khi tách, stack dính Authority mismatch rất khó debug (backend nhận token nhưng `Fetching JWKS` thất bại → 401 lan truyền toàn app). **Đừng gộp 2 biến này về 1** chỉ để "cho gọn". Khi đổi host, chỉ sửa `KEYCLOAK_PUBLIC_URL`; không đụng `KEYCLOAK_INTERNAL_URL` trừ khi đổi tên service.

### 4.3 Không gian mạng & cổng

| Service | Container (internal) | Host (mặc định) | Biến host port |
|---------|----------------------|-----------------|----------------|
| Postgres | 5432 | **không expose** (chỉ trong network) | `POSTGRES_PORT` (chỉ khi override) |
| Redis | 6379 | **không expose** | `REDIS_PORT` |
| Keycloak | 8080 | 8080 | `KEYCLOAK_PORT` |
| API | 5000 | 5000 | `BACKEND_PORT` |
| Frontend | 80 | 80 | `FRONTEND_PORT` |

> Postgres/Redis **không expose ra host** trong compose prod (bảo mật VPS công khai). Muốn truy cập từ host khi debug → xem §5.

### 4.4 Vite build-time args

`VITE_*` được **bake vào bundle lúc build** (ARG/ENV trong `frontend/Dockerfile`), không phải runtime env. Compose truyền chúng qua `build.args`:
```
VITE_API_BASE_URL  = ${VITE_API_BASE_URL:-/api/v1}   (base SERVER — frontend tự nối /api/v1, không double)
VITE_KEYCLOAK_URL  = ${KEYCLOAK_PUBLIC_URL}
VITE_KEYCLOAK_REALM = ${KEYCLOAK_REALM:-aspire-react}
VITE_KEYCLOAK_CLIENT_ID = ${KEYCLOAK_FRONTEND_CLIENT_ID:-frontend}
```
**Ngữ nghĩa `VITE_API_BASE_URL`:** đây là base của **SERVER** (không kèm `/api/v1`).
`api-client.ts` nối thêm `/api/v1` (và đã xử lý trường hợp base đã kết thúc bằng `/api/v1`
để không tạo `/api/v1/api/v1`). Giá trị prod đúng: `/api/v1` (same-origin qua Nginx,
không CORS prod). Giá trị dev: `http://localhost:5428`.

> ⚠️ **Lỗi thực tế đã gặp (DOCKER-8):** nếu `VITE_API_BASE_URL=/api/v1` mà `api-client.ts`
> nối thêm `/api/v1` → request thành `/api/v1/api/v1/...` → toàn bộ dashboard 404.
> Đã fix: `api-client.ts` kiểm tra `endsWith('/api/v1')` trước khi nối.

Do đó **mỗi lần đổi** `KEYCLOAK_PUBLIC_URL`/`KEYCLOAK_REALM`/`VITE_API_BASE_URL`, phải **rebuild image**:
```bash
docker compose build frontend && docker compose up -d frontend
```
(Đã verify: bundle chứa đúng giá trị build-arg — DOCKER-6.)

---

## 5. Debug / Development

### 5.1 Bật pgAdmin (profile `debug`)

pgAdmin **không bật mặc định**. Chỉ cần khi muốn xem DB bằng GUI:

```bash
docker compose --profile debug up -d pgadmin
# truy cập http://localhost:5050  (login: PGADMIN_DEFAULT_EMAIL / PGADMIN_PASSWORD từ .env)
# host DB để kết nối trong pgAdmin: tên service `postgres` (nhưng từ pgAdmin host bạn cần map cổng — xem §5.2)
```

### 5.2 Expose Postgres/Redis ra host khi debug (docker-compose.override.yml)

Tạo file `docker-compose.override.yml` ở repo root (đã có sẵn `.gitignore`? → chưa: file này **không commit**; tự tạo cục bộ):

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

Compose tự đọc override khi bạn chạy `docker compose up -d --build`. Sau khi hết nhu cầu debug, **xóa file override** rồi `docker compose up -d` lại (không expose nữa). Cách khác: `docker exec -it <postgres-container> psql -U postgres`.

> ⚠️ **Ràng buộc:** bất kỳ file `docker-compose.override.yml` nào cũng phải là file cục bộ tạm thời. Đừng commit nó — nó sẽ vô hiệu hóa tính an toàn "không expose DB" của compose prod.

### 5.3 Xem logs / kiểm tra health

```bash
docker compose ps                      # trạng thái + health
docker compose logs -f server keycloak # log chi tiết
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
1. Liệt kê các volume `mirats-*` sẽ xóa + hiển thị **cảnh báo** volume dev Aspire (`postgres-data`, `keycloak-data`) **không đụng tới**.
2. Hỏi gõ `yes` để xác nhận (nếu không, thoát ngay, không xóa gì).
3. `docker compose down -v` → xóa containers + **volume production**: `mirats-postgres-data`, `mirats-redis-data`, `mirats-keycloak-data`.
4. In hướng dẫn khởi tạo lại: `cp .env.example .env` → `up -d --build` → `seed-initial-admin.*`.

**An toàn:** script chỉ xóa volume có tiền tố `mirats-`. Volume dev Aspire (`postgres-data`/`keycloak-data` — không có tiền tố `mirats-`) **luôn được giữ nguyên**, kể cả khi compose fail. (Đã test thật trên volume `mirats-*` TEST: hỏi xác nhận, xóa đúng, `postgres-data`/`keycloak-data` còn nguyên — DOCKER-7.)

---

## 7. Giới hạn hiện tại — Keycloak `start-dev`, CHƯA đủ cho production internet-facing

**Stack hiện tại dùng `command: ["start-dev", "--import-realm"]`** → Keycloak chạy **chỉ HTTP nội bộ**, realm import, không có cache/infinispan production, không TLS. Điều này **đủ cho staging/VPS nội bộ** nhưng **CHƯA an toàn cho production thật phơi ra internet**:

| Hạn chế | Mức ảnh hưởng |
|---------|---------------|
| **Không HTTPS** — mật khẩu/login truyền trần | **Nghiêm trọng** nếu expose internet |
| `start-dev` dùng H2/in-memory cache, warning liên tục | Giảm hiệu năng/ổn định |
| `sslRequired: none` trong realm import | Mọi client không bắt buộc HTTPS |
| Không có backup tự động DB | Cần thêm cơ chế backup riêng |

**Công việc cần làm trước khi đưa lên production internet thật (BACKLOG, chưa thực hiện):**
1. Keycloak chạy **production mode**: `start --optimized` + thay cache để dùng Postgres/Infinispan + HTTPS.
2. **TLS termination** qua reverse proxy (Nginx/Ingress) trước Keycloak + `KEYCLOAK_INTERNAL_URL`/`KEYCLOAK_PUBLIC_URL` chuyển sang `https`.
3. Realm import bật `sslRequired: external` + redirectUris/webOrigins = domain thật (skeleton đã có `localhost` + `https://app.example.com/*` — cần thêm/xác nhận domain thật của bạn).
4. Cấu hình backup Postgres (pg_dump) + Keycloak volume.

> Quyết định này đã được ghi rõ ở DOCKER-1 §4.4 và giữ nguyên: **không tự ý đổi Keycloak sang prod mode** trong phạm vi các task DOCKER — là việc lớn, làm riêng.

---

## 8. Troubleshooting — các lỗi thực tế đã gặp khi xây dựng stack

Dưới đây là 3 lỗi **đã thực sự xảy ra** trong quá trình build (DOCKER-4/DOCKER-5), kèm cách nhận biết và khắc phục — giúp bạn tự chẩn đoán nếu gặp lại.

### 8.1 Postgres mount path sai (PG 18)

- **Triệu chứng:** Postgres container **không healthy**, logs: `PostgreSQL Database directory appears to contain a database; skipping initialization` hoặc `chmod: changing permissions of '/var/lib/postgresql/data': Operation not permitted`; hoặc crash loop.
- **Nguyên nhân:** PG 18 (bản mới) yêu cầu mount **parent** `/var/lib/postgresql` (chứa cả `data` + cấu hình khác), không mount trực tiếp `/var/lib/postgresql/data` như PG cũ.
- **Khắc phục:** volume mount đúng `postgres-data:/var/lib/postgresql` (như trong `docker-compose.yml` hiện tại). Đừng đổi lại `/var/lib/postgresql/data`.

### 8.2 Image base không có `wget`/`curl` (Keycloak RHEL + aspnet)

- **Triệu chứng:** healthcheck của Keycloak/Server **fail vĩnh viễn** (`Unhealthy`) dù service hoạt động; log healthcheck `wget: not found` hoặc `curl: not found`.
- **Nguyên nhân:** `quay.io/keycloak/keycloak:26.6` (RHEL UBI) và `mcr.microsoft.com/dotnet/aspnet:10.0` **không cài** wget/curl — healthcheck dùng `wget`/`curl` sẽ không chạy được.
- **Khắc phục:** healthcheck dùng **bash `/dev/tcp`**: `bash -c 'exec 3<>/dev/tcp/127.0.0.1/9000'` (Keycloak, cổng management) hoặc `.../5000` (server). Frontend (nginx:alpine) có busybox `wget` nên dùng wget được. **Đã áp dụng cho CẢ `docker-compose.yml` LẪN `aspire-react.Server/Dockerfile`** (Dockerfile trước dùng `wget /health` — sẽ fail khi chạy image độc lập; đã đổi sang `bash /dev/tcp` ở DOCKER-8 rà soát).

### 8.3 Keycloak Authority mismatch (tách URL sai / HTTPS sai)

- **Triệu chứng:** đăng nhập frontend **xong** nhưng mọi API trả **401**; server log: `Fetching JWKS from ... failed` hoặc `Unauthorized`. Toàn app vỡ sau login.
- **Nguyên nhân:** dùng chung 1 URL Keycloak cho backend (nội bộ) lẫn trình duyệt (host), hoặc đưa `https://` vào `KEYCLOAK_INTERNAL_URL` trong khi Keycloak chỉ HTTP (`start-dev`).
- **Khắc phục:** đảm bảo 2 biến đúng (xem §4.2): `KEYCLOAK_INTERNAL_URL=http://keycloak:8080` (server), `KEYCLOAK_PUBLIC_URL=http://localhost:8080` (browser). Backend dùng `Keycloak__Authority = ${KEYCLOAK_INTERNAL_URL}/realms/${KEYCLOAK_REALM}`. Nếu đã dính lỗi sau khi đổi URL, **restart server** (`docker compose restart server`) và **rebuild frontend** (VITE_KEYCLOAK_URL bake lúc build).

### 8.5 Keycloak "Invalid parameter: redirect_uri" khi mở app

- **Triệu chứng:** mở frontend, Keycloak trả trang lỗi **"We are sorry... Invalid parameter: redirect_uri"** (HTTP 400), chưa tới được form đăng nhập.
- **Nguyên nhân:** `redirectUris` của client `frontend` trong realm không chứa origin của app. Realm skeleton ban đầu chỉ cho `http://localhost:5173/*` (dev), nhưng compose prod serve app ở `http://localhost` (port 80) → `redirect_uri=http://localhost/` bị chặn.
- **Khắc phục:** (1) `aspire-react-realm.json` đã thêm `http://localhost/*`, `https://localhost/*`, `https://app.example.com/*` vào `redirectUris` + `webOrigins` (fix DOCKER-8). (2) Với realm **đã import rồi**, phải sửa client qua Admin Console/API: bật thêm `redirectUris` + `webOrigins` cho origin thật của bạn rồi thử lại. (3) Khi triển khai domain thật, nhớ đổi cả redirectUris/webOrigins cho đúng domain (xem §7 backlog).

### 8.6 Dashboard trả 500 `duplicate key value violates unique constraint "IX_users_Email"` ngay sau login

- **Triệu chứng:** sau khi đăng nhập lần đầu, dashboard fire **nhiều request đồng thời**; một vài request trả **500**; server log: `23505: duplicate key value violates unique constraint "IX_users_Email"`.
- **Nguyên nhân:** race trong JIT provisioning (`JitUserProvisioningService`): các request song song đều thấy "user chưa tồn tại" rồi cùng insert → một request thắng, phần còn lại đụng unique index.
- **Khắc phục:** đã fix trong code (DOCKER-8): `ProvisionAsync` bắt `DbUpdateException` với `PostgresErrorCodes.UniqueViolation`, `ChangeTracker.Clear()` rồi đọc lại user đã tồn tại (idempotent). Nếu vẫn gặp, restart server để đảm bảo chạy bản image mới: `docker compose build server && docker compose up -d server`.

### 8.7 (Phụ) `docker compose down -v` báo "required variable ... is missing"

- **Triệu chứng:** `docker compose` fail ngay khi parse vì thiếu `.env` hoặc biến BẮT BUỘC trống.
- **Nguyên nhân:** `${VAR:?required}` — đúng thiết kế, bảo vệ khỏi dựng stack thiếu secret.
- **Khắc phục:** điền đủ `.env` (§3 Bước 2). Nếu chỉ muốn **reset dữ liệu**, script `docker-reset.*` tự xử lý trường hợp này (fallback dọn container + volume không cần compose parse) — xem §6.

---

## 9. Biến môi trường tham chiếu đầy đủ

> Nguồn chính thức: `.env.example` (giữ comment chi tiết). Bảng dưới là tóm tắt.

**BẮT BUỘC (compose fail nếu thiếu):** `POSTGRES_PASSWORD`, `REDIS_PASSWORD`, `KEYCLOAK_BACKEND_CLIENT_SECRET`, `CORS_ALLOWED_ORIGINS`, `INITIAL_ADMIN_USERNAME`, `INITIAL_ADMIN_EMAIL`, `INITIAL_ADMIN_PASSWORD`, `KC_BOOTSTRAP_ADMIN_USERNAME`, `KC_BOOTSTRAP_ADMIN_PASSWORD`.

**TÙY CHỌN (có default):**

| Nhóm | Biến | Default |
|------|------|---------|
| Postgres | `POSTGRES_DB`, `POSTGRES_USER`, `POSTGRES_PORT` | `aspire-react-db`, `postgres`, `5432` |
| Redis | `REDIS_PORT` | `6379` |
| Keycloak | `KEYCLOAK_REALM`, `KEYCLOAK_FRONTEND_CLIENT_ID`, `KEYCLOAK_BACKEND_CLIENT_ID`, `KEYCLOAK_SUPERUSER_GROUP_NAME`, `KEYCLOAK_INTERNAL_URL`, `KEYCLOAK_PUBLIC_URL`, `KEYCLOAK_PORT` | `aspire-react`, `frontend`, `backend-service`, `superuser`, `http://keycloak:8080`, `http://localhost:8080`, `8080` |
| Server | `ASPNETCORE_URLS`, `ASPNETCORE_ENVIRONMENT` | `http://+:5000`, `Production` |
| CORS/Frontend | `FRONTEND_URL`, `VITE_API_BASE_URL` | `https://app.example.com`, `/api/v1` |
| Ports | `BACKEND_PORT`, `FRONTEND_PORT` | `5000`, `80` |
| pgAdmin (debug) | `PGADMIN_DEFAULT_EMAIL`, `PGADMIN_PASSWORD`, `PGADMIN_PORT` | `pgadmin@example.com`, `pgadmin`, `5050` |

> Ghi chú security: `CORS_ALLOWED_ORIGINS` được backend đọc khi khởi động (Program.cs) — đổi xong nhớ `docker compose restart server`. `VITE_*` là build-time — đổi xong phải rebuild frontend (§4.4).

---

## 10. Health Checks & Monitoring (tóm tắt)

- **Liveness/readiness:** `/api/v1/health` → 200 (prod). `/health` + `/alive` chỉ tồn tại khi `ASPNETCORE_ENVIRONMENT=Development`. Healthcheck compose dùng TCP-connect: Postgres `pg_isready`, Redis `redis-cli ping`, Keycloak `/dev/tcp/9000`, Server `/dev/tcp/5000`, Frontend `wget`. Dockerfile `aspire-react.Server/Dockerfile` cũng dùng TCP `/dev/tcp/5000` (không dùng HTTP `/health` — trước đây dùng `wget /health` là sai 2 lần: image không có wget + `/health` không tồn tại ở Production; đã sửa để chạy đúng cả khi `docker run` image độc lập lẫn qua compose).
- **Logs:** `docker compose logs -f` (mọi service). OpenTelemetry/traces chỉ hoạt động khi chạy dưới Aspire AppHost (dev) — compose prod không có Aspire Dashboard; logs lấy từ stdout container.
- **Backup (khuyến nghị):** Postgres — `pg_dump` định kỳ; Keycloak — snapshot volume `mirats-keycloak-data`. Xem §7 (backlog).

*Hết hướng dẫn triển khai.*
