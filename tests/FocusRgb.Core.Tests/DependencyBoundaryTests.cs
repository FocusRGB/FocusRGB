using System.Reflection;
using System.Runtime.Versioning;
using System.Xml.Linq;
using Xunit;

namespace FocusRgb.Core.Tests;

/// <summary>
/// Guards the source boundaries in docs/implementation-slices.md: contracts and core stay
/// portable and never reference platform, application, or provider projects.
/// </summary>
public sealed class DependencyBoundaryTests
{
    private static readonly string[] PortableProjects = ["FocusRgb.Contracts", "FocusRgb.Core"];

    private static readonly Dictionary<string, string[]> AllowedProjectReferences = new()
    {
        ["FocusRgb.Contracts"] = [],
        ["FocusRgb.Core"] = ["FocusRgb.Contracts"],
    };

    [Theory]
    [MemberData(nameof(Portable))]
    public void PortableProjectTargetsPlainNet(string project)
    {
        var targets = ReadProject(project)
            .Descendants()
            .Where(e => e.Name.LocalName is "TargetFramework" or "TargetFrameworks")
            .Select(e => e.Value)
            .ToList();

        Assert.All(targets, t => Assert.DoesNotContain("-", t, StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(Portable))]
    public void PortableProjectHasOnlyAllowedReferences(string project)
    {
        var document = ReadProject(project);

        var projectReferences = document.Descendants()
            .Where(e => e.Name.LocalName == "ProjectReference")
            .Select(e => Path.GetFileNameWithoutExtension(e.Attribute("Include")!.Value))
            .ToList();
        var packageReferences = document.Descendants()
            .Where(e => e.Name.LocalName == "PackageReference")
            .Select(e => e.Attribute("Include")!.Value)
            .ToList();

        Assert.Empty(projectReferences.Except(AllowedProjectReferences[project]));
        Assert.Empty(packageReferences);
    }

    [Theory]
    [MemberData(nameof(Portable))]
    public void PortableAssemblyHasNoPlatformOrForeignDependencies(string project)
    {
        var assembly = Assembly.Load(project);

        Assert.Null(assembly.GetCustomAttribute<TargetPlatformAttribute>());
        Assert.Empty(assembly.GetCustomAttributes<SupportedOSPlatformAttribute>());

        var foreign = assembly.GetReferencedAssemblies()
            .Select(a => a.Name!)
            .Where(name => !IsFrameworkAssembly(name) && !AllowedProjectReferences[project].Contains(name))
            .ToList();
        Assert.Empty(foreign);
    }

    [Fact]
    public void ContractVersionMatchesMinimalContract() =>
        Assert.Equal("0.1", Contracts.ContractVersion.Current);

    public static TheoryData<string> Portable() => new(PortableProjects);

    private static bool IsFrameworkAssembly(string name) =>
        (name.StartsWith("System", StringComparison.Ordinal) || name is "netstandard" or "mscorlib")
        && !name.Contains("Windows", StringComparison.OrdinalIgnoreCase);

    private static XDocument ReadProject(string project) =>
        XDocument.Load(Path.Combine(RepositoryRoot(), "src", project, project + ".csproj"));

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "global.json")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
