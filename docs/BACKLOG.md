# BACKLOG — các phát hiện đang chờ xử lý

> Các phát hiện từ audit/migration được đăng ký tại đây để xử lý riêng, KHÔNG trộn vào task
> tái cấu trúc đang chạy (nguyên tắc: parity trước, cải thiện sau).

---

## 🆕 ĐỢT 2 — 2026-10-02 (10 mục I → A → B → C → D → E → F → G → H → J)

> **Trạng thái: xong 10/10 mục, TẤT CẢ commit LOCAL — CHƯA PUSH** (chờ duyệt). Luật mới áp dụng
> xuyên đợt: mỗi commit phải chạy ĐÚNG 6 gate CI ở local (`scripts/precommit-gates.ps1`) và mọi thay
> đổi hành vi phải verify LIVE trên stack thật (§1.9 DEVELOPMENT_WORKFLOW).

| Mục | Nội dung | Commit | Bằng chứng verify |
|---|---|---|---|
| **I** | `.editorconfig` + `.gitattributes` (chốt **LF**, giải thích: index đã 100% LF, 32 file có BOM nên KHÔNG đặt `charset`) + `scripts/precommit-gates.ps1` + §1.9 quy tắc gate/CI | `b6a9add` | `git ls-files --eol` 585/585 `i/lf w/lf`; `git diff --ignore-all-space` rỗng (không cần commit chuẩn hoá riêng) |
| **A (BUG-M)** | `UsersController` thin 100% (chỉ `IMediator`); Create/Update/Delete/AdminReset thành `ILoggableCommand`, guard+company-scope chuyển vào handler → **hết log kép** | `84ba53f` | LIVE trước/sau trên cùng luồng: delta ActionLog **2/2/2 → 1/1/1**; 474 test PASS |
| **B** | `SystemConfigController` (controller FAT cuối cùng) → MediatR; contract format tách sang Domain (`AssetTagFormat`), 2 write command = `ILoggableCommand` | `7a6a9dc` | Parity 2-run **11/11 call giống hệt** (status+body); log vẫn 1 row/change, 0 row cho invalid/no-op |
| **C** | `AccessoriesController.GetCheckouts` → `GetAccessoryCheckoutsQuery` (controller hết EF trực tiếp) | `42814c0` | Parity 2-run **3/3** (kể cả 404 hide-existence) |
| **D** | `ConcurrencyRaceAuditTests` tự dọn fixture qua API (return/checkin trước, rồi báo cáo BLOCKED kèm error_code của delete-guard) + sửa 2 lỗi tiềm ẩn: `AdminUserId` hardcode **stale sau reset DB** (test âm thầm không test gì) và tên fixture trùng khi chạy lại; base URL cấu hình qua `MIRATS_TEST_BASE_URL` | `1842dc0` | Chạy thật: race cho đúng 1 winner/lần; cleanup return 5/5 accessory+component+license-seat; xoá bị chặn **đúng thiết kế** (`ACCESSORY_HAS_CHECKOUTS`, `COMPONENT_HAS_ALLOCATION_HISTORY`, `LICENSE_IN_USE`, `ASSET_CONFIRMED_CANNOT_DELETE`, `CATEGORY_IN_USE`, `COMPANY_IN_USE`) |
| **E** | N3+N4+N10 patch-safety: `UpdateCompanyCommand` Name/ParentId (absent → giữ, `Guid.Empty` → re-root), `UpdateGroupCommand` Description, null-guard `permissions` (handler + controller) | `ca75791` | LIVE: PUT thiếu `parentId` **giữ nguyên cha** (trước: re-root), sentinel re-root OK, tên trùng/blank → 400 (trước: 500), rename-only giữ description; 10 test mới |
| **F** | N6 Web Locks cross-tab refresh + N7 cache quyền **khoá theo danh tính** + `clearPermissionCache()` khi logout + N11 timeout refresh/login/logout | `db439c2` | Browser thật **2 tab**: `navigator.locks` chặn chéo tab (`otherTabLock: BLOCKED-timeout` khi tab kia giữ lock, `storageShared` xác nhận cùng origin); tab 2 boot refresh 200, cả 2 tab vẫn đăng nhập |
| **G** | Docs: ERROR_CODES xoá **7 mã Keycloak chết** + thêm **10 mã auth mới** (kèm HTTP status), ARCHITECTURE/HANDOFF_LATEST/DEVELOPMENT_WORKFLOW/.clinerules/skills bỏ Keycloak "hiện hành" | `8b9e6da` | grep `KEYCLOAK_[A-Z_]+` trong `*.cs` = **0**; 5 mã auth spot-check đều tồn tại trong code |
| **H** | N8 `AssetMaintenanceSection` đọc `company.id` (DTO không có `companyId` phẳng) + dọn mojibake **11 file / 62 dòng** (gồm 2 message API bị ký tự thay thế U+FFFD) | `09bd65e` | UI thật: dropdown "Người phụ trách" của asset thuộc công ty X hiện **đúng 2/37 user**; build 0 warning |
| **J** | N13 tách row-lock ra `IRowLockService` (Application hết ref Npgsql) · N14 đổi mật khẩu **giữ session hiện tại** · N15 2 comment stale · N16 `SuperuserClaims` → `Authentication/` · N17 hết warning (CVE OpenApi → 2.12.0, CS0618, CS8xxx) · N18 chunk threshold có ghi số đo · N19 **`FOR UPDATE`** cho refresh rotation · N20 docs 103 dòng | `c07b7c7` | N19 LIVE **6-way cùng cookie: 1×200 + 5×401**, chỉ 1 access token, family bị revoke; N14 LIVE: session A refresh 200 / session B 401; build **0 warning**; `dotnet ef migrations has-pending-model-changes` = không lệch model |

### ✅ QUYẾT ĐỊNH ĐÃ DUYỆT (đợt 2 — 2026-10-02)

- **N14 — `POST /api/v1/auth/password` (người dùng tự đổi mật khẩu): GIỮ session đang dùng, CHỈ revoke
  các session KHÁC.** Quyết định này **thay thế** quyết định trước đó ("revoke TẤT CẢ session, kể cả
  thiết bị vừa đổi mật khẩu").
  - **Lý do duyệt:** tài liệu `ChangePasswordCommand` đã ghi ý định "all OTHER refresh sessions revoked
    (this device stays logged in)" từ AUTH Phase 1, nhưng code revoke cả credential của chính người gọi
    → thiết bị đó chỉ dùng được tới khi access token hết hạn (15') rồi bị đăng xuất âm thầm. Đổi mật
    khẩu tự phục vụ không nên tự đăng xuất chính người dùng.
  - **Cài đặt:** controller truyền cookie refresh hiện tại (`IAuthCookieService.GetRefreshCookie()`);
    handler so **hash** token đó và loại trừ đúng credential khỏi lệnh revoke. Nếu request **KHÔNG** kèm
    cookie (client khác, gọi API trực tiếp) → giữ **mặc định an toàn cũ**: revoke tất cả.
  - **Verify:** unit `ChangePassword_WithCurrentRefreshToken_KeepsThatSession_RevokesOthers` + LIVE 2
    session thật (login 2 lần → đổi mật khẩu bằng session A có cookie → **refresh A = 200**, refresh B =
    **401**; login mật khẩu cũ = 400, mật khẩu mới = 200). Commit `c07b7c7`.

### 📌 Phát hiện MỚI trong đợt 2 (chưa xử lý — đăng ký để làm sau)

1. **`AppDbContext` có 2 block `modelBuilder.Entity<LicenseSeat>` trùng nhau** (L585 và L648 — phát
   hiện khi sửa N17). Model nhận hợp của cả hai (index + column type ở block 2, navigation License ở
   block 1) nên **hiện không sai**, nhưng rất dễ sửa nhầm một nửa. Đề xuất: gộp về 1 block (phải soát
   `dotnet ef migrations has-pending-model-changes` = rỗng sau khi gộp).
2. **`Invoke-WebRequest`/HttpWebRequest của PowerShell 5.1 ÂM THẦM BỎ header `Cookie` set tay** — chỉ
   dùng `System.Net.Http.HttpClient` (hoặc `-WebSession`) khi cần test cookie; nếu không sẽ kết luận
   sai về hành vi server (đã xảy ra với N14: tưởng fix lỗi, thực ra test không gửi cookie).
3. **`docs/HANDOFF_LATEST.md` (và `docs/sql/`, `backups/*.sql`)** còn mojibake lịch sử/dump — cố ý
   không sửa (là ghi chép sự cố); `backups/*.sql` là artifact backup, không phải code.

### 🚧 OPEN (LOW) — chờ xử lý

1. **Gộp 2 block `modelBuilder.Entity<LicenseSeat>` trùng nhau trong `AppDbContext`** (L585 và L648 —
   cùng phát hiện #1 ở trên).
   - **Tiêu chí nghiệm thu:** sau khi gộp, `dotnet ef migrations has-pending-model-changes --project
     aspire-react.Server` phải trả **"No changes have been made to the model since the last migration."**
     — tức gộp xong KHÔNG được sinh model diff, không cần migration mới.
   - **Ghi chú khi làm:** block **L585** chứa navigation `License` (`WithMany(l => l.LicenseSeats)` +
     cascade); block **L648** chứa index (`LicenseId`; unique `LicenseId`+`SeatNumber`), default
     `SeatNumber`, kiểu cột `CreatedAt`/`UpdatedAt` và check constraint `CK_license_seats_single_target`
     (đã chuyển sang `ToTable(t => t.HasCheckConstraint(...))` ở N17) → gộp phải giữ **đủ cả hai nhóm**,
     và vẫn phải chạy đủ 6 gate trước khi commit.
2. **MT-TZ-B — chuyển 3 cột NGÀY THUẦN sang `date` + `DateOnly`** (`asset_maintenances.StartDate`,
   `asset_maintenances.CompletionDate`, `licenses.TerminationDate`). Hiện vẫn là `timestamp without time zone`
   chứa mốc nửa đêm giờ VN quy đổi UTC (VD `2026-11-09 17:00:00` = 10/11/2026 giờ VN) và đã được trả kèm `Z`
   theo phương án **A-ngày** (MT-TZ, 2026-10-19) nên **không lệch trong luồng hiện tại**.
   - **Điều kiện kích hoạt:** khi xuất hiện **báo cáo / bộ lọc theo NGÀY** trên 3 cột này — lúc đó
     `::date` / `date_trunc` theo UTC sẽ lệch **±1 ngày** so với ngày nghiệp vụ VN; phải backfill
     `AT TIME ZONE 'UTC' AT TIME ZONE 'Asia/Ho_Chi_Minh'`, đổi cột sang `date`, DTO/handler sang `DateOnly`.
   - **Tiêu chí nghiệm thu:** 0 chỗ so sánh/lọc theo ngày lệch 1 ngày khi chạy bộ lọc "theo ngày" trên dữ
     liệu thật; `dotnet ef migrations has-pending-model-changes` rỗng sau migration.
3. **Dọn `ToApiKind`/`ToColumnKind` (LL-3) trong `AssetTimelineQuery.cs`** — 2 helper chuyển `DateTimeKind`
   cho mốc cursor `occurredAt` được tạo ở giai đoạn cột còn naive. Sau MT-TZ (ValueConverter đọc ra
   `Kind=Utc` cho mọi cột naive đã đăng ký) chúng **không còn cần thiết**, nhưng để lại cũng không sai.
   - **Tiêu chí nghiệm thu:** bỏ 2 helper mà `AssetTimelineQueryTests` + phân trang cursor trên **DB thật**
     vẫn PASS, và mốc `occurredAt` trả ra vẫn có `Z` đúng như trước.
4. **BUG-S — `UpdateMaintenanceCommand` ghi ActionLog Update nhưng KHÔNG có `LogMeta.changes`**
   *(✅ RESOLVED 2026-10-19 — AUDIT-LOG **B2**, commit `921fc29`: handler dùng `ActionLogChanges`, `LogMeta`
   gồm 7 field whitelist + **danh sách assignee old/new**; đã verify PUT thật trên DB tạm.)*
   (phát hiện 2026-10-19 khi verify MT-TZ Bước 3 trên DB tạm: dòng `action_logs` Update của bảo trì có
   `LogMeta = NULL` trong khi convention §3.2 yêu cầu `{ changes: { field: { old, new } } }` cho mọi Update).
   - **Hiện trạng:** `BuildLogEntry` (`Application/AssetMaintenances/Commands/UpdateMaintenanceCommand.cs`
     L49-63) chỉ set `ItemType/ItemId/ActionType/CreatedBy/CompanyId/Note`; handler whitelist 7 field
     (Title/Notes/Type/SupplierId/CompletionDate/Cost/IsWarranty) + danh sách assignee nhưng **không chụp
     giá trị cũ** trước khi gán ⇒ hiện không có dữ liệu để dựng old→new.
   - **Hệ quả:** sự kiện UPDATE bảo trì trên timeline thiết bị rơi vào nhánh "details mặc định"
     (`AssetTimelineQuery.DescribeChanges` đọc `LogMeta.changes`) — mất truy vết thay đổi field.
   - **Tiêu chí nghiệm thu:** PUT bảo trì thật (đổi ≥1 field) ⇒ `LogMeta` có dạng
     `{"changes":{"title":{"old":…,"new":…}}}` với `old` **chụp TRƯỚC khi gán**; test unit chứng minh
     old ≠ new khi đổi thật; timeline hiện đúng field thay đổi.

5. ~~Flaky lớp `WebAuthnCeremonyTests` khi chạy toàn bộ suite~~ **✅ RESOLVED 2026-10-19 — nguyên nhân
   KHÔNG phải state dùng chung (giả thuyết ban đầu sai), mà là input XÁC SUẤT trong code test:**
   - **Nguyên nhân gốc:** `TestAuthenticator.AssertionResponse` (`Tests/WebAuthn/TestAuthenticator.cs:94`)
     gọi `AsnWriter.WriteIntegerUnsigned` thẳng trên 32 byte P-1363 thô. DER INTEGER yêu cầu big-endian
     **tối giản**: khi byte cao nhất của r (hoặc s) = `0x00` thì 9 bit đầu đều là 0 ⇒
     `ArgumentException: The first 9 bits of the integer value all have the same value …`. r/s là ngẫu
     nhiên ⇒ ~1/256 mỗi số (~1/128 mỗi chữ ký); lớp có 5 chữ ký/lần chạy ⇒ ~4%/run — **không phải race**.
   - **Bằng chứng tái hiện:** loop 30 lần chạy toàn bộ suite ⇒ FAIL đúng **run 20**, TRX
     `logs/trx/wa4/loop-20.trx` chỉ `TestAuthenticator.cs:94`; 21 run trước đó xanh (đúng bản chất
     xác suất). Trước khi tái hiện, đã kiểm tra các nguồn state dùng chung và loại trừ có dẫn chứng:
     `WebAuthnCeremonyTests.NewService` tạo `MemoryCache` + InMemory DB **mới mỗi test**
     (`wa-{Guid}`), `TestAuthenticator` không có field static (chỉ `const`), `Fido2Service` key cache
     theo `user.Id`/challenge; `AuthAttemptService.IpFailures` static (`AuthAttemptService.cs:24-25`)
     KHÔNG nằm trên đường đi của ceremony.
   - **Lỗi nằm ở CODE TEST, không phải code thật:** `WriteIntegerUnsigned` chỉ xuất hiện trong
     `TestAuthenticator` (grep toàn repo: 4 match, tất cả ở file test); production chỉ *verify* chữ ký.
   - **Fix:** thêm `TestAuthenticator.EncodeEcdsaSignatureForDer` — cắt byte `0x00` thừa (giữ 1 byte nếu
     giá trị = 0), `WriteIntegerUnsigned` tự thêm `0x00` khi bit dấu bật ⇒ giá trị r/s KHÔNG đổi, chỉ
     thành encoding DER chuẩn. **Không** tắt song song, không retry, không tăng timeout, không Skip.
   - **Test tất định:** `TestAuthenticatorDerTests` (6 test) — tái hiện đúng exception của code cũ + khoá
     hợp đồng encode (leading-zero r, high-bit r, r = 0, quét 256 tổ hợp byte cao, 200 chữ ký thật).
   - **Nghiệm thu:** **10 lần liên tiếp chạy toàn bộ suite ⇒ 672/672 PASS ×10**
     (`logs/trx/wa-fixed/fixed-1..10.trx`).

6. ~~**FE: nhãn tiếng Việt cho `ComponentUnitStatus`**~~ **(✅ RESOLVED 2026-10-19 — AUDIT-LOG B7a:**
   `formatLogDetail` map thêm `InStock: 'Trong kho'`, `Allocated: 'Đang cấp phát'`, `Damaged: 'Hỏng'`,
   `Disposed: 'Đã loại bỏ'` cho key `status` (tập giá trị không trùng với AssetStatus). Log cấp phát/thu hồi
   serial + đổi trạng thái unit nay hiển thị tiếng Việt; drawer "Lịch sử serial" dùng chung `ActionLogTable`.)*
7. ~~**FE: câu Note tiếng Anh lẫn trong UI tiếng Việt**~~ **(✅ RESOLVED 2026-10-19 — GĐ3):**
   đã dịch toàn bộ `Note` tiếng Anh sang tiếng Việt (danh sách: `Created/Updated/Deleted group`,
   `Group updated.`, `Group permissions updated`, `Created/Updated/Deleted accessory`, `Created/Updated/
   Deleted/Confirmed/Archived/Unarchived asset`, `Update blocked (confirmed asset)`, `Created/Updated/
   Deactivated user`, `User group assignments updated.`, `Created/Updated/Deleted/Confirmed consumable`).
   Nhật ký CŨ trong DB giữ nguyên (không backfill). 2 test assert note tiếng Anh đã cập nhật
   (`AccessoryTests`, `GroupLockoutWireTests`).

### 🧾 AUDIT-LOG — kế hoạch sửa `LogMeta.changes` (audit Bước 1 ngày 2026-10-19)

Bối cảnh: audit Bước 1 (CHỈ khảo sát) lập bản đồ toàn bộ điểm ghi dữ liệu + điểm ghi log:
**0 command ghi dữ liệu nào thiếu ActionLog hoàn toàn**, **0 controller tự `SaveChangesAsync`**;
vấn đề nằm ở (a) command Update **thiếu** `LogMeta.changes`, (b) `changes` **sai schema/thiếu field**
+ lỗi kiểu **BUG-P** (chụp "old" sau khi đã gán), (c) log **không cùng transaction** với dữ liệu ở
MaintenanceTemplates + MaintenanceCampaigns.

Thứ tự thực hiện: **B2 → B3 → B4 → B5 → B6** (B7 sau B6, **B8 để sau**).

1. **B2 — helper dùng chung + test kiến trúc bánh cóc + 3 command Update thiếu meta** *(✅ XONG —
   commit local B2, 2026-10-19)*
   - `ActionLogChanges` (`aspire-react.Application/Common/ActionLogs/ActionLogChanges.cs`):
     `Track()` chụp giá trị CŨ (bắt buộc gọi TRƯỚC khi gán) → `Set()` giá trị MỚI;
     `ToLogMeta()` = `{"changes":{"field":{"old":…,"new":…}}}` tên field **snake_case**, tự bỏ field
     không đổi, **không đổi gì ⇒ `null`**; `TrackSensitive/SetSensitive` cho trường nhạy cảm chỉ ghi
     `{"changed":true}`; `Set()` khi chưa `Track()` ⇒ ném (chặn BUG-P).
   - `ActionLogMetaRatchetTests` (bánh cóc 2 chiều): mọi request `Update*/Close/Inspect/Reopen` phải có
     dấu hiệu `changes`, trừ `Exempt` ghi tường minh (kèm lý do + bước sẽ xóa); ngoại lệ còn vi phạm lẫn
     ngoại lệ "đã sửa" đều FAIL. Đã negative-test: thêm `UpdateBogusRatchetProbeCommand` ⇒ FAIL đúng tên;
     thêm `UpdateComponentCommand` vào Exempt ⇒ FAIL "XÓA khỏi Exempt".
   - Áp helper cho 3 command từng **không có** `LogMeta`: `UpdateComponentCommand`, `UpdateLicenseCommand`
     (`serial` = nhạy cảm), `UpdateMaintenanceCommand` (**BUG-S**, gồm danh sách assignee old/new) — và
     **xóa 3 command này khỏi danh sách ngoại lệ**.
   - **Tiêu chí nghiệm thu:** test đơn vị helper (14) + test 3 command (6: đổi 1 field ⇒ old là giá trị CŨ
     thật, không đổi ⇒ `LogMeta` null, serial không bị lộ) + ratchet xanh; PUT thật trên DB tạm cho cả 3
     command ra `LogMeta` đúng.
2. **B3 — sửa schema sai** *(✅ XONG — commit local B3+B4, 2026-10-19)*: `UpdateConsumableCommand` cả 2
   nhánh dùng `ActionLogChanges` (nhánh Confirmed: chỉ `location_id`/`notes`; nhánh Unconfirmed: **đủ 15
   field**, `minAmt` chụp old TRƯỚC khi gán — hết BUG-P) + `ComponentAllocationService.SetUnitStatusAsync`
   (`{before, after, serialNo}` → `{"changes":{"status":{"old":…,"new":…}}}`); **đã xóa cả 2 mục khỏi
   `Exempt`**.
   - **Ratchet mở rộng phạm vi** sang tiền tố `Update/Upsert/Set/Edit/Patch/Close/Inspect/Reopen` + thêm
     canary chống thu hẹp phạm vi ⇒ **không lộ command vi phạm mới** (`SetAssetTagFormat`/
     `SetPasskeysEnabled`/`UpsertCampaignResult` đều đã có `changes`).
   - **Còn sót (⇒ B7):** `ComponentAllocationService` dòng ~130/~222 (Allocate/Return — action là
     Checkout/Checkin, không phải Update) vẫn dùng `{before, after, serialNo}`.
   - **Tiêu chí nghiệm thu:** test khẳng định `old` là giá trị cũ THẬT (đã có: 4 test Consumable + 1 unit
     status); PUT/PATCH thật trên DB tạm ra `LogMeta` đúng.
3. **B4 — diệt BUG-P còn lại** *(✅ XONG — commit local B3+B4, 2026-10-19)*:
   `UpdateManufacturerCommand` (5 field) + `UpdateSupplierCommand` (12 field) — `Track()` chụp old TRƯỚC
   mọi nhánh gán nên `code`/`name`/`url`/`city`… có old thật; gỡ cả mô tả "parity quirk" cũ trong file.
   - **Tiêu chí nghiệm thu:** test: đổi code/name/url/city ⇒ `old` là giá trị cũ thật; PUT thật trên DB tạm.
4. **B5 — log cùng transaction với dữ liệu** *(✅ XONG — commit local B5, 2026-10-19)*: 17 handler
   (13 MaintenanceTemplates + 4 MaintenanceCampaigns) đã chuyển sang helper
   `Application/Common/Persistence/DbTransaction` (`CreateExecutionStrategy` + `BeginTransactionAsync`):
   dữ liệu + `ActionLog` ghi trong CÙNG transaction, **commit 1 lần**. Publish giữ 2 SaveChanges có thứ tự
   (unique index `IsCurrent`) nhưng log nằm TRONG transaction; Upsert giữ vòng retry 23505 bằng
   **SAVEPOINT** (`CreateSavepointAsync`/`RollbackToSavepointAsync`, chỉ khi `Database.IsRelational()`).
   - **Trước B5** (số lần `SaveChanges`): Create/Update/Delete Template 2; Create/Publish/Update/Delete
     Version 3 (Publish: 2 trong tx + 1 sau commit); Add/Update/Delete Item 2; Add/Update/Delete Param 2;
     CreateCampaign 2 (1 trong tx + 1 sau commit); Complete 2; UpsertResult 1 (trong vòng retry) + 1 sau;
     DeleteResult 2 — **tất cả đều không có log chung transaction** (trừ 2 tx chỉ chứa dữ liệu).
   - **Test (Postgres thật, `MaintenanceTransactionAtomicityTests`, 18 test)**: mỗi nhóm 1 ca thành công
     (có dữ liệu + có log) + 1 ca thất bại có chủ đích (**không dữ liệu + không log**). **Kỹ thuật gây lỗi**:
     `SaveChangesInterceptor` ném khi change tracker có `ActionLog` ở trạng thái `Added` (bước ghi nhật ký đổ).
     Ca mạnh nhất: `VersionPublish_ActionLogSaveFails_RollsBackVersionFlip` — dữ liệu đã được ghi bằng 2
     SaveChanges TRƯỚC bước log, lỗi ⇒ cả demote/promote rollback.
   - **Tiêu chí nghiệm thu:** 18/18 test atomicity PASS trên DB tạm; gates 6/6; nhóm ThrowawayDb 696/696.
5. **B6 — chuẩn hoá field còn thiếu** — tách 2 phần:
   - **B6a — ✅ XONG (commit local B6a, 2026-10-19):**
     · `UpdateUserCommand` dùng `ActionLogChanges` ⇒ thêm `employee_number`/`job_title`; toàn bộ field
       chuyển snake_case + **chỉ ghi field ĐỔI** (trước: 8/10 field, camelCase, ghi cả field không đổi).
       Đã cập nhật 2 test cũ khẳng định camelCase (`UserUpdatePatchSafetyTests`, `UserActionLogTests`).
     · `UpdateChecklistItemCommand` thêm `position_ids` (old[]/new[], so **không phân biệt thứ tự**) —
       thêm `ActionLogChanges.ToChangesMap()` để ghép `changes` cạnh `scope/versionId/versionNumber/itemId`.
     · `Close/Inspect/ReopenMaintenanceCommand` ghi `LogMeta.changes`: Close = `status` Open→Closed +
       `closed_at` + `closed_by_id`; Inspect = `inspected_by_id` + `inspected_at` (old = lượt kiểm tra
       TRƯỚC, có thể null); Reopen = `status` Closed→Open (không ghi closed_at/by vì cố ý giữ nguyên).
       **Đã xóa 3 lệnh khỏi `Exempt`** — bánh cóc giờ chỉ còn `UpdateComponentUnitStatusCommand`.
     · Test: `AuditLogB6aLogMetaTests` (7) + `ToChangesMap` (1); evidence bằng API thật trên DB tạm.
   - **B6b — ✅ XONG (commit local B6a+B6b, 2026-10-19)** — 23 `Delete*Command` đã khảo sát + bổ sung
     snapshot tối thiểu cho **14 lệnh xóa CỨNG từng có `LogMeta = null`**: Component, Group, CustomField,
     Location, Department, Company, Model, Category, Supplier, Manufacturer, SystemInfo, SystemPosition,
     MaintenanceTemplate, DeletePasskey. Nội dung snapshot CHỈ gồm `id` + `code` (nếu có) + `name`
     (+ `system_info_id` cho template, `item_no` cho component, `name` passkey) — **KHÔNG** dữ liệu nhạy cảm.
     · **Xóa mềm (giữ nguyên cơ chế):** `DeleteMaintenance` (full snapshot); `DeleteLicense` (DeletedAt,
       meta null — serial KHÔNG bị ghi); `DeleteComponentUnit` (DeletedAt + Qty−1, meta null);
       `DeleteUser` (IsActive=false; meta có email nhưng **không** PasswordHash).
     · **Nhạy cảm KHÔNG nằm trong bất kỳ ảnh chụp delete nào:** `User.PasswordHash`, `License.Serial`,
       passkey `CredentialId`/`PublicKey` — có test 2 lớp (`DeleteLogSensitivityTests`): guard tĩnh quét
       mọi `Delete*.cs` + test hành vi (license serial, passkey credential/public key, company snapshot).
     · **Lưu ý còn lại:** `User.Email` (PII) và `CampaignResult.Notes` (free-text) hiện CÓ trong meta —
       chưa thay đổi (cần quyết định sản phẩm nếu muốn ẩn).
   - **Tiêu chí nghiệm thu:** số field phủ = DTO; test khẳng định.
6. **B7 — gộp các `changes` hard-code còn lại về helper** *(✅ XONG 2026-10-19 — GĐ1 + GĐ2)*:
   - **GĐ2.4 (test bánh cóc THEO HÀNH VI):** `ActionLogBehaviorRatchetTests` chạy THẬT **31 command**
     (Update/Upsert/Set + Close/Inspect/Reopen) trên InMemory với dữ liệu thật, đọc `action_logs.LogMeta`,
     parse JSON, khẳng định `changes.<field>.old/new` ĐÚNG; + 1 test phủ sóng: mọi command trong phạm vi
     quét PHẢI có spec (thêm command mới mà quên ⇒ FAIL). **Đã xoá `ActionLogMetaRatchetTests`** (cách tìm
     chữ "changes" trong file) và **xoá nốt ngoại lệ cuối `UpdateComponentUnitStatusCommand`** (nay kiểm
     bằng hành vi). Probe âm đã chứng minh 2 chiều: (a) probe log thiếu wrapper `changes` ⇒ FAIL
     "LogMeta thiếu wrapper 'changes'"; (b) probe command không có spec ⇒ FAIL "CHƯA có spec hành vi".
   - **GĐ2.5 (gộp 28 chỗ `changes` viết tay về helper):** Update Department/Location/Category/Company/
     CustomField/Group/Permissions/UserGroups/Accessory/Asset/SystemInfo/SystemPosition/Model/
     MaintenanceTemplate/StandardParam/TemplateVersion, Checkout/Checkin/Archive/Unarchive/Confirm/Correct
     (Asset), Checkout/Checkin (Accessory), Complete/UpsertResult (Campaign), SetAssetTagFormat,
     SetPasskeysEnabled. Khác biệt CÓ CHỦ ĐÍCH: (i) tên field snake_case, (ii) field không đổi bị bỏ
     (không còn "noise" old==new; PUT không đổi gì ⇒ `LogMeta = null`), (iii) DateTime UTC có `Z`,
     (iv) tập hợp (permissions/group_ids) không đổi khi chỉ đổi thứ tự.
     **BUG-P phát hiện khi gộp:** `CheckoutAssetCommand` chụp `oldSnapshot` SAU khi gán Location/
     SystemPosition ⇒ log ghi old = giá trị MỚI; đã dời snapshot lên TRƯỚC (old nay là giá trị cũ thật).
   - **GĐ2.6 (FE):** `ActionLogTable.CHANGE_LABELS` thêm nhãn tiếng Việt cho ~48 field snake_case mới;
     verify UI thật 1440 (DB tạm): "Tên: … → …", "Trạng thái: Chờ cấp phát → Đã cấp phát"…
   - **GĐ1.1 (luật audit-sweeps):** Sweep 5 đã có (xem mục 5) + luật "commit rồi mới ghi log".
   - **Tiêu chí nghiệm thu:** grep không còn `new { changes` trong Application (ngoài helper) ✓; gates xanh ✓.
   - **Sót cụ thể:** `ComponentAllocationService` ~130/~222 đã sửa ở B7a (GĐ trước).
7. **B8 (ĐỂ SAU) — lưới an toàn auto-changes trong `ActionLogBehavior`** từ EF `ChangeTracker`
   (`OriginalValue` vs `CurrentValue`) cho ~52 command `ILoggableCommand`: phủ 100% field kể cả khi thêm
   field mới, nhưng phải (a) map tên snake_case + cập nhật nhãn timeline, (b) denylist PII
   (`PasswordHash`…), (c) ưu tiên `LogMeta` thủ công khi đã có, (d) không áp cho command tự quản
   transaction. Chỉ làm sau khi B2–B7 ổn định.

### 🤖 Đề xuất đưa `ConcurrencyRaceAuditTests` vào CI (mục D — CHƯA sửa `ci.yml`)

- **Hiện trạng:** test cần stack thật + quyền admin, tag `Category=Concurrency`, mặc định chỉ chạy tay.
- **Khả thi:** thêm 1 job chạy **trên DB dùng-một-lần** — `docker compose up -d --build` (hoặc service
  container Postgres + chạy API trực tiếp) → chờ `/health` → `MIRATS_TEST_BASE_URL=http://localhost:5000`
  + `MIRATS_TEST_ADMIN_PASSWORD` (từ CI secret) → `dotnet test --filter "Category=Concurrency"` →
  `docker compose down -v`.
- **2 điều kiện bắt buộc đã rõ:** (a) admin bootstrap mới bị `MustChangePassword=true` → job phải đổi
  mật khẩu 1 lần (`/auth/password`) trước khi test; (b) **KHÔNG bao giờ chạy trên DB dev/dùng chung** —
  các fixture có lịch sử cấp phát bị delete-guard giữ lại vĩnh viễn (census hiện tại: ~838 row `QCR-*`
  tích luỹ từ nhiều phiên, không xoá được qua API và **không được xoá bằng SQL** theo §8).
- **Chi phí:** ~5–10 phút/job (build image). Đề xuất: chạy ở nhánh `main`/nightly, không chặn PR.

---

## 🆕 AUDIT 2026-10-02 (sau MediatR + Auth migration) — phân loại N1–N20

- **Nguồn:** `docs/AUDIT_POST_MIGRATIONS_2026-10-02.md` (audit 8 mục: Clean Architecture,
  CQRS/MediatR, company-scoping, patch-safety, auth system, frontend FDA, backlog, docs).
- **Đợt 1 (các mục chặn push/triển khai)** — mỗi mục 1 commit LOCAL (chưa push):
  `863b3a9` (N1+N5) · `0ad2099` (N9-artifact) · `7501200` (N2).

### ✅ ĐỢT 1 — RESOLVED

| # | Mức | Phát hiện | Xử lý |
|---|---|---|---|
| **N1** | HIGH | `UpdateUserCommand` (Auth Phase 4 rewrite) gán vô điều kiện 5 field → payload thiếu field **xoá** `CompanyId`/`DepartmentId`/`LocationId`/`EmployeeNumber`/`JobTitle` (regression Task M2; live-confirmed qua API) | Patch-safe theo pattern BUG-E/N. Quy ước 3 giá trị cho Guid? (đã có sẵn trong dự án): **absent → giữ**, **`Guid.Empty` → clear (floater)**, **Guid thật → set**. FE edit-mode gửi sentinel khi admin xoá công ty. 9 test mới. Commit `863b3a9` |
| **N5** | HIGH | `UpdateUserCommand` không validate `CompanyId` **mới** theo scope actor (admin công ty A chuyển user sang công ty B) | Check `COMPANY_MISMATCH` (pattern Task L2/CreateUser) TRƯỚC mọi mutation; body `error_code` snake như CreateUser; superuser bypass; floater (Guid.Empty) cho phép. Live PASS + test. Commit `863b3a9` |
| **N9-artifact** | HIGH | Artifact triển khai còn Keycloak (compose service + `Keycloak__*`/`KC_BOOTSTRAP_*`, `.env.example`, `frontend/Dockerfile` VITE_KEYCLOAK_*, 2 script seed Keycloak, docker-reset filter) | Dọn sạch + **verify bằng `docker compose up -d --build` thật** trên DB trắng: 4 service healthy, 0 container keycloak, login `/api/v1/auth/login` 200, các endpoint bảo vệ 200. **3 blocker thật phát hiện khi verify** (xem dưới). Commit `0ad2099` |
| **N2** | HIGH | `AccessoriesController.Update` sửa bằng EF trực tiếp và **KHÔNG ghi ActionLog** (vi phạm ActionLog mandatory) | Migrate `List`/`GetById`/`Update` sang MediatR; `UpdateAccessoryCommand` = `ILoggableCommand` (log cùng transaction, có LogMeta changes). Parity 2-run baseline↔post: 4/4 call giống hệt field-for-field, log 1→2 entry. Commit `7501200` |

### 🚧 3 BLOCKER TRIỂN KHAI phát hiện khi verify compose (đã fix trong đợt 1)

1. **Không có admin trên DB trắng** → deploy xong không ai đăng nhập được (JIT provisioning đã xóa ở
   AUTH Phase 5; script seed Keycloak bị xóa cùng Keycloak). Fix: `StartupDataSeeder` **tự tạo** admin
   đầu tiên từ `AUTH_BOOTSTRAP_ADMIN_PASSWORD` + `INITIAL_ADMIN_USERNAME/EMAIL` (idempotent, không ghi
   đè hash đã có; bỏ qua + log warning nếu email đã dùng).
2. **`aspire-react.Server/Dockerfile` stale sau tách 4-layer (Giai đoạn 0.1)** — chỉ copy
   `Server` + `ServiceDefaults`, thiếu `Domain`/`Application`/`Infrastructure` → image **không build được**
   (CS0246). Fix: copy + restore đủ 5 project. (CI job build Docker image cũng hỏng vì lý do này.)
3. **`vite.config.ts` bắt buộc cert HTTPS dev cho MỌI command** → `npm run build` trong image production
   throw → `docker compose up --build` fail. Fix: yêu cầu cert chỉ còn ở `vite serve` (chính sách
   HTTPS-dev giữ nguyên; đã verify vẫn fail-loud khi thiếu cert).

### ⏳ ĐỢT 1 — còn OPEN (chờ duyệt đợt sau)

| # | Mức | Phát hiện | Ghi chú |
|---|---|---|---|
| N3 | MEDIUM | `UpdateCompanyCommand.cs:68-69` full-PUT: `Name` absent → DB NOT NULL violation (500); `ParentId` absent → re-root công ty | Cùng lớp BUG-E |
| N4 | MEDIUM | `UpdateGroupCommand.cs:70` `Description` gán trực tiếp → absent clear | Cùng lớp BUG-E |
| N6 | MEDIUM | FE multi-tab refresh race: 2 tab cùng refresh → reuse-detection revoke ALL → mất session cả 2 tab | Cần Web Locks (`navigator.locks`); server-side race 6-way đã PASS |
| N7 | MEDIUM | `clearPermissionCache()` dead code (0 caller) → logout/login user khác không F5 → gate theo user cũ | UI bug (backend vẫn chặn 403) |
| N8 | MEDIUM | `AssetMaintenanceSection.tsx:90` đọc `companyId` scalar không tồn tại trong asset detail → filter assignee vô hiệu | `AssetDetailDto` chỉ có object `company` |
| N9-docs | MEDIUM | Docs stale sau Auth migration: `ERROR_CODES.md` (7 code KEYCLOAK_* chết + thiếu ~10 code auth mới), `ARCHITECTURE.md`, `API.md` (0 endpoint `/auth/*`), `HANDOFF_LATEST.md` (dừng 2026-08-28) | `DEPLOYMENT.md` + artifacts đã xử lý ở đợt 1 |
| N10 | MEDIUM | `UpdateGroupPermissionsCommand` `request.Permissions` không null-guard → NRE 500 thay vì 400 | |
| N11 | LOW | Refresh call FE không timeout (`auth.ts:105`) → `isRefreshing` kẹt nếu server treo | |
| N12 | LOW | `dayjs` phantom dependency (14 file import, không có trong `package.json`) | |
| N13 | LOW | `CreateCampaignCommand.cs:163` dùng `NpgsqlParameter` cho `FromSqlRaw` — ngoài ghi chú ngoại lệ "Npgsql chỉ cho exception-types" | Không phá dependency direction (qua EF DbSet) |
| N14 | LOW | `ChangePasswordCommand` comment nói "revoke OTHER sessions" nhưng code revoke TẤT CẢ | Chốt lại comment hoặc hành vi |
| N15 | LOW | Comment stale: `LocationsController.cs:54` "TODO SECURITY BUG-G" (đã fix ở handler), `AuthController.cs:14` "Dual-auth legacy Keycloak", `UsersController.cs:24` lý do Keycloak | |
| N16 | LOW | `SuperuserClaims.cs` ở `Infrastructure/Services/` nhưng CLAUDE.md ghi `Authentication/` | Doc drift |
| N17 | LOW | Build warnings: 12× CS8xxx nullable, CS0618 `HasCheckConstraint` obsolete, NU1903 `Microsoft.OpenApi` CVE | |
| N18 | LOW | Vite chunk >500kB — code-splitting (backlog T8 cũ) vẫn mở | |
| N19 | LOW | `RefreshTokenCommandHandler` không `FOR UPDATE` (cửa sổ race lý thuyết; 6-way live không reproduce) | Optional hardening |
| N20 | LOW | `Program.cs` 103 dòng (docs ghi ~93) | |

### 📌 Ghi nhận lịch sử (minh bạch)

- **Báo cáo tổng kết MediatR trước đây SAI về Accessories/SystemConfig.** Tài liệu/handoff các phiên
  trước mô tả chiến dịch "24 controller đã migrate sang MediatR" như đã hoàn tất, nhưng audit
  2026-10-02 xác nhận: `AccessoriesController` vẫn chạy EF trực tiếp ở List/GetById/Update (và
  **Update không ghi ActionLog** = N2), `SystemConfigController` chưa hề có `IMediator`. Đợt 1 đã
  migrate 3 action của Accessories; **`GetCheckouts` (accessories) + toàn bộ `SystemConfigController`
  vẫn còn EF trực tiếp** → coi là phần còn lại của chiến dịch CQRS, KHÔNG phải "đã xong".
- **BUG-M (Users log 2 lần) vẫn OPEN**, không nằm trong đợt 1.
- File test cũ còn **mojibake sẵn** (do sửa qua PowerShell từ trước — đúng lỗi mà AGENTS.md cảnh báo):
  `TaskM2PatchSafetyTests.cs`, `TaskL2CreateCompanyScopeTests.cs`. Đợt 1 không sửa (ngoài phạm vi) —
  nên có một lượt dọn mojibake riêng.
- **`AUTH_SIGNING_KEY` rotation**: đổi khóa = vô hiệu mọi access token đã cấp (refresh token vẫn dùng
  được vì lưu dạng hash). Ghi vào `docs/DEPLOYMENT.md` §4.1/§7 đợt 1.
- **CI đã đỏ SUỐT từ trước đợt 1 — nguyên nhân thật KHÔNG phải Dockerfile (đã sửa xong):** gate
  `Format check` (`dotnet format --verify-no-changes`) fail vì (a) 2 file test còn wrap dòng sai chuẩn
  (`ActionLogNameResolutionTests.cs`, `MaintenanceCampaignTests.cs`) + (b) 1 phát hiện analyzer
  `xUnit2013` (`AssetMaintenanceTests.cs:329` — `Assert.Equal(1, x.Count)` → `Assert.Single`), cộng
  thêm tình trạng **line-ending trộn LF/CRLF** trong repo (không có `.editorconfig`). Vì gate backend
  fail, job `Docker — Build Images` (`needs: [backend, frontend]`) **bị skip** ở MỌI run → lỗi
  Dockerfile (thiếu 3 project sau tách 4-layer) chưa bao giờ được CI phát hiện. Đã sửa cả 2 nhóm:
  commit `c5c97b5` → **CI xanh cả 3 job lần đầu tiên** (run 37017686138). Ghi nhận: ghi chú cũ trong
  `ci.yml` ("CI-2: the whole solution was formatted ... passes clean locally") **không còn đúng** tại
  thời điểm đó. Khuyến nghị cho đợt sau: thêm `.editorconfig` (`end_of_line`, `indent_size`,
  `insert_final_newline`) + chuẩn hoá line endings để tránh tái diễn.

---

## BUG-E — DepartmentsController.UpdateDepartment: full-PUT, field không gửi bị clear (vi phạm patch-safety)

- **Trạng thái: RESOLVED 2026-09-05** (patch-safety fix — behavior change theo sketch đã dự kiến).
  Đổi `UpdateDepartmentCommand`/`UpdateDepartmentRequest` sang nullable-only + handler gán theo
  `is not null` (Task M1/M2 pattern): field không gửi → GIỮ NGUYÊN (không còn bị clear). Name chỉ
  bắt buộc KHI GỬI (blank gửi → 400 như cũ); dup-check chỉ chạy khi name thực sự thay đổi (đồng bộ
  rule Create). LogMeta old→new tự phản ánh đúng field thực sự đổi (unchanged → old==new).
  Tests: `DepartmentPatchSafetyTests` ×4 — bug-repro (PUT chỉ {name} → CompanyId/Phone/Fax GIỮ
  nguyên), full-payload vẫn update đủ (positive, frontend full-form không vỡ), blank-name 400,
  dup-name 400. Full suite 404/404.
- **Phát hiện lúc audit (2026-09-01, Giai đoạn 1 pilot):** patch-safety (cùng lớp lỗi Task M1/M2
  đã fix cho 11 entity khác — xem workflow §Patch semantics). Lưu ý verify cũ: parity old==new đã
  được xác nhận (full-PUT cả 2 phía) — bug KHÔNG được tạo ra bởi migration; frontend Department
  form gửi full payload nên chưa trigger.
- **Vị trí:** `aspire-react.Application/Departments/Commands/UpdateDepartmentCommand.cs` (hành vi di chuyển
  verbatim từ `DepartmentsController.Update` — bug CÓ TỪ TRƯỚC, KHÔNG phải do migrate tạo ra;
  dẫn chứng: HEAD `e060ffa` — `[FromBody] Department updated` + gán vô điều kiện
  `d.Name = updated.Name; d.CompanyId = updated.CompanyId; d.ManagerId = updated.ManagerId;
  d.Phone = updated.Phone; d.Fax = updated.Fax;`)
- **Hành vi hiện tại:** PUT /api/v1/departments/{id} — mọi field không có trong payload bị set
  null/clear (VD: PUT chỉ `{name}` → CompanyId=null (trở thành floater), ManagerId/Phone/Fax mất).
  Update gán Name + CompanyId vô điều kiện; chỉ nhận diện được 2 field bắt buộc (Name) — các field
  còn lại không có cơ chế "absent ≠ changed".
- **Impact:** client gửi payload thiếu field sẽ âm thầm mất dữ liệu department (đúng dạng bug
  "wiped real data" từng xảy ra với Serial/AssetTag — workflow doc Patch semantics).
- **Fix sketch (khi thực hiện):** đổi `UpdateDepartmentRequest` sang nullable-only fields; handler
  gán theo `is not null` (Task M2 pattern); giữ nguyên LogMeta changes (old/new sẽ phản ánh đúng
  chỉ field thực sự đổi); thêm/điều chỉnh test patch-safety (PUT 1 field → còn lại giữ nguyên —
  đổi kỳ vọng từ "bị clear" sang "giữ nguyên" LÀ THAY ĐỔI HÀNH VI — cần duyệt riêng lúc fix).
- **Lưu ý verify:** ở Giai đoạn 1, parity old==new đã được xác nhận (full-PUT cả 2 phía) — bug này
  KHÔNG được tạo ra bởi migration; frontend Department form hiện gửi full payload nên chưa trigger.

---

## BUG-F — Category Update không kiểm tra trùng Name+CategoryType như Create (cho phép rename thành tên trùng lặp)

- **Trạng thái: RESOLVED 2026-09-05** (behavior change theo sketch: rename trùng từ 2xx → 400).
  Dup-check `Name+CategoryType` thêm vào `UpdateCategoryCommandHandler` — chỉ khi name THAY ĐỔI
  THẬT (re-send current name vẫn no-op), exclude self, message đồng bộ Create ("Tên danh mục đã
  tồn tại." — 400 không error_code). **Không hồi tố**: dup tạo trước fix vẫn tồn tại (audit dữ liệu
  thật trước fix: 25 categories có duy nhất 1 cặp trùng = fixture parity G2-era
  "G2-baseline-DUP-20260901091044", đã cleanup cả 2 — 0 references; dev data sạch dup). Tests:
  `CategoryPatchSafetyTests` ×4 — bug-repro (rename trùng bị chặn + bản gốc giữ nguyên), rename
  tên tự do ✔, same-name khác-type vẫn cho phép (rule per-type như Create), re-send current name
  không bị chặn. Full suite 408/408.
- **Phát hiện lúc audit (2026-09-01, Giai đoạn 2, parity verification trên binary cũ):**
  business-rule inconsistency (Create có dup-check, Update không). Backend không có code path nào
  tra cứu Category theo Name — ComponentsController/LicensesController đều lookup theo Id; rủi ro
  chính = UI hiển thị 2 danh mục trùng tên trong cùng loại (user confusion).
- **Vị trí:** hành vi CÓ TỪ TRƯỚC trong `AdminController.UpdateCategory` (HEAD `425be5c` trước
  migrate — không có `AnyAsync(x => x.Name == updated.Name ...)`); hành vi được di chuyển verbatim
  vào `aspire-react.Application/Categories/Commands/UpdateCategoryCommand.cs` (đúng nguyên tắc
  parity — KHÔNG phải do migrate tạo ra).
- **Hành vi hiện tại:** `POST /categories` chặn trùng cặp Name+CategoryType (400 "Tên danh mục đã
  tồn tại."); `PUT /categories/{id}` CHO PHÉP rename thành một Name+CategoryType đã tồn tại
  (2xx, verify thực tế: baseline binary cũ trả 2xx khi rename trùng).
- **Impact assessment (quick, 2026-09-01):** backend không có code path nào tra cứu Category theo
  Name — ComponentsController/LicensesController đều lookup theo Id; **rủi ro chính = UI hiển thị
  2 danh mục trùng tên trong cùng loại (user confusion)**. Frontend có nơi nào select Category
  theo name (thay vì id) hay không: **cần điều tra thêm** (chưa điều tra sâu).
- **Fix sketch (khi thực hiện):** thêm dup-check `Name+CategoryType` vào
  `UpdateCategoryCommandHandler` (đối chiếu `x.Id != request.Id`), message/error_code đồng bộ với
  Create; là THAY ĐỔI HÀNH VI (rename trùng từ 2xx → 400) — cần duyệt riêng lúc fix.
- **Lưu ý verify:** parity old==new đã xác nhận ở Giai đoạn 2 (cả 2 phía cho phép rename trùng).

---

## BUG-H — AssetModel Create/Update thiếu toàn bộ validation + FK không kiểm tra tồn tại + client tự set Id (MEDIUM)

- **Trạng thái: RESOLVED 2026-09-05** (behavior change theo sketch). Implement qua
  `ModelValidation` shared helper dùng chung Create/Update:
  (1) empty-name — "Tên model không được để trống." (400, Update chỉ khi name ĐƯỢC GỬI và blank);
  (2) dup-name — "Tên model đã tồn tại." (Create exact; Update chỉ khi name thực sự thay đổi,
  exclude self; re-send current name = no-op);
  (3) FK-existence ManufacturerId/CategoryId/DepreciationId/FieldsetId — GUID không tồn tại →
  **400 `RESOURCE_NOT_FOUND`** + message "Trường tham chiếu không tồn tại: {field}." (trước đây
  raw FK-violation 500 tại SaveChanges) — check trước mọi mutation.
  Thành phần #1 (client-set-Id) đã loại từ lúc migrate (DTO không có field Id). Audit dữ liệu thật
  trước fix: 14 models, 0 empty-name, 0 dup → không hồi tố vấn đề. Tests: `ModelValidationTests`
  ×7 phủ cả negative/positive/patch-safe. Full suite 415/415.
- **Phát hiện lúc audit (2026-09-01, Giai đoạn 2):** MEDIUM (nghiệp vụ — reference data, KHÔNG có
  company-isolation risk như BUG-G). Vị trí gốc: `AdminController.CreateModel` / `UpdateModel`;
  di chuyển verbatim vào `aspire-react.Application/Models/Commands/` (parity trước, fix riêng).
- **Các thành phần riêng biệt của bug (ghi tách bạch để đánh giá lại khi fix):**
  1. **Client có thể tự set Id qua entity binding** — `CreateModel([FromBody] AssetModel m)` cho
     phép JSON chứa `"id": "<guid>"` → tạo model với PK do client chọn → trùng PK đã tồn tại =
     **PK violation → 500** (cùng bug-class với BUG-C/D: race/constraint chưa được xử lý sạch).
     ⚠️ Khác bản chất với "thiếu dup-check" (BUG-F): đây là lỗi chấp nhận PK từ client.
     *Cập nhật sau migrate (Giai đoạn 2): DTO hóa `CreateModelRequest` (không có field Id) đã TỰ
     LOẠI BỎ quirk này ở endpoint API — client gửi id sẽ bị bỏ qua (server tự sinh). Phần còn lại
     của BUG-H dưới đây vẫn OPEN.*
  2. **Không có bất kỳ validation nào**: không empty-name check, không dup-check Name (Create lẫn
     Update đều không có — khác Manufacturer/Supplier có dup cả 2 chiều), cho phép tạo vô số model
     trùng tên.
  3. **4 field FK không kiểm tra tồn tại**: ManufacturerId / CategoryId / DepreciationId /
     FieldsetId nhận GUID tùy ý từ client — GUID sai → FK violation → **500** tại SaveChanges
     (thay vì 400 sạch).
- **Impact:** dữ liệu models bẩn (name rỗng/trùng), 500 thay vì 400 cho input sai, trải nghiệm
  người dùng + tính nhất quán dữ liệu tham chiếu.
- **Fix sketch (khi thực hiện — THAY ĐỔI HÀNH VI, cần duyệt riêng):** FluentValidation hoặc
  soft-fail cho empty-name + dup-check Name; kiểm tra tồn tại 4 FK trước khi save (400
  RESOURCE_NOT_FOUND); quyết định message/error_code đồng bộ phong cách section.

---

## BUG-I — CustomFields Create/Update: thiếu dup-Slug ở Update (→ 500 DB index) + FULL-PUT ×8 + không empty-Name check (MEDIUM)

- **Trạng thái: RESOLVED 2026-09-05** (cả 3 thành phần, behavior change theo sketch — thành phần
  (b) dup-Slug confirmed-500 ưu tiên trước đúng kế hoạch):
  (a) **FULL-PUT ×8 → patch-safe** (Task M1/M2): `UpdateCustomFieldCommand` + request DTO nullable
  toàn bộ, gán theo `is not null` — field không gửi GIỮ NGUYÊN (Name=null trước đây → DB NOT NULL
  violation → 500).
  (b) **Dup-Slug check thêm vào Update** — chỉ khi slug thực sự thay đổi, exclude self, message
  đồng bộ Create ("A field with this slug already exists." — 400 không error_code); thay raw 500
  đã CONFIRMED. Re-send current slug = no-op.
  (c) **Empty-Name/Slug check** cả Create lẫn Update (blank khi gửi → 400 "Field name is
  required." / "Field slug is required.").
  Audit dữ liệu thật trước fix: 0 custom fields trong dev DB → không hồi tố. **Frontend impact:
  0** — scan toàn frontend: KHÔNG có UI nào gọi /custom-fields CRUD (chỉ /custom-fieldsets GET ở
  trang khác). Tests: `CustomFieldValidationTests` ×5 (bug-repro dup-slug-500 + patch-safe +
  guards + positive). Full suite 420/420.
- **Phát hiện lúc audit (2026-09-01/02, Giai đoạn 3):** MEDIUM (CustomField không có CompanyId,
  không isolation risk như BUG-G). Vị trí gốc: `CustomFieldsController.Update/Create`; di chuyển
  verbatim vào `aspire-react.Application/CustomFields/Commands/` (parity trước, fix riêng).
- **3 thành phần — độ tin cậy KHÁC NHAU (ghi rõ để đánh giá lại khi fix):**
  1. **Update FULL-PUT ×8** — tất cả field gán vô điều kiện; payload thiếu field → bị clear/null
     (Name=null → DB NOT NULL violation → 500). *Độ tin cậy: suy luận từ đọc code (chưa reproduce
     chủ động — payload realistic luôn gửi đủ field nên chưa trigger).*
  2. **Update KHÔNG có dup-Slug check** (Create có) → rename sang slug đã tồn tại → **DB unique
     index violation → raw 500 body rỗng**. ✅ **CONFIRMED VIA REPRODUCTION** — xảy ra chắc chắn
     với BẤT KỲ user nào rename field trùng slug, không cần điều kiện đặc biệt (khác BUG-D cần
     concurrency); reproduce bằng parity script trên binary cũ (baseline step-5 → 500 body rỗng,
     2 lần độc lập).
  3. **Create/Update không empty-Name/Slug check** — name/slug rỗng tạo được. *Độ tin cậy: suy
     luận từ đọc code (chưa chủ động reproduce).*
- **Vị trí gốc:** `CustomFieldsController.Update/Create` (HEAD trước migrate); di chuyển verbatim
  vào `aspire-react.Application/CustomFields/Commands/UpdateCustomFieldCommand.cs` +
  `CreateCustomFieldCommand.cs` kèm comment `// TODO BUG-I` in-code.
- **Impact:** user rename field trùng slug nhận 500 thay vì 400 sạch; payload thiếu field âm thầm
  mất dữ liệu; slug trùng phá tính duy nhất mà Create đã cam kết.
- **Fix sketch (khi thực hiện — THAY ĐỔI HÀNH VI, cần duyệt riêng):** thêm dup-Slug check vào
  Update (đối chiếu `x.Id != request.Id` → 400 "A field with this slug already exists." — thay 500);
  empty-Name/Slug soft-fail; FULL-PUT → nullable patch hoặc giữ nguyên tùy quyết định (BUG-E
  precedent). Ưu tiên #2 trước (confirmed, user-facing 500).

---

## BUG-J — Dashboard monthly-checkout-trend: 500 cho MỌI superuser (visibleAssetIds null → Contains trong EF expression) (MEDIUM)

- **Trạng thái: RESOLVED 2026-09-05** (behavior change 500→200; **root cause ĐÃ RE-DIAGNOSE
  đúng lúc fix** — ghi nhận minh bạch):
  - **Chẩn đoán ban đầu (sai một phần):** visibleAssetIds null → `Contains()` throw → 500 chỉ
    superuser. **Sai lệch:** InMemory unit test pass cả 2 phiên bản → không phát hiện được; live
    test sau branch-fix (superuser branch, không Contains) VẪN 500 → chứng tỏ nguyên nhân khác.
  - **Root cause thật (verified live):** inline projection `$"{g.Key.Year}-{g.Key.Month:D2}"`
    KHÔNG translate được bởi Npgsql (format specifier `:D2` trong composite format) → EF throw
    lúc translate → 500 cho MỌI caller (cả superuser lẫn regular; baseline chỉ test superuser
    nên ghi nhầm "chỉ superuser"). Fix: GroupBy (Year, Month) + count aggregate trong SQL, build
    month-string client-side (D2 in memory). Regular-user path giữ contains-filter; superuser
    branch không filter (giữ hành vi "sees all" đúng thiết kế).
  - **Verified:** Release binary + live Postgres → 200 + data chuẩn (month "2026-08"/"2026-09",
    counts khớp ActionLogs thật). Tests: `DashboardTests` +2 (superuser 200 với data, regular
    user scope không leak company B) — InMemory pass vì client-eval projection (bài học: bug này
    KHÔNG test được bằng InMemory, phải live-verify). Full suite 428/428.
- **Phát hiện lúc audit (2026-09-03):** MEDIUM (API defect confirmed-reproduce NHƯNG zero
  frontend impact — DashboardPage không gọi endpoint này). Lưu ý process-lesson: `dotnet run`
  mặc định Debug — các lần verify trước đây dùng `--no-build` sau khi build Release có thể đã
  chạy Debug binary cũ; lần này dùng `--configuration Release` tường minh.
- **Mức độ: MEDIUM** (API defect thật, confirmed reproduce — NHƯNG **zero frontend impact**: grep
  toàn frontend, endpoint monthly-checkout-trend KHÔNG được gọi — DashboardPage chỉ gọi 5 endpoint
  còn lại; khác BUG-K user-facing thật).
- **Hành vi:** GET /api/v1/dashboard/monthly-checkout-trend với superuser → visibleAssetIds null
  (superuser không bị filter company) → `Contains()` trên collection null trong EF expression →
  ArgumentNullException lúc translate → 500. Regular user KHÔNG bị (visibleAssetIds có giá trị).
- **Fix sketch (THAY ĐỔI HÀNH VI, cần duyệt riêng):** guard visibleAssetIds null → skip Contains
  filter (200 + full data) — quyết định cùng đợt dọn patch-safety/bug sau migration.

---

## BUG-G — Location.Create KHÔNG có company-scoping và không có validation nào (SECURITY/HIGH)

- **Trạng thái: RESOLVED 2026-09-05** (SECURITY/HIGH — fix hành vi thật, thiết kế duyệt riêng
  trước khi code theo quy trình behavior-change). Re-audit dữ liệu thật trước fix: 8 locations
  (1 thật "QA AUD Loc" do superuser tạo + 7 PRT fixture floater) — **0 cross-company sign** → fix
  không ảnh hưởng dữ liệu hiện có.
  - **Thay đổi hành vi (thiết kế Bước 2 đã duyệt):** `CreateLocationCommandHandler` thêm check
    ĐẦU TIÊN theo đúng pattern Task L2 (`CreateDepartmentCommand`): regular user chỉ tạo được
    Location cho company của chính họ (hoặc floater CompanyId=null); superuser bỏ qua; mismatch →
    **400 + `error_code: COMPANY_MISMATCH`** + message "Bạn chỉ được tạo địa điểm cho công ty của
    mình." Blocked request không tạo row và không tạo ActionLog. Positive path verified trên
    binary thật (admin tạo location company + floater 200/200, không regression).
  - **Phạm vi fix — CHỈ company-scoping** (tránh gộp): empty-name check + dup-name check vẫn còn
    thiếu → thuộc nhóm patch-safety dọn dẹp sau. **Limitation đã biết, chấp nhận (nhất quán với
    Department — KHÔNG phải thiếu sót riêng của Location):** không có company-existence check
    (CompanyId không tồn tại vẫn pass — chỉ data-hygiene, không security-risk).
  - **Tests:** `LocationCommandTests` ×4 (FakeScope, unit-test là đủ theo duyệt — không tạo user
    Keycloak thật): negative A→B = COMPANY_MISMATCH + 0 row + 0 log; positive A→A ✔; floater ✔;
    superuser → company B ✔. Full suite 400/400 (396 + 4 mới).
- **Phát hiện lúc audit (2026-09-01, Giai đoạn 2):** Mức độ SECURITY/HIGH (khác MEDIUM của
  BUG-E/F): regular user (không phải superuser) có thể
  tạo Location cho **company BẤT KỲ** (cross-company creation) — vi phạm trực tiếp company-isolation
  (nguyên tắc cứng nhất của dự án, convention Task L2: "Create out-of-scope → 400 COMPANY_MISMATCH");
  ngoài ra không có empty-name/dup-name check (location name rỗng cũng tạo được).
- **Vị trí:** hành vi CÓ TỪ TRƯỚC trong `AdminController.CreateLocation` (bind cả entity
  `Location l` + `Add + Save` ngay, không check gì); hành vi di chuyển verbatim vào
  `aspire-react.Application/Locations/Commands/CreateLocationCommand.cs` (đúng nguyên tắc parity —
  KHÔNG phải do migrate tạo ra) kèm comment `// TODO SECURITY BUG-G` ngay trong handler.
- **Scoping hiện tại của section:** GetAll/Update/Delete CÓ scope (filtered/404); CHỈ Create
  thiếu hoàn toàn (3/4 path đã đúng, 1 path sai). GetById MỚI áp dụng scoped-404 theo quyết định
  đã duyệt (không lấy Create sai làm chuẩn).
- **Impact assessment (2026-09-01, read-only SQL):** tổng 1 location trong DB (QA fixture
  "QA AUD Loc"), 0 user/asset tham chiếu, **0 cross-company sign** → chưa có dữ liệu thật bị ảnh
  hưởng → giữ ở backlog chờ; nếu sau này phát hiện dữ liệu thật bị tạo sai company → chuyển xử lý
  ưu tiên riêng.
- **Fix sketch (khi thực hiện — là THAY ĐỔI HÀNH VI, cần duyệt riêng):** thêm
  `ICompanyScopeService.GetCurrentUserCompanyIdAsync()` check vào `CreateLocationCommandHandler`
  (mismatch → 400 COMPANY_MISMATCH, message đồng bộ Department/Asset pattern), quyết định có thêm
  empty-name check hay không.

---

## INCIDENT-1 (LOW, RESOLVED): Xóa nhầm company MIRAT (dev-seed) trong lúc audit Companies guard do dùng entity có sẵn làm fixture thay vì tạo mới

- **Trạng thái:** RESOLVED — xảy ra 2026-09-03 trong Giai đoạn 3 (audit Companies trước khi migrate),
  recovery hoàn tất cùng phiên; không phải BUG (không phải lỗi code — guard 10-blockers chạy ĐÚNG,
  lỗi là quy trình testing của agent)
- **Chuỗi sự kiện:** baseline run-1 dùng company MIRAT có sẵn làm fixture cho DELETE guard với giả
  định "MIRAT có users/assets" KHÔNG kiểm chứng (mọi user seed đều company-less; assets thuộc
  company QCR/QA khác) → guard 10-blockers verified 0 references (đúng logic) → cho qua → DELETE
  thành công thật. Phát hiện ở run-2 (POST dup-code với code=MIRAT → 2xx bất thường; guard → 405
  do id rỗng).
- **Recovery:** recreate qua API ngay trong phiên — ID mới `aefe9209-1890-449d-b4a2-1db18df6f033`
  (name "Công ty Quản lý bay miền Trung" UTF-8 chuẩn, code MIRAT, root) — 0 FK references tại thời
  điểm xóa (chính là lý do guard cho qua) → không orphan dữ liệu nghiệp vụ nào.
- **Hệ quả còn lại:** 14 ActionLog rows mang CompanyId/ItemId cũ (`088d28b7-8268-44f0-892b-c111ea53a9bd`)
  không resolve được — 12 rows = 6 cặp Create+Delete log của component fixture đã xóa từ 2026-09-01
  (history đã đóng) + 2 rows = Create (2026-08-28, seed-era) và Delete log của chính MIRAT cũ.
  **Giữ nguyên, KHÔNG sửa** (audit log là append-only history — sửa log = vi phạm tính toàn vẹn
  audit). Xác minh bằng dynamic scan toàn bộ cột `CompanyId` trong DB qua `information_schema`:
  12 references, 0 ở bảng nào khác.
- **Phạm vi:** dev-only (Aspire local stack, seed data) — không ảnh hưởng production/demo.
- **Quy trình sửa từ đây về sau (áp dụng ngay, xem playbook §8):** fixture test guard BẮT BUỘC
  tạo mới hoàn toàn qua API trong chính lượt test (create → link reference → test guard → cleanup
  ngược thứ tự) — TUYỆT ĐỐI không dùng entity có sẵn trong DB (dù trông giống fixture, dù tên
  nghe như QA/test) làm đối tượng test guard.

---

## BUG-K — Groups Create/Update: không có dup-Name check, không empty-Name check (MEDIUM)

- **Trạng thái: RESOLVED 2026-09-05** (behavior change theo sketch; case-insensitivity đã quyết
  tại thời điểm fix). Audit dữ liệu thật trước fix: 2 groups (Admin, Superuser — đều IsSystem,
  seed) → 0 dup, 0 empty → không hồi tố. Fix:
  1. **Create dup-Name check (CONFIRMED bug)** — case-INSENSITIVE ("Admin"/"admin" = trùng —
     group name là role-like identifier), exclude-none, 400 "A group with this name already
     exists." (không errorCode — soft-fail style section);
  2. **Create empty-Name check** — 400 "Group name is required.";
  3. **Update dup-Name on rename** — chỉ khi name THAY ĐỔI (re-send current name = no-op), case-
     insensitive, exclude self; empty-name cũng chặn; GUARD ORDER: SYSTEM_GROUP_LOCKED vẫn check
     TRƯỚC validation (đúng hàng rào cũ).
  Frontend impact: 0 vỡ — `GroupFormModal` trims name trước khi gửi + hiển thị `message` từ error
  response → dup/empty message hiện đúng chỗ. (Convention `errorCode` camelCase + `Permissions[]
  .Value` int GIỮ NGUYÊN verbatim — ghi nhận riêng, không thuộc BUG-K.) Tests: `GroupValidation
  Tests` ×5. Full suite 426/426.
- **Phát hiện lúc audit (2026-09-03, Giai đoạn 3):** MEDIUM user-facing — GroupListPage quản trị
  group là UI thật (khác BUG-J zero-impact; không phải SECURITY — PermissionGroup không có
  CompanyId, không isolation risk). Vị trí gốc: `GroupsController.CreateGroup/UpdateGroup`;
  di chuyển verbatim vào `aspire-react.Application/Groups/Commands/`.
- **Các thành phần — độ tin cậy:**
  1. **Create không dup-Name check** ✅ **CONFIRMED VIA REPRODUCTION** — POST 2 group cùng tên →
     cả 2 đều 201 (baseline bắt được trên binary cũ; post parity 2xx). Hệ quả: danh sách group trùng
     tên, phân quyền theo tên gây hiểu nhầm.
  2. **Create không empty-Name check** — *suy luận từ đọc code (Create bind Name trực tiếp, không
     check gì — cùng code path với #1 nên xác suất rất cao)*; nhóm tên rỗng tạo được.
  3. **Update rename không dup-Name check** — *suy luận từ đọc code* (Update chỉ check
     SYSTEM_GROUP_LOCKED, không check trùng tên nhóm khác).
- **Vị trí gốc:** `GroupsController.CreateGroup/UpdateGroup` (HEAD trước migrate); di chuyển verbatim
  vào `aspire-react.Application/Groups/Commands/CreateGroupCommand.cs` + `UpdateGroupCommand.cs`.
- **Fix sketch (khi thực hiện — THAY ĐỔI HÀNH VI, cần duyệt riêng):** dup-Name check
  (case-insensitive? quyết định khi fix) → 400 "A group with this name already exists."; empty-Name
  soft-fail. Ưu tiên #1 (confirmed, user-facing).
- **Convention inconsistency (ghi nhận kèm — LOW priority, KHÔNG fix trong migration):** controller
  này dùng `errorCode` (camelCase) trong error bodies thay vì `error_code` (snake_case) như mọi
  controller khác; `GET /groups` trả `Permissions[].Value` là số int thay vì string enum. Cả 2 giữ
  nguyên verbatim vì parity — nếu sau này thống nhất convention phải sửa đồng bộ frontend.

---

## BUG-L — Reports checkout-history: date filters → raw 500 (DateTime Kind=Unspecified vs timestamptz) (MEDIUM)

- **Trạng thái: RESOLVED 2026-09-05** (behavior change 500→200 theo sketch). Fix đúng DateTime
  Kind convention: 2 filter params (bind Kind=Unspecified) được `DateTime.SpecifyKind(value,
  DateTimeKind.Utc)` trước khi so sánh với `timestamptz` ActionDate (reinterpreted AS UTC — khớp
  contract vì ActionDate luôn ghi bằng UtcNow). Verify: **live Postgres + Release binary →
  filtered 200 + unfiltered 200** (trước fix filtered = 500 confirmed). Zero frontend impact giữ
  nguyên (không caller); fix chỉ mở đường cho caller tương lai. Tests: `CheckoutHistoryReport
  Tests` ×2 pin filter-logic (InMemory không reproduce được Npgsql Kind-throw — live-verify là
  bằng chứng chính, ghi rõ). Full suite 430/430.
- **Phát hiện lúc audit (2026-09-03, Giai đoạn 3):** MEDIUM — user-facing 500 khi có filter,
  NHƯNG zero frontend impact (ReportsPage chỉ gọi depreciation + audit) → latent API defect.
  Điều kiện kích hoạt: BẤT KỲ caller nào truyền startDate/endDate (policy reports.view).
- **Điều kiện kích hoạt:** BẤT KỲ caller nào truyền startDate hoặc endDate (chỉ có superuser/
  admin token gọi được — policy reports.view; không user frontend nào bị ảnh hưởng).
- **Fix sketch:** SpecifyKind UTC cho 2 filter params; nếu build UI filter cho report này sau
  này thì PHẢI fix BUG-L trước (ghi dependency).

---

## INFRA-1 — Điều tra hạ tầng dev: Docker Desktop/WSL2 mất engine ×3 + file-loss/revert anomalies
- **Trạng thái:** OPEN — điều tra lần 1 hoàn tất 2026-09-02 (kết quả dưới); reopen khi tái diễn
- **Triệu chứng đã xảy ra (3 lần Docker + 2 lần file):**
  1-3. Docker Desktop engine/daemon mất đột ngột (containers Exited 255, npipe biến mất,
  `docker exec` connection reset) — lần 3 trong phiên Giai đoạn 2; volumes + dữ liệu NGUYÊN VẸN
  cả 3 lần (Aspire volumes persistent); engine lên lại <1 phút sau khi start Docker Desktop.
  a. 31 file (docs + PNG) mất khỏi working tree giữa phiên Giai đoạn 1 (git restore khôi phục
  100% từ index) — KHÔNG có thao tác git nào giải thích được.
  b. AdminController revert về HEAD giữa phiên Models (các edit Models-turn biến mất) — nguyên
  nhân không xác định, phát hiện qua state-audit; viết lại deterministic + verify.
- **Điều tra lần 1 (2026-09-02) — kết quả:**
  - Docker Desktop 29.6.2, backend = WSL2 distro `docker-desktop` (v2); AutoStart=False;
    Windows Event Logs (Application/System, 7 ngày) KHÔNG có bất kỳ error nào của
    Docker/WSL/vmcompute → daemon chết KHÔNG để lại trace ở mức Windows Event (đặc trưng
    WSL2 VM abort âm thầm).
  - Docker host logs (`%LOCALAPPDATA%\Docker\log\host\`) có init.log/monitor.log rotate dày
    (~3-5 phút/lần) → backend churn; cần đọc sâu trong điều tra lần 2 nếu tái diễn.
  - git reflog 25 entry: 100% là commit/amend/reset có chủ đích của agent → mất file/revert
    KHÔNG phải do git; khả năng cao tác nhân ngoài (process khác trên máy, sync/AV, sự cố
    NTFS) — chưa có smoking gun.
- **Đánh giá:** 3 lần Docker = CÙNG LỚP sự cố hệ thống (WSL2/Docker Desktop instability) — không
  phải ngẫu nhiên rời rạc; 2 vụ file anomaly chưa rõ nguyên nhân (không cùng cơ chế với Docker —
  file nằm trên NTFS working tree, không trong WSL).
- **Khuyến nghị:** (1) `wsl --update` + cập nhật Docker Desktop; (2) cân nhắc bật AutoStart và
  rà `.wslconfig` (memory/CPU); (3) giữ quy trình `git status` audit sau mỗi batch (đã áp dụng);
  (4) nếu tái diễn lần 4+ → đọc sâu Docker host logs + Windows Reliability Monitor + cân nhắc
  reinstall WSL2.
- **Tái diễn lần 4 (2026-09-04, subtask B post-verify):** engine mất đột ngột giữa phiên —
  `docker version` chỉ còn Client (không Server section), `docker ps` rỗng, AppHost + API chết
  theo; volumes/dữ liệu nguyên vẹn; `Docker Desktop.exe` start lại → engine lên trong ~1 phút
  (đúng pattern 3 lần trước). Docker Desktop vẫn 29.6.2 / 4.84.0 — khuyến nghị (1)(2) vẫn chưa
  thực hiện. Lần 4 cùng lớp WSL2-instability, KHÔNG kèm file anomaly (xác nhận tách bạch với
  INFRA-2: tree sạch sau restart).
- **Tái diễn lần 5 (2026-10-02, phiên fix đợt 1 — giữa lúc chạy live test audit):** engine mất đột
  ngột (`docker ps` → `failed to connect to the docker API at npipe:////./pipe/dockerDesktopLinuxEngine`),
  AppHost (job `dotnet run`) chết theo với exit code 1; `Docker Desktop.exe` start lại → engine lên
  trong ~5 giây (nhanh nhất trong 5 lần); **volume dev `postgres-data` nguyên vẹn** (fixture QA và
  dữ liệu PRT còn đủ sau restart) → cùng lớp WSL2-instability, KHÔNG kèm file anomaly.
  Bài học vận hành bổ sung: sau khi engine chết, process `aspire-react.AppHost` CŨ vẫn giữ port
  dashboard (VD 22073) → lần `dotnet run` kế tiếp fail `Failed to bind to address ... address already
  in use`; phải diệt theo **port** (không kill mù toàn bộ dotnet) rồi mới start lại.

---

## BUG-M — Users Create/Update/Delete: log 2 lần, log thứ 2 không atomic với data (LOW)

- **Trạng thái:** OPEN — phát hiện 2026-09-03 trong Giai đoạn 3 (audit Users trước khi migrate
  4 action inline; 3 action write giữ nguyên theo ranh giới đã duyệt)
- **Mức độ: LOW** (xác suất trigger thấp — chỉ khi `SaveChanges` của log thứ 2 fail sau khi
  command đã commit; không có isolation risk như BUG-G, không user-facing 500 như BUG-L)
- **Vị trí:** hành vi CÓ TỪ M1 trong `UsersController.CreateUser/UpdateUser/DeleteUser`
  (`aspire-react.Server/Web/Controllers/UsersController.cs` — handler M1 đã `LogAction +
  SaveChanges` 1 lần trong command, controller sau `_mediator.Send` lại `LogAction +
  SaveChanges` lần 2 với note khác: Create `"Tạo người dùng ..."` vs `"Created user: ..."`,
  Update `"Cập nhật người dùng ..."` vs `"Updated user: ..."`, Delete `"Vô hiệu hóa ..."`
  vs `"Deactivated user: ..."`). Migrate verbatim theo nguyên tắc parity — KHÔNG fix trong
  task Users (ranh giới đã duyệt: chỉ migrate 4 action inline, giữ nguyên 3 Command M1).
- **Vi phạm nguyên tắc:** "ActionLog phải atomic với data" (workflow §3.2 — log persist cùng
  transaction với thay đổi, rollback cùng nhau). Ở đây data đã commit trong handler trước khi
  log thứ 2 của controller được stage/persist ở transaction riêng → nếu `SaveChanges` thứ 2
  fail, data tồn tại mà log thứ 2 mất (dù log thứ 1 trong handler vẫn còn → hệ quả thực tế
  chỉ là thiếu 1 bản log trùng lặp, không mất audit hoàn toàn — chính vì vậy mức LOW).
- **Fix sketch (khi thực hiện — là THAY ĐỔI HÀNH VI ghi log, cần duyệt riêng, tốt nhất gộp
  vào đợt dọn dẹp ActionLog toàn diện):** bỏ `LogAction + SaveChanges` thứ 2 ở controller,
  chuyển 3 Command M1 sang `ILoggableCommand` (behavior mở 1 ambient transaction: handler
  `SaveChanges` join vào, behavior stage log + save + commit 1 lần → data+log atomic, đúng
  playbook §4); quyết định giữ note/LogMeta nào trong 2 bản hiện tại (controller bản tiếng
  Việt vs handler bản tiếng Anh + LogMeta chi tiết).

---

## INFRA-2 — 6 file PNG evidence (root) mất khỏi working tree — ⚠️ TÁI DIỄN LẦN 3 (2026-10-15, 13 FILE — MỞ RỘNG NGOÀI 6 FILE CŨ) — biện pháp di chuyển đã áp dụng

- **Trạng thái: MITIGATED 2026-10-15 — TÁI DIỄN LẦN 3.** Phát hiện qua `git status --porcelain`
  (13 dòng ` D`): 6 file INFRA-2 cũ (mc8*/qa7d*) **+ 7 file evidence AUTH Phase 2/3**
  (`phase2-checklist-step1.png`, `phase2-login-{1440,375,768}.png`,
  `phase3-account-{1440,375,768}.png` — commit `71f7e0c`). Lần đầu tiên phạm vi vượt ngoài
  6 file cũ → giả thuyết "target chọn lọc đúng nhóm PNG này" vẫn đứng, nhưng target-set đã mở rộng
  sang mọi PNG evidence ở root.
- **Ghi nhận ngay khi phát hiện (theo quy trình lần 3):**
  - Danh sách chính xác 13 file: xem `git status` trên (6 INFRA-2 + 7 phase2/phase3); cả 13 đều
    tracked, on-disk=False, `git diff HEAD` = N.
  - mtime thư mục gốc: **2026-10-15 13:54:44** (thời điểm phát hiện 13:58:23) → cửa sổ mất file
    nằm giữa lần `read` thư mục gốc (còn thấy đủ file) và `git status` ở phiên trước — trong khi
    agent chỉ chạy: `git clone` ra TEMP ngoài repo, `Copy-Item` vào `.claude/skills/`, `node`
    read-only, `edit` AGENTS.md. **Không một lệnh nào của agent đụng tới root PNG.**
  - Snapshot `Get-Process` đã lưu đặc điểm: Kaspersky AV đang chạy (`avp` ×5, `kpm`, `ksde`),
    `GoogleDriveFS` ×2 (sync), `StorSvc` Running, `Everything` (indexer), `opencode` + `claude`
    (agent), Docker Desktop + WSL2 engine bình thường. WinDefend/Sense Stopped.
- **Kiểm tra giả thuyết (grep toàn repo + skill/script): KHÔNG tìm thấy lệnh xóa.**
  - Grep `Remove-Item.*png|rm *.png|del *.png|review-*|qa7d|mc8b|phase2-|phase3-` toàn repo:
    chỉ trúng `.gitignore` (`review-*.png`), BACKLOG/HANDOFF (ghi chép), và
    `aspire-react/frontend/scripts/e2e-passkey.mjs:153` (GHI screenshot ra root — write, không xóa).
  - Grep scripts skills (`.claude/skills/**`): chỉ `os.unlink(temp_name)` (temp của design_system.py),
    `rm -rf $TEMP_DIR` (deploy-to-vercel) — không target PNG repo.
  - `git log --diff-filter=D` trên cả 13 file = **rỗng** → chưa từng có commit nào xóa chúng.
  - Kết luận: loại trừ agent + script repo + git history → tác nhân NGOÀI (hệ thống/file-sync/AV),
    cùng lớp với lần 1–2 nhưng target-set mở rộng. Ranh giới với INFRA-1: lần này Docker/WSL2
    bình thường (engine up, AppHost process sống) → **giữ INFRA-2 riêng**, không gộp.
- **Khôi phục:** `git checkout HEAD -- <13 file>` → 13/13 OK trên đĩa, `git status` sạch PNG.
- **Biện pháp di chuyển (phòng ngừa tái diễn lần 4):** `git mv` toàn bộ 13 file root vào
  `docs/evidence/` (rename `R`, giữ lịch sử; đã xác nhận không dính `.gitignore` ở vị trí mới).
  Cập nhật tham chiếu: `docs/HANDOFF_LATEST.md` (qa7d trio + mc8 nested) và
  `aspire-react/frontend/scripts/e2e-passkey.mjs:153` (đường ghi screenshot → `docs/evidence/`).
  Root từ nay KHÔNG còn file evidence nào — tác nhân có target root PNG cũng không còn gì để xóa;
  nếu lần 4 mất file trong `docs/evidence/` thì đó là bằng chứng target theo nội dung PNG.
- **Trạng thái: REOPENED — TÁI DIỄN LẦN 2 (2026-09-06, phiên AUTH Phase 1).** Cùng ĐÚNG 6 file bị
  mất LẦN NỮA sau khi đã khôi phục và đã push (`fc84f25` trở đi — file nằm an toàn trên remote),
  phát hiện qua `git status --porcelain` ngay sau commit Phase 1 (`3110436`). Khôi phục lần 2 bằng
  `git checkout HEAD --` (100% OK). **Kết luận đã nâng:** 2 lần cùng một bộ file chính xác = KHÔNG
  còn giải thích được là ngẫu nhiên/tiền-phiên — nghi có tác nhân hệ thống có CHỌN LỌC target đúng
  nhóm PNG này (thời điểm loss lần 2: trong phiên, giữa lúc agent chạy build/test/commit — KHÁC
  với lần 1 vốn là tiền-phiên). Điều tra chuyên sâu lần 2 đã chạy ngay:
  - **Loại trừ agent hiện tại (lần 2):** toàn bộ `Remove-Item` trong phiên nhắm `_parity_*.ps1`
    (root), `apphost.log`, thư mục TEMP — không một lệnh nào target `*.png`; mọi `git add` đều
    path-explicit; chỉ dùng `git checkout HEAD --` (khôi phục, không xóa).
  - **Loại trừ scripts của repo:** `audit-sweeps.ps1` (không đụng file), `docker-reset.ps1`
    (chỉ remove Docker volumes `mirats-*`), `seed-initial-admin.ps1` (chỉ Remove temp body file)
    — KHÔNG script nào quét/xóa PNG.
  - **Loại trừ cloud-sync/NTFS attribute:** file attributes thuần `Archive` (không OneDrive/
    Sparse/Recall flags), drive D:\ NTFS local, free 48.9GB.
  - **Nghi phạm còn lại (điều tra tiếp nếu tái diễn lần 3):** Windows Storage Sense/StorSvc
    (đang Running — cần kiểm tra cấu hình "clean temp files" có target thư mục này không),
    antivirus/EDR quét-xóa, tiến trình khác trên máy. **Hành động phòng ngừa đã áp dụng:** 6
    file đã push lên remote (an toàn vĩnh viễn trên git); khuyến nghị commit lịch theo dõi —
    nếu tái diễn lần 3, snapshot `Get-Process` + `Handle` list ngay khi phát hiện.
- **Lịch sử lần 1 (2026-09-04):** phát hiện trong subtask A (Giai đoạn 3) ở trạng thái `" D"`
  tiền-phiên, khôi phục từ git blob; khi đó đánh giá "không xác định được thời điểm xóa".
- **Hiện tượng (giống hệt 2 lần):** `mc8_template_builder_nested.png`, `mc8b_after_expand.png`,
  `mc8b_after_form.png`, `qa7d_campaign_3of3.png`, `qa7d_campaign_detail.png`,
  `qa7d_template_builder.png` (evidence QA đợt MC-7d/MC-8/MC-9, commit `4c08d9b` 2026-08-29) —
  mất vật lý khỏi working tree, `git diff HEAD` = N → 0 bytes.
- **Vì sao TÁCH RIÊNG khỏi INFRA-1 (giữ nguyên, thêm đánh giá lại):** lần 1 không timeline;
  **nhưng lần 2 CÓ timeline trong phiên → ranh giới với INFRA-1 mờ đi** — nếu lần 3 đồng thời
  với Docker/WSL2 sự cố thì GỘP vào INFRA-1 làm một hồ sơ hạ tầng; nếu lần 3 vẫn độc lập thì
  giữ riêng và điều tra như một tác nhân file-targeting độc lập.

---

## BUG-N — AssetMaintenances Update: SupplierId/CompletionDate/Cost gán trực tiếp, field absent bị clear (cùng lớp BUG-E)

- **Trạng thái: RESOLVED 2026-09-05** (behavior change theo sketch — phương án "chuyển 3 field
  sang conditional assign" đã chọn). `UpdateMaintenanceCommandHandler`: `SupplierId`,
  `CompletionDate`, `Cost` giờ gán CHỈ KHI GỬI (`is not null`/`HasValue` — Task M1/M2 pattern,
  đồng bộ Title/Notes/Type/IsWarranty vốn đã conditional); `Notes` đổi từ `?? m.Notes` sang
  `is not null` cho nhất quán (subtle: empty-string notes trước đây vẫn ghi đè qua `??` — giờ
  empty-string là giá trị gửi, vẫn ghi đè đúng; chỉ absent mới giữ). `CompletionDate` vẫn
  SpecifyKind Unspecified khi gởi. **Known limitation (same as all patch DTOs):** JSON explicit
  null indistinguishable from absent → không có "clear có chủ đích" (FE gửi prefill đủ field khi
  muốn đổi thành null — MaintenanceCompleteModal đã verify). Frontend impact: 0 — 2 caller
  (`AssetMaintenanceSection` full-form, `MaintenanceCompleteModal` gửi 5 field kèm đủ 3 field
  này prefill từ record). Tests: `Update_PatchSafe_PartialPayload_KeepsExistingSupplierCost
  Completion_BugRepro` (PUT chỉ {title} → SupplierId/CompletionDate/Cost GIỮ NGUYÊN). Full suite
  421/421.
- **Phát hiện (subtask C, Giai đoạn 3, nhóm Rất nặng):** MEDIUM — cùng lớp patch-safety
  BUG-E/BUG-I#1: không isolation risk. Hành vi gốc: 3 field gán TRỰC TIẾP từ DTO nullable;
  payload thiếu field → bị clear/null. *Độ tin cậy lúc audit: suy luận từ đọc code (payload
  realistic của frontend luôn gửi đủ field nên chưa trigger).* Migrate verbatim trước, fix riêng
  bây giờ.

---

## DOC-ERR-1 — ERROR_CODES.md ghi `MODEL_IN_USE` nhưng code KHÔNG gắn error_code (LOW, docs-only)

- **Trạng thái: RESOLVED 2026-10-15 (docs-only, không đụng code).** `docs/ERROR_CODES.md` section
  AssetModels + bảng tra nhanh liệt kê `MODEL_IN_USE` (400, DELETE khi model đang được Asset
  tham chiếu) — nhưng `DeleteModelCommandHandler`
  (`aspire-react.Application/Models/Commands/DeleteModelCommand.cs:46-48`) trả
  `new ModelResult(false, "Không thể xóa Model đang có tài sản sử dụng.")` **không gắn error_code**
  (MapFailure null-path → 400 chỉ có message, đúng §1.6 "Response lỗi KHÔNG có error_code").
- **Fix đã áp dụng:** ERROR_CODES.md — dòng bảng đổi thành `(không error_code)` + message verbatim;
  dòng tra nhanh thay bằng tombstone không chứa token (để `docs-verify` giữ 0 mismatch).
  FE không khớp chuỗi này (chỉ hiển thị `message` server) → không bug FE.

---

## DOC-ERR-2 — ERROR_CODES.md ghi `TEMPLATE_NOT_FOUND` nhưng code thực tế trả `NOT_FOUND` (LOW, docs-only)

- **Trạng thái: RESOLVED 2026-10-15 (docs-only, không đụng code).** `docs/ERROR_CODES.md`
  (MaintenanceCampaigns, POST: "`TEMPLATE_NOT_FOUND` (NOT_FOUND) 404 TemplateId chỉ định không
  tồn tại") — nhưng `CampaignAccess.ResolvePinableVersionAsync`
  (`aspire-react.Application/MaintenanceCampaigns/CampaignAccess.cs:69-74`) trả
  `("NOT_FOUND", "Template not found.")`, controller map `"NOT_FOUND" => "Template not found."`
  (`MaintenanceCampaignsController.cs:125`). Client không bao giờ thấy `TEMPLATE_NOT_FOUND`.
- **Fix đã áp dụng:** ERROR_CODES.md — thay bằng `NOT_FOUND` + message verbatim.
  FE không khớp chuỗi này → không bug FE. `docs-verify` trên ERROR_CODES.md: error codes 0 mismatch.

---

## MT-TZ — Endpoint CŨ trả mốc `timestamp without time zone` KHÔNG có hậu tố `Z` → FE đọc lệch 7 tiếng (RESOLVED 2026-10-19 — phương án A + A-ngày)

- **Trạng thái: RESOLVED 2026-10-19** (đăng ký 2026-10-18 theo quyết định của người dùng khi duyệt LL-3 — **KHÔNG** sửa trong LL-3/LL-4; xử lý ở Bước 1–3 — backend commit `ffdc3fd`, Bước 3 = áp dev + kiểm UI thật).
- **Bối cảnh (đã kiểm chứng trên DB dev thật + API thật):** mọi cột `timestamp without time zone` của app này
  chứa **wall-clock UTC**, không phải giờ VN. Bằng chứng: đối chiếu 12/12 bản ghi `asset_maintenances` —
  `CreatedAt` khớp `action_logs.ActionDate AT TIME ZONE 'UTC'` trong vài ms, còn lệch
  `AT TIME ZONE 'Asia/Ho_Chi_Minh'` **đúng `−07:00:00`**. Các endpoint bảo trì CŨ vẫn trả giá trị **không** `Z`:
  - `GET /api/v1/maintenances/{id}` → `"startDate":"2026-09-01T00:00:00"`
  - `GET /api/v1/assets/{id}/maintenances` → `"startDate":"2026-09-01T00:00:00"`

  trong khi `GET /api/v1/assets/{id}/timeline` (LL-3, đã chuẩn hoá) trả `"occurredAt":"2026-09-01T00:00:00Z"`.
- **Hệ quả:** FE `formatDate`/`formatDateTime` (`frontend/src/utils/format.ts`) = `new Date(value)`; JS hiểu chuỗi
  **không** `Z` là giờ LOCAL ⇒ CÙNG một bản ghi hiển thị lệch 7 tiếng giữa trang Bảo trì và timeline. Hiển thị
  chỉ-ngày thường không lộ, nhưng hiển thị có giờ sẽ lộ; với mốc "nửa đêm giờ VN" do người dùng chọn thì trang
  cũ lệch cả NGÀY so với timeline.
- **Hướng xử lý (rà soát tập trung — KHÔNG vá lẻ từng endpoint):**
  1. Rà TẤT CẢ cột `timestamp without time zone`:
     `SELECT table_name, column_name FROM information_schema.columns WHERE data_type = 'timestamp without time zone' AND table_schema = 'public'`
     → lập bảng phân loại: cột nào app ghi bằng UTC wall-clock (đa số) vs cột nào thực sự là "ngày nghiệp vụ" thuần.
  2. Chọn MỘT hướng, áp cho toàn bộ:
     - **(a)** gán `Kind = Utc` khi ĐỌC — tập trung ở tầng cấu hình EF (`AppDbContext`/`ValueConverter` hoặc
       `EnableLegacyTimestampBehavior` + converter) ⇒ JSON tự có `Z`, không phải sửa từng handler; **hoặc**
     - **(b)** chuyển hẳn các cột đó sang `timestamptz` (migration + backfill `... AT TIME ZONE 'UTC'`) —
       sạch nhất về lâu dài nhưng đụng nhiều bảng/endpoint.
  3. **Xử lý `-infinity`/`infinity`:** đã gặp 1 dòng thật `asset_maintenances.StartDate = -infinity`
     (id `cd796eb5…`, asset `DOC-AST-001`, đã soft-delete). Hai giá trị này không map được sang `DateTime`
     ⇒ phải quyết định trước khi bật converter: chặn bằng CHECK constraint ở tầng DB, hay map về
     `MinValue`/`MaxValue` khi đọc.
  4. Sau khi đổi: chạy `docs-verify`, mở UI THẬT cả 2 màn hình (Bảo trì + timeline) trên CÙNG một bản ghi để xác
     nhận khớp nhau, thêm test canary Kind, và cập nhật bảng trong `docs/API.md`.
- **Phạm vi ước lượng:** ~10–20 endpoint trả DTO có trường ngày + (nếu chọn (b)) 1 migration + rà lại FE các chỗ
  format ngày. **Không** nằm trong phạm vi LL-3 (LL-3 chỉ chuẩn hoá đúng 2 trường của nguồn mình:
  `occurredAt`, `maintenanceCompletionDate`).

- **ĐÃ LÀM — Bước 1 (khảo sát, 2026-10-19):** kiểm kê **16 cột naive / 6 bảng** (khớp 100%
  `docs/HANDOFF_DATETIME_KIND_AUDIT.md` §7); phân loại **13 cột THỜI ĐIỂM** vs **3 cột NGÀY THUẦN**
  (`asset_maintenances.StartDate`/`CompletionDate`, `licenses.TerminationDate`); đo lệch thật
  (lệch 7h + lệch 1 NGÀY với mốc nửa đêm VN); đếm ±infinity = 1 dòng thật. **Người dùng duyệt: phương án A**
  (ValueConverter tại `AppDbContext`) cho 13 cột thời điểm + **A-ngày** (giữ cột, chỉ gắn `Z`) cho 3 cột ngày thuần.
- **ĐÃ LÀM — Bước 2 (backend, 2026-10-19):** migration `20261019031002_MT_TZ_NaiveUtcAndNoInfinity`
  (dọn ±infinity: nullable → NULL, NOT NULL → `CreatedAt`; + **16 CHECK** `CK_<table>_<col>_no_infinity`);
  ValueConverter đăng ký **tường minh từng property** (đọc → `Kind=Utc` ⇒ JSON có `Z`; ghi →
  `Unspecified`); test canary `NaiveDateTimeKindTests` (model InMemory + đối chiếu `information_schema`
  + roundtrip `Z`). Đã áp lên DB dev (1 dòng đổi) — chi tiết + bằng chứng + gates: `docs/HANDOFF_LATEST.md`
  mục "2026-10-19 — MT-TZ (Bước 1 + Bước 2)".
- **ĐÃ LÀM — Bước 3 (áp dev + kiểm UI thật, 2026-10-19):** restart stack dev (AppHost) để chạy build mới;
  `GET /api/v1/maintenances/{id}` giờ trả `"startDate":"2026-09-01T00:00:00Z"` (trước restart: không có `Z`).
  Kiểm UI THẬT bằng `playwright-cli`, **chỉ xem** (không tạo/sửa dữ liệu dev) trên bản ghi `ee7d4c09…` của
  asset `G12AST-222003`: trang **Bảo trì** hiện `1/9/2026` (Ngày bắt đầu) — tab **Lý lịch** của asset hiện
  `01/09/2026 07:00` ⇒ **khớp ngày**; ảnh chụp 1440px + 390px trong `screenshots/mttz3-*.png`.
  Kiểm form sửa trên **DB TẠM** (container riêng): tạo phiếu bằng payload y như FE
  (`2026-11-09T17:00:00.000Z` → DB `2026-11-09 17:00:00`), mở form **Sửa** → bấm **Cập nhật** mà không đổi gì
  → `UpdatedAt` đổi (`03:46:22`) nhưng `StartDate`/`CompletionDate` **y nguyên** (SQL log chỉ có
  `UPDATE … SET "UpdatedAt"`), UI vẫn `10/11/2026` / `13/11/2026`, ActionLog Update vẫn ghi.
  Gates `scripts/precommit-gates.ps1` **6/6 xanh** (TRX `logs/trx/dotnet-test.trx`: 635 passed / 0 failed /
  12 skipped — các test `*_OnRealPostgres` tự skip khi không bật ThrowawayDb). Chạy lại kèm
  `MIRATS_TEST_THROWAWAY_DB=1` trên Postgres thật: **647 passed / 0 failed / 0 skipped**
  (TRX `logs/trx/mttz3-throwaway.trx`), bao gồm 2 test canary `NaiveDateTimeKindTests`.
- **GHI CHÚ — 3 cột NGÀY THUẦN:** vẫn là `timestamp` (phương án A-ngày) ⇒ chỉ an toàn khi **chưa** có báo
  cáo/bộ lọc theo NGÀY; đã tách thành mục **MT-TZ-B** ở "🚧 OPEN (LOW)". Việc dọn `ToApiKind`/`ToColumnKind`
  (LL-3) đã tách thành mục 3 cùng chỗ.

---

## BUG-O — UpdateUserCommand: FirstName/LastName/Email gán VÔ ĐIỀU KIỆN ⇒ payload thiếu field bị chặn 400 (và tiềm ẩn xoá trắng)

- **Trạng thái: RESOLVED 2026-10-18** (fix + test + verify trên Postgres thật của **DB TẠM**, không đụng DB dev).
- **Phát hiện:** 2026-10-18 — 3 field cuối cùng của lớp lỗi "gán vô điều kiện" (BUG-E/N) còn sót trong
  `UpdateUserCommandHandler`: `user.FirstName = request.FirstName.Trim();` + LastName + Email (trước fix
  `UpdateUserCommand.cs:177-179`), trong khi **mọi field khác của handler đã conditional** từ FIX-N1.
  Root cause phía DTO: 3 property khai báo `public string X { get; init; } = string.Empty;` ⇒ JSON **thiếu**
  field vẫn bind thành `""`, không phân biệt được với "gửi chuỗi rỗng".
- **Hành vi TRƯỚC fix (ĐÃ ĐO bằng API thật trên DB tạm `bugo_verify_pre`, worktree `fa7ba18` — không suy đoán):**
  - **Qua HTTP:** payload thiếu bất kỳ field nào trong 3 field ⇒ **`400 Validation failed`**
    (`Email is required.` / `First name is required.` / `Last name is required.`) vì validator khai `NotEmpty()`
    **vô điều kiện**. Đo thực tế: `PUT {id,isSuperUser:false}` → 400; `PUT {id,jobTitle:"…"}` → 400;
    dữ liệu **không** bị đổi. ⇒ Hệ quả thực tế là **không thể PATCH** (`/users/{id}` bắt gửi đủ 3 field),
    **không phải** wipe âm thầm.
  - **Ở tầng handler (tiềm ẩn):** nếu validation bị bỏ qua (gọi handler trực tiếp từ seed/import/test, hoặc
    validator bị gỡ) thì 3 field bị ghi đè `""` ⇒ **xoá trắng** tên/email; client gửi `null` tường minh còn
    gây `NullReferenceException` tại `.Trim()`.
- **Fix (2 tầng, đúng patch-semantics của dự án):**
  1. DTO: 3 property → `string?` (null = không gửi).
  2. Handler: `if (request.FirstName is not null) …` / LastName / Email (Email vẫn `trim().ToLowerInvariant()`).
  3. Validator: bọc `When(x => x.Field is not null, () => RuleFor(…))` cho cả 3 ⇒ **không gửi = không validate**,
     **gửi `""` = vẫn 400** (field bắt buộc không thể bị xoá qua payload).
  4. `LogMeta.changes` bổ sung `firstName`/`lastName` — trước đây chỉ có `email`, nên đúng 2 field từng bị xoá
     âm thầm lại là 2 field **không có** trong nhật ký.
- **Hành vi SAU fix (ĐO bằng API + SQL read-only trên DB tạm `bugo_verify`):**
  | Request | Kết quả đo được |
  |---|---|
  | `PUT {id, isSuperUser:false}` | `200`; `firstName/lastName/email/jobTitle` **giữ nguyên**; `isSuperUser` true→false |
  | `PUT {id, firstName, lastName, email, jobTitle}` | `200`; `'  Nguyen Van  '` → `Nguyen Van`; email HOA → lowercase |
  | `PUT {id, email:""}` / `{firstName:""}` / `{lastName:""}` | `400 Validation failed` đúng field (`Email is required.` …) |
  | DB | `FirstName/LastName/Email` đúng như trên; `action_logs` = 1 create + 2 update (3 ca 400 **không** sinh log), `changes` có `firstName`/`lastName` |
- **Frontend impact: 0** — `UserFormModal` **luôn gửi đủ** 3 field và 3 rule `required` chặn chuỗi rỗng
  (`UserFormModal.tsx:216-246`); không có caller nào PUT thiếu field; "vô hiệu hoá user" dùng `DELETE`
  (không dùng PUT); `employeeNumber`/`jobTitle` giữ nguyên quy ước cũ (`""` = xoá nội dung).
- **Tests:** `UserUpdatePatchSafetyTests` 9 → **16** (thêm `Update_PartialPayload_OnlyIsSuperUser_PreservesRequiredTextFields`,
  `Update_PartialPayload_LogMeta_OnlyRecordsSentFields`, `Update_FullPayload_UpdatesRequiredTextFields`,
  `Validator_PartialPayload_OnlyIsSuperUser_IsValid`, `Validator_RequiredTextField_SentEmpty_IsRejected` ×3).
- **Rà soát cùng lớp lỗi — toàn bộ 21 file `Update*Command.cs`: KHÔNG còn chỗ nào** gán vô điều kiện một field
  optional của request. Các chỗ từng bị nghi (đã đọc code, đều ĐÚNG): `UpdateAssetCommand` (guard
  `IsNullOrEmpty`/`IsNullOrWhiteSpace` tại `:113`, `:121`), `UpdateManufacturer`/`UpdateSupplier` (guard
  `IsNullOrWhiteSpace`), `UpdateCompany`/`UpdateGroup` (`if (request.Name is not null)`),
  `UpdateMaintenance`/`Category`/`Component`/`Consumable`/`CustomField`/`Department`/`Location`/`Model`/
  `SystemInfo`/`SystemPosition` (guard đầy đủ), `UpdateGroupPermissions`/`UpdateUserGroups` (full-replace
  **theo thiết kế**). Hai chỗ dùng `?? <giá trị cũ>` (`Component.Notes`, `License.Serial`/`Notes`) là pattern
  BUG-N đã duyệt (hệ quả đã biết: không thể xoá về rỗng) — **không** thuộc lớp lỗi này.
- **Quan sát phụ (đã đăng ký thành BUG-P, CHƯA sửa):** `UpdateConsumableCommand.cs:127` ghi
  `minAmt = new { old = c.MinAmt, @new = c.MinAmt }` ⇒ `LogMeta` của `minAmt` luôn `old == new` (audit sai,
  không ảnh hưởng dữ liệu) — xem § **BUG-P**.

---

## BUG-P — UpdateConsumableCommand: `LogMeta.changes.minAmt` luôn `old == new` (audit sai, KHÔNG mất dữ liệu)

- **Trạng thái: OPEN** (đăng ký 2026-10-18 theo yêu cầu người dùng — **chưa sửa**; nằm ngoài phạm vi BUG-O).
- **Vị trí:** `aspire-react.Application/Consumables/Commands/UpdateConsumableCommand.cs:127`
  (`minAmt = new { old = c.MinAmt, @new = c.MinAmt }`), trong khối `LogMeta` ở `:123-128`.
- **Nguyên nhân:** khối patch-semantics chỉ chụp giá trị cũ cho 2 field — `var oldName = c.Name;` (`:99`) và
  `var oldQty = c.Qty;` (`:100`) — **không có `oldMinAmt`**. Trong khi đó `c.MinAmt` đã bị gán giá trị mới ở
  `:104` (`if (request.MinAmt.HasValue) c.MinAmt = request.MinAmt.Value;`) **trước** khi serialize `LogMeta`,
  nên `old` và `new` cùng đọc một giá trị ⇒ mọi lần đổi `minAmt` đều ghi `{"old": X, "new": X}`.
- **Ảnh hưởng:** chỉ **nhật ký kiểm toán** sai — không mất/sai dữ liệu nghiệp vụ (`UPDATE` vẫn lưu đúng
  `MinAmt`); không phải lỗ hổng bảo mật. Hệ quả: không thể trả lời "ai đổi ngưỡng tồn tối thiểu từ bao nhiêu
  sang bao nhiêu" từ `action_logs`. Mức: **LOW–MEDIUM (audit)**.
- **Ghi chú liên quan (chưa đăng ký riêng, cân nhắc khi sửa):** handler cho phép đổi **15** field (`:101-115`)
  nhưng `LogMeta` chỉ ghi **3** field (`name`, `qty`, `minAmt`) ⇒ **12** field còn lại (`itemNo`, `categoryId`,
  `manufacturerId`, `supplierId`, `locationId`, `companyId`, `modelNumber`, `orderNumber`, `purchaseCost`,
  `purchaseDate`, `notes`, `image`) hiện **không được ghi nhật ký**, trong khi quy ước dự án là
  `LogMeta.changes` cho mọi update.
- **Hướng sửa đề xuất (chưa làm):** thêm `var oldMinAmt = c.MinAmt;` cạnh `:99-100` và dùng
  `minAmt = new { old = oldMinAmt, @new = c.MinAmt }`; thêm test canary (PUT đổi `minAmt` → `LogMeta` phải có
  `old` = giá trị cũ, `new` = giá trị mới). Nếu duyệt mở rộng: bổ sung các field còn lại vào `LogMeta`.
- **Verify khi làm:** fixture trên **DB tạm** (tạo consumable + PUT `minAmt` qua API) rồi đọc `action_logs`
  bằng SQL read-only — **không** dùng DB dev.

---

## BUG-Q — `GET /api/v1/assets/{id}` trả 500 cho MỌI thiết bị đang được cấp phát (HIGH, ĐÃ SỬA)

- **Trạng thái: RESOLVED** — phát hiện khi verify LL-4 (2026-10-18), sửa ngay trong cùng ngày theo yêu cầu người dùng.
- **Triệu chứng:** `GET /api/v1/assets/{id}` → **500** với *mọi* thiết bị có `CurrentAssignment`
  (cấp cho User / Department / SystemPosition); thiết bị không gắn ai → **200**. Hệ quả: trang chi tiết thiết bị
  (và tab "Lý lịch" của LL-4) không mở được cho phần lớn thiết bị đang dùng.
- **Vị trí (bản cũ):** `aspire-react.Application/Assets/Queries/AssetQueries.cs:229-231`, nhánh `assignedTo`
  theo `AssignmentTargetType` trong `GetAssetByIdQueryHandler`.
- **Nguyên nhân:** tra cứu `assignedTo` **chiếu DTO ngay trong `Select`** rồi lọc ở
  `FirstOrDefaultAsync(x => x.Id == …)`:
  `_context.Users.AsNoTracking().Select(u => new AssetAssignedToUserDto(…)).FirstOrDefaultAsync(u => u.Id == asgn.TargetId…)`.
  EF đẩy vị từ xuống **trước** phép chiếu ⇒ sinh biểu thức `new AssetAssignedToUserDto(u.Id, "user", …).Id == @asgn_TargetId`
  mà Npgsql **không dịch được** ⇒ `InvalidOperationException: The LINQ expression … could not be translated` → 500.
- **Đo chính xác tác nhân (không suy đoán)** — 5/5 phép đo bằng `ToQueryString()` trên provider Npgsql (chỉ dịch, không kết nối):
  | Hình dạng | Kết quả |
  |---|---|
  | named DTO (record/class) trong `Select` + `Where(x => x.Id …)` | **THROW** (kể cả khi mọi tham số là cột, không có hằng số) |
  | named DTO + `OrderBy(x => x.Username)` | **THROW** |
  | named DTO có member tính toán (`u.Username.ToString()`) | **THROW** |
  | **anonymous** type (`new { … }`) + `Where(x => x.Id …)` | OK (EF tự gỡ được member access) |
  ⇒ Tác nhân là **chiếu vào TYPE CÓ TÊN** rồi truy cập member của nó trong vị từ/sắp xếp; hằng số bên trong không
  phải nguyên nhân. EF InMemory **không** bắt được lớp lỗi này (không có bước dịch SQL).
- **Thời điểm phát sinh:** có từ `ba582c0` (2026-09-05), tồn tại ở `HEAD` trước bản sửa (không liên quan LL-4 — LL-4 chỉ đụng FE).
- **Cách sửa (đúng 1 chỗ):** lọc trên **entity** trước rồi mới chiếu:
  `.Where(u => u.Id == asgn.TargetId).Select(u => new AssetAssignedToUserDto(…))`; giữ nguyên company-scope và
  giá trị `type` viết thường (`"user"` / `"department"` / `"systemPosition"`).
- **Test hồi quy (mới):** `aspire-react.Tests/AssetDetailAssignmentTests.cs`
  - `AssignmentLookups_Translate_OnNpgsql` — canary chạy trong CI, dùng `ToQueryString()` (không cần DB).
  - `AssetDetail_AssignedTo_User_Department_SystemPosition_OnRealPostgres` — chạy **Postgres THẬT** trên DB tạm, cổng an toàn
    `MIRATS_TEST_THROWAWAY_DB=1` + `MIRATS_TEST_POSTGRES=<connection string DB tạm>` (như test Concurrency); tự `MigrateAsync()`
    (phải chỉ định `MigrationsAssembly("aspire-react.Server")` vì migration nằm ở project Server), seed trực tiếp rồi gọi
    chính handler.
  - **Chứng minh FAIL → PASS:** trước fix test đổ đúng `InvalidOperationException … 'DbSet<User>().Where(u => new
    AssetAssignedToUserDto(…).Id == @asgn_TargetId)' could not be translated`; sau fix PASS.
- **Bằng chứng API (DB tạm `bugq_verify`, fixture tạo HOÀN TOÀN qua API):** `GET /api/v1/assets/{id}` → **200** cho
  asset gắn User (`"type":"user"`), Department (`"type":"department"`), SystemPosition (`"type":"systemPosition"`),
  và 200 + `"assignedTo":null` cho asset control không gắn ai; log SQL của handler cho thấy
  `SELECT u."Id", u."Username", u."FirstName", u."LastName" FROM users AS u WHERE u."Id" = @asgn_TargetId LIMIT 1`.
- **Rà soát cùng mẫu (2026-10-18, quét toàn bộ 431 file `.cs` của solution; CHỈ liệt kê — không sửa):**
  - Nhóm nguy hiểm (**chiếu type có tên** rồi `Where/OrderBy/First…` trên member): **0 điểm** còn lại sau khi sửa BUG-Q.
  - Cùng *hình dạng* nhưng chiếu **anonymous** (đã đo là an toàn, giữ nguyên): `ListAssetMaintenancesQuery.cs:38`,
    `GetMonthlyCheckoutTrendQuery.cs:62`, `CheckoutLicenseSeatCommand.cs:53`, `:61`, `:70`, `ConsumableAllocationService.cs:75`.
- **Việc chưa làm (cân nhắc sau):** không có test tự động nào chặn *mới* phát sinh mẫu "chiếu type có tên rồi lọc member"
  ngoài canary ở trên (canary chỉ chốt 3 hình dạng tra cứu `assignedTo`); muốn chặn toàn cục cần một analyzer/script rà
  (hiện chỉ có script rà tạm, không nằm trong repo).

---

## FIX-GUID-EMPTY — HTTP 500 khi trường Guid FK = Guid.Empty (✅ RESOLVED 2026-10-20)

- **Hiện tượng (đo thật):** `POST /assets/{id}/archive` body `{}` ⇒ **500** `Npgsql 23503` FK
  `FK_assets_locations_LocationId` — DTO `ArchiveUnarchiveRequestDto` dùng `Guid` không nullable nên field
  thiếu bind thành `Guid.Empty`, handler ghi thẳng vào cột FK.
- **Đã sửa 3 nhóm (mỗi nhóm có test: `aspire-react.Tests/GuidFkValidationTests.cs` — 13 test, 5 chạy
  Postgres thật bằng `[ThrowawayDbFact]`):**

  | Nhóm | Endpoint | Validator | Handler |
  |---|---|---|---|
  | A. Archive | `POST /assets/{id}/archive` | `LocationId` NotEmpty → `LOCATION_REQUIRED` | vị trí không tồn tại → `RESOURCE_NOT_FOUND` |
  | B. Checkin | `POST /assets/{id}/checkin` | `LocationId` NotEmpty → `LOCATION_REQUIRED` | vị trí không tồn tại → `RESOURCE_NOT_FOUND` |
  | C. Checkout → SystemPosition | `POST /assets/{id}/checkout` | `LocationId` phải có và `!= Guid.Empty` → `LOCATION_REQUIRED`; vị trí lạ → `RESOURCE_NOT_FOUND` | — |

  - **Bẫy đã gặp:** FluentValidation `NotEmpty()` trên `Guid?` **chỉ chặn null**, KHÔNG chặn `Guid.Empty`
    (default của `Guid?` là null) ⇒ phải `Must(v => v.HasValue && v.Value != Guid.Empty)`.
  - Test tầng handler chạy trên **Postgres THẬT**: InMemory không thực thi FK nên test InMemory thuần sẽ
    "xanh giả" trong khi production vẫn 500.
- **Bảng rà required-Guid FK (52 ứng viên):** 49 chỗ là id entity cha được **load + trả NOT_FOUND**
  (asset/component/unit/license/consumable/template/campaign/user…) ⇒ an toàn; **3 chỗ ghi thẳng FK** đã sửa.
- **✅ ĐÃ XỬ LÝ tiếp (FIX-GUID-EMPTY phần 2 — 2026-10-20):** toàn bộ field `Guid?` **không bắt buộc** trong lệnh
  Update nay theo quy ước 3 trạng thái (vắng = giữ; `Guid.Empty` = xóa liên kết; id thật phải tồn tại →
  400 `RESOURCE_NOT_FOUND`). Đã áp cho: `UpdateAssetCommand` (Model/Location/Supplier/Company),
  `UpdateComponentCommand` (Supplier/Manufacturer/Location), `UpdateConsumableCommand`
  (Category/Manufacturer/Supplier/Location/Company, cả 2 nhánh confirmed/full-patch),
  `UpdateLicenseCommand` (Supplier/Manufacturer), `UpdateLocationCommand` (Parent/Company/Manager),
  `UpdateDepartmentCommand` (Company/Manager), `UpdateSystemInfoCommand` (Company),
  `UpdateMaintenanceCommand` (Supplier, giữ mã `INVALID_SUPPLIER`), `UpdateModelCommand`
  (Manufacturer/Category/Depreciation/Fieldset, giữ `RESOURCE_NOT_FOUND`). Helper:
  `Common/Validation/OptionalForeignKey.cs`; so sánh field khóa cũng chuẩn hoá để `Guid.Empty` trên field
  đang trống không bị báo `FIELD_LOCKED` nhầm.
- **✅ Lưới an toàn toàn cục:** `ForeignKeyViolationExceptionHandler` map Postgres `23503`/`23001` chưa có
  guard riêng → 400 `REFERENCE_NOT_FOUND` / 409 `RESOURCE_IN_USE` (log warning kèm constraint; xem
  `docs/ERROR_CODES.md` §1.8). Test Postgres thật cho cả 2 nhánh:
  `aspire-react.Tests/ForeignKeyViolationHandlerTests.cs`.
- **✅ FE (2026-10-20):** trước đây bỏ chọn (allowClear) ⇒ gửi `undefined`/`null` ⇒ backend hiểu "không gửi"
  ⇒ **không xóa được liên kết**. Nay helper `frontend/src/shared/utils/fk.ts` (`EMPTY_GUID`,
  `fkForUpdate`, `withOptionalFkCleared`) được áp cho: AssetEditModal, ComponentFormModal,
  ConsumableFormModal, AccessoryFormModal, LicenseFormModal, MaintenanceCompleteModal, LocationListPage,
  DepartmentListPage, SystemInfoListPage. Verify UI thật (DB tạm, Vite 5176): xóa "Nhà cung cấp" trên form
  sửa bản quyền ⇒ DB `licenses."SupplierId" = NULL`; ảnh 1440 `screenshots/guidp2-license-before-1440.png` /
  `guidp2-license-after-1440.png`.
- **✅ Ghi chú 409 qua HTTP:** rà toàn bộ endpoint DELETE hiện tại — **tất cả** đã có guard mềm trả 400 với mã
  riêng (`LOCATION_IN_USE`, `ASSET_HAS_*`, `POSITION_IN_USE_*`, `COMPANY_HAS_*`…), nên **không còn** endpoint
  nào để tái hiện 409 bằng fixture thường. Nhánh 409 của lưới an toàn được chứng minh bằng test Postgres
  thật (xóa `locations` cha còn con → `23001` → handler trả 409) và sẽ bảo vệ các race/future endpoint.
- **✅ Đối chiếu FE ↔ backend (2026-10-20, sau review):** lập bảng 12 cặp form ↔ lệnh Update; phát hiện 2 lệch:
  (a) **Accessory** — FE `AccessoryFormModal` đã gửi `Guid.Empty` nhưng `UpdateAccessoryCommand` **chưa** hỗ trợ
  quy ước ⇒ bỏ chọn supplier sẽ ghi `Guid.Empty` vào FK (500). Đã bổ sung `OptionalForeignKey` + kiểm tồn tại
  cho 5 FK (category/manufacturer/supplier/location/company) + 2 test.
  (b) **Model** — backend `UpdateModelCommand` đã hỗ trợ nhưng FE `AssetModelListPage` gửi `values` thô (undefined
  ⇒ không xóa được liên kết). Đã sửa FE gửi `Guid.Empty` cho 4 FK.
  Verify UI thật trên **Accessory**: bỏ chọn "Nhà cung cấp" → Lưu ⇒ DB `accessories."SupplierId" = NULL`;
  ảnh 1440 `screenshots/guidp3-accessory-before-1440.png` / `guidp3-accessory-after-1440.png`.
- **Response lỗi FK (chốt 2026-10-20):** body chỉ có `status`/`message`/`error_code` — **không** trả tên
  constraint/bảng; chi tiết chỉ ghi warning log (test đã cập nhật theo).

---

## BUG-R - `frontend/scripts/dev-server.mjs` bỏ qua `--port` (LOW, ✅ RESOLVED 2026-10-19 - GĐ1.3)

- **Trạng thái: RESOLVED.** Sửa: `dev-server.mjs` forward tham số CLI xuống Vite —
  `spawn(process.execPath, ['node_modules/vite/bin/vite.js', ...process.argv.slice(2)], …)`.
- **Bằng chứng (đo thật 2026-10-19):** `npm run dev -- --port 5174 --strictPort` ⇒ log Vite hiện
  `Local: https://localhost:5174/`; `Get-NetTCPConnection -LocalPort 5174 -State Listen` = **1 listener**,
  `5173` = **0**; `curl -k https://localhost:5174` = **200**.
- **Vị trí (gốc):** `aspire-react/frontend/scripts/dev-server.mjs` — wrapper dev của Vite (package.json: `node scripts/dev-server.mjs ...`).
- **Hành vi cũ (đã quan sát 3 lần):** chạy `npm run dev -- --port 5174 --strictPort` nhưng log Vite vẫn báo
  `ready` + `Local: https://localhost:5173/`; `Get-NetTCPConnection -LocalPort 5174` **không** có listener. Tức tham số
  cổng không được chuyển xuống Vite qua wrapper.
- **Ảnh hưởng (nay đã hết):** không thể dựng dev server PHỤ ở cổng khác bằng CLI ⇒ các phiên verify song song
  (VD FE phụ trỏ API tạm) buộc phải dùng 5173, dễ tranh chấp; mọi hướng dẫn ghi `--port <khác>` đều sai.

---

## LL-6 — Xuất PDF (lý lịch/báo cáo) bằng chức năng IN của trình duyệt (✅ XONG 2026-10-20)

- **Trạng thái: ✅ XONG** (GĐ1 route + tải dữ liệu · GĐ2 bố cục A4/CSS in · GĐ3 kiểm chứng Chrome+Edge).
  Tài liệu: **`docs/LL6_PRINT.md`** (cách in, giới hạn 5.000 sự kiện, số trang + dự phòng, kết quả đo).
- **Đã làm:** trang in RIÊNG `/assets/:id/ly-lich/in?types=…` (tab mới, không sidebar/menu, quyền
  `assets.view`); nút **"In lý lịch"** ở tab Lý lịch (cạnh bộ lọc Sự kiện chính/Tất cả, truyền đúng filter);
  tải đủ timeline (limit 200/cursor, đảo **CŨ → MỚI**), **vượt 5.000 ⇒ KHÔNG in** + thông báo gợi ý
  "Sự kiện chính" (không bao giờ in bản cắt bớt); tự `window.print()` + nút "In lại" (ẩn khi in);
  bố cục A4 (tiêu đề, khối thông tin thiết bị, bảng sự kiện, `≈` + footnote, `[Đã đính chính]`, nhãn bộ lọc,
  footer người in/thời điểm in, số trang `x / y` bằng `@page` margin box + footer fixed dự phòng).
- **Kiểm chứng thật (2026-10-20):** Chrome 155 + Edge 155 trên DB tạm với **217 sự kiện** ⇒ **7 trang A4**;
  `thead` lặp **7/7**, footer **7/7**, số trang `1/7 … 7/7`, **0 dòng bị cắt**, đủ **217** dòng, dòng đầu là
  sự kiện cũ nhất. Ảnh + PDF mẫu: `screenshots/ll6-*` (gitignored). Test loader: `node --test
  tests/timelinePrint.test.ts` (8 test).
- **Ghi chú:** `≈` chỉ xuất hiện với dòng lý lịch `Source = Backfill` (dữ liệu lịch sử từ LL-1, không có API
  tạo mới) ⇒ DB trắng không có mẫu; logic được phủ bằng unit test `formatEventTime`.

---

## LL-5b — Hiện BẢN ĐÃ BỊ THAY THẾ trong tab Lý lịch (`includeSuperseded`) — HOÃN

- **Trạng thái: HOÃN (backlog)** — chốt 2026-10-18 khi làm LL-5 Bước 2: bước này chỉ làm **backend đính chính** + dữ liệu
  cho Tag "Đã đính chính" ở BẢN MỚI (`isCorrection` trong DTO timeline); **chưa** trả bản bị thay thế ra UI.
- **Vì sao hoãn:** bản bị thay thế hiện bị ẩn HOÀN TOÀN khỏi timeline (`SupersededAt == null`, LL-3). Muốn hiện lại phải
  thêm tham số `includeSuperseded` (trên `GET /assets/{id}/timeline` hoặc `GET /assets/{id}/installations`) **và** thiết kế
  UI (render mờ + nhãn "Đã bị đính chính" + ai/khi nào), đồng thời kiểm tra phân trang cursor không vỡ ⇒ tách bước riêng
  để không trộn rủi ro vào Bước 2.
- **Đã có sẵn để làm tiếp:** cột `SupersededAt` + `CorrectsEntryId` (LL-1/LL-5), DTO timeline `isCorrection`, ActionLog
  `Correct` có `LogMeta.changes` (old/new) + lý do — tab **Lịch sử** đã thấy dấu vết bản cũ ngay bây giờ.
- **Tiêu chí nghiệm thu (dự kiến):** bật `includeSuperseded` thì bản cũ hiện mờ kèm nhãn, KHÔNG mất/lặp sự kiện khi đi qua
  các trang; mặc định (không tham số) giữ nguyên hành vi ẩn; có test + ảnh UI 1440/390.

---

## READY — Việc còn lại sau GĐ0/GĐ1/GĐ2 (2026-10-20)

> Bối cảnh: khảo sát READY Bước 1 (chỉ đọc) kết luận chưa đủ điều kiện vận hành an toàn. GĐ0 đã làm:
> fail-fast `Auth:SigningKey` < 32 byte UTF-8; bootstrap admin ≥ 12 ký tự + khác username (chỉ khi tạo trên DB trắng);
> security headers ở API + frontend Nginx (HSTS tại NPM, CSP để P1).
>
> **GĐ2 đã xong:** pgAdmin tách sang `docker-compose.debug.yml` (prod không cần `PGADMIN_PASSWORD`);
> `GET /api/v1/health/ready` kiểm tra Postgres + Redis thật (200/503) + compose healthcheck gọi HTTP;
> log rotation `json-file 10m × 5` cho mọi service; runbook Zabbix (`DEPLOYMENT.md §10.1`).
>
> **GĐ1 đã xong:** service `backup` hằng ngày (age public key, checksum, retention 30 ngày, `last_success` cho
> Zabbix, remote copy TẮT mặc định); `scripts/deploy.sh` backup-trước-khi-up + chờ `/health/ready`;
> `docs/RUNBOOK_BACKUP_RESTORE.md`; **đã diễn tập restore thật** (3 assets / 1 user / 15 action_logs / 1 installation,
> `pg_restore` 0,74s, đăng nhập OK; ca DB down không đổi `last_success`).

### P0 — còn lại, phải xong trước khi dùng thật

| # | Việc | Trạng thái / tiêu chí nghiệm thu |
|---|---|---|
| P0-1 | **Backup DB tự động + diễn tập restore** (`pg_dump -Fc` hằng ngày, mã hoá, retention 30 ngày, offsite) | ✅ **XONG (GĐ1)** — đã diễn tập restore DB trắng, số dòng khớp, login OK; remote copy cấu hình được (mặc định TẮT) |
| P0-2 | **Backup trước migrate + runbook nâng cấp/rollback** (snapshot trước `Database.Migrate()`, image tag cũ để rollback) | 🟡 **Một phần** — `scripts/deploy.sh` đã backup trước build/up + in rollback; runbook có; **chưa diễn tập nâng cấp thật** trên bản sao |
| P0-5 | **TLS/HSTS tại NPM** (mô hình NPM → mirats-frontend; HSTS `max-age=15552000` **không** `includeSubDomains`, HTTP→HTTPS, không publish port backend) | 🟡 **Đã có compose/docs + evidence tạm**; cần xác nhận trên NPM thật: `curl -sI https://<domain>` có HSTS; `http://<IP>:5000` không truy cập được |

### P1 — tuần 1–2 sau go-live

| # | Việc | Ghi chú |
|---|---|---|
| P1-1 | **Giám sát: Zabbix (HTTP `/api/v1/health/ready`, 5xx, dung lượng đĩa, backup `last_success` > 26h) + Wazuh (thu thập/phân tích log)** | Zabbix runbook đã có (`DEPLOYMENT.md §10.1`, trigger 503/timeout 2 phút); Wazuh thay Loki/ELK |
| P1-2 | Rate limit tại NPM cho `/api/v1/auth/*`; chuyển IP limiter sang Redis nếu chạy > 1 instance | App đã có lockout per-username + burst per-IP (process-local) |
| P1-3 | **CSP đầy đủ** (nonce/hash cho inline style antd) | Rủi ro vỡ style antd — phải test UI thật trước khi bật |
| P1-4 | Password policy ≥ 12 + complexity/breach check; cân nhắc MFA bắt buộc cho admin | Hiện policy chỉ ≥ 8 ký tự |
| P1-5 | Container non-root; quét image (Trivy) trong CI; pin digest | Server Dockerfile hiện chạy root |
| P1-6 | Security headers verify trên proxy (không trùng lặp), session/token cleanup job | API + Nginx đã gửi bộ header tối thiểu |

### P2 — sau khi ổn định

| # | Việc | Ghi chú |
|---|---|---|
| P2-1 | Secret manager (Vault/Doppler/SM) + rotate `AUTH_SIGNING_KEY` có kế hoạch | Hiện secret nằm trong `.env` trên server |
| P2-2 | PITR (WAL archiving) + backup immutable/offsite; diễn tập restore định kỳ | Bổ sung cho P0-1 |
| P2-3 | Chuẩn bị scale ngang: migration lock, lockout phân tán, session store, zero-downtime deploy | Hiện single-instance |
| P2-4 | Lưu trữ/xuất ActionLog cho audit, tích hợp SIEM (Wazuh) | Phục vụ điều tra sự cố |
