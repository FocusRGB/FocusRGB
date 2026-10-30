using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using FocusRgb.App.ViewModels;
using FocusRgb.Core.Configuration;

namespace FocusRgb.App.HeadlessTests
{
    public sealed class FocusEditingHeadlessTests
    {
        [AvaloniaFact]
        public async Task RemoveRow_PersistsWithoutFooterSaveControls()
        {
            var model = MainWindowViewModel.CreatePreview();
            await model.SetModeAsync(false);
            var window = new MainWindow(model);
            window.Show();
            try
            {
                var item = model.MappingEditor.Mappings[0];
                var count = model.MappingEditor.Mappings.Count;
                Assert.DoesNotContain(window.GetLogicalDescendants().OfType<Button>(), button => Equals(button.Content, "Save order"));
                window.GetLogicalDescendants().OfType<Button>()
                    .First(button => Equals(button.Content, "Remove") && ReferenceEquals(button.CommandParameter, item))
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.False(model.MappingEditor.IsDirty);
                Assert.Equal(count - 1, model.MappingEditor.AppliedMappings.Count);
            }
            finally { window.AllowClose(); window.Close(); }
        }

        [AvaloniaFact]
        public async Task SelectionRequiresEdit_AndSavedStatusFades()
        {
            var viewModel = MainWindowViewModel.CreatePreview();
            await viewModel.SetModeAsync(false);
            var window = new MainWindow(viewModel);
            window.Show();
            try
            {
                var item = viewModel.MappingEditor.Mappings[0];
                window.FindControl<ListBox>("FocusList")!.SelectedItem = item;
                Assert.False(viewModel.MappingEditor.HasEditor);
                window.GetLogicalDescendants().OfType<Button>()
                    .First(button => Equals(button.Content, "Edit") && ReferenceEquals(button.CommandParameter, item))
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.True(viewModel.MappingEditor.HasEditor);
                viewModel.MappingEditor.EditingMapping!.Eligibility = "while-running";
                window.FindControl<Button>("SaveFocusButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Task.Delay(100, TestContext.Current.CancellationToken);
                Assert.False(viewModel.MappingEditor.IsDirty);
                Assert.Equal("while-running", viewModel.MappingEditor.Mappings[0].Eligibility);
                await Task.Delay(3400, TestContext.Current.CancellationToken);
                Assert.Equal(0, window.FindControl<TextBlock>("FocusSaveStatus")!.Opacity);
            }
            finally { window.AllowClose(); window.Close(); }
        }

        [AvaloniaFact]
        public async Task EmptyLibrary_OffersUnifiedCreateEditor()
        {
            var json = JsonNode.Parse(SettingsJson.Serialize(SettingsJson.LoadDefaults().Settings!))!;
            json["rules"] = new JsonArray();
            var viewModel = MainWindowViewModel.CreatePreview(SettingsJson.Load(json.ToJsonString()).Settings!);
            await viewModel.SetModeAsync(false);
            var window = new MainWindow(viewModel);
            window.Show();
            try
            {
                var create = window.FindControl<Button>("CreateEmptyFocusButton")!;
                Assert.True(create.IsVisible);
                create.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.True(viewModel.MappingEditor.HasEditor);
                Assert.NotNull(viewModel.MappingEditor.EditingMapping);
            }
            finally { window.AllowClose(); window.Close(); }
        }
    }
}
