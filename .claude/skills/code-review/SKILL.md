---
name: code-review
description: >
  Review .NET 9/10 + React 19 + Ant Design 6 code for compliance with the
  Aspire React project's non-negotiable conventions. Use when asked to "review
  code", "check my changes", "audit", "code review", or "chạy review".
  Covers backend (claims, ActionLog, company-scoping, permissions, Patch
  semantics, DateTime Kind, concurrency locks, EF migrations) and frontend
  (usePermission, Card List pattern, enum-string, antd 6 breaking changes,
  responsive). Read-only — no edits.
---

# Code Review — Aspire React (.NET 10 / React 19 / AntD 6)

Review the target code (files, feature, or full solution) for compliance with
the project's authoritative conventions. **Read `docs/DEVELOPMENT_WORKFLOW.md`
first if the conventions change** — code is the source of truth; this skill is a
checklist. This is a read-only review; do not edit code.

## Workflow

1. **Confirm scope** — user must specify files/feature/pattern. If ambiguous, ask.
2. **Read authoritative docs** — skim `docs/DEVELOPMENT_WORKFLOW.md` (Appendices
   A/B are the bug-class + reference tables) and `AGENTS.md`.
3. **Audit real code** — read the actual files, do NOT trust stale docs or prior
   reports.
4. **Run the sweep script** for known bug classes before review:
   ```bash
   pwsh -File scripts/audit-sweeps.ps1
   ```
   Exit 0 = clean, 1 = violations with locations. This runs from REPO ROOT.
5. **Run static checks** when touching those layers:
   - Backend: `dotnet build aspire-react.sln` from `aspire-react/`, then
     `dotnet format aspire-react.sln --verify-no-changes --no-restore`.
   - Frontend: `npm run lint` and `npx tsc --noEmit` from
     `aspire-react/frontend/`.
6. **Report findings** in `file:line` terse format grouped by category, severity,
   and a final verdict (block / approve with nits / approve).

> Note: `dotnet test` PASS is NOT sufficient evidence — EF InMemory does not
> enforce Postgres constraints (DateTime Kind, locks, raw SQL, unique indexes).
> Flag anything touching those layers as "must verify on real Aspire stack".

## Backend checklist (.NET 10)

### User identity / claims
- [ ] Uses `local_user_id` claim via `ICurrentUserService.GetLocalUserId()` /
      `IActionLogService.GetCurrentUserIdAsync()`. **Never** the legacy OIDC `sub` /
      `preferred_username` as a user FK (bug class 1). Grep
      `FindFirst|ClaimTypes|"sub"|preferred_username`.

### ActionLog (audit trail)
- [ ] Every Create/Update/Delete/Checkout/Checkin/Confirm/Close/Reopen/Inspect/
      Dispose/Import logs an ActionLog (bug class 3).
- [ ] CRUD/checkout logs use the typed `ActionLogEntry` builder
      (`_actionLogService.Log(new ActionLogEntry { ... })`) — compiler enforces
      the 5 required fields (`ItemType, ItemId, ActionType, CreatedBy, CompanyId`).
      Grep `_context.ActionLogs.Add(new ActionLog` — should only exist inside
      `ActionLogService.cs`.
- [ ] `LogAction(...)` (enriched: RemoteIp/UserAgent/etc.) is kept for flows that
      need enrichment — don't convert it to the thin `Log(entry)`.
- [ ] `TargetType`, `TargetId`, `CompanyId` correct; `LogMeta.changes` present on
      Update; persisted in the same transaction as the change.
- [ ] ActionLog is soft-deleted (`DeletedAt`), never hard-deleted.

### Company-scoping (multi-tenant)
- [ ] Explicit scope on BOTH read AND write endpoints (List/Detail/Create/Update/
      Delete) via `ICompanyScopeService.GetCurrentUserCompanyIdAsync()` — the
      global EF filter is a no-op (bug class 4). Grep `GetCurrentUserCompanyIdAsync`
      per controller.
- [ ] Out-of-scope read → `404`; create with foreign company → `400
      COMPANY_MISMATCH`; write/delete out-of-scope → `404`. Don't mix conventions.
- [ ] Checkout/checkin/create-child validate the target is same company.

### Permissions
- [ ] Every write endpoint has `[Authorize(Policy = "<resource>.<action>")]`
      with a key existing in `PermissionCatalog.cs`. No bare `[Authorize]` for
      data writes.
- [ ] Sensitive frontend buttons gated with `usePermission('<code>')`, not
      `isSuperUser()` as primary gate (except superuser-only logic like Reopen).
- [ ] `UsersController.UpdateUser` uses policy `admin` (anti privilege-escalation),
      not `users.edit`.
- [ ] Self-lockout guarded (`PermissionLockoutGuard`) on group delete / user
      update / user delete.

### Patch semantics (Update)
- [ ] Request DTO fields are nullable; handler assigns ONLY when the field was
      sent (`is not null` / `HasValue`) (bug class 8). Absent field never
      overwritten with default/null. Grep `= r.X` unconditional assigns in Update
      handlers.
- [ ] Locked fields reject with `FIELD_LOCKED` (not silently ignored, not blocked
      when absent).

### DateTime Kind (Npgsql)
- [ ] `timestamp without time zone` columns written with
      `DateTime.SpecifyKind(value, DateTimeKind.Unspecified)`; `with time zone`
      with `DateTime.UtcNow` (bug class 9). Wrong Kind → 500.

### Concurrency / locks
- [ ] Checkout/allocate endpoints lock rows `FOR UPDATE` inside a transaction
      (Asset `FromSqlRaw ... FOR UPDATE`, License seat, Accessory/Component/
      Consumable) (bug class 10).

### Delete-guard
- [ ] Records with checkout/allocation history are not hard-deleted (block or
      soft-delete). Master-data deletes check ALL referencing tables (bug class 7).

### Schema / DI / Validation
- [ ] Schema changes via EF Core migrations only (review migration file) — no raw
      SQL self-heal in `Program.cs`.
- [ ] DI registered in layer extensions, not `Program.cs` (composition root).
- [ ] New validators run via `ValidationBehavior` → 400; no manual calls.

## Frontend checklist (React 19 / AntD 6)

- [ ] Enums compared as **strings**, never numbers (bug class 2); use
      `normalize*` helpers in `frontend/src/types/asset.ts` when needed.
      Grep `=== [0-9]` near enum fields.
- [ ] Sensitive buttons gated with `usePermission('<code>')`.
- [ ] Main inventory list pages (Asset/Accessory/Component/Consumable/License)
      use the **Card List pattern** (`<ProList>` grid + `<Card>`, model:
      `AccessoryListPage`/`LicenseListPage`), NOT `<ProTable>`.
- [ ] Admin/master-data + Detail pages use `<ProTable>`; wide tables there use
      `scroll={{ x: true }}`.
- [ ] Forms use `Modal` (opened in-place with local state), not full-page forms
      or navigation-then-modal.
- [ ] Company dropdown uses shared `CompanyTreeSelect`, not a flat Select.
- [ ] Company column/filter only for superuser; hidden otherwise.
- [ ] Loading → `Spin/Skeleton`; empty → `<Empty />`; status → semantic `Tag/Badge`.
- [ ] antd 6 breaking changes respected: `destroyOnClose` → `destroyOnHidden`,
      `Drawer width` → `size`, `popupRender` for custom popup content.
- [ ] Responsive: `Grid.useBreakpoint()` + `screens.md` (`isMobile = !screens.md`);
      Modal→Drawer on mobile; layout Sider hidden below md (see `App.tsx`).
- [ ] No React Query/Zustand introduced (plain Axios + `useState`/props).

## Known bug-class sweep (Appendix A)

1. Claim misuse (`sub`/`username` instead of `local_user_id`)
2. Enum compared as number on frontend
3. Missing ActionLog
4. Missing company-scoping
5. Missing `usePermission` on sensitive buttons
6. Invalid PG `ADD CONSTRAINT IF NOT EXISTS` self-heal
7. Hard-deleting records with history / cascade FK
8. Patch-wipe: full-replace Update overwrites absent fields
9. DateTime Kind wrong → Npgsql 500
10. Checkout/allocate race without `FOR UPDATE`
11. EF InMemory pass but Postgres fails (needs real-stack verify)
12. Validator registered but not running (must use ValidationBehavior)

## Output format

Group findings by severity:
- **BLOCK** — violates a non-negotiable convention (security/data-integrity).
- **WARN** — likely bug or testability/maintainability concern.
- **NIT** — style/consistency, non-blocking.

For each: `file:line — <rule> — <what's wrong / why>`.

End with:
```
Verdict: BLOCK | APPROVE WITH NITS | APPROVE
- [ ] Restated required verification (real-API for DateTime/locks/constraints)
```
