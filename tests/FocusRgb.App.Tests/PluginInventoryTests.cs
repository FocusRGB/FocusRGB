using FocusRgb.App.Plugins;
using FocusRgb.TestSupport;

namespace FocusRgb.App.Tests
{
    public sealed class PluginInventoryTests : IDisposable
    {
        private readonly TempDirectory _temporaryDirectory = new(nameof(PluginInventoryTests));

        [Theory]
        [InlineData("steam", "Steam")]
        [InlineData("signalrgb", "SignalRGB")]
        [InlineData("custom-plugin", "custom-plugin")]
        public void List_PreservesKnownBrandCasingWithoutChangingIds(string id, string displayName)
        {
            Directory.CreateDirectory(Path.Combine(_temporaryDirectory.Path, id));
            using var inventory = new PluginInventory(_temporaryDirectory.Path);
            var plugin = Assert.Single(inventory.List([], []));
            Assert.Equal(id, plugin.Id);
            Assert.Equal(displayName, plugin.DisplayName);
        }

        [Fact]
        public async Task List_LabelsManualFolderCustom_UntilOfficialInstallIsRecorded()
        {
            await FakePluginPackage.CreateAsync(
                _temporaryDirectory.Path, TestContext.Current.CancellationToken);
            var inventory = new PluginInventory(_temporaryDirectory.Path);

            var custom = Assert.Single(inventory.List(["fake-output"], []));
            Assert.False(custom.IsOfficial);
            Assert.True(custom.IsEnabled);

            Assert.Null(await inventory.SetOfficialAsync(
                "fake-output", true, TestContext.Current.CancellationToken));

            Assert.True(Assert.Single(inventory.List([], [])).IsOfficial);
        }

        [Fact]
        public async Task SetOfficialAsync_RemovesOfficialOriginOnCustomReplacement()
        {
            await FakePluginPackage.CreateAsync(
                _temporaryDirectory.Path, TestContext.Current.CancellationToken);
            var inventory = new PluginInventory(_temporaryDirectory.Path);
            await inventory.SetOfficialAsync("fake-output", true, TestContext.Current.CancellationToken);

            Assert.Null(await inventory.SetOfficialAsync(
                "fake-output", false, TestContext.Current.CancellationToken));

            Assert.False(Assert.Single(inventory.List([], [])).IsOfficial);
        }

        [Fact]
        public async Task List_ReportsConfigurationOnlyForValidDeclaredSchema()
        {
            await FakePluginPackage.CreateAsync(
                _temporaryDirectory.Path, TestContext.Current.CancellationToken);
            var package = Path.Combine(_temporaryDirectory.Path, "fake-output");
            var inventory = new PluginInventory(_temporaryDirectory.Path);
            Assert.False(Assert.Single(inventory.List([], [])).HasConfiguration);

            File.WriteAllText(Path.Combine(package, PluginConfigurationStore.SchemaFileName),
                PluginConfigurationStoreTests.SchemaJson());

            Assert.True(Assert.Single(inventory.List([], [])).HasConfiguration);
        }

        public void Dispose() => _temporaryDirectory.Dispose();
    }
}
