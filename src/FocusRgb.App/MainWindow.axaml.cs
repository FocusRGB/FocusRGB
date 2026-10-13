using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using FocusRgb.App.ViewModels;

namespace FocusRgb.App
{
    /// <summary>Hosts the preview settings shell before runtime services are connected.</summary>
    public sealed partial class MainWindow : Window
    {
        private static readonly DataFormat<MappingEditorItemViewModel> MappingFormat =
            DataFormat.CreateInProcessFormat<MappingEditorItemViewModel>("FocusRGB.Mapping");

        public MainWindow()
        {
            InitializeComponent();
            DataContext = MainWindowViewModel.CreatePreview();
        }

        private MainWindowViewModel ViewModel => (MainWindowViewModel)DataContext!;

        private void AddApplicationGroup_OnClick(object? sender, RoutedEventArgs e) =>
            ViewModel.MappingEditor.AddApplicationGroup();

        private void AddMapping_OnClick(object? sender, RoutedEventArgs e) =>
            ViewModel.MappingEditor.AddMapping();

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
            if (sender is not Control { DataContext: MappingEditorItemViewModel target }) return;
            var source = e.DataTransfer.TryGetValue(MappingFormat);
            if (source is not null) ViewModel.MappingEditor.MoveTo(source, target);
        }

        private async void BrowseExecutable_OnClick(object? sender, RoutedEventArgs e)
        {
            if (sender is not Button { CommandParameter: ApplicationGroupViewModel group }) return;
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Add Windows executables",
                AllowMultiple = true,
                FileTypeFilter = [new FilePickerFileType("Windows executable") { Patterns = ["*.exe"] }]
            });
            foreach (var file in files) group.AddExecutable(file.Name);
        }

        private void RemoveExecutable_OnClick(object? sender, RoutedEventArgs e)
        {
            if (sender is Button { CommandParameter: ApplicationGroupViewModel group })
                group.RemoveSelectedExecutable();
        }

        private void ApplyMappings_OnClick(object? sender, RoutedEventArgs e) => ViewModel.MappingEditor.Apply();
        private void CancelMappings_OnClick(object? sender, RoutedEventArgs e) => ViewModel.MappingEditor.Cancel();
    }
}
