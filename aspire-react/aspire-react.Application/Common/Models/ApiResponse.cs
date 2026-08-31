namespace aspire_react.Server.Application.Common.Models;

public class ApiResponse<T>
{
    public string Status { get; set; } = "success";
    public string Message { get; set; } = string.Empty;
    public T? Data { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    public static ApiResponse<T> Success(T data, string message = "Operation successful.")
        => new() { Status = "success", Data = data, Message = message };

    public static ApiResponse<T> Error(string message, string? errorCode = null, object? details = null)
        => new() { Status = "error", Message = message };
}

public class PaginatedResponse<T>
{
    public string Status { get; set; } = "success";
    public List<T> Data { get; set; } = new();
    public PaginationMetadata Pagination { get; set; } = new();
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

public class PaginationMetadata
{
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalItems { get; set; }
    public int TotalPages => (int)Math.Ceiling((double)TotalItems / PageSize);
    public bool HasNextPage => Page * PageSize < TotalItems;
    public bool HasPreviousPage => Page > 1;
}