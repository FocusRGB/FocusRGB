using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using FocusRgb.App.ViewModels;

namespace FocusRgb.App.HeadlessTests
{
    public sealed class FocusReorderHeadlessTests
    {
        [AvaloniaFact]
        public async Task DragNearEdge_AutoScrollsWhileKeepingLiftedRowInList()
        {
            var viewModel = MainWindowViewModel.CreatePreview();
            await viewModel.SetModeAsync(false);
            var editor = viewModel.MappingEditor;
            for (var index = 0; index < 15; index++) editor.SaveMappingAs();
            editor.Apply();
            editor.SelectedMapping = editor.Mappings[0];
            var original = editor.Mappings.ToArray();
            var window = new MainWindow(viewModel);
            window.Show();
            try
            {
                window.UpdateLayout();
                var list = window.FindControl<ListBox>("FocusList")!;
                var scroll = list.GetVisualDescendants().OfType<ScrollViewer>().First();
                var row = list.GetVisualDescendants().OfType<Grid>().First(grid => grid.Name == "FocusRow");
                var start = row.TranslatePoint(new Point(row.Bounds.Width / 2, row.Bounds.Height / 2), window)!.Value;
                var bottom = list.TranslatePoint(new Point(list.Bounds.Width / 2, list.Bounds.Height - 4), window)!.Value;
                window.MouseDown(start, MouseButton.Left);
                window.MouseMove(bottom, RawInputModifiers.LeftMouseButton);
                await Task.Delay(250);
                window.UpdateLayout();
                Assert.True(scroll.Offset.Y > 0);
                var container = row.FindAncestorOfType<ListBoxItem>()!;
                var top = container.TranslatePoint(default, list)!.Value.Y;
                Assert.InRange(top, -1, list.Bounds.Height - container.Bounds.Height + 1);
                Assert.Equal(original, editor.Mappings);
                Assert.False(editor.IsDirty);
                window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
                window.MouseUp(bottom, MouseButton.Left);
                Assert.Equal(original, editor.Mappings);
                Assert.False(editor.IsDirty);
            }
            finally
            {
                window.AllowClose();
                window.Close();
            }
        }

        [AvaloniaTheory]
        [InlineData("drop", false)]
        [InlineData("escape", false)]
        [InlineData("capture-loss", false)]
        [InlineData("drop", true)]
        public async Task PointerReorder_PreviewsWithoutDraftChangesAndCommitsOnlyOnDrop(string finish, bool editing)
        {
            var viewModel = MainWindowViewModel.CreatePreview();
            await viewModel.SetModeAsync(false);
            var editor = viewModel.MappingEditor;
            editor.LivePreview = true;
            if (editing)
            {
                editor.BeginEdit(editor.Mappings[0]);
                editor.EditingMapping!.Eligibility = "while-running";
            }
            var revision = editor.PreviewRevision;
            var original = editor.Mappings.ToArray();
            var window = new MainWindow(viewModel);
            IPointer? captured = null;
            window.AddHandler(InputElement.PointerPressedEvent, (_, args) => captured = args.Pointer, RoutingStrategies.Tunnel);
            window.Show();
            try
            {
                var draftStatus = ObserveDraftStatus(window, editor, editing);
                window.UpdateLayout();
                var rows = window.GetVisualDescendants().OfType<Grid>().Where(grid => grid.Name == "FocusRow").ToArray();
                var start = rows[2].TranslatePoint(new Point(rows[2].Bounds.Width / 2, rows[2].Bounds.Height / 2), window)!.Value;
                var target = rows[0].TranslatePoint(new Point(rows[0].Bounds.Width / 2, rows[0].Bounds.Height / 4), window)!.Value;
                window.MouseDown(start, MouseButton.Left);
                Assert.Same(rows[2], captured!.Captured);
                Assert.Null(editor.ReorderingMapping);
                window.MouseMove(target, RawInputModifiers.LeftMouseButton);
                Assert.Same(original[2], editor.ReorderingMapping);
                Assert.Equal(0, editor.ReorderIndex);
                Assert.Equal(original, editor.Mappings);
                Assert.Equal(editing, editor.IsDirty);
                Assert.Equal(editing, draftStatus.IsVisible);
                Assert.Equal(revision, editor.PreviewRevision);
                Assert.True(rows[2].GetVisualDescendants().OfType<Border>().Single(border => border.Name == "FocusLiftSurface").IsVisible);
                var lifted = rows[2].FindAncestorOfType<ListBoxItem>()!;
                Assert.Equal(1, lifted.ZIndex);
                Assert.True(Assert.IsType<TranslateTransform>(lifted.RenderTransform).Y < 0);
                var displaced = rows[0].FindAncestorOfType<ListBoxItem>()!;
                Assert.True(Assert.IsType<TranslateTransform>(displaced.RenderTransform).GetBaseValue(TranslateTransform.YProperty).Value > 0);

                if (finish == "escape") window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
                if (finish == "capture-loss") captured.Capture(null);
                window.MouseUp(target, MouseButton.Left);

                Assert.Null(captured.Captured);
                Assert.Null(editor.ReorderingMapping);
                Assert.Equal(finish == "drop" ? [original[2], original[0], original[1]] : original, editor.Mappings);
                Assert.Equal(editing, editor.IsDirty);
                Assert.Equal(editing, draftStatus.IsVisible);
                Assert.Equal(revision + (finish == "drop" ? 1 : 0), editor.PreviewRevision);
                Assert.False(rows[2].GetVisualDescendants().OfType<Border>().Single(border => border.Name == "FocusLiftSurface").IsVisible);
            }
            finally
            {
                window.AllowClose();
                window.Close();
            }
        }

        [AvaloniaTheory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Hold_LiftsRowButNeverStartsFromButton(bool pressButton)
        {
            var viewModel = MainWindowViewModel.CreatePreview();
            await viewModel.SetModeAsync(false);
            var window = new MainWindow(viewModel);
            window.Show();
            try
            {
                window.UpdateLayout();
                var row = window.GetVisualDescendants().OfType<Grid>().First(grid => grid.Name == "FocusRow");
                Control target = pressButton ? row.GetVisualDescendants().OfType<Button>().First(button => button.IsEnabled) : row;
                var position = target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), window)!.Value;
                window.MouseDown(position, MouseButton.Left);
                await Task.Delay(350);
                Assert.Equal(!pressButton, viewModel.MappingEditor.ReorderingMapping is not null);
                window.MouseUp(position, MouseButton.Left);
                Assert.Null(viewModel.MappingEditor.ReorderingMapping);
            }
            finally
            {
                window.AllowClose();
                window.Close();
            }
        }

        [AvaloniaTheory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task StepButtons_ReflectEdgesAndMoveOnlyOneRow(bool editing)
        {
            var viewModel = MainWindowViewModel.CreatePreview();
            await viewModel.SetModeAsync(false);
            if (editing)
            {
                viewModel.MappingEditor.BeginEdit(viewModel.MappingEditor.Mappings[0]);
                viewModel.MappingEditor.EditingMapping!.Eligibility = "while-running";
            }
            var window = new MainWindow(viewModel);
            window.Show();
            try
            {
                var draftStatus = ObserveDraftStatus(window, viewModel.MappingEditor, editing);
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
                Assert.Equal(editing, viewModel.MappingEditor.IsDirty);
                Assert.Equal(editing, draftStatus.IsVisible);
                window.UpdateLayout();
                var movedUp = window.GetVisualDescendants().OfType<Button>().Single(button =>
                    Equals(button.Content, "▲") && ReferenceEquals(button.CommandParameter, first));
                movedUp.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Same(first, viewModel.MappingEditor.Mappings[0]);
                Assert.Equal(editing, viewModel.MappingEditor.IsDirty);
                Assert.Equal(editing, draftStatus.IsVisible);
            }
            finally
            {
                window.AllowClose();
                window.Close();
            }
        }
        private static TextBlock ObserveDraftStatus(MainWindow window, MappingEditorViewModel editor, bool editing)
        {
            var status = window.FindControl<TextBlock>("FocusDraftStatusText")!;
            Assert.Equal(editing, editor.IsDirty);
            Assert.Equal(editing, status.IsVisible);
            editor.PropertyChanged += (_, _) => Assert.Equal(editing, editor.IsDirty);
            status.PropertyChanged += (_, args) =>
            {
                if (args.Property == Visual.IsVisibleProperty) Assert.Equal(editing, status.IsVisible);
            };
            return status;
        }
    }
}
