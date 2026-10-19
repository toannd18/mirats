using System.Text.RegularExpressions;
using aspire_react.Server.Application.Common.Interfaces;
using MediatR;
using Xunit;

namespace aspire_react.Tests;

/// <summary>
/// [AUDIT-LOG B2] Test kiến trúc kiểu BÁNH CÓC cho `LogMeta.changes`:
/// mọi command Update* (kể cả Close/Inspect/Reopen — nhóm chuyển trạng thái) phải có dấu hiệu ghi
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
/// Phạm vi quét: các type request trong assembly Application có tên bắt đầu bằng Update/Close/Inspect/
/// Reopen, rồi soi file khai báo tương ứng (heuristic cấp FILE: file chứa `ActionLogChanges` hoặc
/// chuỗi `changes`). Heuristic này đủ để chặn hồi quy "quên LogMeta", nhưng nếu tách handler sang file
/// khác thì phải cập nhật test này.
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
        // [B3] UpdateConsumable: LogMeta đang SAI schema (thiếu wrapper "changes") + BUG-P minAmt.
        "UpdateConsumableCommand",
        // [B3] Log nằm trong ComponentAllocationService ({before, after}) — chưa bọc "changes".
        "UpdateComponentUnitStatusCommand",
        // Chuyển trạng thái (không phải field-update) — LogMeta không yêu cầu old/new.
        "CloseMaintenanceCommand",
        "InspectMaintenanceCommand",
        "ReopenMaintenanceCommand",
    };

    private static readonly string[] ScannedPrefixes = { "Update", "Close", "Inspect", "Reopen" };

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
            "Ngoại lệ không còn là command Update*/Close/Inspect/Reopen (đã đổi tên/xóa?) — cập nhật danh sách Exempt: "
            + string.Join(", ", unknownExemptions));

        Assert.True(staleExemptions.Count == 0,
            "Các command sau ĐÃ có LogMeta.changes nhưng vẫn nằm trong danh sách ngoại lệ — XÓA khỏi Exempt: "
            + string.Join(", ", staleExemptions));

        Assert.True(missing.Count == 0,
            "Command Update* thiếu LogMeta.changes (dùng ActionLogChanges hoặc thêm vào Exempt kèm lý do): "
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
