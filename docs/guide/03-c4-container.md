# 03 — Sơ đồ container (dạng flowchart)

> Mirats gồm những khối triển khai (container) nào, nói chuyện với nhau ra sao.
> (Tương đương C4 mức 2 — dùng flowchart vì layout C4 tràn chữ, xem ghi chú cuối.)

## 1. Mục đích / đối tượng đọc

Dev + vận hành: biết request đi qua những hop nào (quan trọng cho debug IP,
scheme, TLS), dữ liệu nằm ở đâu (Postgres/Redis/volume).

## 2. Sơ đồ (dạng flowchart — C4 gốc bị tràn chữ tiếng Việt dài, xem ghi chú cuối)

```mermaid
flowchart TB
    staff([Nhân viên<br/>Thủ kho, kỹ thuật, quản trị])

    subgraph EDGE["Ngoài hệ thống"]
        cf[Cloudflare edge<br/>Tunnel, HTTPS + Access]
    end

    subgraph HOST["Server triển khai"]
        npm[Nginx Proxy Manager<br/>Terminate TLS, proxy HTTP]
        tfd[(cloudflared<br/>Tunnel client)]
        fe[Frontend<br/>React SPA + Nginx]
        api[Backend API<br/>.NET 10, REST /api/v1]
        pg[(PostgreSQL 18<br/>Dữ liệu nghiệp vụ)]
        redis[(Redis 8<br/>OutputCache)]
    end

    staff -- HTTPS --> cf
    cf -- Tunnel --> tfd
    tfd -- HTTPS:443 --> npm
    npm -- HTTP --> fe
    fe -- "/api" --> api
    api --> pg
    api --> redis

    style EDGE fill:#f5f5f5,stroke:#999,stroke-dasharray: 5 5
    style HOST fill:#f5f5f5,stroke:#999,stroke-dasharray: 5 5
```

> Lý do dùng flowchart thay vì `C4Container`: layout C4 tự động tràn chữ mô tả
> tiếng Việt dài ra ngoài hình cylinder/container (đã render kiểm chứng). Nội
> dung tương đương C4 mức 2 (người → container → DB).

Dev (Aspire) thay `fe` bằng Vite dev-server HTTPS + `api` chạy `dotnet run`,
bỏ NPM/tunnel; Postgres/Redis do Aspire cấp container tương đương.

## 3. Đọc sơ đồ

- Browser **không bao giờ gọi API trực tiếp**: mọi `/api` đi qua frontend nginx
  (same-origin, không CORS prod) — xem `aspire-react/frontend/nginx.conf:15-29`.
- Backend **không terminate TLS**: `ASPNETCORE_URLS=http://+:5000`
  (`docker-compose.yml:66`); TLS do NPM/edge làm.
- Redis chỉ cache **reference-data** (5 nhóm, TTL 300s) — dữ liệu nghiệp vụ
  (checkout/tồn kho) không cache (`AGENTS.md` Task P).
- `pgAdmin` chỉ chạy với `--profile debug`, không vẽ ở đây.

## Nguồn tham chiếu

- `docker-compose.yml:22-128` (5 services prod)
- `docker-compose.npm.yml` (network NPM + gỡ ports)
- `aspire-react/aspire-react.AppHost/AppHost.cs:14-40` (4 resources dev)
- `aspire-react/frontend/nginx.conf:15-29` (proxy /api)
