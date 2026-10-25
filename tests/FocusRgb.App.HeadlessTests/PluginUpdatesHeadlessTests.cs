using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using FocusRgb.App.Plugins;
using FocusRgb.App.ViewModels;

namespace FocusRgb.App.HeadlessTests
{
    public sealed class PluginUpdatesHeadlessTests
    {
        [AvaloniaFact]
        public void PluginsTab_RendersCachedInstallAndInstalledUpdateActions()
        {
            var viewModel = MainWindowViewModel.CreatePreview();
            viewModel.SetInstalledPlugins([new InstalledPluginInfo
            {
                Id = "installed", DisplayName = "Installed", Version = "1.0.0", IsOfficial = true
            }]);
            viewModel.SetOfficialPlugins(OfficialPluginCatalogResult.Fresh(DateTimeOffset.UtcNow,
                [Listing("installed", "2.0.0"), Listing("cached", "1.0.0"), Listing("uncached", "1.0.0")], "Ready"),
                listing => listing.Id == "cached");
            var window = new MainWindow(viewModel);
            window.GetLogicalDescendants().OfType<TabItem>()
                .Single(tab => Equals(tab.Header, "Plugins")).IsSelected = true;
            window.Show();
            try
            {
                var buttons = window.GetLogicalDescendants().OfType<Button>().ToArray();
                Assert.Contains(buttons, button => Equals(button.Content, "Install")
                    && button.CommandParameter is OfficialPluginItemViewModel { Id: "cached" });
                Assert.Contains(buttons, button => Equals(button.Content, "Download")
                    && button.CommandParameter is OfficialPluginItemViewModel { Id: "uncached" });
                Assert.Contains(buttons, button => Equals(button.Content, "Update to v2.0.0") && button.IsVisible);
                Assert.DoesNotContain(viewModel.OfficialPlugins, item => item.Id == "installed");
            }
            finally
            {
                window.AllowClose();
                window.Close();
            }
        }

        private static OfficialPluginListing Listing(string id, string version) => new()
        {
            Id = id, Name = id, Version = version, RepositoryUrl = "https://github.com/FocusRGB/example",
            ReleaseUrl = "https://github.com/FocusRGB/example/releases", PackageUrl = "https://example.test/plugin.zip",
            PackageFileName = "plugin.zip", ManifestUrl = "https://example.test/focusrgb-plugin.json",
            ChecksumsUrl = "https://example.test/SHA256SUMS", ContractVersion = "1.2", IsCompatible = true
        };
    }
}
