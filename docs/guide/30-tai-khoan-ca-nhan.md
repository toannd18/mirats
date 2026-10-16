# 30 — Tài khoản cá nhân

> Xem/sửa thông tin của chính mình, đổi mật khẩu, quản lý passkey.

## 1. Ai dùng (quyền cần có)

Mọi người dùng đã đăng nhập (không cần quyền đặc biệt).

## 2. Thao tác

1. Bấm avatar góc phải → **Xem hồ sơ** (thông tin) hoặc **Tài khoản & Passkey**.
2. **Đổi mật khẩu**: nhập mật khẩu hiện tại + mật khẩu mới (≥ 8 ký tự) — xem
   chi tiết và mã lỗi ở `20-dang-nhap.md` mục 3.
3. **Passkey**: đăng ký passkey mới (đặt tên gợi nhớ VD "Laptop công ty"), xóa
   passkey cũ — xem `20-dang-nhap.md` mục 4.

![Tài khoản và Passkey](images/20-account-passkey.png)

## Nguồn tham chiếu

- `aspire-react/frontend/src/App.tsx:380-381` (menu Xem hồ sơ, Tài khoản & Passkey)
- `aspire-react/frontend/src/features/auth/pages/AccountPage.tsx` (trang tài khoản)
- `docs/guide/20-dang-nhap.md` (đăng nhập, đổi MK, passkey)
