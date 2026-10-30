using FocusRgb.App.Plugins;
using FocusRgb.App.ViewModels;

namespace FocusRgb.App.Tests
{
    public sealed class OfficialPluginStateTests
    {
        [Theory]
        [InlineData("0.1.0", false)]
        [InlineData("0.0.9", true)]
        [InlineData("1.0.0", false)]
        public void OfficialList_HidesInstalledAndRestoresUninstalled(string installedVersion, bool hasUpdate)
        {
            var viewModel = MainWindowViewModel.CreatePreview();
            viewModel.SetInstalledPlugins([new InstalledPluginInfo
                {
                    Id = "fake-output", DisplayName = "Fake", Version = installedVersion, IsOfficial = true
                }]);
            viewModel.SetOfficialPlugins(OfficialPluginCatalogResult.Fresh(DateTimeOffset.UtcNow,
                [PluginPackageCacheTests.Listing()], "Ready"));
            Assert.Empty(viewModel.OfficialPlugins);
            Assert.True(viewModel.HasNoOfficialPlugins);
            Assert.Equal("All official plugins are installed.", viewModel.OfficialPluginsEmptyMessage);
            Assert.Equal(hasUpdate, Assert.Single(viewModel.InstalledPlugins).HasUpdate);
            viewModel.RefreshPluginLists();
            Assert.Empty(viewModel.OfficialPlugins);
            viewModel.SetInstalledPlugins([]);
            Assert.Equal("Install", Assert.Single(viewModel.OfficialPlugins).ActionText);
            Assert.False(viewModel.HasNoOfficialPlugins);
        }

        [Fact]
        public void EmptyList_PreservesDiscoveryFailureReason()
        {
            var viewModel = MainWindowViewModel.CreatePreview();
            viewModel.SetOfficialPlugins(OfficialPluginCatalogResult.Unavailable("GitHub rate limit reached."));
            Assert.True(viewModel.HasNoOfficialPlugins);
            Assert.Contains("rate limit", viewModel.PluginsStatus, StringComparison.Ordinal);
            Assert.Equal(viewModel.PluginsStatus, viewModel.OfficialPluginsEmptyMessage);
        }
    }
}
