using System.Reflection;
using System.Text.RegularExpressions;
using KSeF.Client.Core.Interfaces.Clients;

namespace KSeFGateway.Api.Discovery;

public static partial class SdkReflector
{
    private static readonly HashSet<string> InfrastructureParamNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "accessToken",
        "cancellationToken"
    };

    private static readonly HashSet<Type> InfrastructureParamTypes = new()
    {
        typeof(CancellationToken),
        typeof(string) // accessToken - we identify by name, but type helps too
    };

    public static IReadOnlyList<EndpointMetadata> DiscoverEndpoints()
    {
        var clientType = typeof(IKSeFClient);
        var allInterfaces = clientType.GetInterfaces();
        var endpoints = new List<EndpointMetadata>();

        foreach (var iface in allInterfaces)
        {
            // Strip leading "I" prefix (interface convention) but preserve rest
            var rawName = iface.Name.StartsWith("I") && iface.Name.Length > 1 && char.IsUpper(iface.Name[1])
                ? iface.Name[1..]
                : iface.Name;
            var groupName = ToKebabCase(rawName.Replace("Client", ""));

            var seenRoutes = new HashSet<string>();

            foreach (var method in iface.GetMethods())
            {
                var allParams = method.GetParameters();
                var businessParams = allParams
                    .Where(p => !IsInfrastructureParam(p))
                    .ToList();

                var methodName = ToKebabCase(
                    method.Name
                        .Replace("Async", "")
                );

                var route = $"{groupName}/{methodName}";

                // Skip duplicate overloads (e.g. deprecated ExportInvoicesAsync)
                if (!seenRoutes.Add(route))
                {
                    // Keep the overload with more business parameters
                    var existing = endpoints.First(e => e.Route == $"/ksef/{route}");
                    if (businessParams.Count > existing.BusinessParameters.Count)
                    {
                        endpoints.Remove(existing);
                    }
                    else
                    {
                        continue;
                    }
                }

                endpoints.Add(new EndpointMetadata
                {
                    GroupName = groupName,
                    MethodName = methodName,
                    SdkMethod = method,
                    BusinessParameters = businessParams,
                    ReturnType = UnwrapTaskType(method.ReturnType),
                    InterfaceName = iface.Name
                });
            }
        }

        return endpoints;
    }

    private static bool IsInfrastructureParam(ParameterInfo param)
    {
        if (param.ParameterType == typeof(CancellationToken))
            return true;

        if (InfrastructureParamNames.Contains(param.Name ?? ""))
            return true;

        return false;
    }

    private static Type? UnwrapTaskType(Type type)
    {
        if (type == typeof(Task))
            return null; // void-returning async

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Task<>))
            return type.GetGenericArguments()[0];

        return type;
    }

    private static string ToKebabCase(string input)
    {
        if (string.IsNullOrEmpty(input))
            return input;

        // Insert hyphen before uppercase letters that follow lowercase letters or other patterns
        var result = KebabCaseRegex().Replace(input, "$1-$2");
        return result.ToLowerInvariant();
    }

    [GeneratedRegex("([a-z0-9])([A-Z])")]
    private static partial Regex KebabCaseRegex();
}
