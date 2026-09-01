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
StatusLabels/Depreciations là **sections của AdminController** (không phải controller riêng) —
migrate theo pattern "section extraction" (xem §6). Departments = standalone ✔ (pilot này).

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

## 6. Lưu ý riêng: "section extraction" (Categories/Manufacturers/Suppliers/Locations/Models…)

- Section nằm trong AdminController → migrate section = tạo Commands/Queries như thường, nhưng
  **controller action giữ nguyên route/policy trong AdminController** (chỉ thay body bằng Send) —
  hoặc tách controller riêng nếu muốn (đổi cấu trúc route-file, KHÔNG đổi route string).
- OutputCache (`RefData` + `CacheTags.*`): giữ attribute ở action; cache invalidation
  (`ICacheInvalidator.Invalidate*Async`) phải gọi sau khi command thành công — hiện chưa có cơ chế
  invalidate từ Application layer (ICacheInvalidator thuộc Infrastructure/Caching, Web đang dùng).
  Phương án khi migrate: invalidate ở controller sau `Send` thành công (đơn giản, giữ nguyên vị trí)
  hoặc introduce an Application-level cache-invalidation abstraction — **chưa có quyết định, hỏi
  user khi đến lượt migrate section có OutputCache** (Categories/Manufacturers/Suppliers).

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
