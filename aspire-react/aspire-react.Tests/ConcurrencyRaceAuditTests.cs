using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Xunit;
using Xunit.Abstractions;

namespace aspire_react.Tests;

/// <summary>
/// Task O — EMPIRICAL audit (no fix): fire 2 truly-concurrent checkout/allocate requests at the SAME
/// resource with only "1 remaining" unit/seat, on the REAL Aspire stack (real Postgres). Records whether
/// both succeed (overcommit / lost-update) so we can confirm the race in practice, not just from code reading.
///
/// These tests REQUIRE the Aspire stack to be running (server on localhost:5428) and are timing-dependent,
/// so they are tagged Category=Concurrency and are NOT part of the fast CI suite (run explicitly with
/// `dotnet test --filter "Category=Concurrency"` while the stack is up). They intentionally do not assert
/// a pass/fail on the race outcome — they record evidence for the audit report.
/// </summary>
[Trait("Category", "Concurrency")]
public class ConcurrencyRaceAuditTests
{
    private readonly ITestOutputHelper _output;
    public ConcurrencyRaceAuditTests(ITestOutputHelper output) => _output = output;

    private const string BaseUrl = "http://localhost:5428";
    private const string KcTokenUrl = "https://localhost:8080/realms/aspire-react/protocol/openid-connect/token";
    private const string AdminUserId = "eb34917f-843f-4f4e-8651-d505cd317824"; // local admin id
    private const int Iterations = 5;

    // [SECRET-ROTATE 2026-08-29] The app-admin password is no longer hard-coded here (the old
    // value is public in git history). It resolves, in order:
    //   1. environment variable MIRATS_TEST_ADMIN_PASSWORD
    //   2. repo-root file `.mirats-test-admin-password` (gitignored — local dev convenience)
    // When rotating: Keycloak Admin API → reset-password on the realm 'aspire-react' admin
    // user, then update that file / env var. NEVER commit the real value.
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
        using var handler = new HttpClientHandler { ServerCertificateCustomValidationCallback = (_, _, _, _) => true };
        using var kc = new HttpClient(handler);
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["client_id"] = "frontend",
            ["username"] = "admin",
            ["password"] = AdminPassword
        });
        var resp = await kc.PostAsync(KcTokenUrl, form);
        var json = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        _token = json.RootElement.GetProperty("access_token").GetString();
        return _token!;
    }

    private async Task<(int status, string body)> PostJsonAsync(string path, object body)
    {
        var token = await GetTokenAsync();
        using var req = new HttpRequestMessage(HttpMethod.Post, path);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        var resp = await _http.SendAsync(req);
        return ((int)resp.StatusCode, await resp.Content.ReadAsStringAsync());
    }

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

    [Fact]
    public async Task LicenseSeat_OneFreeSeat_TwoConcurrentCheckouts()
    {
        var results = new List<object>();
        var catId = await PostJsonAsync("/api/v1/categories", new { name = "QCR-LIC", categoryType = "License" });
        var catIdParsed = ExtractId(catId.body);

        for (var i = 0; i < Iterations; i++)
        {
            var lic = await PostJsonAsync("/api/v1/licenses", new { name = $"QCR-LIC-{i}", seats = 1, categoryId = catIdParsed, companyId = (Guid?)null });
            var licId = ExtractId(lic.body);
            var res = await FireTwoAsync(
                () => PostJsonAsync($"/api/v1/licenses/{licId}/checkout", new { targetType = "User", targetId = AdminUserId, note = "race-A" }),
                () => PostJsonAsync($"/api/v1/licenses/{licId}/checkout", new { targetType = "User", targetId = AdminUserId, note = "race-B" }));
            results.Add(new { iter = i, sA = res.s1, sB = res.s2, bodyA = res.b1, bodyB = res.b2 });
            _output.WriteLine($"[License iter {i}] A={res.s1} B={res.s2}");
        }
        Record("license", results);
    }

    [Fact]
    public async Task Accessory_OneRemaining_TwoConcurrentCheckouts()
    {
        var results = new List<object>();
        for (var i = 0; i < Iterations; i++)
        {
            var acc = await PostJsonAsync("/api/v1/accessories", new { name = $"QCR-ACC-{i}", qty = 1, minAmt = 0, companyId = (Guid?)null });
            var accId = ExtractId(acc.body);
            var res = await FireTwoAsync(
                () => PostJsonAsync($"/api/v1/accessories/{accId}/checkout", new { checkoutType = "User", targetId = AdminUserId, quantity = 1, note = "race-A" }),
                () => PostJsonAsync($"/api/v1/accessories/{accId}/checkout", new { checkoutType = "User", targetId = AdminUserId, quantity = 1, note = "race-B" }));
            results.Add(new { iter = i, sA = res.s1, sB = res.s2, bodyA = res.b1, bodyB = res.b2 });
            _output.WriteLine($"[Accessory iter {i}] A={res.s1} B={res.s2}");
        }
        Record("accessory", results);
    }

    [Fact]
    public async Task Component_Bulk_OneRemaining_TwoConcurrentAllocates()
    {
        var results = new List<object>();
        var cat = await PostJsonAsync("/api/v1/categories", new { name = "QCR-COMP", categoryType = "Component" });
        var catId = ExtractId(cat.body);
        var comp = await PostJsonAsync("/api/v1/companies", new { name = "QCR-CO", parentId = (Guid?)null });
        var compId = ExtractId(comp.body);

        for (var i = 0; i < Iterations; i++)
        {
            var c = await PostJsonAsync("/api/v1/components", new { name = $"QCR-COMP-{i}", trackingType = "Bulk", qty = 1, minAmt = 0, categoryId = catId, companyId = compId });
            var compId2 = ExtractId(c.body);
            var asset = await PostJsonAsync("/api/v1/assets", new { assetTag = $"QCR-AST-{i}", name = $"QCR Asset {i}", companyId = compId, physical = true, requestable = false });
            var assetId = ExtractId(asset.body);
            var res = await FireTwoAsync(
                () => PostJsonAsync($"/api/v1/components/{compId2}/assign", new { assetId = assetId, assignedQty = 1, note = "race-A" }),
                () => PostJsonAsync($"/api/v1/components/{compId2}/assign", new { assetId = assetId, assignedQty = 1, note = "race-B" }));
            results.Add(new { iter = i, sA = res.s1, sB = res.s2, bodyA = res.b1, bodyB = res.b2 });
            _output.WriteLine($"[Component iter {i}] A={res.s1} B={res.s2}");
        }
        Record("component", results);
    }

    [Fact]
    public async Task Consumable_OneRemaining_TwoConcurrentCheckouts()
    {
        var results = new List<object>();
        for (var i = 0; i < Iterations; i++)
        {
            var con = await PostJsonAsync("/api/v1/consumables", new { name = $"QCR-CON-{i}", qty = 1, minAmt = 0, companyId = (Guid?)null });
            var conId = ExtractId(con.body);
            var res = await FireTwoAsync(
                () => PostJsonAsync($"/api/v1/consumables/{conId}/checkout", new { userId = AdminUserId, quantity = 1, note = "race-A" }),
                () => PostJsonAsync($"/api/v1/consumables/{conId}/checkout", new { userId = AdminUserId, quantity = 1, note = "race-B" }));
            results.Add(new { iter = i, sA = res.s1, sB = res.s2, bodyA = res.b1, bodyB = res.b2 });
            _output.WriteLine($"[Consumable iter {i}] A={res.s1} B={res.s2}");
        }
        Record("consumable", results);
    }

    private static string? ExtractId(string body)
    {
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.TryGetProperty("data", out var d)
               && d.TryGetProperty("id", out var id)
            ? id.ToString()
            : null;
    }
}
