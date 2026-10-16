# 04 — Sơ đồ component backend (dạng flowchart)

> Trong backend .NET có những khối code nào, request đi qua chúng theo thứ tự gì.
> (Tương đương C4 mức 3 — dùng flowchart vì layout C4 chồng nhãn mũi tên, xem
> ghi chú cuối.)

## 1. Mục đích / đối tượng đọc

Dev backend: biết viết code mới ở đâu (DI đặt ở extension từng layer — Task Q),
request đi qua pipeline nào, log/audit gắn ở đâu.

## 2. Sơ đồ

```mermaid
flowchart TB
    spa[Frontend SPA<br/>REST /api/v1]

    subgraph API["Backend API (.NET 10)"]
        direction TB
        web[Web Controllers<br/>30 controllers]
        pipe[Middleware pipeline<br/>ProxyHeaders, CORS, JWT, PwdGate]
        app[Application CQRS<br/>Commands/Queries, 30 modules]
        beh[Behaviors<br/>CacheInvalidation, Validation, ActionLog]
        infra[Infrastructure<br/>DbContext, services, auth]
        dom[Domain<br/>Entities, PermissionCatalog]
    end

    pg[(PostgreSQL<br/>45+ bảng)]
    redis[(Redis<br/>Reference-data)]

    spa --> web
    web --> pipe
    pipe --> app
    app --> beh
    app --> infra
    app --> dom
    infra --> pg
    infra --> redis

    style API fill:#f5f5f5,stroke:#999,stroke-dasharray: 5 5
```

> Lý do dùng flowchart thay vì `C4Component`: layout C4 tự động chồng nhãn các
> mũi tên quan hệ lên nhau khi mô tả dài (đã render kiểm chứng). Nội dung tương
> đương C4 mức 3 (container → component → DB).

## 3. Thứ tự pipeline (quan trọng — sai thứ tự là bug)

1. `UseProxyHeaders` (IP + scheme thật từ proxy tin cậy) — ĐẦU TIÊN.
2. `UseExceptionHandler` → `UseCors` → `UseAuthentication` (JWT tự ký).
3. `PasswordChangeGateMiddleware` (403 `MUST_CHANGE_PASSWORD` trừ 2 endpoint).
4. `UseAuthorization` (policy `<resource>.<action>` từ `PermissionCatalog`) →
   `UseOutputCache` → Controllers (mapping mỏng sang MediatR).
5. Trong MediatR: `CacheInvalidation` (ngoài) → `Validation` → Handler →
   `ActionLog` log+commit (trong) → Evict cache.

## 4. Đặt code mới ở đâu

| Muốn thêm | Đặt tại |
|---|---|
| Endpoint mới | Controller trong `Web/Controllers/` + Command/Query trong `Application/<Module>/` |
| Service dùng chung | `Infrastructure/Services/` + đăng ký trong `InfrastructureServiceCollectionExtensions.cs` (KHÔNG sửa `Program.cs` ngoài gọi extension) |
| Quyền mới | Thêm key vào `PermissionCatalog.All` (tự sinh policy + catalog API) |
| Validation | FluentValidation validator — tự chạy qua `ValidationBehavior` |
| Audit | Handler implement `ILoggableCommand` (log cùng transaction) |

## Nguồn tham chiếu

- `aspire-react/aspire-react.Server/Program.cs:58-103` (pipeline)
- `aspire-react/aspire-react.Application/ApplicationServiceCollectionExtensions.cs:18-37` (3 behaviors + thứ tự)
- `aspire-react/aspire-react.Infrastructure/InfrastructureServiceCollectionExtensions.cs:16-66` (DI theo layer)
- `aspire-react/aspire-react.Domain/Authorization/PermissionCatalog.cs:16-140` (~85 keys)
