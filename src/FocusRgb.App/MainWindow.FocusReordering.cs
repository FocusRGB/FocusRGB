using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FocusRgb.App.ViewModels;

namespace FocusRgb.App
{
    public sealed partial class MainWindow
    {
        private IPointer? _mappingPointer;
        private Grid? _mappingPressedRow;
        private MappingEditorViewModel? _mappingDragEditor;
        private Point _mappingDragStart;
        private Point _mappingPointerPosition;
        private IDisposable? _mappingHoldTimer;
        private IDisposable? _mappingScrollTimer;
        private ScrollViewer? _mappingScroll;
        private FocusDragRow[] _mappingDragRows = [];
        private FocusDragRow? _mappingLiftedRow;

        private sealed record FocusDragRow(
            MappingEditorItemViewModel Item, ListBoxItem Container, Grid Row,
            double Top, double Height, TranslateTransform Transform);

        private IEnumerable<ListBoxItem> FocusRowContainers() =>
            FocusList.GetVisualDescendants().OfType<ListBoxItem>()
                .Where(container => container.DataContext is MappingEditorItemViewModel);

        private void MappingDrag_OnPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (sender is not Grid { DataContext: MappingEditorItemViewModel item } row
                || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed
                || e.Source is Button
                || e.Source is Visual source && source.FindAncestorOfType<Button>() is not null)
            {
                return;
            }
            FinishMappingDrag(false);
            _mappingDragEditor = ViewModel.MappingEditor;
            _mappingDragEditor.SelectedMapping = item;
            _mappingPressedRow = row;
            _mappingPointer = e.Pointer;
            _mappingDragStart = _mappingPointerPosition = e.GetPosition(FocusList);
            e.Handled = true;
            e.Pointer.Capture(row);
            _mappingHoldTimer = DispatcherTimer.RunOnce(BeginMappingDrag, TimeSpan.FromMilliseconds(275));
        }

        internal static bool HasMappingDragThreshold(Point start, Point current) =>
            Math.Abs(current.X - start.X) >= 4 || Math.Abs(current.Y - start.Y) >= 4;

        private void BeginMappingDrag()
        {
            _mappingHoldTimer?.Dispose();
            _mappingHoldTimer = null;
            if (_mappingPointer?.Captured != _mappingPressedRow || !FocusList.IsEffectivelyVisible
                || _mappingPressedRow?.DataContext is not MappingEditorItemViewModel item
                || !ReferenceEquals(_mappingDragEditor, ViewModel.MappingEditor)
                || !_mappingDragEditor.BeginReorder(item))
            {
                FinishMappingDrag(false);
                return;
            }
            ResetFocusStatus();
            _mappingScroll = FocusList.GetVisualDescendants().OfType<ScrollViewer>().First();
            // Keep real layout slots fixed; transforms alone show the tentative order.
            _mappingDragRows = FocusRowContainers().Select(container =>
            {
                var row = container.GetVisualDescendants().OfType<Grid>().Single(grid => grid.Name == "FocusRow");
                row.RenderTransform = null;
                container.RenderTransform = null;
                var transform = new TranslateTransform();
                container.RenderTransform = transform;
                return new FocusDragRow((MappingEditorItemViewModel)container.DataContext!, container, row,
                    container.TranslatePoint(default, FocusList)!.Value.Y + _mappingScroll.Offset.Y,
                    container.Bounds.Height, transform);
            }).OrderBy(row => row.Top).ToArray();
            _mappingLiftedRow = _mappingDragRows.Single(row => ReferenceEquals(row.Item, item));
            foreach (var row in _mappingDragRows.Where(row => row != _mappingLiftedRow))
            {
                row.Transform.Transitions = [new DoubleTransition
                {
                    Property = TranslateTransform.YProperty, Duration = TimeSpan.FromMilliseconds(150)
                }];
            }
            _mappingLiftedRow.Container.ZIndex = 1;
            LiftSurface(_mappingLiftedRow).IsVisible = true;
            _mappingDragStart = new Point(_mappingDragStart.X, _mappingDragStart.Y + _mappingScroll.Offset.Y);
            UpdateMappingDrag();
            if (_mappingLiftedRow is null) return;
            _mappingScrollTimer = DispatcherTimer.Run(() =>
            {
                if (!FocusList.IsEffectivelyVisible || !ReferenceEquals(_mappingDragEditor, ViewModel.MappingEditor))
                {
                    FinishMappingDrag(false);
                    return false;
                }
                var delta = MappingAutoScrollDelta(_mappingPointerPosition.Y, FocusList.Bounds.Height);
                var offset = Math.Clamp(_mappingScroll.Offset.Y + delta, 0,
                    Math.Max(0, _mappingScroll.Extent.Height - _mappingScroll.Viewport.Height));
                _mappingScroll.Offset = new Vector(_mappingScroll.Offset.X, offset);
                UpdateMappingDrag();
                return true;
            }, TimeSpan.FromMilliseconds(30));
        }

        private static Border LiftSurface(FocusDragRow row) =>
            row.Row.GetVisualDescendants().OfType<Border>().Single(border => border.Name == "FocusLiftSurface");

        internal static double MappingAutoScrollDelta(double pointerY, double height)
        {
            const double edge = 36;
            if (pointerY < edge) return -12 * Math.Clamp((edge - pointerY) / edge, 0, 1);
            if (pointerY > height - edge) return 12 * Math.Clamp((pointerY - height + edge) / edge, 0, 1);
            return 0;
        }

        private void MappingDrag_OnPointerMoved(object? sender, PointerEventArgs e)
        {
            if (!ReferenceEquals(e.Pointer, _mappingPointer)) return;
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                FinishMappingDrag(false);
                return;
            }
            _mappingPointerPosition = e.GetPosition(FocusList);
            if (_mappingLiftedRow is null && HasMappingDragThreshold(_mappingDragStart, _mappingPointerPosition))
            {
                BeginMappingDrag();
            }
            else
            {
                UpdateMappingDrag();
            }
            e.Handled = true;
        }

        private void UpdateMappingDrag()
        {
            if (_mappingLiftedRow is not { } lifted || _mappingScroll is null || _mappingDragEditor is null) return;
            if (!ReferenceEquals(_mappingDragEditor, ViewModel.MappingEditor)
                || _mappingDragEditor.ReorderingMapping != lifted.Item
                || !_mappingDragEditor.Mappings.SequenceEqual(_mappingDragRows.Select(row => row.Item)))
            {
                FinishMappingDrag(false);
                return;
            }
            var pointerY = _mappingPointerPosition.Y + _mappingScroll.Offset.Y;
            var others = _mappingDragRows.Where(row => row != lifted).ToList();
            _mappingDragEditor.PreviewReorder(others.Count(row => pointerY > row.Top + row.Height / 2));
            others.Insert(_mappingDragEditor.ReorderIndex, lifted);
            var top = _mappingDragRows[0].Top;
            foreach (var row in others)
            {
                if (row != lifted) row.Transform.Y = top - row.Top;
                top += row.Height;
            }
            var minimum = Math.Max(_mappingDragRows[0].Top, _mappingScroll.Offset.Y);
            var maximum = Math.Min(top - lifted.Height,
                _mappingScroll.Offset.Y + _mappingScroll.Viewport.Height - lifted.Height);
            var liftedTop = Math.Clamp(lifted.Top + pointerY - _mappingDragStart.Y, minimum, Math.Max(minimum, maximum));
            lifted.Transform.Y = liftedTop - lifted.Top;
        }

        private void MappingDrag_OnPointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (!ReferenceEquals(e.Pointer, _mappingPointer)) return;
            _mappingPointerPosition = e.GetPosition(FocusList);
            UpdateMappingDrag();
            FinishMappingDrag(true);
            e.Handled = true;
        }

        private void MappingDrag_OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
        {
            if (ReferenceEquals(e.Pointer, _mappingPointer)) FinishMappingDrag(false);
        }

        private void MappingDrag_OnKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key != Key.Escape || _mappingPointer is null) return;
            FinishMappingDrag(false);
            e.Handled = true;
        }

        private void FinishMappingDrag(bool commit)
        {
            _mappingHoldTimer?.Dispose();
            _mappingScrollTimer?.Dispose();
            _mappingHoldTimer = _mappingScrollTimer = null;
            var pointer = _mappingPointer;
            _mappingPointer = null;
            if (_mappingLiftedRow is not null)
            {
                AnimateFocusMove(() =>
                {
                    foreach (var row in _mappingDragRows)
                    {
                        row.Container.ZIndex = 0;
                        row.Container.RenderTransform = null;
                        LiftSurface(row).IsVisible = false;
                    }
                    if (commit && ReferenceEquals(_mappingDragEditor, ViewModel.MappingEditor))
                    {
                        _mappingDragEditor!.CommitReorder();
                    }
                    else
                    {
                        _mappingDragEditor!.CancelReorder();
                    }
                });
            }
            _mappingDragEditor = null;
            _mappingPressedRow = null;
            _mappingLiftedRow = null;
            _mappingDragRows = [];
            _mappingScroll = null;
            pointer?.Capture(null);
        }
    }
}
