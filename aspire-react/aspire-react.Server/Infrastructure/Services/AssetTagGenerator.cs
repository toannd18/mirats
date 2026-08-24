using aspire_react.Server.Domain.Entities;
using aspire_react.Server.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using System.Text.RegularExpressions;

namespace aspire_react.Server.Infrastructure.Services;

/// <summary>
/// Default implementation of <see cref="IAssetTagGenerator"/> (Task ASSET-TAG-AUTO).
/// Format syntax: a free string with tokens <c>{COMPANY}</c> (company Code, "NOCO" for company-less),
/// <c>{YYYY}</c> (4-digit year) and <c>{SEQ:n}</c> (sequence padded to n digits, n=1..9); any other
/// characters are literal. Example: "AST-{COMPANY}-{YYYY}-{SEQ:3}" → "AST-ABC-2026-001".
/// The {COMPANY} token keeps tags GLOBALLY unique even though the sequence counter is per-company,
/// satisfying the global unique constraint on AssetTag (Task L) while counters stay independent per
/// (CompanyId, Year). The sequence counter is incremented inside a transaction + FOR UPDATE
/// (Task O/O-FIX race pattern) so concurrent creates never collide.
/// </summary>
public class AssetTagGenerator : IAssetTagGenerator
{
    public const string DefaultFormat = "AST-{COMPANY}-{YYYY}-{SEQ:3}";
    public const string FormatSettingKey = "AssetTagFormat";
    /// <summary>Description stamped on the SystemSetting row when it is first created (shared by
    /// SetFormatAsync and the SystemConfigController write path so the text never diverges).</summary>
    public const string FormatDescription = "Format tự sinh Mã tài sản (Asset Tag). Hỗ trợ {COMPANY} (mã công ty, NOCO nếu không có), {YYYY} (năm 4 số) và {SEQ:n} (số thứ tự đệm n chữ số). Nên giữ {COMPANY} để mã unique toàn hệ thống.";
    /// <summary>Reserved code for company-less (floater) assets.</summary>
    public const string NoCompanyCode = "NOCO";

    private static readonly Regex SeqTokenRegex = new(@"\{SEQ:(\d)\}", RegexOptions.Compiled);
    private static readonly Regex CompanyTokenRegex = new(@"\{COMPANY\}", RegexOptions.Compiled);

    /// <summary>Validates an asset-tag format candidate (SEC-FIX A1: extracted so the controller's
    /// single-transaction write path validates identically to <see cref="SetFormatAsync"/>).
    /// Throws <see cref="ArgumentException"/> with the same messages SetFormatAsync has always used.</summary>
    public static void ValidateFormat(string? format)
    {
        var trimmed = format?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed)) throw new ArgumentException("Format không được để trống.");
        if (!SeqTokenRegex.IsMatch(trimmed))
            throw new ArgumentException("Format phải chứa token {SEQ:n} (VD {SEQ:3}).");
    }

    private readonly AppDbContext _context;

    public AssetTagGenerator(AppDbContext context)
    {
        _context = context;
    }

    public async Task<string> ResolveAssetTagAsync(string? explicitTag, Guid? companyId, CancellationToken ct = default)
    {
        var tag = explicitTag?.Trim();
        if (!string.IsNullOrWhiteSpace(tag)) return tag;

        var format = await GetFormatAsync(ct);
        var year = DateTime.UtcNow.Year;

        // Resolve the company Code for {COMPANY} (NOCO for company-less floaters).
        string companyCode = NoCompanyCode;
        if (companyId.HasValue)
        {
            companyCode = await _context.Companies.AsNoTracking()
                .Where(c => c.Id == companyId.Value)
                .Select(c => c.Code)
                .FirstOrDefaultAsync(ct);
            if (string.IsNullOrWhiteSpace(companyCode)) companyCode = NoCompanyCode;
        }

        // Transaction + FOR UPDATE: read-and-increment the per-(company, year) counter atomically.
        // Npgsql's retrying execution strategy requires the transaction to run inside
        // CreateExecutionStrategy (Task O/O-FIX convention).
        var strategy = _context.Database.CreateExecutionStrategy();
        long seq = 0;
        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _context.Database.BeginTransactionAsync(ct);
            // Explicit typed parameter so Npgsql resolves the NULL companyId (raw SQL with an
            // interpolated null Guid cannot infer its type → PostgresException 42P18).
            var companyParam = new NpgsqlParameter("companyId", NpgsqlDbType.Uuid)
            {
                Value = (object?)companyId ?? DBNull.Value
            };
            var yearParam = new NpgsqlParameter("year", NpgsqlDbType.Integer)
            {
                Value = year
            };
            var counter = await _context.AssetTagCounters
                .FromSqlRaw(@"
                    SELECT * FROM public.""asset_tag_counters""
                    WHERE ""Year"" = @year
                      AND ((""CompanyId"" IS NULL AND @companyId IS NULL) OR (""CompanyId"" = @companyId))
                    FOR UPDATE",
                    yearParam, companyParam)
                .FirstOrDefaultAsync(ct);

            if (counter == null)
            {
                counter = new AssetTagCounter { CompanyId = companyId, Year = year, CurrentSeq = 0 };
                _context.AssetTagCounters.Add(counter);
            }

            counter.CurrentSeq += 1;
            counter.UpdatedAt = DateTime.UtcNow;
            seq = counter.CurrentSeq;
            await _context.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        });

        return Render(format, year, seq, companyCode);
    }

    public async Task<string> GetFormatAsync(CancellationToken ct = default)
    {
        var setting = await _context.SystemSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Key == FormatSettingKey, ct);
        return string.IsNullOrWhiteSpace(setting?.Value) ? DefaultFormat : setting!.Value;
    }

    public async Task SetFormatAsync(string format, Guid? updatedBy, CancellationToken ct = default)
    {
        ValidateFormat(format);
        var trimmed = format!.Trim();

        var setting = await _context.SystemSettings.FirstOrDefaultAsync(s => s.Key == FormatSettingKey, ct);
        if (setting == null)
        {
            setting = new SystemSetting
            {
                Key = FormatSettingKey,
                Value = trimmed,
                Description = FormatDescription,
                UpdatedBy = updatedBy
            };
            _context.SystemSettings.Add(setting);
        }
        else
        {
            setting.Value = trimmed;
            setting.UpdatedBy = updatedBy;
            setting.UpdatedAt = DateTime.UtcNow;
        }
        await _context.SaveChangesAsync(ct);
    }

    /// <summary>Renders the format with the given year, sequence and company code.</summary>
    public static string Render(string format, int year, long seq, string companyCode)
    {
        var result = format.Replace("{YYYY}", year.ToString("D4"));
        result = CompanyTokenRegex.Replace(result, companyCode);
        return SeqTokenRegex.Replace(result, m =>
        {
            var width = int.Parse(m.Groups[1].Value);
            return seq.ToString($"D{width}");
        });
    }
}
