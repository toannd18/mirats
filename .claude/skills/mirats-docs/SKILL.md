---
name: mirats-docs
description: Quy tắc viết + kiểm chứng tài liệu cho dự án AspireReact (MIRAT). Code là nguồn sự thật — không đoán; ghi [CẦN XÁC NHẬN] khi chưa chắc; đối chiếu endpoint/env/error_code/link bằng scripts/docs-verify; không đưa secret/IP/domain production; chụp màn hình chỉ với fixture tạo qua API (§8). Dùng khi viết/sửa docs/, guide, ERROR_CODES.md, API.md.
---

# mirats-docs — Quy tắc viết tài liệu dự án

Skill riêng của dự án. Mục đích: mọi tài liệu kỹ thuật phải **khớp với code thật**,
không bịa, không copy dán từ nguồn ngoài mà chưa kiểm chứng.

## 1. Code là nguồn sự thật (source of truth)

- Tài liệu là **bản mô tả thứ cấp**; code (`.cs`, `.tsx`, `.ts`, config, migration) là chuẩn.
- Trước khi viết bất kỳ nhận định nào về hành vi/endpoint/mã lỗi/field, **grep code thật**
  (`grep`/`read`) và trích dẫn **file + số dòng** làm bằng chứng.
- Docs **có thể sai** vì stale — khi docs mâu thuẫn code, **code đúng**, và phải sửa docs.
- Dùng `scripts/docs-verify` (bên dưới) để đối chiếu tự động trước khi coi là xong.

## 2. Không đoán — ghi `[CẦN XÁC NHẬN]`

- Chỗ nào chưa kiểm chứng được bằng code/UI thật → ghi `[CẦN XÁC NHẬN]` ngay sau phát biểu,
  kèm lý do (chưa có file tham chiếu / chưa chạy được / cần hỏi).
- KHÔNG bịa số, không bịa endpoint, không bịa mã lỗi, không bịa label UI — nhãn UI phải trích
  từ `frontend/src` thật (xem `references/glossary.md`).

## 3. Cấu trúc trang tài liệu

Mỗi trang docs/guide nên có:

1. **Tiêu đề + 1 câu tóm tắt** nội dung trang.
2. **Mục đích / đối tượng đọc** (ai đọc trang này, đọc xong làm được gì).
3. **Nội dung chính** (bảng/đoạn, có chú thích code ref khi cần).
4. **Nguồn tham chiếu** — bắt buộc ở cuối mỗi trang (xem §4).
5. *(nếu có)* **Checklist / ví dụ** kèm code ref.

## 4. Mục "Nguồn tham chiếu" (bắt buộc)

Cuối mỗi trang docs đặt mục `## Nguồn tham chiếu`, liệt kê file code đã dùng để viết trang,
dạng `path/file:line` hoặc `path/file (hàm/endpoint)`:

```markdown
## Nguồn tham chiếu
- aspire-react/aspire-react.Server/Web/Controllers/AssetsController.cs:45 (`GET /api/v1/assets`)
- aspire-react/aspire-react.Application/Assets/Commands/CheckoutAssetCommand.cs
- aspire-react/frontend/src/features/asset/types/asset.ts:19 (`ASSET_STATUS_LABELS`)
```

## 5. Không đưa secret / IP / domain production

- CẤM đưa vào tài liệu: mật khẩu, signing key, connection string thật, IP/hostname/domain
  production, token, nội dung file `.mirats-test-admin-password`, giá trị user-secrets.
- Chỉ dùng tên tham số + placeholder (`<pg-password>`, `<accessToken>`), URL localhost dev
  (`https://localhost:5173`, `http://localhost:5428`) khi cần minh hoạ.
- Repo là **PUBLIC** — coi mọi tài liệu là sẽ bị công khai.

## 6. Chụp màn hình chỉ với fixture tạo qua API (§8)

- Mọi ảnh minh hoạ UI trong docs phải dùng **fixture tạo mới qua API trong chính lượt chụp**,
  dọn sạch qua API sau khi chụp — theo quy tắc **§8 fixture-first**
  (`docs/MEDIATR_MIGRATION_PLAYBOOK.md` §8, bài học INCIDENT-1).
- TUYỆT ĐỐI không dùng entity có sẵn làm đối tượng minh hoạ; không tạo dữ liệu bằng SQL.
- Ảnh lưu vào `screenshots/` (đã gitignore) — không commit ảnh QA.

## 7. Kiểm chứng tự động — `scripts/docs-verify`

```bash
node .claude/skills/mirats-docs/scripts/docs-verify.js docs/ERROR_CODES.md docs/API.md
# hoặc quét cả thư mục:
node .claude/skills/mirats-docs/scripts/docs-verify.js --dir docs
```

Script **chỉ đọc** file + in stdout (không ghi file → không rủi ro mojibake), đối chiếu:
error_code / endpoint / biến môi trường / link nội bộ với code thật, in danh sách **không khớp**.

## 8. Quy tắc kèm theo

- Dùng `references/glossary.md` để dịch **code → nhãn UI** cho nhất quán, không tự đặt nhãn.
- Sơ đồ kiến trúc C4 → skill `c4-architecture`; ERD/sequence/flowchart/class/state →
  `mermaid-diagrams`. Xem AGENTS.md mục "Skills bên thứ ba".
- Sau mọi thao tác hàng loạt trên file chứa tiếng Việt: grep mojibake pattern
  (`KhÃ´|Lá»—|á»|Ã¢`) trước khi coi là xong (quy tắc AGENTS.md).
