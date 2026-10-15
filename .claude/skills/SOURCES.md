# SOURCES.md — Nguồn gốc skill bên thứ ba trong `.claude/skills/`

> Ghi lại provenance của các skill bên thứ ba đã cài vào dự án, để truy vết license +
> commit khi cần cập nhật/audit. KHÔNG sửa nội dung skill gốc — chỉ bổ sung metadata tại đây.

## Skills đã cài (2026-10-15)

| Skill | Repo | Đường dẫn trong repo | Commit SHA (clone) | License |
|---|---|---|---|---|
| `mermaid-diagrams` | [softaworks/agent-toolkit](https://github.com/softaworks/agent-toolkit) | `skills/mermaid-diagrams/` | `3027f20f3181758385a1bb8c022d4041dfb4de84` | MIT |
| `c4-architecture` | [davila7/claude-code-templates](https://github.com/davila7/claude-code-templates) | `cli-tool/components/skills/creative-design/c4-architecture/` | `b1ee28a182ef7af9cc817384522ff066bb608edf` | MIT |
| `webapp-testing` | [anthropics/skills](https://github.com/anthropics/skills) | `skills/webapp-testing/` | `dbd4588f9e1033efb41dad4bef2f7947c8993d44` | Apache 2.0 (có `LICENSE.txt` trong skill) |
| `doc-coauthoring` | [anthropics/skills](https://github.com/anthropics/skills) | `skills/doc-coauthoring/` | `dbd4588f9e1033efb41dad4bef2f7947c8993d44` | [CẦN XÁC NHẬN] — không có LICENSE riêng; repo gốc không có root LICENSE |

## Ghi chú

- **Ngày cài**: 2026-10-15. Clone theo `git clone --depth 1` branch `main`; commit SHA ở trên là HEAD tại thời điểm clone (xác nhận bằng `git ls-remote <repo> HEAD`).
- **webapp-testing**: `LICENSE.txt` (Apache 2.0) được giữ nguyên trong skill để đảm bảo attribution.
- **mermaid-diagrams / c4-architecture**: bản clone không kèm file LICENSE nên đã bổ sung `LICENSE`
  (MIT, lấy đúng từ commit đã ghi ở trên) vào mỗi skill theo yêu cầu attribution của MIT.
- **doc-coauthoring**: repo `anthropics/skills` không khai báo license ở root (API trả 404), và skill này không có `LICENSE.txt` riêng. Vì vậy skill này **tạm bị ignore** (`.gitignore`: `.claude/skills/doc-coauthoring/`) cho đến khi xác nhận license với chủ repo — không track, không phân phối lại.
- **Các skill còn lại** trong `.claude/skills/` là skill có sẵn từ trước của dự án — chưa liệt kê ở đây; bổ sung dần khi cần.

## Quy tắc sử dụng

Xem `AGENTS.md` mục **"Skills bên thứ ba"** — AGENTS.md luôn thắng khi mâu thuẫn với chỉ dẫn skill gốc.
