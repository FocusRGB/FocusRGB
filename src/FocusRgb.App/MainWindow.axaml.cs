using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using FocusRgb.App.Diagnostics;
using FocusRgb.App.ViewModels;
using FocusRgb.Core.Commands;

namespace FocusRgb.App
{
    /// <summary>Hosts persisted local state and minimizes to the tray while the runtime continues.</summary>
    public sealed partial class MainWindow : Window
    {
        private static readonly DataFormat<MappingEditorItemViewModel> MappingFormat =
            DataFormat.CreateInProcessFormat<MappingEditorItemViewModel>("FocusRGB.Mapping");
        private readonly string _dataDirectory;
        private bool _allowClose;
        private bool _exitRequested;

        public MainWindow()
            : this(MainWindowViewModel.CreatePreview(), null)
        {
        }

        public MainWindow(MainWindowViewModel viewModel)
            : this(viewModel, null)
        {
        }

        public MainWindow(MainWindowViewModel viewModel, string? dataDirectory)
        {
            InitializeComponent();
            DataContext = viewModel;
            _dataDirectory = dataDirectory ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "FocusRGB");
            Closing += MainWindow_OnClosing;
        }

        public event EventHandler? ExitRequested;
        public event EventHandler? HiddenToTray;

        private MainWindowViewModel ViewModel => (MainWindowViewModel)DataContext!;

        public void ShowFromTray()
        {
            ShowInTaskbar = true;
            Show();
            WindowState = WindowState.Normal;
            Activate();
        }

        public void NotifyStartedMinimized() => ViewModel.NotifyWindowHidden();

        public void ReportQuitFailure() =>
            ViewModel.ReportUnexpectedError("Could not save before quitting. Choose Quit again to exit without saving.");

        public void ReportUnexpectedError(string message) => ViewModel.ReportUnexpectedError(message);

        public void ApplyOutputStatus(SelectionRuntimeStatus status) => ViewModel.ApplyOutputStatus(status);

        public async Task<bool> PrepareQuitAsync(CancellationToken cancellationToken = default) =>
            await ViewModel.SaveRuntimeStateAsync(cancellationToken);

        public void AllowClose() => _allowClose = true;

        public void CancelExitRequest() => _exitRequested = false;

        private void MainWindow_OnClosing(object? sender, WindowClosingEventArgs e)
        {
            if (_allowClose) return;
            e.Cancel = true;
            if (ViewModel.ShouldMinimizeOnClose)
            {
                ShowInTaskbar = false;
                Hide();
                ViewModel.NotifyWindowHidden();
                HiddenToTray?.Invoke(this, EventArgs.Empty);
            }
            else if (!_exitRequested)
            {
                _exitRequested = true;
                ExitRequested?.Invoke(this, EventArgs.Empty);
            }
        }

        private async void SelectedProfile_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (sender is ComboBox { SelectedItem: ProfileOptionViewModel profile }
                && !ReferenceEquals(profile, ViewModel.SelectedProfile))
                await RunUiActionAsync(() => ViewModel.SelectProfileAsync(profile));
        }

        private async void ManualMode_OnClick(object? sender, RoutedEventArgs e)
        {
            if (!ViewModel.IsManual) await RunUiActionAsync(() => ViewModel.SetModeAsync(manual: true));
        }

        private async void AutomaticMode_OnClick(object? sender, RoutedEventArgs e)
        {
            if (ViewModel.IsManual) await RunUiActionAsync(() => ViewModel.SetModeAsync(manual: false));
        }

        private async void AcceptRecovery_OnClick(object? sender, RoutedEventArgs e) =>
            await RunUiActionAsync(() => ViewModel.RespondToRecoveryAsync(accept: true));

        private async void DeclineRecovery_OnClick(object? sender, RoutedEventArgs e) =>
            await RunUiActionAsync(() => ViewModel.RespondToRecoveryAsync(accept: false));

        private async void SaveProfile_OnClick(object? sender, RoutedEventArgs e) =>
            await RunUiActionAsync(() => ViewModel.SaveProfileAsync());
        private async void SaveProfileAs_OnClick(object? sender, RoutedEventArgs e) =>
            await RunUiActionAsync(() => ViewModel.SaveProfileAsAsync());
        private async void RemoveProfile_OnClick(object? sender, RoutedEventArgs e) =>
            await RunUiActionAsync(() => ViewModel.RemoveProfileAsync());
        private void CancelProfile_OnClick(object? sender, RoutedEventArgs e) => ViewModel.CancelProfile();

        private async void SaveApplicationSettings_OnClick(object? sender, RoutedEventArgs e)
        {
            if (await RunUiActionAsync(() => ViewModel.SaveApplicationSettingsAsync()))
            {
                (Application.Current as App)?.ApplyTheme(ViewModel.ApplicationTheme);
                AppLog.Configure(_dataDirectory, ViewModel.SavedVerboseLogging);
                AppLog.Write("Application preferences saved.");
            }
        }

        private void ApplicationTheme_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (sender is ComboBox { SelectedItem: string theme })
                (Application.Current as App)?.ApplyTheme(theme);
        }

        private void CancelApplicationSettings_OnClick(object? sender, RoutedEventArgs e)
        {
            ViewModel.CancelApplicationSettings();
            (Application.Current as App)?.ApplyTheme(ViewModel.SavedApplicationTheme);
        }

        private void OpenSettingsFolder_OnClick(object? sender, RoutedEventArgs e) =>
            OpenFolder(_dataDirectory, "Opened the settings folder.");

        private void OpenLogsFolder_OnClick(object? sender, RoutedEventArgs e) =>
            OpenFolder(Path.Combine(_dataDirectory, "logs"), "Opened the logs folder.");

        private async void ExportDiagnostics_OnClick(object? sender, RoutedEventArgs e)
        {
            try
            {
                var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "Export FocusRGB diagnostics",
                    SuggestedFileName = $"FocusRGB-diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.zip",
                    FileTypeChoices = [new FilePickerFileType("ZIP archive") { Patterns = ["*.zip"] }]
                });
                if (file is null) return;
                await using var output = await file.OpenWriteAsync();
                output.SetLength(0);
                using var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
                var entry = archive.CreateEntry("diagnostics.json", CompressionLevel.Optimal);
                await using (var entryStream = entry.Open())
                await using (var writer = new StreamWriter(entryStream, new UTF8Encoding(false)))
                    await writer.WriteAsync(DiagnosticExport.CreateJson(ViewModel, DateTimeOffset.UtcNow));
                var logPath = Path.Combine(_dataDirectory, "logs", "focusrgb.log");
                if (ViewModel.SavedVerboseLogging && File.Exists(logPath))
                {
                    var logEntry = archive.CreateEntry("focusrgb.log", CompressionLevel.Optimal);
                    await using var logOutput = logEntry.Open();
                    await using var writer = new StreamWriter(logOutput, new UTF8Encoding(false));
                    var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                    foreach (var line in await File.ReadAllLinesAsync(logPath))
                        await writer.WriteLineAsync(string.IsNullOrEmpty(userProfile)
                            ? line
                            : line.Replace(userProfile, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase));
                }
                ViewModel.ReportSettingsAction("Exported diagnostics without profiles, mappings, paths, or secrets.");
            }
            catch (Exception exception)
            {
                ViewModel.ReportSettingsAction($"Could not export diagnostics: {exception.Message}");
            }
        }

        private void OpenFolder(string path, string successMessage)
        {
            try
            {
                Directory.CreateDirectory(path);
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                ViewModel.ReportSettingsAction(successMessage);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                ViewModel.ReportSettingsAction($"Could not open the folder: {exception.Message}");
            }
        }

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
            await RunUiActionAsync(() => DragDrop.DoDragDropAsync(e, transfer, DragDropEffects.Move));
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
            var files = await RunUiActionAsync(() => StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Add Windows executables",
                AllowMultiple = true,
                FileTypeFilter = [new FilePickerFileType("Windows executable") { Patterns = ["*.exe"] }]
            }));
            if (files is null) return;
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

        private async Task RunUiActionAsync(Func<Task> action)
        {
            try
            {
                await action();
            }
            catch (Exception exception)
            {
                ViewModel.ReportUnexpectedError($"The action failed: {exception.Message}");
            }
        }

        private async Task<T?> RunUiActionAsync<T>(Func<Task<T>> action)
        {
            try
            {
                return await action();
            }
            catch (Exception exception)
            {
                ViewModel.ReportUnexpectedError($"The action failed: {exception.Message}");
                return default;
            }
        }
    }
}
