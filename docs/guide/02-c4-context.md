# 02 — Sơ đồ C4 mức 1: Bối cảnh hệ thống (Context)

> Ai dùng Mirats, hệ thống tương tác với cái gì bên ngoài.

## 1. Mục đích / đối tượng đọc

Mọi đối tượng (lãnh đạo, dev, vận hành): một bức tranh duy nhất về ranh giới
hệ thống — người dùng và hệ ngoài nào chạm vào Mirats.

## 2. Sơ đồ

```mermaid
C4Context
  title System Context - Mirats (AspireReact)

  Person(staff, "Nhân viên", "Thủ kho, kỹ thuật, quản trị")
  System(mirats, "Mirats", "Quản lý tài sản CNTT đa công ty")

  System_Ext(browser, "Trình duyệt", "React SPA qua HTTPS")
  System_Ext(cloudflare, "Cloudflare", "Edge + Tunnel (cấu hình c)")
  System_Ext(npm, "Nginx Proxy Manager", "Terminate TLS, reverse proxy")

  Rel(staff, browser, "Đăng nhập, thao tác")
  Rel(browser, cloudflare, "HTTPS (c)")
  Rel(browser, npm, "HTTPS (b)")
  Rel(browser, mirats, "HTTP trực tiếp (a, không khuyến nghị)")
  Rel(cloudflare, npm, "Tunnel HTTPS:443")
  Rel(npm, mirats, "Proxy HTTP nội bộ")
```

> Không có tích hợp chiều ra (mail/webhook/API công khai cho hệ khác) trong
> code hiện tại — backup và cảnh báo trong `DEPLOYMENT.md` đều làm thủ công.

## 3. Đọc sơ đồ

- Mirats là **một hệ thống duy nhất** đối với người dùng, dù trong có nhiều
  container (xem `03-c4-container.md`).
- Ba đường vào (a)/(b)/(c) khác nhau ở TLS + IP client backend thấy (xem
  `14-trien-khai.md`), nhưng nghiệp vụ phía trên không đổi.
- Không có tích hợp chiều ra (webhook/API công khai cho hệ khác) trong code
  hiện tại.

## Nguồn tham chiếu

- `docs/DEPLOYMENT.md` (§11: 3 cấu hình a/b/c)
- `aspire-react/aspire-react.AppHost/AppHost.cs` (ranh giới dev)
- `docker-compose.yml:22-128` (ranh giới prod)
