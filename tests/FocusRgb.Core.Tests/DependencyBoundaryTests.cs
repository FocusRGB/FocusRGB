using System.Reflection;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;
using FocusRgb.Core.Protocol;
using FocusRgb.TestSupport;

namespace FocusRgb.Core.Tests
{
    /// <summary>
    /// Guards the repository source boundaries: Core stays portable and never references
    /// platform, application, or provider projects. A failure
    /// here means a dependency leaked into the portable layer, not that the test is flaky.
    /// </summary>
    public sealed class DependencyBoundaryTests
    {
        private static readonly Dictionary<string, string[]> AllowedProjectReferences = new()
        {
            ["FocusRgb.Core"] = [],
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
        public void HostContractVersion_MatchesSupported12Baseline() =>
            Assert.Equal("1.2", ContractVersion.Current);

        [Theory]
        [InlineData("plugin-manifest.schema.json")]
        [InlineData("plugin-protocol.schema.json")]
        public void ProtocolSchema_IdTargetsStableSpecTag(string fileName)
        {
            using var schema = JsonDocument.Parse(File.ReadAllText(
                Path.Combine(RepositoryPaths.Root, "sdk", "spec", fileName)));

            Assert.Equal(
                $"https://raw.githubusercontent.com/FocusRGB/FocusRGB/spec-v1.3.0/sdk/spec/{fileName}",
                schema.RootElement.GetProperty("$id").GetString());
        }

        [Fact]
        public void ProtocolSchema_MethodsMatchContractConstants()
        {
            using var schema = JsonDocument.Parse(File.ReadAllText(
                Path.Combine(RepositoryPaths.Root, "sdk", "spec", "plugin-protocol.schema.json")));
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
            { typeof(PluginGameCatalogMetadata), "pluginGameCatalogMetadata" },
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
                Path.Combine(RepositoryPaths.Root, "sdk", "spec", "plugin-protocol.schema.json")));
            var definition = schema.RootElement.GetProperty("$defs").GetProperty(definitionName);
            var schemaProperties = definition.GetProperty("properties").EnumerateObject()
                .ToDictionary(property => property.Name, property => property.Value, StringComparer.Ordinal);
            var contractProperties = contractType.GetProperties().ToDictionary(
                property => property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name
                    ?? JsonNamingPolicy.CamelCase.ConvertName(property.Name),
                StringComparer.Ordinal);

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
                Path.Combine(RepositoryPaths.Root, "sdk", "spec", "plugin-manifest.schema.json")));
            var schemaProperties = schema.RootElement.GetProperty("properties").EnumerateObject()
                .Select(property => property.Name)
                .ToHashSet(StringComparer.Ordinal);
            var contractProperties = typeof(PluginManifest).GetProperties()
                .Select(property => JsonNamingPolicy.CamelCase.ConvertName(property.Name))
                .ToHashSet(StringComparer.Ordinal);

            Assert.True(contractProperties.SetEquals(schemaProperties));
            Assert.DoesNotContain("runtime", schema.RootElement.GetProperty("required")
                .EnumerateArray().Select(value => value.GetString()));
            Assert.Equal(["node", "python"], schema.RootElement.GetProperty("properties")
                .GetProperty("runtime").GetProperty("enum").EnumerateArray()
                .Select(value => value.GetString()));
            Assert.Equal("^1\\.(?:[1-9][0-9]*)$", schema.RootElement.GetProperty("allOf")[0]
                .GetProperty("then").GetProperty("properties").GetProperty("contractVersion")
                .GetProperty("pattern").GetString());
            Assert.Equal("^1\\.(?:[3-9]|[1-9][0-9]+)$", schema.RootElement.GetProperty("allOf")[1]
                .GetProperty("then").GetProperty("properties").GetProperty("contractVersion")
                .GetProperty("pattern").GetString());
            Assert.Equal(typeof(string), typeof(PluginManifest).GetProperty(nameof(PluginManifest.Runtime))!.PropertyType);
            Assert.Equal(
                ["process.read", "process.start", "filesystem.read", "filesystem.write", "network.listen.loopback"],
                schema.RootElement.GetProperty("properties").GetProperty("resources")
                    .GetProperty("items").GetProperty("enum").EnumerateArray()
                    .Select(value => value.GetString()));
        }

        [Fact]
        public void GameCatalogSchema_RequiresUniqueExecutableFileNames()
        {
            using var schema = JsonDocument.Parse(File.ReadAllText(
                Path.Combine(RepositoryPaths.Root, "sdk", "spec", "plugin-protocol.schema.json")));
            var definitions = schema.RootElement.GetProperty("$defs");
            var kinds = definitions.GetProperty("catalogListParameters").GetProperty("properties")
                .GetProperty("kind").GetProperty("enum").EnumerateArray()
                .Select(item => item.GetString());
            var executables = definitions
                .GetProperty("pluginGameCatalogMetadata").GetProperty("properties")
                .GetProperty("executables");

            Assert.Contains("game", kinds);
            Assert.True(executables.GetProperty("uniqueItems").GetBoolean());
            Assert.Equal(1, executables.GetProperty("items").GetProperty("minLength").GetInt32());
            Assert.Equal("^[^/\\\\]+$", executables.GetProperty("items").GetProperty("pattern").GetString());
        }

        [Fact]
        public void CatalogSchema_ReservesIncompleteScanReasonWithoutChangingWireShape()
        {
            using var schema = JsonDocument.Parse(File.ReadAllText(
                Path.Combine(RepositoryPaths.Root, "sdk", "spec", "plugin-protocol.schema.json")));
            var rule = schema.RootElement.GetProperty("$defs").GetProperty("pluginCatalogResult").GetProperty("allOf")[0];
            Assert.Equal("catalog-scan-incomplete", rule.GetProperty("if").GetProperty("properties")
                .GetProperty("reason").GetProperty("properties").GetProperty("code").GetProperty("const").GetString());
            Assert.Equal("refreshedAt", rule.GetProperty("then").GetProperty("not").GetProperty("required")[0].GetString());
            Assert.Equal(1, rule.GetProperty("then").GetProperty("properties").GetProperty("reason")
                .GetProperty("properties").GetProperty("message").GetProperty("minLength").GetInt32());
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
            if (type == typeof(double)) return "number";
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
            XDocument.Load(Path.Combine(RepositoryPaths.Root, "src", project, project + ".csproj"));
    }
}
