using System.Text.RegularExpressions;
using aspire_react.Server.Application.Common.Interfaces;
using MediatR;
using Xunit;

namespace aspire_react.Tests;

/// <summary>
/// [AUDIT-LOG B2/B3] Test kiến trúc kiểu BÁNH CÓC cho `LogMeta.changes`:
/// mọi request GHI DỮ LIỆU trong assembly Application — tên bắt đầu <c>Update/Upsert/Set/Edit/Patch</c>
/// (field update) hoặc <c>Close/Inspect/Reopen</c> (chuyển trạng thái) — phải có dấu hiệu ghi
/// `changes`, TRỪ danh sách ngoại lệ ghi tường minh dưới đây.
///
/// <para>
/// Bánh cóc 2 chiều:
/// (1) command mới/đang vi phạm mà không nằm trong <see cref="Exempt"/> ⇒ FAIL;
/// (2) ngoại lệ đã được sửa (file đã có changes) ⇒ FAIL với yêu cầu XÓA khỏi danh sách;
/// (3) ngoại lệ trỏ tới command không còn tồn tại ⇒ FAIL.
/// </para>
///
/// <para>
/// <b>Hạn chế đã biết (⇒ backlog B7)</b>: phạm vi quét chỉ là các COMMAND, và phép kiểm là heuristic
/// cấp FILE (file chứa `ActionLogChanges` hoặc chuỗi `changes`) — không kiểm được (a) log viết TRONG
/// SERVICE (VD <c>ComponentAllocationService</c>) không qua command, (b) command ghi log nhưng thiếu
/// field / sai giá trị old. B7 sẽ thay bằng kiểm tra theo HÀNH VI (chạy command thật rồi soi
/// `action_logs`) thay vì tìm chữ "changes" trong file.
/// </para>
/// </summary>
public class ActionLogMetaRatchetTests
{
    /// <summary>
    /// Ngoại lệ TƯỜNG MINH. Mỗi mục phải ghi rõ lý do + bước sẽ xóa nó.
    /// Khi sửa xong ⇒ XÓA khỏi danh sách này (test sẽ báo nếu để lại).
    /// </summary>
    private static readonly HashSet<string> Exempt = new(StringComparer.Ordinal)
    {
        // [B7] Command chỉ DELEGATE — log nằm trong ComponentAllocationService.SetUnitStatusAsync
        // (đã bọc "changes" từ B3), file command không có dấu hiệu nào để soi.
        "UpdateComponentUnitStatusCommand",
    };

    /// <summary>Các tiền tố "ghi dữ liệu" được quét (mở rộng ở B3 — trước đó chỉ Update/Close/Inspect/Reopen).</summary>
    private static readonly string[] ScannedPrefixes =
        { "Update", "Upsert", "Set", "Edit", "Patch", "Close", "Inspect", "Reopen" };

    /// <summary>Canary: nếu phạm vi quét bị thu hẹp/hỏng, các tên này phải còn xuất hiện.</summary>
    private static readonly string[] ScopeCanaries =
    {
        "UpdateAssetCommand", "SetAssetTagFormatCommand", "SetPasskeysEnabledCommand",
        "UpsertCampaignResultCommand", "CloseMaintenanceCommand"
    };

    [Fact]
    public void EveryUpdateLikeCommand_HasLogMetaChanges_ExceptExplicitExemptions()
    {
        var assembly = typeof(ILoggableCommand<>).Assembly;
        var commands = assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && typeof(IBaseRequest).IsAssignableFrom(t))
            .Select(t => t.Name)
            .Where(n => ScannedPrefixes.Any(p => n.StartsWith(p, StringComparison.Ordinal)))
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        Assert.NotEmpty(commands);
        foreach (var canary in ScopeCanaries)
            Assert.True(commands.Contains(canary),
                $"Phạm vi quét bánh cóc thiếu '{canary}' — kiểm tra lại ScannedPrefixes/bộ lọc IBaseRequest.");

        var sources = LoadApplicationSources();
        var missing = new List<string>();
        var staleExemptions = new List<string>();

        foreach (var name in commands)
        {
            var declaration = new Regex(
                $@"\b(record|class)\s+{Regex.Escape(name)}\b", RegexOptions.CultureInvariant);
            var pair = sources.FirstOrDefault(kv => declaration.IsMatch(kv.Value));
            Assert.False(string.IsNullOrEmpty(pair.Key),
                $"Không tìm thấy file khai báo cho '{name}' trong aspire-react.Application — không thể kiểm chứng LogMeta.");

            var hasChanges = HasChangesMarker(pair.Value);
            if (Exempt.Contains(name))
            {
                if (hasChanges)
                    staleExemptions.Add(name);
            }
            else if (!hasChanges)
            {
                missing.Add(name);
            }
        }

        var unknownExemptions = Exempt.Where(e => !commands.Contains(e)).ToList();

        Assert.True(unknownExemptions.Count == 0,
            "Ngoại lệ không còn là command thuộc phạm vi quét (đã đổi tên/xóa?) — cập nhật danh sách Exempt: "
            + string.Join(", ", unknownExemptions));

        Assert.True(staleExemptions.Count == 0,
            "Các command sau ĐÃ có LogMeta.changes nhưng vẫn nằm trong danh sách ngoại lệ — XÓA khỏi Exempt: "
            + string.Join(", ", staleExemptions));

        Assert.True(missing.Count == 0,
            "Command ghi dữ liệu thiếu LogMeta.changes (dùng ActionLogChanges hoặc thêm vào Exempt kèm lý do): "
            + string.Join(", ", missing));
    }

    /// <summary>Dấu hiệu "có ghi changes": helper dùng chung, hoặc JSON `changes` viết tay.</summary>
    private static bool HasChangesMarker(string source)
        => source.Contains("ActionLogChanges", StringComparison.Ordinal)
           || source.Contains("changes", StringComparison.Ordinal);

    private static Dictionary<string, string> LoadApplicationSources()
    {
        var root = FindApplicationSourceRoot();
        var sources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            // Bỏ file sinh tự động / build output (nếu có).
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
                continue;
            sources[file] = File.ReadAllText(file);
        }

        Assert.NotEmpty(sources);
        return sources;
    }

    private static string FindApplicationSourceRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "aspire-react.Application");
            if (Directory.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            "Không tìm thấy thư mục source 'aspire-react.Application' từ " + AppContext.BaseDirectory
            + " — test kiến trúc cần chạy trong repo (không chạy từ thư mục publish).");
    }
}
