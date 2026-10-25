using Microsoft.OpenApi;
using System.Text.Json.Nodes;
using KSeFGateway.Api.Invoice;

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
                if (type == typeof(VatRate))
                {
                    schema.Type = null;
                    schema.Properties = null;
                    schema.Required = null;
                    schema.OneOf = [
                        new OpenApiSchema { Type = JsonSchemaType.Integer, Enum = new List<JsonNode> { JsonValue.Create(0)!, JsonValue.Create(5)!, JsonValue.Create(8)!, JsonValue.Create(23)! } },
                        new OpenApiSchema { Type = JsonSchemaType.String, Enum = new[] { "0 WDT", "0 EX", "zw", "np", "np I", "np II" }.Select(s => (JsonNode)JsonValue.Create(s)!).ToList() }
                    ];
                }
                if (type == typeof(BuyerData))
                    schema.Description = "Provide exactly one of nip, euVatNumber (including country prefix), taxId (country from address.country), or noId=true. 0 WDT requires euVatNumber.";
                return Task.CompletedTask;
            });
        });
}
