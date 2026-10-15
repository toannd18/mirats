# SOURCES.md — Nguồn gốc skill trong `.claude/skills/`

> Ghi provenance của skill: repo, đường dẫn, commit, license — để truy vết khi
> audit/cập nhật, và để `scripts/install-skills.mjs` tái lập skill không-track
> trên máy mới mà không phân phối lại nội dung. KHÔNG sửa nội dung skill gốc.

## 1. Skills được track trong repo này

| Skill | Repo | Đường dẫn trong repo | Commit / bản | License |
|---|---|---|---|---|
| `mermaid-diagrams` | [softaworks/agent-toolkit](https://github.com/softaworks/agent-toolkit) | `skills/mermaid-diagrams/` | `3027f20` (clone 2026-10-15) | MIT (bổ sung `LICENSE` đúng commit) |
| `c4-architecture` | [davila7/claude-code-templates](https://github.com/davila7/claude-code-templates) | `cli-tool/components/skills/creative-design/c4-architecture/` | `b1ee28a` (clone 2026-10-15) | MIT (bổ sung `LICENSE` đúng commit) |
| `webapp-testing` | [anthropics/skills](https://github.com/anthropics/skills) | `skills/webapp-testing/` | `dbd4588` (clone 2026-10-15) | Apache 2.0 (`LICENSE.txt` gốc) |
| `mirats-docs` | **dự án tự viết** | — | — | (nội bộ dự án) |
| `aspire`, `aspire-init`, `aspire-orchestration`, `aspire-monitoring`, `aspire-deployment`, `aspireify` | [microsoft/aspire-skills](https://github.com/microsoft/aspire-skills) | `skills/<tên>/` | có sẵn trước 2026-10-15; đối chiếu cấu trúc + SKILL.md khớp upstream, **CŨ hơn HEAD `851c8c1`** (thiếu `evals/`, thiếu `aspire-13-5-breaking-changes.md`) | MIT Microsoft (bổ sung `LICENSE` từ upstream vào mỗi skill) |
| `playwright-cli` | [microsoft/playwright](https://github.com/microsoft/playwright) (bản bundle trong `packages/playwright-core/src/tools/cli-client/skill/` — khớp verbatim `allowed-tools`) | `packages/playwright-core/src/tools/cli-client/skill/` | có sẵn trước 2026-10-15; HEAD tra cứu `a038fe7` | Apache 2.0 (bổ sung `LICENSE` từ upstream) |
| `ant-design`, `antd` | chưa xác định chắc chắn | — | có sẵn trước 2026-10-15 | MIT (**file `LICENSE` có sẵn trong mỗi skill**: `antd` © 2025 không ghi holder; `ant-design` © Ant UED) |

## 2. Skills KHÔNG track — cài lại bằng `scripts/install-skills.mjs`

Máy mới chạy `node scripts/install-skills.mjs` (đọc manifest bên dưới, clone đúng
commit, copy vào `.claude/skills/`). Không commit nội dung vào repo.

## Reinstall manifest (máy đọc — `scripts/install-skills.mjs` parse bảng này)

| skill | repo | path | commit |
|---|---|---|---|
| doc-coauthoring | anthropics/skills | skills/doc-coauthoring | dbd4588f9e1033efb41dad4bef2f7947c8993d44 |
| dotnet-inspect | richlander/dotnet-inspect | skills/dotnet-inspect | f35eb6ea298dfe1395ff302fbddbfb310234d687 |

- `doc-coauthoring`: Apache 2.0 (`LICENSE.txt` trong skill); repo
  `anthropics/skills` không có root LICENSE. **Tạm ignore**
  (`.gitignore`: `.claude/skills/doc-coauthoring/`) cho đến khi xác nhận license.
- `dotnet-inspect`: nguồn `richlander/dotnet-inspect` (`skills/dotnet-inspect/`,
  HEAD tra cứu `f35eb6e`); MIT theo NuGet Gallery nhưng **không có file LICENSE
  trong repo/skill** → giữ trên máy, ignore tường minh, không phân phối lại.
  Lưu ý: SKILL.md local khác wording với upstream HEAD (bản local cũ hơn) —
  cài lại sẽ lấy bản mới.

## 3. Skills đã chuyển ra archive ngoài repo (không track, không cài lại)

Ngày 2026-10-15 chuyển 22 skill không liên quan/sai stack ra
`D:\Person\skills-archive\.claude-skills\` (giữ nguyên nội dung, không xóa).
OpenCode tự nạp skill theo mô tả nên các skill này có thể gây áp dụng chỉ dẫn
sai (Blazor/MudBlazor, Vercel, design generic): `frontend-dev`,
`vercel-cli-with-tokens`, `vercel-composition-patterns`, `vercel-optimize`,
`vercel-react-best-practices`, `vercel-react-native-skills`,
`vercel-react-view-transitions`, `deploy-to-vercel`, `web-design-guidelines`,
`writing-guidelines`, `banner-design`, `brand`, `design`, `design-system`,
`slides`, `ui-styling`, `ui-ux-pro-max`, `dotnet-backend-patterns`,
`dotnet-best-practices`, `dotnet-core-expert`, `dotnet-design-pattern-review`,
`dotnet-upgrade`.

⚠️ Tên trùng vẫn còn trong `.agents/skills/` (17) và `.opencode/skills/` (7,
personal libs, gitignored) — OpenCode gộp skill từ cả 3 thư mục nên vẫn có thể
liệt kê; dọn tiếp 2 chỗ đó cần quyết riêng.

## Quy tắc sử dụng

Xem `AGENTS.md` mục **"Skills bên thứ ba"** — AGENTS.md luôn thắng khi mâu thuẫn
với chỉ dẫn skill gốc.
