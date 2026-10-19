using System.Runtime.Versioning;
using System.Text.Json;
using System.Xml.Linq;
using FocusRgb.Contracts;
using FocusRgb.TestSupport;

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
            ["FocusRgb.PluginSdk"] = ["FocusRgb.Contracts"],
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
        public void ContractVersion_MatchesStableProtocol() =>
            Assert.Equal("1.0", ContractVersion.Current);

        [Fact]
        public void ProtocolSchema_MethodsMatchContractConstants()
        {
            using var schema = JsonDocument.Parse(File.ReadAllText(
                Path.Combine(RepositoryRoot(), "sdk", "spec", "plugin-protocol.schema.json")));
            var definitions = schema.RootElement.GetProperty("$defs");
            var schemaMethods = definitions.GetProperty("request").GetProperty("properties")
                .GetProperty("method").GetProperty("enum").EnumerateArray()
                .Concat(definitions.GetProperty("notification").GetProperty("properties")
                    .GetProperty("method").GetProperty("enum").EnumerateArray())
                .Select(value => value.GetString()!)
                .ToHashSet(StringComparer.Ordinal);
            var contractMethods = typeof(PluginMethods).GetFields(BindingFlags.Public | BindingFlags.Static)
                .Select(field => (string)field.GetRawConstantValue()!)
                .ToHashSet(StringComparer.Ordinal);

            Assert.True(contractMethods.SetEquals(schemaMethods));
        }

        public static TheoryData<Type, string> ProtocolShapes() => new()
        {
            { typeof(PluginHostInfo), "pluginHostInfo" },
            { typeof(InitializeParameters), "initializeParameters" },
            { typeof(InitializeResult), "initializeResult" },
            { typeof(PluginSdkInfo), "pluginSdkInfo" },
            { typeof(PluginCapability), "pluginCapability" },
            { typeof(PluginReason), "pluginReason" },
            { typeof(PluginStatusResult), "pluginStatusResult" },
            { typeof(PluginSelection), "pluginSelection" },
            { typeof(PluginResourceReference), "pluginResourceReference" },
            { typeof(CatalogListParameters), "catalogListParameters" },
            { typeof(PluginCatalogResult), "pluginCatalogResult" },
            { typeof(PluginCatalogItem), "pluginCatalogItem" },
            { typeof(PluginDeviceScope), "pluginDeviceScope" },
            { typeof(BindingApplyParameters), "bindingApplyParameters" },
            { typeof(BindingApplyResult), "bindingApplyResult" },
            { typeof(BindingReleaseParameters), "bindingReleaseParameters" },
            { typeof(BindingReleaseResult), "bindingReleaseResult" },
            { typeof(CancelParameters), "cancelParameters" },
        };

        [Theory]
        [MemberData(nameof(ProtocolShapes))]
        public void ProtocolSchema_ShapesMatchContractTypes(Type contractType, string definitionName)
        {
            using var schema = JsonDocument.Parse(File.ReadAllText(
                Path.Combine(RepositoryRoot(), "sdk", "spec", "plugin-protocol.schema.json")));
            var definition = schema.RootElement.GetProperty("$defs").GetProperty(definitionName);
            var schemaProperties = definition.GetProperty("properties").EnumerateObject()
                .ToDictionary(property => property.Name, property => property.Value, StringComparer.Ordinal);
            var contractProperties = contractType.GetProperties().ToDictionary(
                property => JsonNamingPolicy.CamelCase.ConvertName(property.Name), StringComparer.Ordinal);

            Assert.True(contractProperties.Keys.ToHashSet(StringComparer.Ordinal)
                .SetEquals(schemaProperties.Keys));

            var required = definition.TryGetProperty("required", out var requiredElement)
                ? requiredElement.EnumerateArray().Select(value => value.GetString()!)
                    .ToHashSet(StringComparer.Ordinal)
                : [];
            var nullability = new NullabilityInfoContext();
            var contractRequired = contractProperties
                .Where(pair => Nullable.GetUnderlyingType(pair.Value.PropertyType) is null
                    && (pair.Value.PropertyType.IsValueType
                        || nullability.Create(pair.Value).WriteState == NullabilityState.NotNull))
                .Select(pair => pair.Key)
                .ToHashSet(StringComparer.Ordinal);
            Assert.True(contractRequired.SetEquals(required));

            foreach (var property in contractProperties)
                Assert.Equal(ExpectedSchemaType(property.Value.PropertyType), SchemaType(schemaProperties[property.Key]));
        }

        [Fact]
        public void ManifestSchema_PropertiesMatchContractType()
        {
            using var schema = JsonDocument.Parse(File.ReadAllText(
                Path.Combine(RepositoryRoot(), "sdk", "spec", "plugin-manifest.schema.json")));
            var schemaProperties = schema.RootElement.GetProperty("properties").EnumerateObject()
                .Select(property => property.Name)
                .ToHashSet(StringComparer.Ordinal);
            var contractProperties = typeof(PluginManifest).GetProperties()
                .Select(property => JsonNamingPolicy.CamelCase.ConvertName(property.Name))
                .ToHashSet(StringComparer.Ordinal);

            Assert.True(contractProperties.SetEquals(schemaProperties));
        }

        [Fact]
        public void AppProject_DoesNotReferenceFakePlugin()
        {
            var references = ReadProject("FocusRgb.App").Descendants()
                .Where(element => element.Name.LocalName == "ProjectReference")
                .Select(element => Path.GetFileNameWithoutExtension(element.Attribute("Include")!.Value));

            Assert.DoesNotContain("FocusRgb.FakePlugin", references);
        }

        private static bool IsFrameworkAssembly(string name) =>
            (name.StartsWith("System", StringComparison.Ordinal) || name is "netstandard" or "mscorlib")
            && !name.Contains("Windows", StringComparison.OrdinalIgnoreCase);

        private static string ExpectedSchemaType(Type type)
        {
            type = Nullable.GetUnderlyingType(type) ?? type;
            if (type == typeof(string)) return "string";
            if (type == typeof(bool)) return "boolean";
            if (type == typeof(long) || type == typeof(int)) return "integer";
            if (type == typeof(DateTimeOffset)) return "string:date-time";
            var itemType = type.GetInterfaces().Append(type)
                .FirstOrDefault(candidate => candidate.IsGenericType
                    && candidate.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                ?.GetGenericArguments()[0];
            return itemType is not null
                ? "array:" + ExpectedSchemaType(itemType)
                : "ref:" + JsonNamingPolicy.CamelCase.ConvertName(type.Name);
        }

        private static string SchemaType(JsonElement schema)
        {
            if (schema.TryGetProperty("$ref", out var reference))
                return "ref:" + reference.GetString()!.Split('/')[^1];
            var type = schema.GetProperty("type").GetString()!;
            if (type == "array") return "array:" + SchemaType(schema.GetProperty("items"));
            return schema.TryGetProperty("format", out var format) ? type + ":" + format.GetString() : type;
        }

        private static XDocument ReadProject(string project) =>
            XDocument.Load(Path.Combine(
                RepositoryPaths.Root,
                project is "FocusRgb.Contracts" or "FocusRgb.PluginSdk" ? Path.Combine("sdk", "dotnet") : "src",
                project,
                project + ".csproj"));
    }
}
