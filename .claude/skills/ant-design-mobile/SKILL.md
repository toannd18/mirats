---
name: ant-design-mobile
description: >
  Decision guide for building responsive/mobile UIs on antd 6.x (no separate
  antd-mobile library needed). Use when a task involves mobile layouts, narrow
  screens, responsive grid (Row/Col), the useBreakpoint hook, switching
  Drawer/Modal by screen size, mobile-friendly Table/ProList/Card list patterns,
  or any "works on phone" requirement. Focuses on the Card List pattern used by
  the Aspire inventory pages and modal-to-drawer transitions.
---

# Ant Design Mobile (responsive on antd 6.x)

The project ships a single web UI built on `antd@^6`. "Mobile" is achieved with
antd's built-in responsive primitives — **no `antd-mobile` package**. Follow the
existing in-repo patterns (see `.claude/skills/ant-design/SKILL.md` for the
general antd decision guide; this skill is the mobile/responsive specialization).

## S - Scope
- Target: `antd@^6` + React 18-19 (TypeScript), single responsive web app.
- Tooling: `@ant-design/cli` for offline component metadata (`antd info|demo|doc|token --format json`).
- Baseline helpers the repo already uses: `Grid.useBreakpoint()` and `screens.md` (see `App.tsx`, `features/*/components/*FormModal.tsx`).
- Source policy: official antd docs only; no `.ant-*` selector coupling.

### Breakpoint reference (antd 6.x)
| Token | Min width | `useBreakpoint()` key |
| --- | --- | --- |
| `screenXS` | 0 | `xs` |
| `screenSM` | 576 | `sm` |
| `screenMD` | 768 | `md` |
| `screenLG` | 992 | `lg` |
| `screenXL` | 1200 | `xl` |
| `screenXXL` | 1600 | `xxl` |
| `screenXXXL` | 1920 | `xxxl` |

`useBreakpoint()` returns a `Record<key, boolean>` where the key is `true`
when the viewport is **at or above** that breakpoint. Derive the mobile flag
with `const screens = Grid.useBreakpoint(); const isMobile = !screens.md;`.

## P - Process
### 1) Classify
- Determine the container type: full page (list/detail) or overlay (Modal/Drawer).
- Check which breakpoint gate the existing sibling components use (repo convention is `md`).
- Confirm whether a wide desktop table needs collapsing to the Card List pattern.

### 2) Query authoritative sources
- Before writing component code, run `antd info <Component> --format json` (e.g. `Drawer`, `Modal`, `Table`, `Grid`).
- For responsive layout look at `Grid` demos: `antd demo Grid responsive`, `antd demo Grid useBreakpoint`, `antd demo Grid responsive-flex`.
- For overlays compare `Drawer` vs `Modal` props (`size`, `placement`, `destroyOnHidden`).

### 3) Decide — pick the right primitive
- **Narrow the grid**: use `Row`/`Col` with per-breakpoint spans, e.g.
  `<Col xs={24} sm={12} lg={8}>`. Never hardcode a single fixed `span`.
- **Form/Edit overlays on mobile → Drawer**: repo pattern is to show a `Drawer`
  (placement `right`, `size` tuned for phone) instead of a wide `Modal` when
  `!screens.md`. See `AssetEditModal`, `LicenseFormModal`, `UserFormModal`.
- **Wide desktop tables → Card List on mobile**: main inventory list pages use
  the Card List pattern (`<ProList>` grid + `<Card>`, see `AccessoryListPage` /
  `LicenseListPage`) — NOT `<ProTable>`. Admin/master-data pages still use
  `<ProTable>`; add `scroll={{ x: true }}` for horizontal scroll there.
- **Navigation**: hide the `Sider` below `md` and open it via a `Drawer`
  triggered by a hamburger button (see `App.tsx`).

## O - Output
- State the chosen breakpoint gate (`screens.md` unless there's a reason otherwise).
- For overlays: `Drawer` on mobile / `Modal` (or Drawer) on desktop, with exact `size`/`placement`.
- For lists: Card List pattern on mobile vs the existing desktop layout; call out
  `<ProTable scroll={{ x: true }}>` only where tables remain.
- Note Ant Design 6 breaking changes that matter here: `destroyOnClose` → `destroyOnHidden`, `Drawer width` → `size`, `popupRender` for custom popup content.

## Regression checklist
- [ ] Breakpoint gate consistent with sibling components (`screens.md`).
- [ ] No fixed `Col span` alone; responsive spans present where layout matters.
- [ ] Overlay switches to `Drawer` (with correct `size`) below `md`.
- [ ] Inventory list uses Card List pattern; remaining tables have `scroll={{ x: true }}`.
- [ ] `antd lint` passed; UI verified at ~375 / 768 / 1440px (not just build passing).
