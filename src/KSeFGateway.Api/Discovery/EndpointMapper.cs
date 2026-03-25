using System.Reflection;
using System.Text.Json;
using KSeF.Client.Core.Interfaces.Clients;
using KSeFGateway.Api.Auth;
using KSeFGateway.Api.Models;
using System.IO;

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
                .WithName($"{endpoint.GroupName}_{endpoint.MethodName}")
                .WithOpenApi();
        }
    }

    private static Delegate CreateHandler(EndpointMetadata endpoint)
    {
        return async (HttpContext context) =>
        {
            try
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

                var args = await BuildArguments(
                    endpoint, context, accessToken);

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
            }
            catch (TargetInvocationException ex) when (ex.InnerException is not null)
            {
                return Results.Json(
                    ApiResponse.Fail(ex.InnerException.Message),
                    statusCode: 500);
            }
            catch (Exception ex)
            {
                return Results.Json(
                    ApiResponse.Fail(ex.Message),
                    statusCode: 500);
            }
        };
    }

    private static async Task<object?[]> BuildArguments(
        EndpointMetadata endpoint,
        HttpContext context,
        string accessToken)
    {
        var sdkParams = endpoint.SdkMethod.GetParameters();
        var args = new object?[sdkParams.Length];

        // Read body once if there are business parameters
        JsonElement? body = null;
        if (endpoint.BusinessParameters.Count > 0 && context.Request.ContentLength > 0)
        {
            body = await JsonSerializer.DeserializeAsync<JsonElement>(
                context.Request.Body, JsonOptions);
        }

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
            else if (body.HasValue)
            {
                args[i] = ResolveParameter(param, body.Value);
            }
            else if (param.HasDefaultValue)
            {
                args[i] = param.DefaultValue;
            }
            else
            {
                args[i] = null;
            }
        }

        return args;
    }

    private static object? ResolveParameter(ParameterInfo param, JsonElement body)
    {
        // If the body has a property matching the parameter name, use it
        if (body.ValueKind == JsonValueKind.Object &&
            body.TryGetProperty(param.Name!, out var propValue))
        {
            return JsonSerializer.Deserialize(
                propValue.GetRawText(), param.ParameterType, JsonOptions);
        }

        // If there's only one business parameter, try deserializing the whole body as that type
        if (body.ValueKind == JsonValueKind.Object)
        {
            try
            {
                return JsonSerializer.Deserialize(
                    body.GetRawText(), param.ParameterType, JsonOptions);
            }
            catch
            {
                // Fall through to default
            }
        }

        return param.HasDefaultValue ? param.DefaultValue : null;
    }
}
