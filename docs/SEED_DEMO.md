# Seed dữ liệu demo cho review giao diện

> Mục đích: tạo dữ liệu mẫu sát nghiệp vụ để review toàn bộ UI. **Không dùng DB production**.
> Mọi bản ghi được tạo **qua API thật** — không SQL, không ghi trực tiếp EF.
>
> ⚠️ **CẢNH BÁO:** script tạo công ty, người dùng và tài sản demo. **TUYỆT ĐỐI KHÔNG chạy trên
> môi trường thật/production.** Script chỉ chấp nhận `localhost`/`127.0.0.1`; muốn trỏ nơi khác
> phải có đồng thời cờ `--allow-remote` **và** biến `DEMO_SEED_CONFIRM=yes`. Khi dùng
> `--allow-remote`, bắt buộc đặt `DEMO_TECH_PASSWORD` và `DEMO_LEADER_PASSWORD` (không dùng mật
> khẩu demo mặc định).

## 1. Môi trường tạm

Tạo DB rỗng (ví dụ bằng container `tmp-demo-pg`, cổng khác 5428/5173), chạy API trỏ vào DB đó,
sau đó chạy seed.

Ví dụ (cổng minh họa):

```powershell
docker run -d --name tmp-demo-pg -e POSTGRES_PASSWORD=DemoTmp2026 -p 55492:5432 postgres:18.3
docker run -d --name tmp-demo-redis -p 56492:6379 redis:8.6
# tạo database mirats_tmp_demo, chạy API với ConnectionStrings__aspire-react-db trỏ DB này
```

## 2. Chạy seed

```powershell
cd <repo-root>
$env:DEMO_API_BASE = 'http://127.0.0.1:5442/api/v1'
$env:DEMO_ADMIN_PASSWORD = '<mật khẩu admin đã đổi>'   # BẮT BUỘC — không có mật khẩu mặc định
node scripts/seed-demo.mjs
```

Có thể override mật khẩu hai tài khoản demo (mặc định như bảng dưới) bằng
`DEMO_TECH_PASSWORD` và `DEMO_LEADER_PASSWORD`.

Khi kiểm chứng idempotent (chạy seed 2 lần liên tiếp và đếm bảng), có thể truyền
`DEMO_ADMIN_TOKEN=<access token ngắn hạn>` để bỏ qua bước login admin — tránh tăng
`auth_login_attempts`/`user_credentials` ngoài dữ liệu nghiệp vụ.

Script idempotent: chạy lại sẽ tìm theo mã/tên/tag và bỏ qua bản ghi đã có. Script ghi ID tóm tắt
vào `logs/demo-seed.json` (không commit).

Sau khi seed:

| Tài khoản | Mật khẩu | Quyền chính |
|---|---|---|
| `demo-tech` | `DemoTech-2026!` | Xem/tạo/sửa tài sản, kiểm kê, cấp phát–thu hồi, cấp phát vật tư/linh kiện, nhập kết quả bảo dưỡng (không có báo cáo/bản quyền/đính chính) |
| `demo-leader` | `DemoLeader-2026!` | Xem tài sản/hệ thống, xem báo cáo, export |
| `admin` | mật khẩu admin hiện tại | Toàn quyền |

## 3. Nội dung dữ liệu

- 1 đơn vị, 3 phòng ban, 3 địa điểm.
- 5 hệ thống, mỗi hệ thống 3–6 vị trí.
- Danh mục, nhà sản xuất, model thiết bị CNTT.
- 60 tài sản; 22 tài sản có lịch sử lắp/thu hồi/điều chuyển.
- Vật tư, linh kiện, phụ kiện dưới ngưỡng tồn; bản quyền có cái sắp/đã hết hạn.
- 13 phiếu sửa chữa trải 2 quý, có phiếu mở/thiếu chi phí/bảo hành.
- Mẫu bảo dưỡng và 3 đợt: hoàn thành, đang làm, cũ/chưa hoàn thành.
- 2 tài khoản hạn chế quyền để review.

## 4. Dọn môi trường

```powershell
docker rm -f tmp-demo-pg tmp-demo-redis
```

Không commit DB, file `logs/demo-seed.json`, ảnh chụp tạm hoặc secret.
