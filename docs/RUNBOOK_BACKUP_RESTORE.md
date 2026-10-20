# RUNBOOK — Backup & khôi phục (Postgres)

> Phạm vi: backup hằng ngày đã mã hoá, khôi phục bản backup, kiểm tra sau khôi phục, rollback khi nâng cấp lỗi,
> và giám sát độ "tươi" của backup bằng Zabbix.
> Stack: `docker compose` tại repo root. Service backup: `backup` (image build từ `deploy/backup/`, base chính thức
> `postgres:18` + `age`/`rsync`).

## 0. Mô hình an toàn (đọc trước)

- Backup được mã hoá bằng **age PUBLIC key** (`BACKUP_AGE_RECIPIENT`, dạng `age1...`).
- **Private key KHÔNG nằm trên server.** Sinh cặp khoá trên máy quản trị:
  ```bash
  age-keygen -o backup-key.txt
  # public key in ra stderr/stdout (age1...): điền vào .env → BACKUP_AGE_RECIPIENT
  # private key nằm trong backup-key.txt: cất offline (USB/két sắt), KHÔNG copy lên server
  ```
- Mất private key ⇒ **không thể khôi phục bất kỳ bản backup nào**. Sao lưu private key an toàn, tách khỏi server.
- Plaintext dump bị xoá ngay sau khi mã hoá thành công.
- Backup lỗi ⇒ **không** cập nhật `last_success`; Zabbix sẽ cảnh báo sau 26 giờ.
- Image backup dùng base **chính thức `postgres:18`** (PostgreSQL License); chỉ cài thêm gói Debian:
  `age` (BSD-3-Clause), `rsync` (GPL-3.0), `openssh-client` (BSD), `tzdata`/`ca-certificates`.
  **Không dùng image bên thứ ba.**

## 1. Cấu hình

Các biến trong `.env` (xem `.env.example`):

| Biến | Mặc định | Ý nghĩa |
|---|---|---|
| `BACKUP_AGE_RECIPIENT` | **(bắt buộc)** | age public key (`age1...`) |
| `BACKUP_TIMEZONE` | `Asia/Ho_Chi_Minh` | múi giờ lịch chạy |
| `BACKUP_SCHEDULE_HOUR` / `BACKUP_SCHEDULE_MINUTE` | `1` / `0` | giờ chạy hằng ngày (01:00 giờ VN) |
| `BACKUP_RETENTION_DAYS` | `30` | giữ bao nhiêu ngày, tự xoá bản cũ hơn |
| `BACKUP_HOST_DIR` | `./backup-data` | thư mục host chứa bản backup + `last_success` |
| `BACKUP_REMOTE` | rỗng (TẮT) | đích rsync ngoài máy, VD `backup@10.0.0.5:/srv/mirats` |
| `BACKUP_REMOTE_SSH_KEY` | rỗng | đường dẫn SSH private key **trong container** khi bật remote |

- Bật remote copy: đặt `BACKUP_REMOTE`, mount/copy SSH key vào container (ví dụ đặt key trong
  `$BACKUP_HOST_DIR/ssh/id_ed25519` rồi set `BACKUP_REMOTE_SSH_KEY=/backups/ssh/id_ed25519`), đảm bảo
  `known_hosts`/`StrictHostKeyChecking=accept-new` phù hợp.
- Remote copy thất bại ⇒ coi như backup thất bại (không cập nhật `last_success`).

## 2. Backup thủ công (không chờ lịch)

```bash
cd <repo root>
docker compose run --rm --no-deps backup --once
```

- Thành công: log `SUCCESS: ...`, tạo:
  - `backup-data/aspire-react-db-YYYYmmdd-HHMMSS.dump.age` (đã mã hoá)
  - `backup-data/aspire-react-db-YYYYmmdd-HHMMSS.dump.age.sha256`
  - cập nhật `backup-data/last_success` (ISO timestamp)
- Kiểm tra checksum:
  ```bash
  cd backup-data
  sha256sum -c aspire-react-db-YYYYmmdd-HHMMSS.dump.age.sha256
  ```
- Xem log service định kỳ: `docker compose logs -f backup` (log rotation 10 MB × 5).

## 3. Giải mã (trên máy quản trị, KHÔNG trên server)

```bash
age -d -i ./backup-key.txt \
  backup-data/aspire-react-db-YYYYmmdd-HHMMSS.dump.age \
  > /tmp/aspire-react-db.dump
```

- Kiểm tra file: `pg_restore --list /tmp/aspire-react-db.dump | head`.
- Không để file plaintext trên server lâu hơn cần thiết; xoá sau khi khôi phục (`shred -u` nếu có).

## 4. Khôi phục vào DB trắng (theo thứ tự)

> Dừng ứng dụng trước để không có ghi mới trong lúc restore. Redis là cache — flush sau khi restore.

```bash
cd <repo root>

# 4.1 Dừng server/frontend/backup (giữ postgres + redis)
docker compose stop server frontend backup

# 4.2 (Khuyến nghị) backup hiện trạng trước khi xoá - nếu còn dùng được
docker compose run --rm --no-deps backup --once || true

# 4.3 Giải mã trên máy quản trị (xem §3) rồi restore
#     Cách A: pipe trực tiếp (Linux/macOS)
age -d -i ./backup-key.txt \
  backup-data/aspire-react-db-YYYYmmdd-HHMMSS.dump.age \
  | docker compose exec -T postgres pg_restore -U postgres -d aspire-react-db --clean --if-exists --no-owner

#     Cách B: nếu pipe lỗi, copy file vào container rồi restore
#     docker cp /tmp/aspire-react-db.dump $(docker compose ps -q postgres):/tmp/restore.dump
#     docker compose exec -T postgres pg_restore -U postgres -d aspire-react-db --clean --if-exists --no-owner /tmp/restore.dump

# 4.4 Nếu cần DB trắng hoàn toàn (KHÔNG dùng --clean), thay 4.3 bằng:
#     docker compose exec -T postgres dropdb   -U postgres --if-exists aspire-react-db
#     docker compose exec -T postgres createdb -U postgres aspire-react-db
#     age -d -i ./backup-key.txt <file>.age | docker compose exec -T postgres pg_restore -U postgres -d aspire-react-db --no-owner

# 4.5 Flush cache Redis (tránh dữ liệu tham chiếu cũ)
docker compose exec -T redis sh -c 'REDISCLI_AUTH="$REDIS_PASSWORD" redis-cli FLUSHALL'

# 4.6 Khởi động lại
docker compose start server frontend backup

# 4.7 Chờ readiness
until [ "$(curl -s -o /dev/null -w '%{http_code}' http://127.0.0.1:${BACKEND_PORT:-5000}/api/v1/health/ready)" = "200" ]; do sleep 3; done
```

> `pg_restore --clean --if-exists` chỉ an toàn khi DB đích đúng là DB của stack này. Kiểm tra `POSTGRES_DB` trước.

## 5. Kiểm tra sau khôi phục

```bash
# 5.1 Đếm dòng các bảng chính (so với trước khi backup)
docker compose exec -T postgres psql -U postgres -d aspire-react-db -c \
  'select
     (select count(*) from assets)               as assets,
     (select count(*) from users)                as users,
     (select count(*) from action_logs)          as action_logs,
     (select count(*) from asset_installations)  as asset_installations;'

# 5.2 Migration đã đủ phiên bản
docker compose exec -T postgres psql -U postgres -d aspire-react-db -c \
  'select count(*) from "__EFMigrationsHistory";'

# 5.3 Readiness + đăng nhập
curl -fsS http://127.0.0.1:${BACKEND_PORT:-5000}/api/v1/health/ready
# mở UI, đăng nhập tài khoản admin, kiểm tra Dashboard + một thiết bị có lý lịch
```

Tiêu chí đạt: `/health/ready` = 200; số dòng 4 bảng khớp bản backup; đăng nhập được; tab Lý lịch hiển thị dữ liệu.

## 6. Rollback khi nâng cấp lỗi

`scripts/deploy.sh` tự backup trước khi build/up; nếu readiness fail, script in hướng dẫn. Quy trình chuẩn:

1. **Khôi phục DB** từ bản backup mới nhất (§4) — migration của bản mới có thể đã chạy.
2. **Quay code/image**:
   ```bash
   git checkout <commit-trước-đó>
   docker compose up -d --build
   ```
3. Kiểm tra `/health/ready` = 200 + đăng nhập + dữ liệu.
4. Ghi lại sự cố (thời điểm, migration, bản backup dùng để restore).

## 7. Zabbix — kiểm tra độ "tươi" của backup

File trạng thái: `${BACKUP_HOST_DIR:-./backup-data}/last_success` (host), nội dung ISO timestamp.

- **Item (khuyến nghị):** `vfs.file.time[/path/to/backup-data/last_success,modify]` — trả về epoch mtime.
- **Trigger:**
  ```
  now() - last(/<host>/vfs.file.time[/path/to/backup-data/last_success,modify]) > 93600
  ```
  (93600 giây = 26 giờ; backup chạy 01:00 hằng ngày.)
- Mức: **High**; gửi email/Telegram.
- Nếu file không tồn tại (backup chưa từng thành công) → trigger `nodata(26h)` hoặc `last()=0`.
- Kiểm tra nhanh trên server:
  ```bash
  cat backup-data/last_success
  docker compose logs --tail=100 backup
  ```
- Backup lỗi (DB down, hết đĩa, remote copy lỗi) ⇒ `last_success` **không đổi** ⇒ trigger bật.

## 8. Lưu ý vận hành

- Đĩa: mỗi bản `.age` xấp xỉ kích thước dump nén; retention 30 ngày. Theo dõi dung lượng `backup-data` (Zabbix).
- Không bao giờ commit `backup-data/`, `backup-key.txt`, `.env`.
- Không chạy `docker compose down -v` khi chưa có backup ngoài máy.
- Remote copy nên bật ở production (ổ/khác máy) để chống mất host.

## 9. Kết quả diễn tập (2026-10-20)

Môi trường: compose project tạm `ready-gd1` (API cổng **5523**, Postgres/Redis container riêng, volume tên riêng);
backup dir `C:\Users\Public\gd1-ready\backups`; private age key giữ ngoài stack (thư mục admin tạm, không mount vào
Postgres/API).

| Hạng mục | Kết quả |
|---|---|
| Dữ liệu tạo qua API | 3 assets, 1 user, 15 action_logs, 1 asset_installations |
| Backup (mã hoá age) | **1,2s**; file `aspire-react-db-20261020-134527.dump.age` (**157.969 bytes**) + `.sha256` |
| Checksum | `sha256sum -c` → **OK** |
| Ca backup lỗi (dừng Postgres) | exit **1** (`pg_dump ... could not translate host name`); `last_success` **KHÔNG đổi** (content + mtime) |
| Xoá volume DB + tạo DB trắng | `information_schema.tables` = **0** trước restore |
| Giải mã + `pg_restore` | **0,74s** (157.737 bytes plaintext), exit 0 |
| Số dòng sau restore | `assets=3, users=1, action_logs=15, asset_installations=1` — **khớp trước backup** |
| Đăng nhập sau restore | **OK** (admin, mật khẩu hiện hành; `MustChangePassword=false`) |
| API sau restore | `GET /assets` trả **3**; `/api/v1/health/ready` = **200** sau **4,4s** kể từ lúc start server |
| Dọn dẹp | đã xoá container/volume/image tạm; không còn `ready-gd1-*` |
