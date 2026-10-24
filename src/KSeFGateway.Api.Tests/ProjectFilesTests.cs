using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace KSeFGateway.Api.Tests;

public class ProjectFilesTests
{
    [Fact]
    public void PackageReferences_UseExactVersions()
    {
        var root = FindProjectRoot();
        var projects = Directory.GetFiles(root, "*.csproj", SearchOption.AllDirectories);
        Assert.NotEmpty(projects);

        var invalid = projects.SelectMany(path => XDocument.Load(path)
            .Descendants().Where(element => element.Name.LocalName == "PackageReference")
            .Select(reference => new
            {
                Project = Path.GetRelativePath(root, path),
                Package = (string?)reference.Attribute("Include") ?? (string?)reference.Attribute("Update"),
                Version = (string?)reference.Attribute("Version")
                    ?? reference.Elements().FirstOrDefault(element => element.Name.LocalName == "Version")?.Value
            }))
            .Where(reference => !IsExactVersion(reference.Version))
            .Select(reference => $"{reference.Project}: {reference.Package} Version=\"{reference.Version ?? "<missing>"}\"")
            .ToArray();

        Assert.True(invalid.Length == 0,
            "PackageReference versions must be exact:\n" + string.Join("\n", invalid));
    }

    [Fact]
    public void TargetFrameworks_RequireNet10OrNewer()
    {
        var root = FindProjectRoot();
        var projects = Directory.GetFiles(root, "*.csproj", SearchOption.AllDirectories);
        Assert.NotEmpty(projects);

        var invalid = projects.SelectMany(path => XDocument.Load(path).Descendants()
            .Where(element => element.Name.LocalName is "TargetFramework" or "TargetFrameworks")
            .SelectMany(element => element.Value.Split(';'))
            .DefaultIfEmpty("")
            .Where(framework => !IsNet10OrNewer(framework))
            .Select(framework => $"{Path.GetRelativePath(root, path)}: {framework}"))
            .ToArray();

        Assert.True(invalid.Length == 0,
            "All projects must target .NET 10 or newer:\n" + string.Join("\n", invalid));
    }

    [Theory]
    [InlineData("net9.0", false)]
    [InlineData("net8.0", false)]
    [InlineData("net10.0", true)]
    [InlineData("net11.0", true)]
    [InlineData("net10.0-windows", true)]
    [InlineData("netstandard2.1", false)]
    [InlineData("net48", false)]
    [InlineData("$(TargetFramework)", false)]
    [InlineData("", false)]
    public void MinimumFramework_RejectsOlderAndUnresolvedTargets(string framework, bool expected)
    {
        Assert.Equal(expected, IsNet10OrNewer(framework));
    }

    private static bool IsNet10OrNewer(string framework)
    {
        var match = Regex.Match(framework, @"\Anet([0-9]+)\.([0-9]+)(?:-[0-9A-Za-z.-]+)?\z");
        return match.Success && int.TryParse(match.Groups[1].Value, out var major) && major >= 10;
    }

    [Theory]
    [InlineData("2.9.0", true)]
    [InlineData("2.9.0-rc.1", true)]
    [InlineData("2.9.0+build.1", true)]
    [InlineData("1.2.3.4", true)]
    [InlineData("1.2", true)]
    [InlineData("*-*", false)]
    [InlineData("2.*", false)]
    [InlineData("9.0.*", false)]
    [InlineData("2.9.0-*", false)]
    [InlineData("[2.9.0]", false)]
    [InlineData("[2.0.0,3.0.0)", false)]
    [InlineData("(,3.0.0]", false)]
    [InlineData("$(SdkVersion)", false)]
    [InlineData("latest", false)]
    [InlineData("2.9.0-", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void ExactVersion_RejectsFloatingRangesAndMissingVersions(string? version, bool expected)
    {
        Assert.Equal(expected, IsExactVersion(version));
    }

    private static bool IsExactVersion(string? version) => version is not null && Regex.IsMatch(version,
        @"\A[0-9]+(?:\.[0-9]+){1,3}(?:-[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?\z");

    private static string FindProjectRoot()
    {
        string? sourceRoot = null;
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "docker-compose.yml"))
                || File.Exists(Path.Combine(directory.FullName, "KSeFGateway.sln")))
                return directory.FullName;

            // The test image copies only the two source projects into /src.
            if (File.Exists(Path.Combine(directory.FullName, "KSeFGateway.Api", "KSeFGateway.Api.csproj"))
                && File.Exists(Path.Combine(directory.FullName, "KSeFGateway.Api.Tests", "KSeFGateway.Api.Tests.csproj")))
                sourceRoot = directory.FullName;
        }

        return sourceRoot ?? throw new DirectoryNotFoundException("Cannot locate the gateway project files.");
    }
}
