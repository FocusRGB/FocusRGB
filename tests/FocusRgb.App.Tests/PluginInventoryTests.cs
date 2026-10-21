using FocusRgb.App.Plugins;
using FocusRgb.TestSupport;

namespace FocusRgb.App.Tests
{
    public sealed class PluginInventoryTests : IDisposable
    {
        private readonly TempDirectory _temporaryDirectory = new(nameof(PluginInventoryTests));

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

        public void Dispose() => _temporaryDirectory.Dispose();
    }
}
