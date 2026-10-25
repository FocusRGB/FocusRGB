using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using FocusRgb.App.ViewModels;
using FocusRgb.Core.Configuration;

namespace FocusRgb.App.HeadlessTests
{
    public sealed class GameCatalogHeadlessTests
    {
        [AvaloniaFact]
        public void DiscoveredGame_ShowsReadOnlyExecutableList()
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
                            Executables = ["Sample.exe"],
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
                var executables = window.FindControl<ListBox>("GameExecutableList")!;
                var editControls = window.FindControl<StackPanel>("GameEditControls")!;
                Assert.True(executables.IsVisible);
                Assert.Equal(["Sample.exe"], executables.Items.Cast<string>());
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
