using System.Text.Json;
using FocusRgb.App.Plugins;
using FocusRgb.TestSupport;

namespace FocusRgb.App.Tests
{
    public sealed class PluginConfigurationStoreTests : IDisposable
    {
        private readonly TempDirectory _temporaryDirectory = new(nameof(PluginConfigurationStoreTests));

        [Fact]
        public async Task SaveAsync_PersistsValidatedOpaqueValues_WithoutChangingPackage()
        {
            var packages = Path.Combine(_temporaryDirectory.Path, "plugins");
            var data = Path.Combine(_temporaryDirectory.Path, "plugin-data");
            var package = Path.Combine(packages, "example");
            Directory.CreateDirectory(package);
            var schemaJson = SchemaJson();
            File.WriteAllText(Path.Combine(package, PluginConfigurationStore.SchemaFileName), schemaJson);
            using var store = new PluginConfigurationStore(packages, data);
            var loaded = store.Load("example");

            Assert.True(loaded.IsSuccess, loaded.Error);
            var values = loaded.Values!.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            values["port"] = JsonSerializer.SerializeToElement(4567);
            values["enabled"] = JsonSerializer.SerializeToElement(false);

            Assert.Null(await store.SaveAsync(
                "example", loaded.Schema!, values, TestContext.Current.CancellationToken));
            var reloaded = store.Load("example");
            Assert.Equal(4567, reloaded.Values!["port"].GetInt32());
            Assert.False(reloaded.Values["enabled"].GetBoolean());
            Assert.Equal(schemaJson,
                File.ReadAllText(Path.Combine(package, PluginConfigurationStore.SchemaFileName)));
        }

        [Fact]
        public void CanConfigure_RejectsInvalidOrMissingSchema()
        {
            var packages = Path.Combine(_temporaryDirectory.Path, "plugins");
            var package = Path.Combine(packages, "example");
            Directory.CreateDirectory(package);
            using var store = new PluginConfigurationStore(
                packages, Path.Combine(_temporaryDirectory.Path, "plugin-data"));
            Assert.False(store.CanConfigure("example"));

            File.WriteAllText(Path.Combine(package, PluginConfigurationStore.SchemaFileName),
                """{"schemaVersion":1,"fields":[{"id":"port","displayName":"Port","type":"integer","default":"bad"}]}""");

            Assert.False(store.CanConfigure("example"));
        }

        [Fact]
        public void Load_DropsObsoleteValuesAndKeepsValidFields()
        {
            var packages = Path.Combine(_temporaryDirectory.Path, "plugins");
            var data = Path.Combine(_temporaryDirectory.Path, "plugin-data");
            Directory.CreateDirectory(Path.Combine(packages, "example"));
            Directory.CreateDirectory(Path.Combine(data, "example"));
            File.WriteAllText(Path.Combine(packages, "example", PluginConfigurationStore.SchemaFileName), SchemaJson());
            File.WriteAllText(Path.Combine(data, "example", PluginConfigurationStore.SettingsFileName),
                """{"port":3000,"enabled":true,"unexpected":"value"}""");
            using var store = new PluginConfigurationStore(packages, data);

            var result = store.Load("example");

            Assert.True(result.IsSuccess, result.Error);
            Assert.False(result.Values!.ContainsKey("unexpected"));
            Assert.Equal(3000, result.Values["port"].GetInt32());
            Assert.Single(result.Notices);
        }

        [Theory]
        [InlineData("\"not-a-port\"")]
        [InlineData("999999")]
        public async Task Load_RepairsInvalidSavedValuesAndAllowsResaving(string invalidPort)
        {
            var packages = Path.Combine(_temporaryDirectory.Path, "plugins");
            var data = Path.Combine(_temporaryDirectory.Path, "plugin-data");
            Directory.CreateDirectory(Path.Combine(packages, "example"));
            Directory.CreateDirectory(Path.Combine(data, "example"));
            File.WriteAllText(Path.Combine(packages, "example", PluginConfigurationStore.SchemaFileName), SchemaJson());
            var savedPath = Path.Combine(data, "example", PluginConfigurationStore.SettingsFileName);
            var savedJson = "{\"port\":" + invalidPort + ",\"enabled\":false}";
            File.WriteAllText(savedPath, savedJson);
            using var store = new PluginConfigurationStore(packages, data);

            var loaded = store.Load("example");

            Assert.True(loaded.IsSuccess, loaded.Error);
            Assert.Equal(3000, loaded.Values!["port"].GetInt32());
            Assert.False(loaded.Values["enabled"].GetBoolean());
            Assert.Single(loaded.Notices);
            Assert.Equal(savedJson, File.ReadAllText(savedPath));
            Assert.Null(await store.SaveAsync("example", loaded.Schema!, loaded.Values,
                TestContext.Current.CancellationToken));
            Assert.Empty(store.Load("example").Notices);
        }

        [Theory]
        [InlineData("{\"schemaVersion\":1,\"fields\":null}")]
        [InlineData("{\"schemaVersion\":1,\"fields\":[null]}")]
        [InlineData("{\"schemaVersion\":1,\"fields\":[{\"id\":null,\"displayName\":\"Name\",\"type\":\"string\",\"default\":\"\"}]}")]
        public void CanConfigure_RejectsNullSchemaMembersWithoutThrowing(string json)
        {
            var packages = Path.Combine(_temporaryDirectory.Path, "plugins");
            Directory.CreateDirectory(Path.Combine(packages, "example"));
            File.WriteAllText(Path.Combine(packages, "example", PluginConfigurationStore.SchemaFileName), json);
            using var store = new PluginConfigurationStore(packages, Path.Combine(_temporaryDirectory.Path, "data"));

            Assert.False(store.CanConfigure("example"));
        }

        [Theory]
        [InlineData("{broken")]
        [InlineData("null")]
        [InlineData("[]")]
        [InlineData("oversized")]
        public async Task Load_ArchivesInvalidDocumentAndOpensDefaults_WithoutReplacingOriginalUntilSave(string document)
        {
            var packages = Path.Combine(_temporaryDirectory.Path, "plugins");
            var data = Path.Combine(_temporaryDirectory.Path, "plugin-data");
            Directory.CreateDirectory(Path.Combine(packages, "example"));
            Directory.CreateDirectory(Path.Combine(data, "example"));
            File.WriteAllText(Path.Combine(packages, "example", PluginConfigurationStore.SchemaFileName), SchemaJson());
            var path = Path.Combine(data, "example", PluginConfigurationStore.SettingsFileName);
            var original = document == "oversized" ? new string(' ', 1024 * 1024 + 1) : document;
            await File.WriteAllTextAsync(path, original, TestContext.Current.CancellationToken);
            var bytes = await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken);
            using var store = new PluginConfigurationStore(packages, data);

            var loaded = store.Load("example");

            Assert.True(loaded.IsSuccess, loaded.Error);
            Assert.True(store.CanConfigure("example"));
            Assert.Equal(3000, loaded.Values!["port"].GetInt32());
            Assert.True(loaded.Values["enabled"].GetBoolean());
            Assert.Contains("Cancel", Assert.Single(loaded.Notices), StringComparison.Ordinal);
            Assert.Equal(bytes, await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
            var archive = Assert.Single(Directory.GetFiles(Path.GetDirectoryName(path)!, "settings.json.corrupt-*"));
            Assert.Equal(bytes, await File.ReadAllBytesAsync(archive, TestContext.Current.CancellationToken));

            Assert.Null(await store.SaveAsync("example", loaded.Schema!, loaded.Values, TestContext.Current.CancellationToken));

            Assert.Empty(store.Load("example").Notices);
            Assert.Equal(3000, store.Load("example").Values!["port"].GetInt32());
            Assert.Equal(bytes, await File.ReadAllBytesAsync(archive, TestContext.Current.CancellationToken));
        }

        public void Dispose() => _temporaryDirectory.Dispose();

        internal static string SchemaJson() =>
            """{"schemaVersion":1,"description":"Example settings","fields":[{"id":"port","displayName":"Port","description":"Loopback port","type":"integer","default":3000,"minimum":1,"maximum":65535},{"id":"enabled","displayName":"Enabled","type":"boolean","default":true}]}""";
    }
}
