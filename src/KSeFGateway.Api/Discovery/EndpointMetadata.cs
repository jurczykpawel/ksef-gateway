using System.Reflection;

namespace KSeFGateway.Api.Discovery;

public record EndpointMetadata
{
    public required string GroupName { get; init; }
    public required string MethodName { get; init; }
    public required MethodInfo SdkMethod { get; init; }
    public required IReadOnlyList<ParameterInfo> BusinessParameters { get; init; }
    public required Type? ReturnType { get; init; }
    public required string InterfaceName { get; init; }

    public string Route => $"/ksef/{GroupName}/{MethodName}";
    public string Tag => InterfaceName
        .Replace("Client", "")
        .Replace("I", "");
}
