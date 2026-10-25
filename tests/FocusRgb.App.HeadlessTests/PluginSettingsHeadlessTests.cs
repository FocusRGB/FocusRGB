using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using FocusRgb.App.Plugins;
using FocusRgb.App.ViewModels;

namespace FocusRgb.App.HeadlessTests
{
    public sealed class PluginSettingsHeadlessTests
    {
        [AvaloniaFact]
        public void PluginsTab_EnablesConfigureOnlyForDeclaredConfiguration()
        {
            var viewModel = MainWindowViewModel.CreatePreview();
            viewModel.SetInstalledPlugins([
                Plugin("configurable", hasConfiguration: true),
                Plugin("plain", hasConfiguration: false)
            ]);
            var window = new MainWindow(viewModel);
            window.GetLogicalDescendants().OfType<TabItem>()
                .Single(tab => Equals(tab.Header, "Plugins")).IsSelected = true;
            window.Show();
            try
            {
                var configure = window.GetLogicalDescendants().OfType<Button>()
                    .Where(button => Equals(button.Content, "Configure")
                        && button.CommandParameter is InstalledPluginItemViewModel)
                    .GroupBy(button => ((InstalledPluginItemViewModel)button.CommandParameter!).Id)
                    .ToDictionary(group => group.Key, group => group.ToArray());
                Assert.Equal(["configurable", "plain"], configure.Keys.Order());
                Assert.All(configure["configurable"], button => Assert.True(button.IsEnabled));
                Assert.All(configure["plain"], button => Assert.False(button.IsEnabled));
            }
            finally
            {
                window.AllowClose();
                window.Close();
            }
        }

        [AvaloniaFact]
        public void PluginSettingsWindow_RendersDeclaredFieldsAndMetadata()
        {
            var schema = new PluginConfigurationSchema
            {
                SchemaVersion = 1,
                Fields =
                [
                    new PluginConfigurationField
                    {
                        Id = "port",
                        DisplayName = "Port",
                        Description = "Loopback port",
                        Type = "integer",
                        Default = JsonSerializer.SerializeToElement(3000)
                    },
                    new PluginConfigurationField
                    {
                        Id = "enabled",
                        DisplayName = "Enabled",
                        Type = "boolean",
                        Default = JsonSerializer.SerializeToElement(true)
                    }
                ]
            };
            var window = new PluginSettingsWindow(
                Plugin("configurable", true), schema, PluginConfigurationStore.Defaults(schema));
            window.Show();
            try
            {
                Assert.Equal("configurable", window.FindControl<TextBlock>("PluginNameText")!.Text);
                Assert.Contains(window.GetLogicalDescendants().OfType<TextBox>(),
                    control => control.Name == "PluginSetting_port");
                Assert.Contains(window.GetLogicalDescendants().OfType<CheckBox>(),
                    control => control.Name == "PluginSetting_enabled");
                Assert.NotNull(window.FindControl<Button>("SaveButton"));
                Assert.NotNull(window.FindControl<Button>("ResetButton"));
            }
            finally
            {
                window.Close();
            }
        }

        private static InstalledPluginInfo Plugin(string id, bool hasConfiguration) => new()
        {
            Id = id,
            DisplayName = id,
            Version = "1.0.0",
            HasConfiguration = hasConfiguration
        };
    }
}
