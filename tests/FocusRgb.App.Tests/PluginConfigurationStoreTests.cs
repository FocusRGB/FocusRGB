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
        public void Load_RejectsUnknownPersistedValues()
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

            Assert.False(result.IsSuccess);
            Assert.Contains("Unknown plugin setting", result.Error, StringComparison.Ordinal);
        }

        public void Dispose() => _temporaryDirectory.Dispose();

        internal static string SchemaJson() =>
            """{"schemaVersion":1,"description":"Example settings","fields":[{"id":"port","displayName":"Port","description":"Loopback port","type":"integer","default":3000,"minimum":1,"maximum":65535},{"id":"enabled","displayName":"Enabled","type":"boolean","default":true}]}""";
    }
}
