using System.Globalization;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using FocusRgb.App.Plugins;

namespace FocusRgb.App
{
    public sealed partial class PluginSettingsWindow : Window
    {
        private static JsonSerializerOptions IndentedJson { get; } = new() { WriteIndented = true };
        private readonly Dictionary<string, Control> _editors = new(StringComparer.Ordinal);
        private PluginConfigurationSchema? _schema;

        public PluginSettingsWindow()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public PluginSettingsWindow(
            InstalledPluginInfo plugin,
            PluginConfigurationSchema schema,
            IReadOnlyDictionary<string, JsonElement> values) : this()
        {
            ArgumentNullException.ThrowIfNull(plugin);
            ArgumentNullException.ThrowIfNull(schema);
            ArgumentNullException.ThrowIfNull(values);
            _schema = schema;
            this.FindControl<TextBlock>("PluginNameText")!.Text = plugin.DisplayName;
            this.FindControl<TextBlock>("PluginDetailsText")!.Text =
                $"{plugin.Id} · {plugin.Version ?? "unknown version"}"
                + (string.IsNullOrWhiteSpace(schema.Description) ? string.Empty : $"\n{schema.Description}");
            Populate(values);
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
                        $"{field.DisplayName}: {exception.Message}";
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
                group.Children.Add(new TextBlock { Text = field.DisplayName, FontWeight = Avalonia.Media.FontWeight.SemiBold });
                if (!string.IsNullOrWhiteSpace(field.Description))
                    group.Children.Add(new TextBlock { Text = field.Description, Opacity = 0.7, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
                var editor = CreateEditor(field, values[field.Id]);
                editor.Name = $"PluginSetting_{field.Id}";
                group.Children.Add(editor);
                panel.Children.Add(group);
                _editors.Add(field.Id, editor);
            }
        }

        private static Control CreateEditor(PluginConfigurationField field, JsonElement value) => field.Type switch
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

        private static JsonElement ReadValue(PluginConfigurationField field, Control editor)
        {
            if (field.Type == "boolean") return JsonSerializer.SerializeToElement(((CheckBox)editor).IsChecked == true);
            var text = ((TextBox)editor).Text ?? string.Empty;
            return field.Type switch
            {
                "string" => JsonSerializer.SerializeToElement(text),
                "integer" => JsonSerializer.SerializeToElement(long.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture)),
                "number" => JsonSerializer.SerializeToElement(double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture)),
                "json" => JsonDocument.Parse(text).RootElement.Clone(),
                _ => throw new FormatException("Unsupported field type.")
            };
        }

        private static string ValueText(JsonElement value) => value.ValueKind == JsonValueKind.String
            ? value.GetString()!
            : value.GetRawText();

        private void Reset_OnClick(object? sender, RoutedEventArgs e)
        {
            Populate(PluginConfigurationStore.Defaults(_schema!));
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
