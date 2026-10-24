namespace KSeFGateway.Api.Models;

public record ApiResponse<T>(bool Success, T? Data, string? Error = null)
{
    public static ApiResponse<T> Ok(T data) => new(true, data);
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? CorrelationId { get; init; }
    public static ApiResponse<T> Fail(string error) => new(false, default, error) { CorrelationId = ErrorCorrelation.Current.Value };
}

public record ApiResponse(bool Success, object? Data, string? Error = null)
{
    public static ApiResponse Ok(object data) => new(true, data);
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? CorrelationId { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public object? Errors { get; init; }
    public static ApiResponse Fail(string error) => new(false, null, error) { CorrelationId = ErrorCorrelation.Current.Value };
}

// Scoped by ErrorHandlingMiddleware; async flows keep simultaneous requests isolated.
internal static class ErrorCorrelation
{
    internal static readonly AsyncLocal<string?> Current = new();
}
