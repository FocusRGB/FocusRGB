using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using FocusRgb.App.ViewModels;
using FocusRgb.Core.Configuration;

namespace FocusRgb.App.HeadlessTests
{
    public sealed class GameCatalogHeadlessTests
    {
        [AvaloniaFact]
        public void GamesTab_ExplainsWhySharedExecutablesDoNotTriggerSpecificRules()
        {
            var viewModel = MainWindowViewModel.CreatePreview(SettingsJson.LoadDefaults().Settings!,
                new GameCatalogDocument
                {
                    SchemaVersion = 1,
                    Games = [new GameCatalogEntry
                    {
                        Launcher = "manual", Id = "first", DisplayName = "First", Available = true,
                        Executables = ["Shared.exe", "First.exe"]
                    }, new GameCatalogEntry
                    {
                        Launcher = "manual", Id = "second", DisplayName = "Second", Available = true,
                        Executables = ["SHARED.EXE", "Second.exe"]
                    }]
                });
            viewModel.EditingGame = viewModel.Games.First();
            var window = new MainWindow(viewModel);
            window.GetLogicalDescendants().OfType<TabItem>()
                .Single(tab => Equals(tab.Header, "Games")).IsSelected = true;
            window.Show();
            try
            {
                var notice = window.FindControl<TextBlock>("GameMatchingNotice")!;
                Assert.True(notice.IsVisible);
                Assert.Contains("Shared.exe", notice.Text, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("specific rules", notice.Text, StringComparison.Ordinal);
                Assert.DoesNotContain("First.exe", notice.Text, StringComparison.Ordinal);
            }
            finally
            {
                window.AllowClose();
                window.Close();
            }
        }

        [AvaloniaFact]
        public void DiscoveredGame_AllowsSavingExecutableChoicesWithoutEditingDiscoveredMetadata()
        {
            var viewModel = MainWindowViewModel.CreatePreview(
                SettingsJson.LoadDefaults().Settings!,
                new GameCatalogDocument
                {
                    SchemaVersion = GameCatalogJson.CurrentSchemaVersion,
                    Games =
                    [
                        new GameCatalogEntry
                        {
                            Launcher = "example",
                            Id = "sample-game",
                            DisplayName = "Sample Game",
                            Executables = ["Sample.exe", "Tool.exe"],
                            Available = true,
                            LastRefreshedAt = new DateTimeOffset(2026, 10, 24, 12, 0, 0, TimeSpan.Zero)
                        }
                    ]
                });
            viewModel.EditingGame = Assert.Single(viewModel.Games);
            var window = new MainWindow(viewModel);
            window.GetLogicalDescendants().OfType<TabItem>()
                .Single(tab => Equals(tab.Header, "Games")).IsSelected = true;

            window.Show();
            try
            {
                var executables = window.FindControl<ListBox>("DiscoveredGameExecutables")!;
                var editControls = window.FindControl<StackPanel>("GameEditControls")!;
                var tabs = window.GetLogicalDescendants().OfType<TabControl>().Single();
                tabs.SelectedItem = tabs.Items.OfType<TabItem>().Single(tab => Equals(tab.Header, "Games"));
                Assert.True(executables.IsVisible);
                Assert.Equal(["Sample.exe", "Tool.exe"], executables.Items.Cast<GameExecutableOptionViewModel>().Select(option => option.Name));
                window.UpdateLayout();
                Assert.True(executables.IsEffectivelyVisible);
                executables.ScrollIntoView(viewModel.GameExecutableOptions.Single(option => option.Name == "Tool.exe"));
                window.UpdateLayout();
                var checkboxes = executables.GetVisualDescendants().OfType<CheckBox>().ToArray();
                Assert.Contains("Tool.exe", checkboxes.Select(box => box.Content));
                var tool = checkboxes.Single(box => Equals(box.Content, "Tool.exe"));
                tool.IsChecked = false;
                var save = window.FindControl<Button>("SaveDiscoveredGameButton")!;
                Assert.True(save.IsEnabled);
                save.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(["Sample.exe"], viewModel.EditingGame!.MatchingExecutables);
                Assert.False(editControls.IsEnabled);
            }
            finally
            {
                window.AllowClose();
                window.Close();
            }
        }
    }
}
