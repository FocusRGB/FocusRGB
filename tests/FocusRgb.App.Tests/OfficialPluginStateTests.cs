using FocusRgb.App.Plugins;
using FocusRgb.App.ViewModels;

namespace FocusRgb.App.Tests
{
    public sealed class OfficialPluginStateTests
    {
        [Theory]
        [InlineData(null, "Install", true)]
        [InlineData("0.1.0", "Installed", false)]
        [InlineData("0.0.9", "Update to v0.1.0", true)]
        [InlineData("1.0.0", "Installed", false)]
        public void OfficialList_KeepsEntriesAndShowsInstallationState(string? installedVersion, string state, bool canAct)
        {
            var viewModel = MainWindowViewModel.CreatePreview();
            if (installedVersion is not null)
                viewModel.SetInstalledPlugins([new InstalledPluginInfo
                {
                    Id = "fake-output", DisplayName = "Fake", Version = installedVersion, IsOfficial = true
                }]);
            viewModel.SetOfficialPlugins(OfficialPluginCatalogResult.Fresh(DateTimeOffset.UtcNow,
                [PluginPackageCacheTests.Listing()], "Ready"));
            var item = Assert.Single(viewModel.OfficialPlugins);
            Assert.Equal(state, item.ActionText);
            Assert.Equal(canAct, item.CanInstall);
            Assert.False(viewModel.HasNoOfficialPlugins);
            viewModel.SetInstalledPlugins([]);
            Assert.Equal("Install", Assert.Single(viewModel.OfficialPlugins).ActionText);
        }

        [Fact]
        public void EmptyList_PreservesDiscoveryFailureReason()
        {
            var viewModel = MainWindowViewModel.CreatePreview();
            viewModel.SetOfficialPlugins(OfficialPluginCatalogResult.Unavailable("GitHub rate limit reached."));
            Assert.True(viewModel.HasNoOfficialPlugins);
            Assert.Contains("rate limit", viewModel.PluginsStatus, StringComparison.Ordinal);
        }
    }
}
