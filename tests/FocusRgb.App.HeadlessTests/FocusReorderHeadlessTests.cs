using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using FocusRgb.App.ViewModels;

namespace FocusRgb.App.HeadlessTests
{
    public sealed class FocusReorderHeadlessTests
    {
        [AvaloniaFact]
        public async Task StepButtons_ReflectEdgesAndMoveOnlyOneRow()
        {
            var viewModel = MainWindowViewModel.CreatePreview();
            await viewModel.SetModeAsync(false);
            var window = new MainWindow(viewModel);
            window.Show();
            try
            {
                var first = viewModel.MappingEditor.Mappings[0];
                var last = viewModel.MappingEditor.Mappings.Last();
                var buttons = window.GetVisualDescendants().OfType<Button>().ToArray();
                var up = buttons.Single(button => Equals(button.Content, "▲") && ReferenceEquals(button.CommandParameter, first));
                var down = buttons.Single(button => Equals(button.Content, "▼") && ReferenceEquals(button.CommandParameter, first));
                Assert.False(up.IsEnabled);
                Assert.True(down.IsEnabled);
                Assert.Equal(first.MoveDownAccessibleName, AutomationProperties.GetName(down));
                Assert.False(buttons.Single(button => Equals(button.Content, "▼") && ReferenceEquals(button.CommandParameter, last)).IsEnabled);
                Assert.DoesNotContain(buttons, button => Equals(button.Content, "⇈") || Equals(button.Content, "⇊"));
                var rows = window.GetVisualDescendants().OfType<Grid>().Where(grid => grid.Name == "FocusRow").ToArray();
                Assert.Equal(3, rows.Length);
                Assert.All(rows, row => Assert.Equal(Colors.Transparent, Assert.IsAssignableFrom<ISolidColorBrush>(row.Background).Color));
                down.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Same(first, viewModel.MappingEditor.Mappings[1]);
                Assert.True(viewModel.MappingEditor.IsDirty);
            }
            finally
            {
                window.AllowClose();
                window.Close();
            }
        }
    }
}
