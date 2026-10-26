using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Interactivity;
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

        [AvaloniaFact]
        public void Save_ShowsValidationErrorForNonFiniteNumbersAndIntegerOverflow()
        {
            foreach (var (type, input) in new[]
            {
                ("number", "NaN"), ("number", "Infinity"), ("number", "-Infinity"),
                ("number", "1e999"), ("integer", "9223372036854775808"),
                ("integer", "-9223372036854775809")
            })
            {
                var schema = new PluginConfigurationSchema
                {
                    SchemaVersion = 1,
                    Fields = [new PluginConfigurationField
                    {
                        Id = "value", DisplayName = "Value", Type = type,
                        Default = JsonSerializer.SerializeToElement(1)
                    }]
                };
                var window = new PluginSettingsWindow(Plugin("example", true), schema,
                    PluginConfigurationStore.Defaults(schema));
                window.Show();
                try
                {
                    window.GetLogicalDescendants().OfType<TextBox>()
                        .Single(box => box.Name == "PluginSetting_value").Text = input;

                    window.FindControl<Button>("SaveButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

                    Assert.True(window.IsVisible);
                    Assert.Contains("Value:", window.FindControl<TextBlock>("StatusText")!.Text, StringComparison.Ordinal);
                }
                finally
                {
                    window.Close();
                }
            }
        }

        [AvaloniaFact]
        public void SettingsWindow_SanitizesPluginDisplayTextAndShowsRepairNotice()
        {
            var schema = new PluginConfigurationSchema
            {
                SchemaVersion = 1, Description = "Description\n\u202e\u200b",
                Fields = [new PluginConfigurationField
                {
                    Id = "value", DisplayName = "Name\u202e\u200b\t", Description = "Help\u2066\u200b\r",
                    Type = "string", Default = JsonSerializer.SerializeToElement("")
                }]
            };
            var window = new PluginSettingsWindow(Plugin("example", true), schema,
                PluginConfigurationStore.Defaults(schema), ["Restored invalid saved settings."]);
            window.Show();
            try
            {
                Assert.EndsWith("Description", window.FindControl<TextBlock>("PluginDetailsText")!.Text, StringComparison.Ordinal);
                var texts = window.GetLogicalDescendants().OfType<TextBlock>().Select(block => block.Text).ToArray();
                Assert.Contains("Name", texts);
                Assert.Contains("Help", texts);
                Assert.Contains("Restored invalid saved settings.", texts);
            }
            finally
            {
                window.Close();
            }
        }

        [AvaloniaFact]
        public void RecoveredSettings_OpenWithNoticeAndReset_WhileCancelPreservesBrokenFile()
        {
            using var temporary = new FocusRgb.TestSupport.TempDirectory(nameof(PluginSettingsHeadlessTests));
            var packages = Path.Combine(temporary.Path, "plugins");
            var data = Path.Combine(temporary.Path, "plugin-data");
            Directory.CreateDirectory(Path.Combine(packages, "example"));
            Directory.CreateDirectory(Path.Combine(data, "example"));
            File.WriteAllText(Path.Combine(packages, "example", PluginConfigurationStore.SchemaFileName),
                """{"schemaVersion":1,"fields":[{"id":"port","displayName":"Port","type":"integer","default":3000}]}""");
            var path = Path.Combine(data, "example", "settings.json");
            File.WriteAllText(path, "{broken");
            using var store = new PluginConfigurationStore(packages, data);
            var loaded = store.Load("example");
            Assert.True(loaded.IsSuccess, loaded.Error);
            var window = new PluginSettingsWindow(Plugin("example", true), loaded.Schema!, loaded.Values!, loaded.Notices);
            window.Show();
            try
            {
                Assert.Contains("backup", window.FindControl<TextBlock>("StatusText")!.Text!, StringComparison.Ordinal);
                var port = window.GetLogicalDescendants().OfType<TextBox>().Single(box => box.Name == "PluginSetting_port");
                port.Text = "4567";
                window.FindControl<Button>("ResetButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal("3000", window.GetLogicalDescendants().OfType<TextBox>().Single(box => box.Name == "PluginSetting_port").Text);
                window.GetLogicalDescendants().OfType<Button>().Single(button => Equals(button.Content, "Cancel"))
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.False(window.IsVisible);
                Assert.Equal("{broken", File.ReadAllText(path));
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
