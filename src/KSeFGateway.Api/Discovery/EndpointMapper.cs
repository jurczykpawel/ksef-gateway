using System.Reflection;
using System.Text.Json;
using KSeF.Client.Core.Interfaces.Clients;
using KSeFGateway.Api.Auth;
using KSeFGateway.Api.Middleware;
using KSeFGateway.Api.Models;
using System.Text.Json.Serialization.Metadata;

namespace KSeFGateway.Api.Discovery;

public static class EndpointMapper
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static void MapDiscoveredEndpoints(
        this WebApplication app,
        IReadOnlyList<EndpointMetadata> endpoints)
    {
        foreach (var endpoint in endpoints)
        {
            app.MapPost(endpoint.Route, CreateHandler(endpoint))
                .WithTags(endpoint.Tag)
                .WithName($"{endpoint.GroupName}_{endpoint.MethodName}");
        }
    }

    private static Delegate CreateHandler(EndpointMetadata endpoint)
    {
        return async (HttpContext context) => await EndpointErrorHandling.Guard(async () =>
        {
            var pool = context.RequestServices.GetRequiredService<TokenPool>();
            var ctxProvider = context.RequestServices.GetRequiredService<ContextProvider>();
            var ksefClient = context.RequestServices.GetRequiredService<IKSeFClient>();

            // For raw SDK endpoints: use X-KSeF-NIP header or default context
            var nip = ContextResolver.ResolveNip(context, ctxProvider);
            if (nip is null)
                return Results.Json(ApiResponse.Fail("No KSeF context. Set X-KSeF-NIP header or configure default."), statusCode: 400);

            var accessToken = await pool.GetAccessTokenAsync(nip, context.RequestAborted);
            if (accessToken is null)
                return Results.Json(ApiResponse.Fail($"Not authenticated with KSeF for NIP {nip}"), statusCode: 503);

            object?[] args;
            try
            {
                args = await BuildArguments(endpoint, context, accessToken);
            }
            catch (JsonException)
            {
                return Results.Json(ApiResponse.Fail("Invalid JSON request body or parameter type."), statusCode: 400);
            }
            catch (BadHttpRequestException ex)
            {
                return Results.Json(ApiResponse.Fail(ex.Message), statusCode: ex.StatusCode);
            }

            var result = endpoint.SdkMethod.Invoke(ksefClient, args);

            // Await if Task
            if (result is Task task)
            {
                await task;

                if (endpoint.ReturnType is not null)
                {
                    // Get the Result property from Task<T>
                    var resultProp = task.GetType().GetProperty("Result");
                    var data = resultProp?.GetValue(task);
                    return Results.Json(ApiResponse.Ok(data!));
                }

                return Results.Json(new ApiResponse(true, null));
            }

            return Results.Json(ApiResponse.Ok(result!));
        }, context.RequestServices.GetRequiredService<ILogger<Program>>(), $"{endpoint.GroupName}/{endpoint.MethodName} failed");
    }

    private static async Task<object?[]> BuildArguments(
        EndpointMetadata endpoint,
        HttpContext context,
        string accessToken)
    {
        var sdkParams = endpoint.SdkMethod.GetParameters();
        var args = new object?[sdkParams.Length];

        // ContentLength can be unknown (chunked). Read once, including empty bodies.
        JsonElement? body = null;
        if (endpoint.BusinessParameters.Count > 0)
        {
            using var reader = new StreamReader(context.Request.Body, leaveOpen: true);
            var json = await reader.ReadToEndAsync(context.RequestAborted);
            if (!string.IsNullOrWhiteSpace(json))
            {
                body = JsonSerializer.Deserialize<JsonElement>(json, JsonOptions);
                if (body.Value.ValueKind is not (JsonValueKind.Object or JsonValueKind.Null))
                    throw new BadHttpRequestException("JSON request body must be an object.");
            }
        }
        var allowWholeBody = endpoint.BusinessParameters.Count == 1 &&
            JsonSerializerOptions.Default.GetTypeInfo(endpoint.BusinessParameters[0].ParameterType).Kind == JsonTypeInfoKind.Object;

        for (int i = 0; i < sdkParams.Length; i++)
        {
            var param = sdkParams[i];

            if (param.ParameterType == typeof(CancellationToken))
            {
                args[i] = context.RequestAborted;
            }
            else if (param.Name == "accessToken" && param.ParameterType == typeof(string))
            {
                args[i] = accessToken;
            }
            else
            {
                args[i] = ResolveParameter(param, body, allowWholeBody);
            }
        }

        return args;
    }

    private static object? ResolveParameter(ParameterInfo param, JsonElement? body, bool allowWholeBody)
    {
        if (body is { ValueKind: JsonValueKind.Object } value)
        {
            if (value.TryGetProperty(param.Name!, out var property))
            {
                if (property.ValueKind != JsonValueKind.Null || param.HasDefaultValue)
                    return property.Deserialize(param.ParameterType, JsonOptions);
            }
            else if (allowWholeBody)
            {
                return value.Deserialize(param.ParameterType, JsonOptions);
            }
        }

        if (param.HasDefaultValue)
            return param.DefaultValue;

        throw new BadHttpRequestException($"Missing required parameter '{param.Name}'.");
    }
}
