namespace KSeFGateway.Api.Models;

public record ApiResponse<T>(bool Success, T? Data, string? Error = null)
{
    public static ApiResponse<T> Ok(T data) => new(true, data);
    public static ApiResponse<T> Fail(string error) => new(false, default, error);
}

public record ApiResponse(bool Success, object? Data, string? Error = null)
{
    public static ApiResponse Ok(object data) => new(true, data);
    public static ApiResponse Fail(string error) => new(false, null, error);
}

public record HealthResponse(
    string Status,
    int DiscoveredEndpoints,
    string KsefEnvironment,
    bool Authenticated,
    DateTimeOffset? TokenExpiresAt
);
