using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using FocusRgb.App.ViewModels;

namespace FocusRgb.App
{
    /// <summary>Hosts persisted local state and minimizes to the tray while the runtime continues.</summary>
    public sealed partial class MainWindow : Window
    {
        private static readonly DataFormat<MappingEditorItemViewModel> MappingFormat =
            DataFormat.CreateInProcessFormat<MappingEditorItemViewModel>("FocusRGB.Mapping");
        private bool _allowClose;

        public MainWindow()
            : this(MainWindowViewModel.CreatePreview())
        {
        }

        public MainWindow(MainWindowViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
            Closing += MainWindow_OnClosing;
        }

        private MainWindowViewModel ViewModel => (MainWindowViewModel)DataContext!;

        public void ShowFromTray()
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
        }

        public async Task<bool> PrepareQuitAsync(CancellationToken cancellationToken = default) =>
            await ViewModel.SaveRuntimeStateAsync(cancellationToken);

        public void AllowClose() => _allowClose = true;

        private void MainWindow_OnClosing(object? sender, WindowClosingEventArgs e)
        {
            if (_allowClose) return;
            e.Cancel = true;
            Hide();
            ViewModel.NotifyWindowHidden();
        }

        private async void SelectedProfile_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (sender is ComboBox { SelectedItem: ProfileOptionViewModel profile }
                && !ReferenceEquals(profile, ViewModel.SelectedProfile))
                await ViewModel.SelectProfileAsync(profile);
        }

        private async void ManualMode_OnClick(object? sender, RoutedEventArgs e)
        {
            if (!ViewModel.IsManual) await ViewModel.SetModeAsync(manual: true);
        }

        private async void AutomaticMode_OnClick(object? sender, RoutedEventArgs e)
        {
            if (ViewModel.IsManual) await ViewModel.SetModeAsync(manual: false);
        }

        private async void SaveProfile_OnClick(object? sender, RoutedEventArgs e) =>
            await ViewModel.SaveProfileAsync();
        private async void SaveProfileAs_OnClick(object? sender, RoutedEventArgs e) =>
            await ViewModel.SaveProfileAsAsync();
        private async void RemoveProfile_OnClick(object? sender, RoutedEventArgs e) =>
            await ViewModel.RemoveProfileAsync();
        private void CancelProfile_OnClick(object? sender, RoutedEventArgs e) => ViewModel.CancelProfile();

        private void SaveApplicationGroup_OnClick(object? sender, RoutedEventArgs e) => ViewModel.SaveApplicationGroup();
        private void SaveApplicationGroupAs_OnClick(object? sender, RoutedEventArgs e) => ViewModel.SaveApplicationGroupAs();
        private void RemoveApplicationGroup_OnClick(object? sender, RoutedEventArgs e) => ViewModel.RemoveApplicationGroup();

        private void SaveMappingAs_OnClick(object? sender, RoutedEventArgs e) =>
            ViewModel.MappingEditor.SaveMappingAs();

        private void MoveMappingUp_OnClick(object? sender, RoutedEventArgs e) =>
            MoveFromButton(sender, -1);

        private void MoveMappingDown_OnClick(object? sender, RoutedEventArgs e) =>
            MoveFromButton(sender, 1);

        private void MoveFromButton(object? sender, int offset)
        {
            if (sender is Button { CommandParameter: MappingEditorItemViewModel item })
            {
                ViewModel.MappingEditor.Move(item, offset);
            }
        }

        private async void MappingDrag_OnPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (sender is not Control { DataContext: MappingEditorItemViewModel item }) return;
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed
                || e.Source is Button
                || e.Source is Visual source && source.FindAncestorOfType<Button>() is not null)
                return;
            ViewModel.MappingEditor.SelectedMapping = item;
            var transfer = new DataTransfer();
            transfer.Add(DataTransferItem.Create(MappingFormat, item));
            await DragDrop.DoDragDropAsync(e, transfer, DragDropEffects.Move);
        }

        private void MappingRow_OnDragOver(object? sender, DragEventArgs e)
        {
            e.DragEffects = e.DataTransfer.Contains(MappingFormat) ? DragDropEffects.Move : DragDropEffects.None;
        }

        private void MappingRow_OnDrop(object? sender, DragEventArgs e)
        {
            if (sender is not Control { DataContext: MappingEditorItemViewModel target } row) return;
            var source = e.DataTransfer.TryGetValue(MappingFormat);
            if (source is null) return;
            if (e.GetPosition(row).Y < row.Bounds.Height / 2)
                ViewModel.MappingEditor.MoveBefore(source, target);
            else
                ViewModel.MappingEditor.MoveAfter(source, target);
        }

        private async void BrowseExecutable_OnClick(object? sender, RoutedEventArgs e)
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Add Windows executables",
                AllowMultiple = true,
                FileTypeFilter = [new FilePickerFileType("Windows executable") { Patterns = ["*.exe"] }]
            });
            foreach (var file in files) ViewModel.AddDraftExecutable(file.Name);
        }

        private void RemoveExecutable_OnClick(object? sender, RoutedEventArgs e)
        {
            ViewModel.RemoveDraftExecutable();
        }

        private void EditMapping_OnClick(object? sender, RoutedEventArgs e)
        {
            if (sender is Button { CommandParameter: MappingEditorItemViewModel item })
                ViewModel.MappingEditor.SelectedMapping = item;
        }

        private void RemoveMapping_OnClick(object? sender, RoutedEventArgs e)
        {
            if (sender is Button { CommandParameter: MappingEditorItemViewModel item })
                ViewModel.MappingEditor.Remove(item);
        }

        private void ApplyMappings_OnClick(object? sender, RoutedEventArgs e) => ViewModel.MappingEditor.Apply();
        private void CancelMappings_OnClick(object? sender, RoutedEventArgs e) => ViewModel.MappingEditor.Cancel();
    }
}
