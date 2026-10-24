using System.Text.Json;
using KSeFGateway.Api.Discovery;

namespace KSeFGateway.Api.Tests.Discovery;

public class SdkContractTests
{
    [Fact]
    public void SdkContract_MatchesApprovedManifest()
    {
        var endpoints = SdkReflector.DiscoverEndpoints();
        var actual = JsonSerializer.Serialize(endpoints.OrderBy(e => e.Route, StringComparer.Ordinal).Select(e => new
        {
            route = e.Route,
            group = e.GroupName,
            method = e.MethodName,
            sdkMethod = e.SdkMethod.Name,
            parameters = e.SdkMethod.GetParameters().Select(p => new
            {
                name = p.Name,
                type = p.ParameterType.ToString(),
                business = e.BusinessParameters.Any(b => b.Position == p.Position),
                optional = p.HasDefaultValue,
                defaultValue = p.HasDefaultValue && p.DefaultValue is not null
                    ? Convert.ToString(p.DefaultValue, System.Globalization.CultureInfo.InvariantCulture) : null
            }),
            returnType = e.SdkMethod.ReturnType.ToString()
        }), new JsonSerializerOptions { WriteIndented = true }) + "\n";
        var path = Path.Combine(AppContext.BaseDirectory, "Discovery", "sdk-contract.approved.json");
        var approved = File.Exists(path) ? File.ReadAllText(path).Replace("\r\n", "\n") : "";
        if (approved != actual)
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "sdk-contract.actual.json"), actual);
        Assert.True(approved == actual,
            "SDK contract changed: zaktualizuj manifest świadomie i opisz zmianę w CHANGELOG. " +
            "Compare sdk-contract.actual.json in the test output directory with Discovery/sdk-contract.approved.json.");
    }
}
