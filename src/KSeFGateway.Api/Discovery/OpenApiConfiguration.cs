using Microsoft.OpenApi;

namespace KSeFGateway.Api.Discovery;

public static class OpenApiConfiguration
{
    public static IServiceCollection AddGatewayOpenApi(this IServiceCollection services) =>
        services.AddOpenApi(options =>
        {
            // .NET 10 defaults to 3.1; retain the existing public document format.
            options.OpenApiVersion = OpenApiSpecVersion.OpenApi3_0;
            options.AddSchemaTransformer((schema, context, _) =>
            {
                // .NET 10 reflects AllowReadingFromString as integer|string in schemas.
                // Preserve the .NET 9 integer schema without changing JSON deserialization.
                var type = context.JsonTypeInfo.Type;
                if (type == typeof(int) || type == typeof(long))
                {
                    schema.Type = JsonSchemaType.Integer;
                    schema.AnyOf = null;
                    schema.Pattern = null;
                }
                return Task.CompletedTask;
            });
        });
}
