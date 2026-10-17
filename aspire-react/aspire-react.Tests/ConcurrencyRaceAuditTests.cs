using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Xunit;
using Xunit.Abstractions;

namespace aspire_react.Tests;

/// <summary>
/// Task O — EMPIRICAL audit: fire 2 truly-concurrent checkout/allocate requests at the SAME resource
/// with only "1 remaining" unit/seat, on the REAL Aspire stack (real Postgres). Records whether both
/// succeed (overcommit / lost-update) so the race is confirmed in practice, not just from code reading.
///
/// These tests REQUIRE the Aspire stack to be running (server on localhost:5428) and are timing-dependent,
/// so they are tagged Category=Concurrency and are NOT part of the fast CI suite (run explicitly with
/// `dotnet test --filter "Category=Concurrency"` while the stack is up). They do not assert a pass/fail
/// on the race outcome — they record evidence for the audit report.
///
/// [BƯỚC 0.1 / LL-3] SAFETY GATE: mọi test trong class dùng `[ThrowawayDbFact]` — chỉ chạy khi
/// `MIRATS_TEST_THROWAWAY_DB=1` (xác nhận stack đang trỏ vào DB TẠM). Thiếu cờ → SKIP kèm lý do, để
/// không ghi fixture vào DB dev/prod. Xem `ThrowawayDbGuard`.
///
/// [ITEM D 2026-10-02] SELF-CLEANUP: every test now returns/checks-in whatever it allocated and then
/// attempts to DELETE each fixture it created — all through the API (no SQL, §8). The outcome is
/// printed and recorded under "&lt;resource&gt;_cleanup" so residue is never silent. Some fixtures are
/// **un-deletable by design** (the project's delete-guard rules: records with allocation/checkout
/// history must not be hard-deleted) — those are reported as BLOCKED with the API's own error code
/// instead of being ignored.
/// </summary>
[Trait("Category", "Concurrency")]
public class ConcurrencyRaceAuditTests
{
    private readonly ITestOutputHelper _output;
    public ConcurrencyRaceAuditTests(ITestOutputHelper output) => _output = output;

    /// <summary>[ITEM D] Base URL is configurable so the same tests can run against another stack
    /// (e.g. the compose stack on :5000 in CI) — default stays the dev Aspire API on :5428.</summary>
    private static string BaseUrl =>
        Environment.GetEnvironmentVariable("MIRATS_TEST_BASE_URL")?.TrimEnd('/') ?? "http://localhost:5428";

    private const string LoginUrl = "/api/v1/auth/login";
    private const int Iterations = 5;

    /// <summary>[ITEM D] Per-run suffix for unique-constrained fixtures (category name, company code,
    /// asset tag) so repeat runs never collide, and leftovers stay identifiable.</summary>
    private static readonly string RunId = DateTime.UtcNow.ToString("HHmmss");

    /// <summary>[ITEM D] The local admin id is resolved from GET /users/me instead of being hardcoded:
    /// the old constant went stale the moment the dev database was reset/re-seeded (every checkout then
    /// failed with TARGET_NOT_FOUND/404 and the race tests silently stopped testing anything).</summary>
    private static string? _adminUserId;

    private async Task<string> GetAdminUserIdAsync()
    {
        if (_adminUserId != null) return _adminUserId;
        var (status, body) = await GetAsync("/api/v1/users/me");
        if (status != 200 || !TryReadDataId(body, out var id))
            throw new InvalidOperationException($"Cannot resolve admin user id (HTTP {status}): {Shorten(body)}");
        _adminUserId = id;
        return id;
    }

    private static bool TryReadDataId(string body, out string id)
    {
        id = string.Empty;
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("data", out var d) && d.TryGetProperty("id", out var v))
            {
                id = v.ToString();
                return !string.IsNullOrWhiteSpace(id);
            }
        }
        catch (JsonException) { }
        return false;
    }

    // [AUTH Phase 4] Login source switched to the LOCAL /auth/login endpoint (Keycloak token
    // path retired for tests — D-7). The app-admin password resolves, in order:
    //   1. environment variable MIRATS_TEST_ADMIN_PASSWORD
    //   2. repo-root file `.mirats-test-admin-password` (gitignored — local dev convenience)
    // The file now holds the LOCAL admin password (bootstrap-seeded hash), not the Keycloak one.
    private static string AdminPassword
    {
        get
        {
            var fromEnv = Environment.GetEnvironmentVariable("MIRATS_TEST_ADMIN_PASSWORD");
            if (!string.IsNullOrWhiteSpace(fromEnv)) return fromEnv;
            var repoRoot = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..");
            return File.ReadAllText(Path.Combine(repoRoot, ".mirats-test-admin-password")).Trim();
        }
    }

    private static readonly HttpClient _http = new() { BaseAddress = new Uri(BaseUrl) };
    private static string? _token;
    private static readonly object _tokenLock = new();

    private async Task<string> GetTokenAsync()
    {
        if (_token != null) return _token;
        lock (_tokenLock)
        {
            if (_token != null) return _token;
        }
        var resp = await _http.PostAsJsonAsync(LoginUrl, new { username = "admin", password = AdminPassword });
        var body = await resp.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(body);
        if (!json.RootElement.TryGetProperty("accessToken", out var token))
            throw new InvalidOperationException($"LOGIN FAILED (HTTP {(int)resp.StatusCode}): {body.Substring(0, Math.Min(300, body.Length))}");
        _token = token.GetString();
        return _token!;
    }

    private async Task<(int status, string body)> SendAsync(HttpMethod method, string path, object? body = null)
    {
        var token = await GetTokenAsync();
        using var req = new HttpRequestMessage(method, path);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body != null)
            req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        var resp = await _http.SendAsync(req);
        return ((int)resp.StatusCode, await resp.Content.ReadAsStringAsync());
    }

    private Task<(int status, string body)> PostJsonAsync(string path, object body)
        => SendAsync(HttpMethod.Post, path, body);

    private Task<(int status, string body)> GetAsync(string path) => SendAsync(HttpMethod.Get, path);

    private Task<(int status, string body)> DeleteAsync(string path) => SendAsync(HttpMethod.Delete, path);

    private static async Task<(int s1, string b1, int s2, string b2)> FireTwoAsync(
        Func<Task<(int, string)>> a, Func<Task<(int, string)>> b)
    {
        var t1 = a();
        var t2 = b();
        var r1 = await t1;
        var r2 = await t2;
        return (r1.Item1, r1.Item2, r2.Item1, r2.Item2);
    }

    private void Record(string resource, List<object> results)
    {
        var file = Path.Combine(Path.GetTempPath(), "opencode", "concurrency_results.json");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        Dictionary<string, List<object>> all;
        if (File.Exists(file))
        {
            try { all = JsonSerializer.Deserialize<Dictionary<string, List<object>>>(File.ReadAllText(file)) ?? new(); }
            catch { all = new(); }
        }
        else all = new();
        all[resource] = results;
        File.WriteAllText(file, JsonSerializer.Serialize(all, new JsonSerializerOptions { WriteIndented = true }));
    }

    // =========================================================================
    // [ITEM D] Self-cleanup helpers — API only, delete-guard aware
    // =========================================================================

    private sealed class CleanupReport
    {
        public List<string> Returned { get; } = new();
        public List<string> Deleted { get; } = new();
        public List<string> Blocked { get; } = new();

        public List<object> ToRows() => new()
        {
            new { returned = Returned, deleted = Deleted, blockedByDeleteGuard = Blocked }
        };
    }

    private static string Shorten(string s) => s.Length > 180 ? s.Substring(0, 180) + "..." : s;

    private async Task TryDeleteAsync(CleanupReport rep, string label, string path)
    {
        try
        {
            var (status, body) = await DeleteAsync(path);
            if (status is 200 or 204) rep.Deleted.Add($"{label} -> deleted");
            else rep.Blocked.Add($"{label} -> HTTP {status} {Shorten(body)}");
        }
        catch (Exception ex)
        {
            rep.Blocked.Add($"{label} -> {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>Returns every outstanding accessory unit, then tries to delete the accessory
    /// (expected BLOCKED: ACCESSORY_HAS_CHECKOUTS — history is intentionally permanent).</summary>
    private async Task CleanupAccessoryAsync(CleanupReport rep, string accessoryId)
    {
        var (_, body) = await GetAsync($"/api/v1/accessories/{accessoryId}/checkouts");
        foreach (var row in ParseArray(body))
        {
            var remaining = row.GetProperty("remainingOut").GetInt32();
            if (remaining <= 0) continue;
            var checkoutId = row.GetProperty("id").GetString();
            var (status, _) = await PostJsonAsync($"/api/v1/accessories/checkouts/{checkoutId}/checkin",
                new { returnQty = remaining, note = "item D cleanup" });
            if (status is 200) rep.Returned.Add($"accessory {accessoryId} checkout {checkoutId} returned {remaining}");
        }
        await TryDeleteAsync(rep, $"accessory {accessoryId}", $"/api/v1/accessories/{accessoryId}");
    }

    /// <summary>Checks in every ASSIGNED license seat, then tries to delete the license.
    /// Seat DTO shape (GET /licenses/{id}/seats): { id, seatNumber, assigned, targetType, user, asset,
    /// systemInfo, note, assignedAt } — the boolean `assigned` is the reliable signal.</summary>
    private async Task CleanupLicenseAsync(CleanupReport rep, string licenseId)
    {
        var (_, body) = await GetAsync($"/api/v1/licenses/{licenseId}/seats");
        foreach (var seat in ParseArray(body))
        {
            var assigned = seat.TryGetProperty("assigned", out var a) && a.ValueKind == JsonValueKind.True;
            if (!assigned) continue;
            var seatId = seat.GetProperty("id").GetString();
            var (status, resp) = await PostJsonAsync($"/api/v1/licenses/{licenseId}/checkin",
                new { seatId, note = "item D cleanup" });
            if (status is 200) rep.Returned.Add($"license {licenseId} seat {seatId} checked in");
            else rep.Blocked.Add($"license {licenseId} seat {seatId} checkin failed: HTTP {status} {Shorten(resp)}");
        }
        await TryDeleteAsync(rep, $"license {licenseId}", $"/api/v1/licenses/{licenseId}");
    }

    /// <summary>Checks the allocated component unit back in, then tries to delete the component.</summary>
    private async Task CleanupComponentAsync(CleanupReport rep, string componentId, string assetId)
    {
        var (status, _) = await PostJsonAsync($"/api/v1/components/{componentId}/checkin",
            new { assetId, quantity = 1, note = "item D cleanup" });
        if (status is 200) rep.Returned.Add($"component {componentId} unit checked in from asset {assetId}");
        await TryDeleteAsync(rep, $"component {componentId}", $"/api/v1/components/{componentId}");
    }

    private void ReportCleanup(string resource, CleanupReport rep)
    {
        _output.WriteLine($"[{resource} cleanup] returned={rep.Returned.Count} deleted={rep.Deleted.Count} blocked={rep.Blocked.Count}");
        foreach (var b in rep.Blocked) _output.WriteLine($"  BLOCKED: {b}");
        Record($"{resource}_cleanup", rep.ToRows());
    }

    private static IEnumerable<JsonElement> ParseArray(string body)
    {
        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            yield break;
        foreach (var row in data.EnumerateArray()) yield return row.Clone();
    }

    private static string? ExtractId(string body)
    {
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.TryGetProperty("data", out var d)
               && d.TryGetProperty("id", out var id)
            ? id.ToString()
            : null;
    }

    // =========================================================================
    // Race scenarios
    // =========================================================================

    [ThrowawayDbFact]
    public async Task LicenseSeat_OneFreeSeat_TwoConcurrentCheckouts()
    {
        var results = new List<object>();
        var createdLicenses = new List<string>();
        string? categoryId = null;
        try
        {
            var adminId = await GetAdminUserIdAsync();
            var catId = await PostJsonAsync("/api/v1/categories", new { name = $"QCR-LIC-{RunId}", categoryType = "License" });
            categoryId = ExtractId(catId.body);

            for (var i = 0; i < Iterations; i++)
            {
                var lic = await PostJsonAsync("/api/v1/licenses", new { name = $"QCR-LIC-{RunId}-{i}", seats = 1, categoryId, companyId = (Guid?)null });
                var licId = ExtractId(lic.body);
                if (licId != null) createdLicenses.Add(licId);
                var res = await FireTwoAsync(
                    () => PostJsonAsync($"/api/v1/licenses/{licId}/checkout", new { targetType = "User", targetId = adminId, note = "race-A" }),
                    () => PostJsonAsync($"/api/v1/licenses/{licId}/checkout", new { targetType = "User", targetId = adminId, note = "race-B" }));
                results.Add(new { iter = i, sA = res.s1, sB = res.s2, bodyA = res.b1, bodyB = res.b2 });
                _output.WriteLine($"[License iter {i}] A={res.s1} B={res.s2}");
            }
            Record("license", results);
        }
        finally
        {
            var rep = new CleanupReport();
            foreach (var licId in createdLicenses) await CleanupLicenseAsync(rep, licId);
            if (categoryId != null) await TryDeleteAsync(rep, $"category {categoryId}", $"/api/v1/categories/{categoryId}");
            ReportCleanup("license", rep);
        }
    }

    [ThrowawayDbFact]
    public async Task Accessory_OneRemaining_TwoConcurrentCheckouts()
    {
        var results = new List<object>();
        var created = new List<string>();
        try
        {
            var adminId = await GetAdminUserIdAsync();
            for (var i = 0; i < Iterations; i++)
            {
                var acc = await PostJsonAsync("/api/v1/accessories", new { name = $"QCR-ACC-{RunId}-{i}", qty = 1, minAmt = 0, companyId = (Guid?)null });
                var accId = ExtractId(acc.body);
                if (accId != null) created.Add(accId);
                var res = await FireTwoAsync(
                    () => PostJsonAsync($"/api/v1/accessories/{accId}/checkout", new { checkoutType = "User", targetId = adminId, quantity = 1, note = "race-A" }),
                    () => PostJsonAsync($"/api/v1/accessories/{accId}/checkout", new { checkoutType = "User", targetId = adminId, quantity = 1, note = "race-B" }));
                results.Add(new { iter = i, sA = res.s1, sB = res.s2, bodyA = res.b1, bodyB = res.b2 });
                _output.WriteLine($"[Accessory iter {i}] A={res.s1} B={res.s2}");
            }
            Record("accessory", results);
        }
        finally
        {
            var rep = new CleanupReport();
            foreach (var accId in created) await CleanupAccessoryAsync(rep, accId);
            ReportCleanup("accessory", rep);
        }
    }

    [ThrowawayDbFact]
    public async Task Component_Bulk_OneRemaining_TwoConcurrentAllocates()
    {
        var results = new List<object>();
        var created = new List<(string componentId, string assetId)>();
        var createdAssets = new List<string>();
        string? categoryId = null;
        string? companyId = null;
        try
        {
            var adminId = await GetAdminUserIdAsync(); // resolves/validates the session before creating fixtures
            var cat = await PostJsonAsync("/api/v1/categories", new { name = $"QCR-COMP-{RunId}", categoryType = "Component" });
            categoryId = ExtractId(cat.body);
            var comp = await PostJsonAsync("/api/v1/companies", new { name = $"QCR-CO-{RunId}", parentId = (Guid?)null });
            companyId = ExtractId(comp.body);

            for (var i = 0; i < Iterations; i++)
            {
                var c = await PostJsonAsync("/api/v1/components", new { name = $"QCR-COMP-{RunId}-{i}", trackingType = "Bulk", qty = 1, minAmt = 0, categoryId, companyId });
                var compId2 = ExtractId(c.body);
                // physical=false → the asset is NOT auto-confirmed, so it stays deletable in cleanup
                var asset = await PostJsonAsync("/api/v1/assets", new { assetTag = $"QCR-AST-{RunId}-{i}", name = $"QCR Asset {RunId} {i}", companyId, physical = false, requestable = false });
                var assetId = ExtractId(asset.body);
                if (assetId != null) createdAssets.Add(assetId);
                if (compId2 != null && assetId != null) created.Add((compId2, assetId));
                var res = await FireTwoAsync(
                    () => PostJsonAsync($"/api/v1/components/{compId2}/assign", new { assetId, assignedQty = 1, note = "race-A" }),
                    () => PostJsonAsync($"/api/v1/components/{compId2}/assign", new { assetId, assignedQty = 1, note = "race-B" }));
                results.Add(new { iter = i, sA = res.s1, sB = res.s2, bodyA = res.b1, bodyB = res.b2 });
                _output.WriteLine($"[Component iter {i}] A={res.s1} B={res.s2}");
            }
            Record("component", results);
        }
        finally
        {
            var rep = new CleanupReport();
            foreach (var (componentId, assetId) in created)
                await CleanupComponentAsync(rep, componentId, assetId);
            // assets are tracked separately: they must be cleaned even when the component create failed
            foreach (var assetId in createdAssets)
                await TryDeleteAsync(rep, $"asset {assetId}", $"/api/v1/assets/{assetId}");
            if (categoryId != null) await TryDeleteAsync(rep, $"category {categoryId}", $"/api/v1/categories/{categoryId}");
            if (companyId != null) await TryDeleteAsync(rep, $"company {companyId}", $"/api/v1/companies/{companyId}");
            ReportCleanup("component", rep);
        }
    }

    [ThrowawayDbFact]
    public async Task Consumable_OneRemaining_TwoConcurrentCheckouts()
    {
        var results = new List<object>();
        var created = new List<string>();
        try
        {
            var adminId = await GetAdminUserIdAsync();
            for (var i = 0; i < Iterations; i++)
            {
                var con = await PostJsonAsync("/api/v1/consumables", new { name = $"QCR-CON-{RunId}-{i}", qty = 1, minAmt = 0, companyId = (Guid?)null });
                var conId = ExtractId(con.body);
                if (conId != null) created.Add(conId);
                var res = await FireTwoAsync(
                    () => PostJsonAsync($"/api/v1/consumables/{conId}/checkout", new { userId = adminId, quantity = 1, note = "race-A" }),
                    () => PostJsonAsync($"/api/v1/consumables/{conId}/checkout", new { userId = adminId, quantity = 1, note = "race-B" }));
                results.Add(new { iter = i, sA = res.s1, sB = res.s2, bodyA = res.b1, bodyB = res.b2 });
                _output.WriteLine($"[Consumable iter {i}] A={res.s1} B={res.s2}");
            }
            Record("consumable", results);
        }
        finally
        {
            // Consumables have no check-in semantics (checkout = consume) → the delete-guard
            // CONSUMABLE_HAS_CHECKOUTS is expected to block deletion; reported, not ignored.
            var rep = new CleanupReport();
            foreach (var conId in created) await TryDeleteAsync(rep, $"consumable {conId}", $"/api/v1/consumables/{conId}");
            ReportCleanup("consumable", rep);
        }
    }

    // =========================================================================
    // [LL-2b] Lý lịch lắp đặt — Archive vs Checkin trên CÙNG asset đang gắn vị trí
    // =========================================================================

    /// <summary>
    /// [LL-2b] Race Archive ⟷ Checkin trên cùng một asset ĐANG GẮN vị trí hệ thống. Cả hai thao tác đều
    /// xoá `SystemPositionId` và ĐÓNG dòng lý lịch đang mở; cả hai handler nay đều chạy trong transaction
    /// + khoá hàng <c>FOR UPDATE</c> trên asset (Archive được bọc ở LL-2), nên phải TUẦN TỰ hoá:
    /// <list type="bullet">
    ///   <item>không response nào ≥ 500 (không lọt 500 thô, không deadlock);</item>
    ///   <item>tối đa một thao tác thắng (thao tác sau trả 400 hợp lệ: ASSET_ALREADY_ARCHIVED / ASSET_NOT_CHECKED_OUT);</item>
    ///   <item>trạng thái cuối ∈ {Archived, Pending} — không còn Deployed (đã tháo khỏi vị trí);</item>
    ///   <item>asset KHÔNG còn xuất hiện trong danh sách asset của hệ thống (proxy qua API cho
    ///     <c>SystemPositionId == null</c> ⇒ không còn khoảng lắp đặt mở ⇒ bất biến giữ nguyên).</item>
    /// </list>
    /// Bất biến DB thật (SystemPositionId ⟺ đúng 1 dòng mở) được kiểm bằng SQL sau khi chạy — xem báo cáo.
    /// Fixture: asset/system/position để nguyên sau test. Asset tạo qua API luôn <c>IsConfirmed = true</c>
    /// nên delete-guard chặn thật là <c>ASSET_CONFIRMED_CANNOT_DELETE</c> (ASSET_HAS_INSTALLATION_HISTORY
    /// đứng sau trong thứ tự guard — LL-1) — báo cáo là BLOCKED, không bỏ qua.
    /// </summary>
    [ThrowawayDbFact]
    public async Task AssetInstallation_ArchiveAndCheckinConcurrently_InvariantHolds()
    {
        var results = new List<object>();
        string? locationId = null, systemId = null, positionId = null, assetId = null;
        try
        {
            var suffix = (int.Parse(RunId.Substring(3)) % 1000).ToString("D3"); // 3 chữ số cuối của mmss
            var loc = await PostJsonAsync("/api/v1/locations", new { name = $"QCR-LL2-LOC-{RunId}" });
            locationId = ExtractId(loc.body);

            var sys = await PostJsonAsync("/api/v1/system-infos", new { code = $"QCS-2026-{suffix}", name = $"QCR LL2 System {RunId}" });
            systemId = ExtractId(sys.body);
            var pos = await PostJsonAsync($"/api/v1/system-infos/{systemId}/positions", new { code = $"QCP-2026-{suffix}", name = $"QCR LL2 Position {RunId}" });
            positionId = ExtractId(pos.body);
            results.Add(new { step = "fixtures", loc = loc.status, sys = sys.status, pos = pos.status, positionId });

            // physical=false → asset KHÔNG auto-confirm (để delete-guard ASSET_HAS_INSTALLATION_HISTORY
            // là guard chặn thật, không bị ASSET_CONFIRMED_CANNOT_DELETE che trước).
            var asset = await PostJsonAsync("/api/v1/assets", new
            {
                assetTag = $"QCR-LL2-{RunId}",
                name = $"QCR LL2 Asset {RunId}",
                physical = false,
                requestable = false
            });
            assetId = ExtractId(asset.body);

            var mount = await PostJsonAsync($"/api/v1/assets/{assetId}/checkout",
                new { targetType = "SystemPosition", targetId = positionId, locationId, note = "LL-2b: gan vi tri" });
            results.Add(new { step = "mount", status = mount.status, body = mount.body });
            _output.WriteLine($"[LL-2b] mount asset {assetId} -> position {positionId}: HTTP {mount.status}");
            Assert.Equal(200, mount.status);

            // RACE: archive vs checkin, cùng asset, cùng thời điểm
            var res = await FireTwoAsync(
                () => PostJsonAsync($"/api/v1/assets/{assetId}/archive", new { locationId, note = "LL-2b race: archive" }),
                () => PostJsonAsync($"/api/v1/assets/{assetId}/checkin", new { locationId, note = "LL-2b race: checkin" }));
            results.Add(new
            {
                step = "race",
                sArchive = res.s1,
                bArchive = res.b1,
                sCheckin = res.s2,
                bCheckin = res.b2
            });
            Record("asset_ll2_archive_vs_checkin", results);
            _output.WriteLine($"[LL-2b race] archive={res.s1} checkin={res.s2}");
            _output.WriteLine($"  archive body: {Shorten(res.b1)}");
            _output.WriteLine($"  checkin body: {Shorten(res.b2)}");

            // (1) Không 500
            Assert.True(res.s1 < 500, $"archive trả HTTP {res.s1}: {Shorten(res.b1)}");
            Assert.True(res.s2 < 500, $"checkin trả HTTP {res.s2}: {Shorten(res.b2)}");
            // (2) Ít nhất một thao tác thành công (tuần tự hoá, không cùng hỏng)
            Assert.True(res.s1 == 200 || res.s2 == 200,
                $"không thao tác nào thành công: archive={res.s1}, checkin={res.s2}");

            // (3) Trạng thái cuối không còn Deployed
            var (getStatus, getBody) = await GetAsync($"/api/v1/assets/{assetId}");
            Assert.Equal(200, getStatus);
            using var doc = JsonDocument.Parse(getBody);
            var dto = doc.RootElement.GetProperty("data");
            var status = dto.GetProperty("status").GetString();
            _output.WriteLine($"[LL-2b] trang thai cuoi = {status}");
            Assert.True(status is "Archived" or "Pending", $"trạng thái cuối = {status} (mong đợi Archived/Pending)");

            // (4) Không còn gắn vị trí: asset biến mất khỏi danh sách asset của hệ thống
            var (sysStatus, sysBody) = await GetAsync($"/api/v1/systems/{systemId}/assets?pageSize=100");
            Assert.Equal(200, sysStatus);
            var stillMounted = ParseArray(sysBody)
                .Any(row => row.TryGetProperty("assetId", out var a) && a.GetString() == assetId
                            && row.TryGetProperty("systemPositionId", out var sp) && sp.ValueKind != JsonValueKind.Null);
            _output.WriteLine($"[LL-2b] con gan vi tri sau race = {stillMounted}");
            Assert.False(stillMounted, "asset vẫn còn SystemPositionId sau race (bất biến lý lịch có thể đã lệch)");
        }
        finally
        {
            var rep = new CleanupReport();
            if (assetId != null) await TryDeleteAsync(rep, $"asset {assetId}", $"/api/v1/assets/{assetId}");
            ReportCleanup("asset_ll2", rep);
            _output.WriteLine("[LL-2b] fixture giu lai co chu dich de kiem bat bien bang SQL (system/position/location)");
        }
    }
}
