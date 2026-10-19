using System.Collections;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace aspire_react.Server.Application.Common.ActionLogs;

/// <summary>
/// [AUDIT-LOG B2] Builder dùng chung cho `LogMeta` của hành động UPDATE — thay cho việc mỗi command
/// tự serialize `new { changes = new { field = new { old, @new } } }`.
///
/// <para>
/// Hợp đồng (workflow §3.2): kết quả là JSON `{"changes":{"<field>":{"old":…,"new":…}}}` với tên
/// field **snake_case**, CHỈ chứa field thực sự thay đổi; nếu không có field nào thay đổi thì
/// <see cref="ToLogMeta"/> trả <c>null</c> (caller truyền null ⇒ log không có `changes`).
/// </para>
///
/// <para>
/// Cách dùng (thứ tự BẮT BUỘC — <see cref="Track"/> TRƯỚC khi gán, <see cref="Set"/> SAU khi gán):
/// <code>
/// var changes = new ActionLogChanges()
///     .Track("title", m.Title)
///     .Track("cost", m.Cost);
/// if (request.Title is not null) m.Title = request.Title;
/// if (request.Cost.HasValue) m.Cost = request.Cost.Value;
/// var logMeta = changes.Set("title", m.Title).Set("cost", m.Cost).ToLogMeta();
/// </code>
/// Gọi <see cref="Set"/> cho field chưa <see cref="Track"/> sẽ NÉM ngay (fail-fast: quên chụp giá
/// trị cũ là lớp lỗi BUG-P — nhật ký ghi "old" sai/không có).
/// </para>
///
/// <para>
/// Trường NHẠY CẢM (serial, số giấy tờ…): dùng <see cref="TrackSensitive"/>/<see cref="SetSensitive"/>
/// — kết quả chỉ ghi <c>{"<field>":{"changed":true}}</c>, KHÔNG bao giờ chứa giá trị cũ/mới. Giá trị
/// vẫn được giữ trong bộ nhớ để so sánh "có đổi hay không" (không serialize).
/// </para>
///
/// <para>
/// So sánh: dùng <see cref="object.Equals(object,object)"/> cho giá trị vô hướng; với tập hợp
/// (List/array — VD danh sách assignee) thì so **không phân biệt thứ tự** để tập hợp chỉ đổi thứ tự
/// KHÔNG bị coi là thay đổi.
/// </para>
/// </summary>
public sealed class ActionLogChanges
{
    /// <summary>Enum serialize dạng chuỗi (đọc được trong nhật ký, khớp `JsonStringEnumConverter` toàn hệ thống).</summary>
    private static readonly JsonSerializerOptions MetaJson = new()
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly Dictionary<string, object?> _old = new(StringComparer.Ordinal);
    private readonly Dictionary<string, object?> _new = new(StringComparer.Ordinal);
    private readonly HashSet<string> _sensitive = new(StringComparer.Ordinal);
    private readonly List<string> _order = new();

    /// <summary>Chụp giá trị CŨ của field — gọi TRƯỚC khi gán giá trị mới.</summary>
    public ActionLogChanges Track(string field, object? oldValue)
    {
        var name = Register(field);
        _old[name] = oldValue;
        return this;
    }

    /// <summary>Như <see cref="Track"/> nhưng field nhạy cảm: giá trị KHÔNG bao giờ được serialize.</summary>
    public ActionLogChanges TrackSensitive(string field, object? oldValue)
    {
        Track(field, oldValue);
        _sensitive.Add(ToSnakeCase(field));
        return this;
    }

    /// <summary>Ghi giá trị MỚI của field — gọi SAU khi đã gán (field phải đã được Track).</summary>
    public ActionLogChanges Set(string field, object? newValue)
    {
        var name = Register(field);
        if (!_old.ContainsKey(name))
            throw new InvalidOperationException(
                $"ActionLogChanges: field '{name}' phải được Track() (giá trị cũ) TRƯỚC khi Set() (giá trị mới).");
        _new[name] = newValue;
        return this;
    }

    /// <summary>Như <see cref="Set"/> nhưng field nhạy cảm: giá trị KHÔNG bao giờ được serialize.</summary>
    public ActionLogChanges SetSensitive(string field, object? newValue)
    {
        Set(field, newValue);
        _sensitive.Add(ToSnakeCase(field));
        return this;
    }

    /// <summary>Số field thực sự thay đổi.</summary>
    public int Count => ChangedFields().Count;

    /// <summary>Có ít nhất 1 field thay đổi?</summary>
    public bool HasChanges => ChangedFields().Count > 0;

    /// <summary>Tên (snake_case) các field thực sự thay đổi, theo thứ tự Track.</summary>
    public IReadOnlyList<string> ChangedFields()
    {
        var changed = new List<string>();
        foreach (var field in _order)
        {
            if (!_new.ContainsKey(field))
                continue; // Set chưa được gọi ⇒ coi như không tham gia so sánh
            if (!ValuesEqual(_old[field], _new[field]))
                changed.Add(field);
        }
        return changed;
    }

    /// <summary>
    /// JSON `{"changes":{…}}` đã lọc field không đổi; trả <c>null</c> khi không có thay đổi
    /// (caller để LogMeta = null). Field nhạy cảm chỉ còn <c>{"changed":true}</c>.
    /// </summary>
    public string? ToLogMeta()
    {
        var changed = ChangedFields();
        if (changed.Count == 0)
            return null;

        var map = new Dictionary<string, object?>(changed.Count, StringComparer.Ordinal);
        foreach (var field in changed)
        {
            map[field] = _sensitive.Contains(field)
                ? new { changed = true }
                : new { old = _old[field], @new = _new[field] };
        }

        return JsonSerializer.Serialize(new { changes = map }, MetaJson);
    }

    /// <summary>camelCase/PascalCase → snake_case (đã snake_case thì giữ nguyên).</summary>
    public static string ToSnakeCase(string field)
    {
        if (string.IsNullOrEmpty(field))
            return field;

        var sb = new StringBuilder(field.Length + 4);
        for (var i = 0; i < field.Length; i++)
        {
            var c = field[i];
            if (char.IsUpper(c))
            {
                if (i > 0 && (char.IsLower(field[i - 1]) || char.IsDigit(field[i - 1])))
                    sb.Append('_');
                sb.Append(char.ToLowerInvariant(c));
            }
            else
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }

    private string Register(string field)
    {
        var name = ToSnakeCase(field);
        if (!_order.Contains(name))
            _order.Add(name);
        return name;
    }

    private static bool ValuesEqual(object? a, object? b)
    {
        if (ReferenceEquals(a, b))
            return true;
        if (a is null || b is null)
            return false;
        if (a is string || b is string)
            return a is string sa && b is string sb && string.Equals(sa, sb, StringComparison.Ordinal);

        // Tập hợp (List/array — VD assignees): so không phân biệt thứ tự.
        if (a is IEnumerable ea && b is IEnumerable eb)
        {
            var la = ea.Cast<object?>().ToList();
            var lb = eb.Cast<object?>().ToList();
            if (la.Count != lb.Count)
                return false;
            var remaining = new List<object?>(lb);
            foreach (var item in la)
            {
                var idx = remaining.FindIndex(x => ValuesEqual(item, x));
                if (idx < 0)
                    return false;
                remaining.RemoveAt(idx);
            }
            return true;
        }

        return a.Equals(b);
    }
}
