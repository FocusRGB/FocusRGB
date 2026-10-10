using System.Reflection;
using System.Runtime.Versioning;
using System.Xml.Linq;
using FocusRgb.Contracts;

namespace FocusRgb.Core.Tests
{
    /// <summary>
    /// Guards the source boundaries in docs/implementation-slices.md: Contracts and Core stay
    /// portable and never reference platform, application, or provider projects. A failure
    /// here means a dependency leaked into the portable layer, not that the test is flaky.
    /// </summary>
    public sealed class DependencyBoundaryTests
    {
        private static readonly Dictionary<string, string[]> AllowedProjectReferences = new()
        {
            ["FocusRgb.Contracts"] = [],
            ["FocusRgb.Core"] = ["FocusRgb.Contracts"],
        };

        public static TheoryData<string> PortableProjects() => new(AllowedProjectReferences.Keys);

        [Theory]
        [MemberData(nameof(PortableProjects))]
        public void ProjectFile_TargetsPlainNet_WithoutPlatformSuffix(string project)
        {
            var targets = ReadProject(project)
                .Descendants()
                .Where(element => element.Name.LocalName is "TargetFramework" or "TargetFrameworks")
                .Select(element => element.Value)
                .ToList();

            Assert.All(targets, target => Assert.DoesNotContain("-", target, StringComparison.Ordinal));
        }

        [Theory]
        [MemberData(nameof(PortableProjects))]
        public void ProjectFile_HasNoPackages_AndOnlyAllowedProjectReferences(string project)
        {
            var document = ReadProject(project);

            var projectReferences = document.Descendants()
                .Where(element => element.Name.LocalName == "ProjectReference")
                .Select(element => Path.GetFileNameWithoutExtension(element.Attribute("Include")!.Value))
                .ToList();
            var packageReferences = document.Descendants()
                .Where(element => element.Name.LocalName == "PackageReference")
                .Select(element => element.Attribute("Include")!.Value)
                .ToList();

            Assert.Empty(projectReferences.Except(AllowedProjectReferences[project]));
            Assert.Empty(packageReferences);
        }

        [Theory]
        [MemberData(nameof(PortableProjects))]
        public void Assembly_HasNoPlatformAttributes_OrForeignReferences(string project)
        {
            var assembly = Assembly.Load(project);

            Assert.Null(assembly.GetCustomAttribute<TargetPlatformAttribute>());
            Assert.Empty(assembly.GetCustomAttributes<SupportedOSPlatformAttribute>());

            var foreignReferences = assembly.GetReferencedAssemblies()
                .Select(reference => reference.Name!)
                .Where(name => !IsFrameworkAssembly(name) && !AllowedProjectReferences[project].Contains(name))
                .ToList();
            Assert.Empty(foreignReferences);
        }

        [Fact]
        public void ContractVersion_MatchesMinimalContract() =>
            Assert.Equal("0.1", ContractVersion.Current);

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
}
