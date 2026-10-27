using System.Globalization;
using System.Text;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using FocusRgb.App.Plugins;

namespace FocusRgb.App
{
    public sealed partial class PluginSettingsWindow : Window
    {
        private static JsonSerializerOptions IndentedJson { get; } = new() { WriteIndented = true };
        private readonly Dictionary<string, Control> _editors = new(StringComparer.Ordinal);
        private PluginConfigurationSchema? _schema;
        private IReadOnlyDictionary<string, JsonElement>? _defaults;
        internal Func<bool, Task<IReadOnlyList<string>>> FolderPicker { get; set; }

        public PluginSettingsWindow()
        {
            AvaloniaXamlLoader.Load(this);
            FolderPicker = PickLocalFoldersAsync;
        }

        public PluginSettingsWindow(
            InstalledPluginInfo plugin,
            PluginConfigurationSchema schema,
            IReadOnlyDictionary<string, JsonElement> values,
            IReadOnlyList<string>? notices = null,
            IReadOnlyDictionary<string, JsonElement>? defaults = null) : this()
        {
            ArgumentNullException.ThrowIfNull(plugin);
            ArgumentNullException.ThrowIfNull(schema);
            ArgumentNullException.ThrowIfNull(values);
            _schema = schema;
            _defaults = defaults ?? PluginConfigurationStore.Defaults(schema);
            this.FindControl<TextBlock>("PluginNameText")!.Text = SafeDisplayText(plugin.DisplayName);
            this.FindControl<TextBlock>("PluginDetailsText")!.Text =
                $"{plugin.Id} · {plugin.Version ?? "unknown version"}"
                + (string.IsNullOrWhiteSpace(schema.Description) ? string.Empty : $"\n{SafeDisplayText(schema.Description)}");
            Populate(values);
            this.FindControl<TextBlock>("StatusText")!.Text = string.Join(Environment.NewLine, notices ?? []);
        }

        internal IReadOnlyDictionary<string, JsonElement>? CollectValues()
        {
            if (_schema is null) return null;
            var values = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (var field in _schema.Fields)
            {
                try
                {
                    values[field.Id] = ReadValue(field, _editors[field.Id]);
                }
                catch (Exception exception) when (exception is FormatException or JsonException)
                {
                    this.FindControl<TextBlock>("StatusText")!.Text =
                        $"{SafeDisplayText(field.DisplayName)}: {exception.Message}";
                    return null;
                }
            }
            var error = PluginConfigurationStore.Validate(_schema, values);
            this.FindControl<TextBlock>("StatusText")!.Text = error;
            return error is null ? values : null;
        }

        private void Populate(IReadOnlyDictionary<string, JsonElement> values)
        {
            var panel = this.FindControl<StackPanel>("FieldsPanel")!;
            panel.Children.Clear();
            _editors.Clear();
            foreach (var field in _schema!.Fields)
            {
                var group = new StackPanel { Spacing = 4 };
                group.Children.Add(new TextBlock { Text = SafeDisplayText(field.DisplayName), FontWeight = Avalonia.Media.FontWeight.SemiBold });
                if (!string.IsNullOrWhiteSpace(field.Description))
                    group.Children.Add(new TextBlock { Text = SafeDisplayText(field.Description), Opacity = 0.7, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
                var value = values[field.Id];
                if (field.Format == "directory" && value.GetString() == ""
                    && _defaults!.TryGetValue(field.Id, out var detected) && detected.ValueKind == JsonValueKind.String)
                    value = detected;
                var editor = CreateEditor(field, value);
                editor.Name = $"PluginSetting_{field.Id}";
                if (field.Format == "directory")
                {
                    var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
                    row.Children.Add(editor);
                    var browse = new Button { Content = "Browse…", Name = $"Browse_{field.Id}" };
                    Grid.SetColumn(browse, 1);
                    browse.Click += async (_, _) => await BrowseFoldersAsync(
                        paths => { if (paths.Count > 0) ((TextBox)editor).Text = paths[0]; }, false);
                    row.Children.Add(browse);
                    group.Children.Add(row);
                }
                else
                {
                    group.Children.Add(editor);
                }
                panel.Children.Add(group);
                _editors.Add(field.Id, editor);
            }
        }

        private Control CreateEditor(PluginConfigurationField field, JsonElement value)
        {
            if (field.Format == "directory-list")
            {
                var list = new FolderListEditor(value.EnumerateArray().Select(item => item.GetString()!));
                var add = new Button { Content = "Add folder…", Name = $"AddFolders_{field.Id}" };
                add.Click += async (_, _) => await BrowseFoldersAsync(list.AddFolders, true);
                list.Children.Add(add);
                return list;
            }
            return field.Type switch
        {
            "boolean" => new CheckBox { IsChecked = value.GetBoolean() },
            "json" => new TextBox
            {
                Text = JsonSerializer.Serialize(value, IndentedJson),
                AcceptsReturn = true,
                MinHeight = 100,
                TextWrapping = Avalonia.Media.TextWrapping.Wrap
            },
            _ => new TextBox { Text = ValueText(value) }
            };
        }

        private async Task<IReadOnlyList<string>> PickLocalFoldersAsync(bool multiple)
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Choose plugin folders", AllowMultiple = multiple
            });
            return folders.Select(folder => folder.TryGetLocalPath()).OfType<string>().ToArray();
        }

        private async Task BrowseFoldersAsync(Action<IReadOnlyList<string>> apply, bool multiple)
        {
            try
            {
                apply(await FolderPicker(multiple));
            }
            catch (Exception exception)
            {
                this.FindControl<TextBlock>("StatusText")!.Text = $"Could not choose a folder: {SafeDisplayText(exception.Message)}";
            }
        }

        private sealed class FolderListEditor : StackPanel
        {
            private readonly StackPanel _rows = new() { Spacing = 4 };
            public List<string> Folders { get; } = [];

            public FolderListEditor(IEnumerable<string> folders)
            {
                Spacing = 8;
                Children.Add(_rows);
                AddFolders(folders.ToArray());
            }

            public void AddFolders(IReadOnlyList<string> folders)
            {
                foreach (var path in folders)
                {
                    if (Folders.Contains(path, StringComparer.OrdinalIgnoreCase)) continue;
                    Folders.Add(path);
                    var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
                    row.Children.Add(new TextBlock { Text = SafeDisplayText(path), TextWrapping = Avalonia.Media.TextWrapping.Wrap });
                    var remove = new Button { Content = "Remove" };
                    Avalonia.Automation.AutomationProperties.SetName(remove, $"Remove folder {SafeDisplayText(path)}");
                    Grid.SetColumn(remove, 1);
                    remove.Click += (_, _) => { Folders.Remove(path); _rows.Children.Remove(row); };
                    row.Children.Add(remove);
                    _rows.Children.Add(row);
                }
            }
        }

        private static JsonElement ReadValue(PluginConfigurationField field, Control editor)
        {
            if (field.Type == "boolean") return JsonSerializer.SerializeToElement(((CheckBox)editor).IsChecked == true);
            if (editor is FolderListEditor folders) return JsonSerializer.SerializeToElement(folders.Folders);
            var text = ((TextBox)editor).Text ?? string.Empty;
            return field.Type switch
            {
                "string" => JsonSerializer.SerializeToElement(text),
                "integer" when long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer)
                    => JsonSerializer.SerializeToElement(integer),
                "integer" => throw new FormatException("Enter a whole number within the 64-bit integer range."),
                "number" when double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
                    && double.IsFinite(number) => JsonSerializer.SerializeToElement(number),
                "number" => throw new FormatException("Enter a finite number."),
                "json" => JsonDocument.Parse(text).RootElement.Clone(),
                _ => throw new FormatException("Unsupported field type.")
            };
        }

        private static string ValueText(JsonElement value) => value.ValueKind == JsonValueKind.String
            ? value.GetString()!
            : value.GetRawText();

        private static string SafeDisplayText(string text)
        {
            var safe = new StringBuilder(text.Length);
            foreach (var rune in text.EnumerateRunes())
            {
                if (Rune.GetUnicodeCategory(rune) is not (UnicodeCategory.Control or UnicodeCategory.Format))
                {
                    safe.Append(rune.ToString());
                }
            }
            return safe.ToString();
        }

        private void Reset_OnClick(object? sender, RoutedEventArgs e)
        {
            Populate(_defaults!);
            this.FindControl<TextBlock>("StatusText")!.Text = "Defaults restored in the editor. Save to apply them.";
        }

        private void Cancel_OnClick(object? sender, RoutedEventArgs e) =>
            Close((IReadOnlyDictionary<string, JsonElement>?)null);

        private void Save_OnClick(object? sender, RoutedEventArgs e)
        {
            var values = CollectValues();
            if (values is not null) Close(values);
        }
    }
}
