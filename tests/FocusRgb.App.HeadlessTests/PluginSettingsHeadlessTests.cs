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
        public void Configure_UsesInstalledPluginDisplayNameInHeaderAndTitle()
        {
            using var temporary = new FocusRgb.TestSupport.TempDirectory(nameof(PluginSettingsHeadlessTests));
            Directory.CreateDirectory(Path.Combine(temporary.Path, "steam"));
            using var inventory = new PluginInventory(temporary.Path);
            var plugin = Assert.Single(inventory.List([], []));
            var window = new PluginSettingsWindow(plugin, new PluginConfigurationSchema { SchemaVersion = 1, Fields = [] },
                new Dictionary<string, JsonElement>());
            window.Show();
            try
            {
                Assert.Equal("Steam settings", window.Title);
                Assert.Equal("Steam", window.FindControl<TextBlock>("PluginNameText")!.Text);
            }
            finally { window.Close(); }
        }

        [AvaloniaFact]
        public void UnknownFormat_UsesNormalEditorAndRejectsOversizedStoredPath()
        {
            using var temporary = new FocusRgb.TestSupport.TempDirectory(nameof(PluginSettingsHeadlessTests));
            var packages = Path.Combine(temporary.Path, "plugins");
            var data = Path.Combine(temporary.Path, "data");
            Directory.CreateDirectory(Path.Combine(packages, "example"));
            Directory.CreateDirectory(Path.Combine(data, "example"));
            File.WriteAllText(Path.Combine(packages, "example", PluginConfigurationStore.SchemaFileName),
                """{"schemaVersion":1,"fields":[{"id":"root","displayName":"Root","type":"string","format":"future-picker","default":"C:/Default"}]}""");
            File.WriteAllText(Path.Combine(data, "example", "settings.json"),
                JsonSerializer.Serialize(new { root = new string('x', 4097) }));
            using var store = new PluginConfigurationStore(packages, data);
            var loaded = store.Load("example");
            Assert.True(loaded.IsSuccess, loaded.Error);
            Assert.NotEmpty(loaded.Notices);
            Assert.Equal("C:/Default", loaded.Values!["root"].GetString());
            var window = new PluginSettingsWindow(Plugin("example", true), loaded.Schema!, loaded.Values!);
            window.Show();
            try
            {
                var editor = window.GetLogicalDescendants().OfType<TextBox>().Single();
                Assert.Equal("C:/Default", editor.Text);
                Assert.DoesNotContain(window.GetLogicalDescendants().OfType<Button>(), button => button.Name == "Browse_root");
                editor.Text = new string('x', 4097);
                Assert.Null(window.CollectValues());
                editor.Text = "D:/Valid";
                Assert.Equal("D:/Valid", window.CollectValues()!["root"].GetString());
            }
            finally
            {
                window.Close();
            }
        }

        [AvaloniaFact]
        public void FolderEditors_BrowseAddRemoveRoundTripAndResetDetectedDefaults()
        {
            var schema = new PluginConfigurationSchema
            {
                SchemaVersion = 1,
                Fields = [new PluginConfigurationField
                {
                    Id = "root", DisplayName = "Root", Type = "string", Format = "directory",
                    Default = JsonSerializer.SerializeToElement("")
                }, new PluginConfigurationField
                {
                    Id = "folders", DisplayName = "Libraries", Type = "json", Format = "directory-list",
                    Default = JsonSerializer.SerializeToElement(Array.Empty<string>())
                }]
            };
            var defaults = new Dictionary<string, JsonElement>
            {
                ["root"] = JsonSerializer.SerializeToElement("C:/Detected"),
                ["folders"] = JsonSerializer.SerializeToElement<string[]>(["D:/Existing"])
            };
            var valuesWithEmptyRoot = defaults.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            valuesWithEmptyRoot["root"] = JsonSerializer.SerializeToElement("");
            var window = new PluginSettingsWindow(Plugin("example", true), schema, valuesWithEmptyRoot, defaults: defaults)
            {
                FolderPicker = multiple => Task.FromResult<IReadOnlyList<string>>(
                    multiple ? ["E:/Added", "F:/Added"] : ["C:/Chosen"])
            };
            window.Show();
            try
            {
                var root = window.GetLogicalDescendants().OfType<TextBox>().Single(box => box.Name == "PluginSetting_root");
                Assert.Equal("C:/Detected", root.Text);
                window.GetLogicalDescendants().OfType<Button>().Single(button => button.Name == "Browse_root")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal("C:/Chosen", root.Text);
                window.GetLogicalDescendants().OfType<Button>().Single(button => button.Name == "AddFolders_folders")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                window.GetLogicalDescendants().OfType<Button>().First(button => Equals(button.Content, "Remove"))
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var values = window.CollectValues()!;
                Assert.Equal("C:/Chosen", values["root"].GetString());
                Assert.Equal(["E:/Added", "F:/Added"], values["folders"].EnumerateArray().Select(item => item.GetString()));
                Assert.Single(window.GetLogicalDescendants().OfType<TextBox>());
                window.FindControl<Button>("ResetButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var reset = window.CollectValues()!;
                Assert.Equal("C:/Detected", reset["root"].GetString());
                Assert.Equal(["D:/Existing"], reset["folders"].EnumerateArray().Select(item => item.GetString()));
            }
            finally
            {
                window.Close();
            }
        }

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
