# 14 — Triển khai: 3 cấu hình (a)(b)(c) + tuyến NPM

> Tóm tắt vận hành từ `docs/DEPLOYMENT.md` (nguồn chi tiết): truy cập trực tiếp,
> qua NPM, qua tunnel; tuyến khuyến nghị và bảng biến môi trường.

## 1. Mục đích / đối tượng đọc

Người triển khai/vận hành: chọn cấu hình, điền env, chuyển đổi a→b→c. Chi tiết
từng bước ở `DEPLOYMENT.md` (§3, §11); trang này là bản đồ quyết định.

## 2. Ba cấu hình

| # | Đường vào | TLS | IP backend thấy | Dùng khi |
|---|---|---|---|---|
| (a) | Browser → frontend `:8080` trực tiếp bằng IP | Không (HTTP) | IP container frontend (sai — chung 1 IP) | Tạm/dev nội bộ, **không production** |
| (b) | Browser → NPM `:443` → frontend → backend (domain nội bộ) | NPM terminate (Force SSL) | IP client thật (proxy tin cậy) | **Đích triển khai gần** |
| (c) | Browser → Cloudflare edge → tunnel → NPM → frontend → backend | Edge + NPM | IP client thật (`CF-Connecting-IP`) | Public sau này |

Ở (a): cookie refresh (`Secure`) bị bỏ qua → mất phiên âm thầm; mật khẩu
plaintext; passkey không chạy trên IP; brute-force theo IP đếm chung.

## 3. Tuyến khuyến nghị: NPM → frontend nginx → backend

Đã đo bằng NPM thật: `X-Forwarded-Proto` từ edge được giữ, `X-Forwarded-Scheme`
luôn là scheme NPM terminate, `XFF` append, `Host` giữ nguyên. Tuyến này giữ
một origin duy nhất (không CORS), frontend nginx proxy `/api` về `server:5000`.
Không dùng tuyến NPM → backend trực tiếp (đòi CORS + chỗ serve static khác).

Quy tắc backend (fail-closed): chỉ tin header proxy từ `Proxy__KnownNetworks`;
`CF-Connecting-IP` chỉ khi `PROXY_TRUST_CLOUDFLARE=true` + NPM tunnel-only
(firewall); `Proxy__KnownNetworks` trống ngoài Development → log WARNING.

## 4. Chuyển đổi và biến môi trường

- a→b: DNS nội bộ + NPM proxy host + SSL Custom/Force SSL/HSTS + override compose
  (**chỉ frontend** join network NPM, gỡ publish ports) — xem `DEPLOYMENT.md` §11.2.
- b→c: Public Hostname trỏ `https://<NPM>:443` + `originServerName`, **không**
  `noTLSVerify`; cloudflared→NPM bằng HTTP sẽ bị NPM `301` (đã đo) — xem §11.3
  và sự cố lặp chuyển hướng của trình duyệt (§8.8).
- Bảng biến theo cấu hình: `DEPLOYMENT.md` §11.4 (bắt buộc: secrets DB/Redis/
  auth/CORS + `NPM_NETWORK`, `PROXY_KNOWN_NETWORKS`, `WEBAUTHN_RPID/ORIGINS`;
  `PROXY_FORWARD_LIMIT` 1/2/3).

## 5. Biến môi trường bắt buộc theo cấu hình (đối chiếu `.env.example`)

| Biến | (a) | (b) | (c) | Nguồn sự thật |
|---|---|---|---|---|
| `POSTGRES_PASSWORD`, `REDIS_PASSWORD`, `AUTH_SIGNING_KEY`, `AUTH_BOOTSTRAP_ADMIN_PASSWORD`, `CORS_ALLOWED_ORIGINS` | Bắt buộc | Bắt buộc | Bắt buộc | `docker-compose.yml:30,48,74,79,83` |
| `NPM_NETWORK` | — | Bắt buộc | Bắt buộc | `docker-compose.npm.yml` (network external) |
| `MIRATS_NETWORK` | — | Tùy chọn (`mirats-internal`) | Tùy chọn | `docker-compose.npm.yml` |
| `PROXY_KNOWN_NETWORKS` | Khuyến nghị | Bắt buộc | Bắt buộc (+ IP cloudflared) | `ProxyOptions.cs` (mặc định loopback) |
| `PROXY_FORWARD_LIMIT` | 1 | 2 | 3 | `ProxyOptions.cs` (mặc định 2) |
| `PROXY_TRUST_CLOUDFLARE` | `false` | `false` | `true` (NPM tunnel-only) | `ProxyOptions.cs` (mặc định false) |
| `WEBAUTHN_RPID`, `WEBAUTHN_ORIGINS` | — | Bắt buộc | Bắt buộc | `Fido2Service.cs:36-38` |
| `FRONTEND_PORT`, `BACKEND_PORT` | Mở | **Không publish** (override) | **Không publish** | `docker-compose.npm.yml` (`ports: !reset []`) |

## Nguồn tham chiếu

- `docs/DEPLOYMENT.md` (§4.5 TLS, §8.5/§8.8 sự cố, §9 env, §11 NPM/tunnel)
- `docker-compose.yml:22-128` (5 services), `docker-compose.npm.yml` (override)
- `.env.example` (toàn bộ biến + comment)
- `aspire-react/aspire-react.Infrastructure/Networking/` (middleware tin cậy)
