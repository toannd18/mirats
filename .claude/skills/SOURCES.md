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
| `aspire`, `aspire-init`, `aspire-orchestration`, `aspire-monitoring`, `aspire-deployment`, `aspireify` | [microsoft/aspire-skills](https://github.com/microsoft/aspire-skills) | `skills/<tên>/` | **`35f41b0` (2026-05-26, "Sync Aspire skills with 13.4 guidance") — KHỚP BYTE (chuẩn hoá CRLF) toàn bộ SKILL.md ×6 + cây `references/`** (lệch duy nhất: upstream có thêm `evals/` — installer gốc đã lược khi cài; `LICENSE` do ta bổ sung) | MIT Microsoft (bổ sung `LICENSE` từ upstream vào mỗi skill) |
| `playwright-cli` | [microsoft/playwright](https://github.com/microsoft/playwright) (bản bundle trong `packages/playwright-core/src/tools/cli-client/skill/` — khớp verbatim `allowed-tools`) | `packages/playwright-core/src/tools/cli-client/skill/` | có sẵn trước 2026-10-15; HEAD tra cứu `a038fe7` (chưa đối chiếu byte — bản local khớp biến thể bundle, khác biến thể repo standalone `microsoft/playwright-cli`) | Apache 2.0 (bổ sung `LICENSE` từ upstream) |
| `ant-design`, `antd` | [ant-design/antd-skill](https://github.com/ant-design/antd-skill) | `skills/ant-design/`, `skills/antd/` | **`e65d2d3` (HEAD tra cứu 2026-10-15) — `LICENSE` khớp byte (chuẩn hoá CRLF) cả 2 skill với file do tác giả phát hành** (upstream `skills/antd/LICENSE` 1056B © 2025; `skills/ant-design/LICENSE` = Ant UED) | MIT (file `LICENSE` do tác giả phát hành, giữ nguyên trong mỗi skill) |

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

Ngày 2026-10-15 chuyển toàn bộ skill không liên quan/sai stack ra
`D:\Person\skills-archive\` (giữ nguyên nội dung + cấu trúc theo thư mục nguồn,
không xóa — khôi phục bằng cách move ngược lại). OpenCode tự nạp skill theo mô tả
nên các skill này có thể gây áp dụng chỉ dẫn sai (Blazor/MudBlazor, Vercel, design
generic). Đã xác nhận OpenCode delist toàn bộ 22 skill sau khi dọn.

| Archive subdir | Nguồn | Skills |
|---|---|---|
| `.claude-skills/` (22) | `.claude/skills/` trong repo | `frontend-dev`, `vercel-cli-with-tokens`, `vercel-composition-patterns`, `vercel-optimize`, `vercel-react-best-practices`, `vercel-react-native-skills`, `vercel-react-view-transitions`, `deploy-to-vercel`, `web-design-guidelines`, `writing-guidelines`, `banner-design`, `brand`, `design`, `design-system`, `slides`, `ui-styling`, `ui-ux-pro-max`, `dotnet-backend-patterns`, `dotnet-best-practices`, `dotnet-core-expert`, `dotnet-design-pattern-review`, `dotnet-upgrade` |
| `.agents-skills/` (17) | `.agents/skills/` trong repo (personal lib, gitignored) | 17 bản trùng tên (trừ 5 `dotnet-*` vốn không có ở đây) |
| `.opencode-skills/` (7) | `.opencode/skills/` trong repo (personal lib, gitignored) | `banner-design`, `brand`, `design`, `design-system`, `slides`, `ui-styling`, `ui-ux-pro-max` |
| `.global-agents-skills/` (5) | `~/.agents/skills/` toàn cục | 5 `dotnet-*` chung |

Các skill GIỮ LẠI không đụng tới: `aspire`×6, `playwright-cli`, `dotnet-inspect`,
`ant-design`, `antd` (mọi nơi), `agent-browser`, `ant-design-mobile`,
`code-review` (`.agents/skills/`).

## Quy tắc sử dụng

Xem `AGENTS.md` mục **"Skills bên thứ ba"** — AGENTS.md luôn thắng khi mâu thuẫn
với chỉ dẫn skill gốc.
