# MEDIATR MIGRATION PLAYBOOK — Migrate 1 controller CRUD sang MediatR

> Giai đoạn 1 — pilot: `DepartmentsController` (2026-09-01). Tài liệu này là quy trình chuẩn để
> migrate lần lượt 20 controller còn lại. Mỗi mục đều bắt nguồn từ việc thực tế đã làm/té ở pilot.
> **Nguyên tắc gốc: đây là DI CHUYỂN logic, KHÔNG phải đổi hành vi** — mọi response body, error
> code, message tiếng Việt, thứ tự check phải giữ y nguyên. Code là nguồn sự thật: đọc controller
> hiện hành TRƯỚC, không đọc từ tài liệu cũ.

---

## 0. Phân loại controller trước khi migrate (5 phút)

Đọc controller và trả lời:

| Câu hỏi | Nếu ĐÚNG | Nếu KHÔNG |
|---|---|---|
| Có đầy đủ GetAll/GetById/Create/Update/Delete? | Migrate trọn bộ | Ghi rõ action nào thiếu — KHÔNG thêm endpoint mới (thêm endpoint = đổi hành vi, cần duyệt riêng) |
| Có company-scoping? | Scoping chuyển vào Query/Command handler (dùng `ICompanyScopeService`) | Bỏ qua scoping — đừng "cải tiến" thêm scoping trong lúc migrate |
| 400/404 có body tùy chỉnh (message tiếng Việt riêng, error_code riêng)? | Rule đó giữ ở handler dạng **soft-fail** (xem §3) | Rule không có response tùy chỉnh → có thể đưa vào FluentValidation Validator |
| Handler có tự quản transaction (FOR UPDATE/checkout/checkin)? | **KHÔNG** implement `ILoggableCommand` — giữ logging thủ công trong handler | Được phép dùng `ILoggableCommand` |
| List endpoint có `[OutputCache]`? | Giữ nguyên attribute trên controller action; cache invalidation phải chạy SAU khi command thành công (xem §5 lưu ý) | — |

Bảng phân loại đã audit sẵn (2026-09-01): Categories/Manufacturers/Suppliers/Locations/Models/
Depreciations là **sections của AdminController** (không phải controller riêng) —
migrate theo pattern "section extraction" (xem §6). Departments = standalone ✔ (pilot này).
*(Cập nhật 2026-09-02: StatusLabels KHÔNG migrate — đã bị XÓA hẳn khỏi hệ thống vì dead feature
(0 rows/0 FK/0 usage); Category/Location/Manufacturer/Supplier/Models đã migrate xong.)*

## 1. Cấu trúc file tạo mới (namespace `aspire_react.Server.Application.<Feature>`)

```
Application/<Feature>/
  Queries/List<Feature>sQuery.cs          ← record Query + Handler (1 file, convention đã có)
  Queries/Get<Feature>ByIdQuery.cs
  Commands/<Feature>Result.cs             ← record result dùng chung (Success, Message, ErrorCode, ...)
  Commands/Create<Feature>Command.cs
  Commands/Update<Feature>Command.cs
  Commands/Delete<Feature>Command.cs
```
- Tên file = tên type chính trong file (1 record Query/Command + Handler cùng file, như Accessories).
- Query trả **DTO projection** (record) đặt cạnh Query — property names phải khớp 1:1 với anonymous
  projection cũ của controller (JSON camelCase tự map).
- GetById có thể trả **entity Domain** nếu controller cũ serialize raw entity (Department trả nguyên
  entity, navs null) — trả entity là giữ đúng JSON, đừng tạo DTO rồi mất field.
- Result dùng chung cho Create/Update/Delete: `Success, Message, ErrorCode, + field để controller
  dựng response thành công (Id/Name...) + LogMeta/Note (chỉ Update cần, xem §4)`.

## 2. Controller sau migrate

- Ctor chỉ còn `IMediator` (+ giữ helper `GetCurrentUserId()` đọc `local_user_id`).
- Mỗi action = 1 `Send(...)` + map result → HTTP. KHÔNG còn `_context`/`ICompanyScopeService`/
  `IActionLogService` trực tiếp.
- **MapFailure phải tái tạo đúng body cũ:**
  ```csharp
  if (result.ErrorCode == "NOT_FOUND") return NotFound(new { status = "error", message = result.Message });
  object body = result.ErrorCode is null
      ? new { status = "error", message = result.Message }
      : new { status = "error", message = result.Message, error_code = result.ErrorCode };
  return BadRequest(body);
  ```
  Cạm bẫy thật: body cũ của Department không có key `error_code` khi rule name-empty/dup —
  serialize `error_code = null` sẽ THÊM key mới vào JSON (khác cũ). Dùng conditional object như trên.
  Cạm bẫy thật #2: `MapFailure` để `static` → CS0120 vì `NotFound()`/`BadRequest()` là instance
  methods của ControllerBase.
- **Request DTO mới thay cho entity-binding**: tạo `record <Action>Request(...)` với đúng các field
  nghiệp vụ. Cạm bẫy: binding thẳng entity cũ cho phép client gửi bừa field (Id, nav...) — DTO mới
  thu hẹp về field nghiệp vụ là chủ đích an toàn, ghi rõ trong review.
- Thứ tự policy attribute, route, tên action giữ nguyên (frontend + permission catalog không đổi).

## 3. Quy tắc đặt validation/validation-failure

- **Response body tùy chỉnh (message riêng/error_code riêng) → soft-fail trong handler**, trả
  `Result(false, message, errorCode)` — controller map như §2. Lý do: `ValidationExceptionHandler`
  trả `{status:"error", message:"Validation failed.", errors:{field:[...]}}` — đưa rule vào
  FluentValidation sẽ ĐỔI body 400 (message cụ thể biến thành "Validation failed." + errors dict)
  → frontend hiện `message` sẽ hiển thị sai. Precedent: `CreateAccessoryCommand` không có validator.
- **FluentValidation Validator chỉ thêm khi** dự án chấp nhận shape chuẩn "Validation failed."
  (ví dụ các rule format/độ dài không có message tùy chỉnh cũ), hoặc controller cũ đã tự trả shape
  đó (UsersController.Create tự gọi validator → giữ nguyên cơ chế đó khi migrate Users).
- **Thứ tự check phải giữ nguyên** (VD Department: 404 → scope 404 → empty-name 400 → dup 400).

## 4. ActionLog qua ActionLogBehavior (ILoggableCommand)

- Create/Update/Delete command implement `ILoggableCommand<TResponse>`, viết `BuildLogEntry(response)`:
  - `!response.Success → return null` (soft-fail không log — mirror early-return cũ).
  - Copy NGUYÊN Văn các field log cũ vào `ActionLogEntry` (ItemType/ItemId/ActionType/CreatedBy/
    CompanyId/Note/LogMeta). `required` fields của entry bắt lỗi thiếu lúc compile.
- **Update cần before-snapshot**: LogMeta `changes{old,new}` cần giá trị trước khi gán — handler
  chụp `before` TRƯỚC khi gán, build JSON LogMeta trong handler, trả về qua `Result.LogMeta/Note`,
  `BuildLogEntry` copy từ response. (Command là record bất biến — đừng nhét state trước vào command.)
- **Behavior tự mở 1 ambient transaction**: handler's `SaveChanges` join vào (không commit),
  behavior stage log + SaveChanges + commit 1 lần → data+log atomic. Với InMemory (tests) là no-op.
- **Enrichment 2a (đã duyệt 2026-09-01)**: behavior ghi qua `LogAction` (enriched) — các call site
  cũ dùng `Log(entry)` THIN sẽ THÊM 3 field `RemoteIp/UserAgent/ActionSource`. Đây là thay đổi hành
  vi CÓ CHỦ ĐÍCH; khi migrate từng controller phải: (1) ghi rõ trong báo cáo, (2) check phía đọc log
  (API view DTO không expose 3 field này; ActionLogTable.tsx map field tường minh → an toàn),
  (3) test thật: đủ field cũ + 3 field mới có giá trị hợp lý (không null/rác).
- **Giới hạn**: log cần ActionDate override → giữ manual `Log(entry)`; command tự quản tx → không
  opt-in marker.

## 5. Các bước thực thi chuẩn (checklist từng controller)

1. [ ] Audit controller: bảng action/company-scoping/guards/log style (mẫu §0).
2. [ ] **Capture baseline API trên binary CŨ** (script 2-phase như `_g1-api-verify.ps1` đã dùng):
       mỗi action 1 lần gọi thật + snapshot response + snapshot ActionLog (API + DB query
       `RemoteIp/UserAgent/ActionSource` để so enrichment).
3. [ ] Tạo Queries/Commands/Result (§1) — copy logic verbatim, chỉ đổi nơi đứng.
4. [ ] Rewrite controller (§2) — xóa hết `_context`… ctor còn `IMediator`.
5. [ ] Implement `ILoggableCommand` cho Create/Update/Delete (§4); log thủ công xóa khỏi handler.
6. [ ] Build Release 0 error (fix compile errors = danh sách nơi logic mới phải phủ).
7. [ ] Adapt tests: test gọi controller trực tiếp → gọi handler/query trực tiếp (giữ nguyên số
       [Fact], assertions cùng chất — scoping vẫn assert đúng phạm vi thấy/bị chặn).
8. [ ] Restart stack (AppHost rebuild Debug) → chạy lại script phase `post` → so parity từng field.
9. [ ] DB check: negative case = 0 log row (marker query); enrichment = 3 field mới có giá trị.
10. [ ] Báo cáo: bảng parity + khác biệt có chủ đích + fixture QA đã sinh (nếu có) — chờ duyệt
      rồi mới commit.

## 6. Section-extraction (Categories/Manufacturers/Suppliers/Locations/Models/StatusLabels/Depreciations trong AdminController)

**QUYẾT ĐỊNH ĐÃ CHỐT (Giai đoạn 2, pilot Category — 2026-09-01): MỖI SECTION = 1 CONTROLLER
RIÊNG standalone** (VD `CategoriesController`), KHÔNG giữ action trong AdminController với
IMediator lai tạp. Lý do: mục tiêu là AdminController biến mất dần; ctor lai tạp
(IMediator + AppDbContext + ICacheInvalidator + IActionLogService cùng lúc) chỉ trì hoãn vấn đề.
Áp dụng NHẤT QUÁN cho Location/Manufacturer/Supplier — không cần hỏi lại trừ khi section có
đặc thù thật sự khác.

Quy trình section-extraction đã chạy thực tế trên Category:

1. **Route giữ nguyên 100% URL**: AdminController khai báo class-route `api/v1` + action segment
   `categories`; controller mới khai báo class-route `api/v1/categories` + action rỗng/
   `{id:guid}` — URL cuối identical, frontend không đổi. Policy attribute copy verbatim.
2. **Requests DTO hóa**: `[FromBody] Entity` cũ (mass-binding, cho phép client gửi Id/nav) →
   `Create<Entity>Request`/`Update<Entity>Request` với đúng field nghiệp vụ — narrowing an toàn,
   ghi chú trong review. `Update<Entity>Request` record MOVE khỏi AdminController (tránh CS0101).
3. **Kiểm tra patch-safety NGAY lúc audit**: Category Update vốn PATCH-SAFE (Task M2, conditional
   assigns) → giữ verbatim, không cần backlog. Department thì ngược lại (full-PUT → BUG-E).
   Mỗi section phải kết luận riêng mục này.
4. **Kiểm tra dup-check NGAY lúc audit**: Category dup-check (Name+CategoryType) chỉ tồn tại ở
   CREATE — UPDATE cho phép rename trùng tên (verify bằng binary cũ trước khi ngờ nhầm bug).
   Các rule "không tồn tại từ trước" thì đừng thêm vào lúc migrate.
5. **GetById thiếu** → bổ sung theo route convention (`GET .../{id:guid}`, trả entity, 404 khi
   thiếu) — đã được duyệt là feature-add nhỏ.
6. **Cả 2 Behavior cùng lúc**: Create/Update/Delete command implement `ILogggableCommand` +
   `ICacheInvalidatingCommand` (tags = `CacheTags.*`, `ShouldInvalidateCache` = Success).
   Log(entry) THIN cũ → enrichment 2a (đã duyệt) — log mới thêm RemoteIp/UserAgent/ActionSource.
7. **OutputCache attribute giữ trên controller action mới** (`RefData` + `CacheTags.*`) —
   ICacheInvalidator typed methods CHỈ xóa khỏi controller khi section đó migrate xong; các
   section chưa migrate trong AdminController vẫn dùng ICacheInvalidator như cũ.
8. **AdminController thu nhỏ**: xóa section + DTO move đi; usings dọn theo (CacheTags using
   Application.Common vẫn cần nếu còn section dùng tag).
9. **Test adaptation**: các test gọi action AdminController → chuyển sang handler-level
   (pattern TaskK/TaskL2) hoặc drive qua 2 behavior thật nếu test assert ActionLog
   (xem CategoryAndComponentTests.DeleteCategory_Unused — cache outermost → ActionLog → handler).
10. **Cạm bẫy InMemory mới**: test file có CreateContext riêng CẦN
    `ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))` — ActionLogBehavior
    BeginTransaction sẽ throw trên InMemory nếu thiếu (CategoryAndComponentTests đã từng thiếu).

Các section còn lại áp dụng: **Location** ✔ (migrate xong 2026-09-01), **Manufacturer/Supplier** ✔
(migrate xong — không scoping; Manufacturer/Supplier có Code 2-5 ký tự rule), **Models** ✔ (migrate
xong — log thin + LogMeta ×9 qua response; TODO BUG-H trong handler), **Depreciations** (chỉ GET —
chỉ cần Query, không Command). *(StatusLabels: ĐÃ XÓA hẳn 2026-09-02 — dead feature, không migrate.)*

**Ghi chú thực tế từ Location (migrate 2026-09-01):**
- Section KHÔNG có OutputCache/ICacheInvalidator → command KHÔNG implement
  `ICacheInvalidatingCommand` (chỉ ILoggableCommand). Section có cache thì làm cả 2 marker
  (như Category).
- Phát hiện bug cũ khi audit (VD Location.Create thiếu company-scoping + toàn bộ validation):
  migrate VERBATIM + comment `// TODO SECURITY BUG-xx: ... see BACKLOG.md` NGAY TRONG handler
  (không chỉ ghi backlog) + đăng ký backlog với mức độ phù hợp (BUG-G = SECURITY/HIGH vì
  cross-company creation). Điều tra nhanh dữ liệu thật (đếm row cross-company qua SQL read-only)
  và báo cáo kết quả trong report của section.
- GetById mới: áp dụng company-scoping theo MAJORITY của chính section đó (Location: 3/4 path
  đã scoped → GetById scoped-404; KHÔNG lấy path đang sai làm chuẩn).
- Entity có nav collection khởi tạo non-null (VD `Location.Children = new()`) → GetById trả
  entity sẽ serialize `children: []` — chấp nhận được cho endpoint MỚI (không có parity constraint).

## 7. Cạm bẫy đã gặp ở pilot (đừng té lại)

1. `MapFailure` static → CS0120 (NotFound/BadRequest là instance).
2. Quên rằng body 400 cũ KHÔNG có `error_code` → conditional object (§2).
3. Update: `before` phải chụp trước khi gán; LogMeta đi qua response, không nhét vào command.
4. Tests construct controller trực tiếp (`new DepartmentsController(db, scope, actionLogSvc)`)
   sẽ vỡ ctor — adapt sang handler tests (§5.7), giữ count [Fact].
5. Guard test (`*_IN_USE`) cần row tham chiếu thật: user soft-delete vẫn chặn → department fixture
   sẽ không xóa được — đặt tên fixture rõ ràng (`G1GUARD-*`), ghi trong báo cáo.
6. Script verify: `GET /action-logs` trả envelope `{status,data[]}` — filter phải vào `.data`.
7. Restore/restart stack: container name Postgres ĐỔI mỗi lần chạy AppHost — resolve bằng
   `docker ps` trước khi query DB.
