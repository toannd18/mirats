# DF-7 — Tách quyền phiếu sửa chữa (PLAN)

> **Trạng thái:** PLAN + QUYẾT ĐỊNH — đã triển khai trong CP-F (2026-10-25).
> **Phạm vi:** quyền cho `AssetMaintenance` (phiếu sửa chữa/bảo trì).
> **Ngoài phạm vi:** không thay đổi luồng trạng thái hay quyền `Confirm` tài sản.

---

## 0b. Ảnh hưởng từ DF-8 — mã lỗi/HTTP trước và sau

Các test cũ đã đổi assertion trong CP-E:

| Test | Endpoint/handler | TRƯỚC CP-E | SAU CP-E |
|---|---|---|---|
| `AssetTests.Create_NonSuperUser_OtherCompanyId_Rejected` | `POST /assets` | `COMPANY_MISMATCH` / 400 | `FORBIDDEN` / 403 |
| `TaskL2CreateCompanyScopeTests.CreateAsset_CrossCompany_Rejected_NotCreated` | `POST /assets` | `COMPANY_MISMATCH` / 400 | `FORBIDDEN` / 403 |
| `TaskL2CreateCompanyScopeTests.CreateAccessory_CrossCompany_Rejected_NotCreated` | `POST /accessories` | `COMPANY_MISMATCH` / 400 | `FORBIDDEN` / 403 |
| `TaskL2CreateCompanyScopeTests.Department_Create_CrossCompany_Returns400_NotCreated` | `POST /departments` | `COMPANY_MISMATCH` / 400 | `FORBIDDEN` / 403 |
| `TaskL2CreateCompanyScopeTests.Consumable_Create_CrossCompany_Forbidden_NotCreated` | `POST /consumables` | `BadRequest` 400 (`COMPANY_MISMATCH`) | `Forbid` 403 |
| `LocationCommandTests.RegularUser_TargetingOtherCompany_BlockedWithCompanyMismatch` | `POST /locations` | `COMPANY_MISMATCH` / 400 | `FORBIDDEN` / 403 |
| `SystemInfoCreateCompanyScopeTests.RegularUser_CreateForOtherCompany_Forbidden` | `POST /system-infos` | `COMPANY_MISMATCH` / 400 | `FORBIDDEN` / 403 |
| `SystemInfoCreateCompanyScopeTests.RegularUser_CreateWithoutCompany_FillsOwnCompany` | `POST /system-infos` | 200, `companyId = null` | 200, `companyId = own` |
| `UserUpdatePatchSafetyTests.Update_CrossCompany_RegularUser_Rejected_NotMutated` | `PUT /users/{id}` | `COMPANY_MISMATCH` / 400 | `FORBIDDEN` / 403 |
| `UserUpdatePatchSafetyTests.Update_CompanyLessRegularUser_AssigningRealCompany_Rejected` | `PUT /users/{id}` | `COMPANY_MISMATCH` / 400 | `FORBIDDEN` / 403 |
| `UserUpdatePatchSafetyTests.Update_FloaterSentinel_RegularUser_Forbidden` | `PUT /users/{id}` | 200, clear company | `FORBIDDEN` / 403 |

Các thay đổi status là do controller map `FORBIDDEN → Forbid()`; `COMPANY_MISMATCH` cũ trả 400.

### Frontend đang phụ thuộc mã cũ?

Grep `frontend/src`:

- `shared/utils/formErrors.ts:17` — có map `COMPANY_MISMATCH`.
- `shared/utils/formErrors.ts:18` — có map `COMPANY_REQUIRED`.
- **Không có map `FORBIDDEN`.**
- `features/import/services/import.service.ts:30` — comment còn ghi “out-of-scope company → 403”; import flow đã dùng 403.
- `MaintenanceCampaignDetailPage.tsx` — dùng state `forbidden` theo HTTP status, không theo `error_code`.
- Các form tạo/sửa dùng `serverErrorMessage`; khi API trả `Forbid()` (body rỗng) sẽ rơi về fallback chung, **không hiển thị thông báo công ty cụ thể**.

**Việc cần đưa vào DF-7/DF-8 follow-up (chưa sửa):**
- Thêm message cho `FORBIDDEN` (và các mã mới của DF-7) trong `formErrors.ts`.
- Rà các chỗ so sánh `COMPANY_MISMATCH` trực tiếp (hiện chỉ thấy map trung tâm; không thấy điều kiện `if (code === 'COMPANY_MISMATCH')` rải rác).
- Import flow giữ nguyên vì đã dùng 403.

---

## 0c. Quyết định (đã duyệt)

- `maintenance.tickets`: create, update, complete.
- `maintenance.tickets.approve`: inspect, close.
  - Chặn tự nghiệm thu: actor == `CreatedById` của phiếu → 403 `SELF_APPROVAL_FORBIDDEN`;
  - superuser được miễn.
- Delete: superuser-only (gate trong handler + giữ policy controller).
- Reopen: giữ nguyên.
- Đọc phiếu: giữ `assets.view`.
- `PermissionCatalog`: thêm 2 key + nhãn tiếng Việt.
- Chuyển đổi dữ liệu: chạy đúng một lần bằng `SystemSetting` key
  `migration.df7_ticket_permissions`; nhóm có `assets.edit` nhận cả 2 key; ghi ActionLog
  cho mỗi nhóm được cấp. Có cờ thì không làm gì nữa.
- 403 do company-scope/nghiệp vụ trả body `{ error_code, message }`, không dùng `Forbid()` rỗng.
- Frontend: tạo/sửa/hoàn thành = `maintenance.tickets`; kiểm tra/đóng = `.approve`;
  xóa/mở lại = superuser; ẩn nút Kiểm tra với người tạo phiếu.
- Seed: KTV thêm `maintenance.tickets`, không `.approve`, giữ `assets.edit`;
  thêm `demo-lead` (Tổ trưởng) với `maintenance.tickets` + `.approve` + `assets.view`.
  - Lưu ý: script seed không tự thêm `.approve` cho KTV; nếu nhóm KTV đã tồn tại với
    `assets.edit` tại thời điểm migration chạy, migration cấp `.approve` theo quy tắc chung.

> Đối chiếu hiện trạng: workflow code đúng `CompletionDate → Inspect → Close`; `Confirm` tài sản
> vẫn thuộc `assets.edit`. Không có mâu thuẫn cần dừng.

---

## 1. Hiện trạng endpoint `AssetMaintenances`

Prefix: `/api/v1`.

| Route | Policy | Handler gate | Company-scope |
|---|---|---|---|
| `GET /assets/{assetId}/maintenances` | `assets.view` | list query | Chỉ phiếu thuộc asset/company nhìn thấy |
| `GET /maintenances` | `assets.view` | list-all query | `CompanyId == null` hoặc own company |
| `GET /maintenances/{id}` | `assets.view` | detail query | Ngoài company → 404 |
| `POST /assets/{assetId}/maintenances` | `assets.edit` | `CreateMaintenanceCommand` | Asset not found → 404; asset company khác → `FORBIDDEN` 403; `CompanyId = Asset.CompanyId ?? Guid.Empty` |
| `POST /maintenances` | `assets.edit` | cùng `CreateMaintenanceCommand` | như trên |
| `PUT /maintenances/{id}` | `assets.edit` | `UpdateMaintenanceCommand` | Ngoài company → 404; closed → `MAINTENANCE_CLOSED`; whitelist + field lock |
| `DELETE /maintenances/{id}` | `assets.edit` | `DeleteMaintenanceCommand` | **Superuser gate trước lookup**; soft-delete; full snapshot log |
| `POST /maintenances/{id}/close` | `assets.edit` | `CloseMaintenanceCommand` | Ngoài company → 403; closed/completion/inspected guards |
| `POST /maintenances/{id}/inspect` | `assets.edit` | `InspectMaintenanceCommand` | Ngoài company → 403; closed/completion guards |
| `POST /maintenances/{id}/reopen` | `assets.edit` | `ReopenMaintenanceCommand` | **Superuser gate trước lookup**; company-scope defense; must be closed |

Lưu ý: `reopen` đã được phục hồi superuser gate ở DF-1R; `assets.edit` chỉ là policy ngoài controller.

---

## 2. Ngữ nghĩa từng thao tác

### Create
`CreateMaintenanceCommand.cs:80-139`:
- Validate title/date/cost/supplier.
- Asset phải tồn tại; asset company khác → `FORBIDDEN`.
- `CompanyId = Asset.CompanyId ?? Guid.Empty`.
- Có thể set `CompletionDate` ngay khi tạo; snapshot tài sản được chụp tại thời điểm tạo.
- Không có cột `Status` riêng; trạng thái suy ra từ `CompletionDate`, `InspectedById`, `IsClosed`.

### Update
`UpdateMaintenanceCommand.cs:88-154`:
- Ngoài company → 404 (hide existence).
- `IsClosed` → reject toàn bộ field.
- `StartDate` là field khóa; gửi khác → `FIELD_LOCKED`.
- Patch-safe whitelist: Title, Notes, Type, SupplierId, CompletionDate, Cost, IsWarranty; assignees replace-all (max 5).
- Có thể set `CompletionDate` = hoàn thành; **không có endpoint `/complete` riêng**.

### Complete
- Không có command/endpoint riêng. “Hoàn thành” = `PUT /maintenances/{id}` với `CompletionDate`.
- Không có gate riêng ngoài `assets.edit`.
- Sau complete, phiếu vẫn sửa được (trừ field khóa) cho đến khi close.

### Inspect
`MaintenanceWorkflowCommands.cs:177-213`:
- Actor: bất kỳ ai có `assets.edit` + cùng company/floater.
- Trạng thái trước: `IsClosed == false`, `CompletionDate != null`.
- Sau: `InspectedById = CurrentUserId`, `InspectedAt = now`.
- Lặp lại được (ghi đè lần trước); **không khóa** phiếu.
- Là bước “nghiệm thu” trước close theo comment code (`Hoàn thành → Kiểm tra → Đóng`).
- **Không có kiểm tra người tạo/sửa phiếu**: creator/editor có `assets.edit` **có thể tự inspect**.
- **Không có kiểm tra assignee/reviewer**.

### Close
`MaintenanceWorkflowCommands.cs:89-134`:
- Actor: `assets.edit` + cùng company/floater.
- Guard: chưa closed, đã completion, đã inspected.
- Sau: `IsClosed = true`, `ClosedAt/ClosedById`; phiếu bị đóng băng hoàn toàn.
- **Không chặn tự nghiệm thu/tự đóng**; cùng một người có thể complete → inspect → close.
- Không có reviewer riêng.

### Reopen
`MaintenanceWorkflowCommands.cs:257-291`:
- Controller `assets.edit`; handler **superuser gate** trước lookup.
- Company-scope defense (floater/own); nhưng regular user bị chặn bởi superuser gate.
- Chỉ phiếu đã closed; giữ `ClosedAt/ClosedById` làm lịch sử lần đóng gần nhất.
- ActionLog `Reopen`.

### Delete
`DeleteMaintenanceCommand.cs:64-...`:
- Controller `assets.edit`; handler **superuser gate** trước lookup.
- Soft-delete `DeletedAt`; ActionLog full snapshot.
- Hiện tại không có quyền riêng; superuser-only ở handler.

---

## 3. Sau khi tách, `assets.edit` còn gate gì?

Nếu tách toàn bộ quyền phiếu khỏi `assets.edit`, các endpoint còn dùng `assets.edit`:

| Route | Controller |
|---|---|
| `PUT /assets/{id}` | `AssetsController.UpdateAsset` |
| `POST /assets/{id}/confirm` | `AssetsController.Confirm` |
| `POST /assets/{id}/archive` | `AssetsController.Archive` |
| `POST /assets/{id}/unarchive` | `AssetsController.Unarchive` |

(`assets.edit` hiện cũng gate 7 endpoint `AssetMaintenances`; sau tách sẽ bỏ khỏi các endpoint đó.)

Các quyền khác vẫn giữ:
- `assets.checkout` / `assets.checkin`: checkout/checkin tài sản.
- `assets.correct`: đính chính lịch sử lắp đặt.
- `assets.delete`: xóa tài sản.

---

## 4. Đề xuất (chờ duyệt — KHÔNG tự quyết)

### 4.1 `maintenance.tickets` gồm thao tác nào?

Phương án đề xuất để duyệt:

| Thao tác | Quyền đề xuất |
|---|---|
| Xem danh sách/chi tiết phiếu | Giữ `assets.view` (hiện tại) hoặc thêm `maintenance.tickets.view` |
| Tạo phiếu | `maintenance.tickets` |
| Sửa phiếu / nhập CompletionDate | `maintenance.tickets` |
| Xóa phiếu | Xem 4.3 |
| Kiểm tra (inspect) | Xem 4.2 |
| Đóng (close) | Xem 4.2 |
| Mở lại (reopen) | Giữ superuser gate (mặc định) |

Nếu muốn tách đọc riêng, thêm `maintenance.tickets.view`; nếu không, giữ `assets.view` để tránh thêm key.

### 4.2 Inspect/close — chung hay tách? Có chặn tự nghiệm thu?

Các phương án:

**A. Một quyền `maintenance.tickets` cho cả create/update/inspect/close**
- Đơn giản, ít key.
- Không chặn self-approval.
- Rủi ro: người tạo phiếu tự complete → inspect → close.

**B. Tách `maintenance.tickets` (create/update) và `maintenance.tickets.approve` (inspect/close)**
- Có thể chặn `CreatedById == CurrentUserId` (không cho tự inspect/close).
- Hiện model chỉ có `CreatedById`, **không có `UpdatedById`/last-editor**; nếu muốn chặn “người sửa cuối” phải thêm cột/migration.
- Cần thêm key mới vào `PermissionCatalog`.
- Đề xuất để duyệt: **B**, với self-approval block dựa trên `CreatedById`; nếu cần chặn cả editor, thêm `UpdatedById` ở phase sau.

**C. Dùng `maintenance.campaigns` cho inspect/close**
- Không đúng ngữ nghĩa (campaign là đợt bảo dưỡng), không đề xuất.

> Không tự chọn A/B/C. Cần quyết định sản phẩm: KTV có được tự nghiệm thu phiếu mình tạo không?

### 4.3 Delete — quyền nào hiện có?

`PermissionCatalog` hiện có các lựa chọn:
- `assets.delete` — quyền xóa tài sản, phù hợp bản chất xóa lịch sử tài sản.
- `assets.correct` — quyền đính chính lịch sử nhạy cảm.
- `admin` — quyền quản trị chung.
- `superuser` — toàn quyền.

Đề xuất mặc định: **giữ superuser-only** như hiện tại (an toàn nhất, không đổi behavior).
Nếu muốn non-superuser xóa: ưu tiên `assets.delete`; `assets.correct` là lựa chọn chặt hơn. Không thêm key mới nếu chưa cần.
Không đổi soft-delete + full snapshot ActionLog.

### 4.4 Reopen

Mặc định **giữ superuser gate** (theo yêu cầu). Controller policy sau tách có thể là `maintenance.tickets` hoặc giữ `assets.edit`; nếu giữ `assets.edit` thì user có `assets.edit` vẫn thấy nút reopen nhưng handler vẫn 403 nếu không superuser. Đề xuất đổi policy controller sang `maintenance.tickets` để quyền hiển thị khớp thực tế; handler superuser gate không đổi.

---

## 5. Chuyển đổi dữ liệu khi nâng cấp

Mục tiêu: nhóm đang có `assets.edit` tự nhận key mới (`maintenance.tickets`, và `maintenance.tickets.approve` nếu chọn B), **không cấp cho nhóm không có `assets.edit`**.

### Cơ chế đề xuất: `StartupDataSeeder` idempotent

Lý do:
- Permission/group là dữ liệu runtime, không phải schema.
- Seeder đã chạy mỗi startup và idempotent; dễ test.
- Tránh SQL trực tiếp trong migration.

Luồng:
1. Sau khi seed system groups + demo groups, đọc tất cả `group_permissions` có `assets.edit` Grant.
2. Với mỗi group, kiểm tra key mới đã tồn tại chưa.
3. Chỉ `Add` các `GroupPermission` còn thiếu; `SaveChanges` một lần; bọc transaction.
4. Ghi log rõ số group/key đã thêm; không xóa/không sửa quyền cũ.
5. Không áp dụng cho superuser/admin system groups nếu họ đã có toàn quyền; vẫn idempotent.

### Chứng minh idempotent
- Test chạy seeder 2 lần trên cùng context; count `group_permissions` của key mới không đổi sau lần 2.
- Test group có `assets.edit` → được thêm; group không có → không thêm.
- Chạy `scripts/check-company-scope.sql`/query count trước-sau trên DB tạm; lần 2 = lần 1.

### Rollback
- Ứng dụng rollback: policy mới không còn được dùng; grant mới **vô hại** (không tự xóa để tránh mất quyền do admin cấp tay).
- Nếu buộc phải thu hồi: dùng API admin (không SQL trực tiếp), chỉ xóa đúng key mới khỏi danh sách group ID đã ghi log; ActionLog của thao tác nhóm quyền là audit trail.
- Không rollback bằng cách xóa toàn bộ `group_permissions` hoặc sửa schema.

---

## 6. Frontend — nút/màn hình cần đổi gate

| File | Hiện tại | Đề xuất |
|---|---|---|
| `features/maintenance/pages/MaintenanceListPage.tsx:27` | `assets.edit` cho “Thêm bảo trì” | `maintenance.tickets` (create) |
| `features/maintenance/components/MaintenanceTable.tsx:209-210` | `assets.edit` cho edit/delete | edit = `maintenance.tickets`; delete = quyền delete đã duyệt |
| `MaintenanceTable.tsx:372-383` | complete/inspect/close dùng `canEditMaintenance` | complete = `maintenance.tickets`; inspect/close = `.approve` nếu chọn B |
| `MaintenanceTable.tsx:405-415` | reopen hiện `superUser`; delete `canDeleteMaintenance` | giữ superuser reopen; delete theo quyền mới |
| `features/asset/components/AssetMaintenanceSection.tsx:27` | `assets.edit` cho create/edit/inspect/close/delete | tách theo cùng map |
| `AssetMaintenanceSection.tsx:322-326` | reopen `superUser`; delete `canDeleteMaintenance` | giữ reopen; delete theo quyền mới |
| `features/asset/pages/AssetDetailPage.tsx:59` | `assets.edit` cho sửa tài sản | giữ `assets.edit` (không phải phiếu) |
| `features/maintenance/pages/MaintenanceCampaign*` | `maintenance.campaigns` | giữ nguyên (đợt bảo dưỡng, không phải phiếu) |
| `shared/utils/formErrors.ts` | map `COMPANY_MISMATCH`/`COMPANY_REQUIRED`, thiếu `FORBIDDEN` | thêm message `FORBIDDEN` và mã DF-7 |
| `features/import/services/import.service.ts:30` | comment 403 out-of-scope | giữ; chỉ rà lại text nếu cần |

Ngoài ra, menu “Phiếu bảo trì” hiện gate theo `assets.view`; quyết định có cần `maintenance.tickets.view` hay không.

---

## 7. Seed demo — quyền KTV sau DF-7 (đề xuất)

Đề xuất mặc định (chờ duyệt):

```text
demo-tech:
  assets.view, systems.view
  models.view, locations.view, suppliers.view, categories.view
  assets.create, assets.edit, assets.checkout, assets.checkin, assets.audit
  consumables.checkout, components.checkout
  maintenance.view
  maintenance.tickets            # create/update phiếu của mình
  maintenance.campaigns          # đợt bảo dưỡng (giữ)
  # KHÔNG maintenance.tickets.approve nếu chọn B
  # KHÔNG maintenance.templates, companies.view, reports.view
```

Nếu chọn A (một quyền), KTV có `maintenance.tickets` và có thể inspect/close; cần quyết định self-approval.

`demo-leader`: giữ `assets.view/systems.view/reports.view/export`; nếu được duyệt làm người nghiệm thu, thêm `maintenance.tickets.approve` — chưa tự thêm.

Admin/superuser: không đổi.

---

## 8. Test sẽ thêm/sửa

### Test thêm
- `CreateTicket_WithTicketsPermission_WithoutAssetsEdit_Succeeds`
- `CreateTicket_WithAssetsEditOnly_Forbidden` (sau khi tách)
- `UpdateTicket_WithTicketsPermission_Succeeds`
- `DeleteTicket_NonSuperuser_Forbidden` (nếu giữ superuser-only)
- `DeleteTicket_Superuser_Succeeds_AndLogs`
- `Inspect_ByCreator_Forbidden` (nếu chọn B + self-block)
- `Inspect_ByDifferentApprover_Succeeds`
- `Close_RequiresApproved...` (nếu tách)
- `Reopen_NonSuperuser_Forbidden` / `Reopen_Superuser_Succeeds`
- `StartupSeeder_GrantsTicketsToGroupsWithAssetsEdit_AndIsIdempotent`
- `StartupSeeder_DoesNotGrantToGroupsWithoutAssetsEdit`

### Test cũ sẽ đổi assertion
- `AssetMaintenanceTests` hiện gọi handler trực tiếp; nếu policy chuyển từ controller sang quyền mới, các test controller cần assert `ForbidResult`/403 thay vì gọi handler.
- Các test `Create/Update/Delete/Close/Inspect` đang dùng `assets.edit` trong controller mocks; sẽ đổi policy attribute test hoặc tách test authorization riêng.
- `MaintenanceTable`/`AssetMaintenanceSection` không có test frontend runner; verify UI thật theo workflow.

Lý do đổi: quyền mới không còn nằm trong `assets.edit`; test phải chứng minh user chỉ có `assets.edit` bị chặn, user có `maintenance.tickets` mới được phép.

---

## Quyết định cần duyệt

1. `maintenance.tickets` gồm create/update/complete; có thêm `.view` không?
2. Inspect/close: dùng chung `maintenance.tickets` hay tách `.approve`; có chặn tự nghiệm thu theo `CreatedById` không?
3. Delete: giữ superuser-only, `assets.delete`, hay `assets.correct`?
4. Có thêm `UpdatedById` để chặn “người sửa cuối tự nghiệm thu” không?
5. Seed KTV có được inspect/close phiếu mình tạo không?

**DỪNG — chỉ viết plan, chưa sửa code, chưa commit.**
