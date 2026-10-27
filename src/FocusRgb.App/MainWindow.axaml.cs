using System.Diagnostics;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using FocusRgb.App.Diagnostics;
using FocusRgb.App.Mcp;
using FocusRgb.App.Plugins;
using FocusRgb.App.ViewModels;
using FocusRgb.Core.Commands;
using FocusRgb.Platform.Windows;

namespace FocusRgb.App
{
    /// <summary>Hosts persisted local state and minimizes to the tray while the runtime continues.</summary>
    public sealed partial class MainWindow : Window
    {
        private readonly string _dataDirectory;
        private bool _allowClose;
        private bool _exitRequested;
        private bool _pluginsLoaded;
        private bool _pluginsIncludeBeta;
        private string? _mcpApprovalId;
        private bool _hideAfterMcpApproval;
        private CancellationTokenSource? _previewCancellation;
        private readonly IStartupRegistration? _startupRegistration;
        private bool _updatingStartupControls;
        private IDisposable? _focusStatusTimer;

        public MainWindow()
            : this(MainWindowViewModel.CreatePreview(), null)
        {
        }

        public MainWindow(MainWindowViewModel viewModel)
            : this(viewModel, null)
        {
        }

        public MainWindow(MainWindowViewModel viewModel, string? dataDirectory, IStartupRegistration? startupRegistration = null)
        {
            InitializeComponent();
            // ListBox selection can capture and handle pointer events before they reach the row.
            AddHandler(PointerMovedEvent, MappingDrag_OnPointerMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
            AddHandler(PointerReleasedEvent, MappingDrag_OnPointerReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
            AddHandler(KeyDownEvent, MappingDrag_OnKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);
            Deactivated += (_, _) => FinishMappingDrag(false);
            _startupRegistration = startupRegistration;
            DataContext = viewModel;
            _dataDirectory = dataDirectory ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "FocusRGB");
            Closing += MainWindow_OnClosing;
            Opened += (_, _) =>
            {
                UpdateMcpControls();
                UpdateStartupControls();
            };
        }

        public event EventHandler? ExitRequested;
        public event EventHandler? HiddenToTray;
        public event EventHandler<McpApprovalDecisionEventArgs>? McpApprovalDecided;

        private MainWindowViewModel ViewModel => (MainWindowViewModel)DataContext!;

        public void ShowFromTray()
        {
            ShowInTaskbar = true;
            Show();
            WindowState = WindowState.Normal;
            Activate();
        }

        public void ShowMcpApproval(McpApprovalRequest request, bool openedFromTray)
        {
            _mcpApprovalId = request.Id;
            _hideAfterMcpApproval = openedFromTray;
            McpApprovalDetailsText.Text = $"Reported client: {request.ClientName}. Requests {request.Method}. Expires {request.ExpiresAt.LocalDateTime:t}.";
            McpApprovalSummaryText.Text = request.Summary;
            McpApprovalPanel.IsVisible = true;
        }

        public void ClearMcpApproval()
        {
            _mcpApprovalId = null;
            McpApprovalPanel.IsVisible = false;
            var hide = _hideAfterMcpApproval && IsVisible;
            _hideAfterMcpApproval = false;
            if (!hide) return;
            ShowInTaskbar = false;
            Hide();
            ViewModel.NotifyWindowHidden();
            HiddenToTray?.Invoke(this, EventArgs.Empty);
        }

        public void NotifyStartedMinimized() => ViewModel.NotifyWindowHidden();

        public void CreateHiddenPlatformWindow()
        {
            Opacity = 0;
            ShowInTaskbar = false;
            ShowActivated = false;
            Show();
            Hide();
            Opacity = 1;
            ShowInTaskbar = true;
            ShowActivated = true;
        }

        public void ReportQuitFailure() =>
            ViewModel.ReportUnexpectedError("Could not save before quitting. Choose Quit again to exit without saving.");

        public void ReportUnexpectedError(string message) => ViewModel.ReportUnexpectedError(message);

        public async Task<bool> PrepareQuitAsync(CancellationToken cancellationToken = default) =>
            await ViewModel.SaveRuntimeStateAsync(cancellationToken);

        public void AllowClose() => _allowClose = true;

        public void CancelExitRequest() => _exitRequested = false;

        private void MainWindow_OnClosing(object? sender, WindowClosingEventArgs e)
        {
            if (_allowClose) return;
            DeclineMcpApproval();
            _previewCancellation?.Cancel();
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

        private void ApproveMcpApproval_OnClick(object? sender, RoutedEventArgs e) => DecideMcpApproval(true);

        private void DeclineMcpApproval_OnClick(object? sender, RoutedEventArgs e) => DecideMcpApproval(false);

        private void DeclineMcpApproval()
        {
            if (_mcpApprovalId is not null) DecideMcpApproval(false);
        }

        private void DecideMcpApproval(bool approved)
        {
            if (_mcpApprovalId is not { } id) return;
            McpApprovalDecided?.Invoke(this, new McpApprovalDecisionEventArgs(id, approved));
        }

        private async void SelectedProfile_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (sender is ComboBox { SelectedItem: ProfileOptionViewModel profile }
                && !ReferenceEquals(profile, ViewModel.SelectedProfile))
                await RunUiActionAsync(() => ViewModel.SelectProfileAsync(profile));
        }

        private async void MainTabs_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (ReferenceEquals(e.Source, sender) && sender is TabControl { SelectedItem: TabItem { Header: "Settings" } })
            {
                UpdateStartupControls();
            }
            if (!ReferenceEquals(e.Source, sender)
                || sender is not TabControl { SelectedItem: TabItem { Header: "Plugins" } }
                || (_pluginsLoaded && _pluginsIncludeBeta == ViewModel.SavedIncludeBetaPlugins)) return;
            _pluginsLoaded = true;
            _pluginsIncludeBeta = ViewModel.SavedIncludeBetaPlugins;
            await LoadPluginsAsync(refresh: false);
        }

        private async void RefreshPlugins_OnClick(object? sender, RoutedEventArgs e) =>
            await LoadPluginsAsync(refresh: true);

        private async void RefreshGames_OnClick(object? sender, RoutedEventArgs e)
        {
            if (Application.Current is not App app || !ViewModel.BeginPluginAction()) return;
            try
            {
                await app.RefreshGameCatalogsAsync();
            }
            catch (Exception exception)
            {
                ViewModel.ReportUnexpectedError($"Could not refresh games: {exception.Message}");
            }
            finally
            {
                ViewModel.EndPluginAction();
            }
        }

        private async Task LoadPluginsAsync(bool refresh)
        {
            if (Application.Current is not App app || !ViewModel.BeginPluginAction()) return;
            try
            {
                ViewModel.ReportPluginAction(refresh ? "Refreshing official plugins…" : "Loading plugins…");
                await app.LoadPluginPageAsync(refresh);
            }
            catch (Exception exception)
            {
                ViewModel.ReportPluginAction($"Could not load plugins: {exception.Message}");
            }
            finally
            {
                ViewModel.EndPluginAction();
            }
        }

        private async void InstallOfficialPlugin_OnClick(object? sender, RoutedEventArgs e)
        {
            if (sender is not Button { CommandParameter: OfficialPluginItemViewModel item }
                || !item.CanInstall || Application.Current is not App app || !ViewModel.BeginPluginAction()) return;
            try
            {
                var inspected = await app.InspectOfficialPluginAsync(item.Listing);
                if (!inspected.IsSuccess)
                {
                    ViewModel.ReportPluginAction(inspected.ErrorMessage ?? "The release metadata could not be verified.");
                    return;
                }
                var choice = await ConfirmInstallAsync(
                    inspected.Manifest!.Id!, inspected.Manifest.Version!, item.Source,
                    string.Join(", ", inspected.Manifest.Resources!), inspected.Sha256, custom: false, collision: false,
                    enableInitially: item.Installed?.Plugin.IsEnabled ?? true);
                if (!choice.Confirmed) return;
                ViewModel.ReportPluginAction($"Downloading and verifying {item.Name}…");
                var downloaded = await app.DownloadOfficialPluginAsync(item.Listing);
                if (!downloaded.IsSuccess)
                {
                    ViewModel.ReportPluginAction(downloaded.Message);
                    return;
                }
                var result = await app.InstallOfficialPluginAsync(item.Listing, choice.Enable, inspected);
                ViewModel.ReportPluginAction(result.Message);
            }
            catch (Exception exception)
            {
                ViewModel.ReportPluginAction($"Could not install the plugin: {exception.Message}");
            }
            finally
            {
                ViewModel.EndPluginAction();
            }
        }

        private async void InstallCustomZip_OnClick(object? sender, RoutedEventArgs e)
        {
            var files = await RunUiActionAsync(() => StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Choose a FocusRGB plugin package",
                AllowMultiple = false,
                FileTypeFilter = [new FilePickerFileType("ZIP archive") { Patterns = ["*.zip"] }]
            }));
            var path = files?.SingleOrDefault()?.TryGetLocalPath();
            if (path is null) return;
            await InstallCustomArchiveAsync(path, "Local ZIP");
        }

        private async void UpdatePlugin_OnClick(object? sender, RoutedEventArgs e)
        {
            if (sender is not Button { CommandParameter: InstalledPluginItemViewModel item }
                || item.AvailableRelease is not { } listing || Application.Current is not App app
                || !ViewModel.BeginPluginAction()) return;
            try
            {
                var inspected = await app.InspectOfficialPluginAsync(listing);
                if (!inspected.IsSuccess)
                {
                    ViewModel.ReportPluginAction(inspected.ErrorMessage ?? "The release metadata could not be verified.");
                    return;
                }
                var choice = await ConfirmInstallAsync(inspected.Manifest!.Id!, inspected.Manifest.Version!,
                    listing.RepositoryUrl, string.Join(", ", inspected.Manifest.Resources!), inspected.Sha256,
                    custom: false, collision: false, enableInitially: item.Plugin.IsEnabled);
                if (!choice.Confirmed) return;
                ViewModel.ReportPluginAction($"Downloading update for {item.Name}…");
                var downloaded = await app.DownloadOfficialPluginAsync(listing);
                if (!downloaded.IsSuccess)
                {
                    ViewModel.ReportPluginAction(downloaded.Message);
                    return;
                }
                var installed = await app.InstallOfficialPluginAsync(listing, choice.Enable, inspected);
                ViewModel.ReportPluginAction(installed.Message);
            }
            catch (Exception exception)
            {
                ViewModel.ReportPluginAction($"Could not update the plugin: {exception.Message}");
            }
            finally
            {
                ViewModel.EndPluginAction();
            }
        }

        private async void InstallCustomGitHub_OnClick(object? sender, RoutedEventArgs e)
        {
            if (Application.Current is not App app || !ViewModel.BeginPluginAction()) return;
            try
            {
                ViewModel.ReportPluginAction("Downloading the custom GitHub release for inspection…");
                using var download = await app.DownloadCustomPluginAsync(ViewModel.CustomPluginGitHubUrl);
                await InstallCustomArchiveCoreAsync(app, download.ArchivePath, download.Source!);
            }
            catch (Exception exception)
            {
                ViewModel.ReportPluginAction($"Could not install the custom plugin: {exception.Message}");
            }
            finally
            {
                ViewModel.EndPluginAction();
            }
        }

        private async Task InstallCustomArchiveAsync(string path, string source)
        {
            if (Application.Current is not App app || !ViewModel.BeginPluginAction()) return;
            try
            {
                await InstallCustomArchiveCoreAsync(app, path, source);
            }
            catch (Exception exception)
            {
                ViewModel.ReportPluginAction($"Could not install the custom plugin: {exception.Message}");
            }
            finally
            {
                ViewModel.EndPluginAction();
            }
        }

        private async Task InstallCustomArchiveCoreAsync(App app, string path, string source)
        {
            var inspected = await app.InspectCustomPluginAsync(path);
            if (!inspected.IsSuccess)
            {
                ViewModel.ReportPluginAction(inspected.ErrorMessage ?? "The plugin package is invalid.");
                return;
            }
            var manifest = inspected.Manifest!;
            var collision = ViewModel.OfficialPlugins.Any(plugin => plugin.Id == manifest.Id)
                || ViewModel.InstalledPlugins.Any(plugin =>
                    plugin.Id == manifest.Id && plugin.Plugin.IsOfficial);
            var resources = manifest.Resources!.Count == 0
                ? "Resources: none declared"
                : $"Resources: {string.Join(", ", manifest.Resources)}";
            var choice = await ConfirmInstallAsync(
                manifest.Id!, manifest.Version!, source, resources, inspected.Sha256,
                custom: true, collision);
            if (!choice.Confirmed) return;
            ViewModel.ReportPluginAction($"Verifying and installing {manifest.Id}…");
            var result = await app.InstallCustomPluginAsync(
                path, manifest.Id!, inspected.Sha256!, choice.Enable);
            ViewModel.ReportPluginAction(result.Message);
        }

        private async void EnablePlugin_OnClick(object? sender, RoutedEventArgs e) =>
            await SetPluginEnabledAsync(sender, enabled: true);

        private async void DisablePlugin_OnClick(object? sender, RoutedEventArgs e) =>
            await SetPluginEnabledAsync(sender, enabled: false);

        private async void ConfigurePlugin_OnClick(object? sender, RoutedEventArgs e)
        {
            if (sender is not Button { CommandParameter: InstalledPluginItemViewModel item }
                || !item.CanConfigure || Application.Current is not App app
                || !ViewModel.BeginPluginAction()) return;
            try
            {
                var loaded = await Task.Run(() => app.LoadPluginConfiguration(item.Id));
                if (!loaded.IsSuccess)
                {
                    ViewModel.ReportPluginAction(loaded.Error ?? "The plugin settings could not be loaded.");
                    return;
                }
                var window = new PluginSettingsWindow(
                    item.Plugin, loaded.Schema!, loaded.Values!, loaded.Notices);
                var values = await window.ShowDialog<IReadOnlyDictionary<string, System.Text.Json.JsonElement>?>(this);
                if (values is null) return;
                var result = await app.SavePluginConfigurationAsync(item.Id, loaded.Schema!, values);
                ViewModel.ReportPluginAction(result.Message);
            }
            catch (Exception exception)
            {
                ViewModel.ReportPluginAction($"Could not configure the plugin: {exception.Message}");
            }
            finally
            {
                ViewModel.EndPluginAction();
            }
        }

        private async Task SetPluginEnabledAsync(object? sender, bool enabled)
        {
            if (sender is not Button { CommandParameter: InstalledPluginItemViewModel item }
                || Application.Current is not App app || !ViewModel.BeginPluginAction()) return;
            try
            {
                var result = await app.SetPluginEnabledAsync(item.Id, enabled);
                ViewModel.ReportPluginAction(result.Message);
            }
            catch (Exception exception)
            {
                ViewModel.ReportPluginAction($"Could not change the plugin: {exception.Message}");
            }
            finally
            {
                ViewModel.EndPluginAction();
            }
        }

        private async void UninstallPlugin_OnClick(object? sender, RoutedEventArgs e)
        {
            if (sender is not Button { CommandParameter: InstalledPluginItemViewModel item }
                || Application.Current is not App app || !ViewModel.BeginPluginAction()) return;
            try
            {
                if (!await ConfirmActionAsync(
                    "Uninstall plugin", $"Stop and uninstall '{item.Name}'? Saved settings will be kept.")) return;
                var result = await app.UninstallPluginAsync(item.Id);
                ViewModel.ReportPluginAction(result.Message);
            }
            catch (Exception exception)
            {
                ViewModel.ReportPluginAction($"Could not uninstall the plugin: {exception.Message}");
            }
            finally
            {
                ViewModel.EndPluginAction();
            }
        }

        private async Task<PluginInstallChoice> ConfirmInstallAsync(
            string id,
            string version,
            string source,
            string resources,
            string? sha256,
            bool custom,
            bool collision,
            bool enableInitially = true)
        {
            var enable = new CheckBox
            {
                Content = "Enable and start this plugin after installation",
                IsChecked = enableInitially
            };
            var message = new StackPanel { Spacing = 8 };
            message.Children.Add(new TextBlock { Text = $"Plugin: {id}" });
            message.Children.Add(new TextBlock { Text = $"Version: {version}" });
            message.Children.Add(new TextBlock { Text = $"Source: {source}", TextWrapping = Avalonia.Media.TextWrapping.Wrap });
            message.Children.Add(new TextBlock { Text = resources, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
            if (sha256 is not null)
                message.Children.Add(new TextBlock { Text = $"SHA-256: {sha256}", TextWrapping = Avalonia.Media.TextWrapping.Wrap });
            if (custom)
                message.Children.Add(new TextBlock { Text = "Custom, unverified. FocusRGB does not verify this publisher.", FontWeight = Avalonia.Media.FontWeight.SemiBold });
            if (collision)
                message.Children.Add(new TextBlock
                {
                    Text = "This custom package uses an official plugin ID. Continuing will replace the installed package with unverified code.",
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                    FontWeight = Avalonia.Media.FontWeight.Bold
                });
            message.Children.Add(new TextBlock
            {
                Text = "Plugins run as your user and have your full user permissions.",
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                FontWeight = Avalonia.Media.FontWeight.Bold
            });
            message.Children.Add(enable);
            var dialog = CreateConfirmationWindow("Install plugin", message, "Install");
            var confirmed = await dialog.Window.ShowDialog<bool>(this);
            return new PluginInstallChoice(confirmed, enable.IsChecked == true);
        }

        private async Task<bool> ConfirmActionAsync(string title, string text)
        {
            var content = new TextBlock { Text = text, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
            var dialog = CreateConfirmationWindow(title, content, "Continue");
            return await dialog.Window.ShowDialog<bool>(this);
        }

        private static ConfirmationWindow CreateConfirmationWindow(
            string title,
            Control content,
            string confirmText)
        {
            var confirm = new Button { Content = confirmText };
            var cancel = new Button { Content = "Cancel" };
            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 8,
                Children = { cancel, confirm }
            };
            var window = new Window
            {
                Title = title,
                Width = 560,
                SizeToContent = SizeToContent.Height,
                MaxHeight = 620,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Content = new StackPanel { Margin = new Thickness(20), Spacing = 16, Children = { content, buttons } }
            };
            confirm.Click += (_, _) => window.Close(true);
            cancel.Click += (_, _) => window.Close(false);
            return new ConfirmationWindow(window);
        }

        private readonly record struct PluginInstallChoice(bool Confirmed, bool Enable);
        private sealed record ConfirmationWindow(Window Window);

        public sealed class McpApprovalDecisionEventArgs(string approvalId, bool approved) : EventArgs
        {
            public string ApprovalId { get; } = approvalId;
            public bool Approved { get; } = approved;
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

        private async void PreviewProfile_OnClick(object? sender, RoutedEventArgs e)
        {
            if (_previewCancellation is not null || ViewModel.EditingProfile is not { } profile) return;
            using var cancellation = new CancellationTokenSource();
            _previewCancellation = cancellation;
            try
            {
                await RunUiActionAsync(async () =>
                {
                    if (Application.Current is App app) await app.RequestProfilePreviewAsync(profile.Id, cancellation.Token);
                });
            }
            finally { _previewCancellation = null; }
        }

        private async void StopPreview_OnClick(object? sender, RoutedEventArgs e) =>
            await RunUiActionAsync(async () =>
            {
                _previewCancellation?.Cancel();
                if (Application.Current is App app) await app.CancelProfilePreviewAsync();
            });
        private async void SaveProfileAs_OnClick(object? sender, RoutedEventArgs e) =>
            await RunUiActionAsync(() => ViewModel.SaveProfileAsAsync());
        private async void RemoveProfile_OnClick(object? sender, RoutedEventArgs e) =>
            await RunUiActionAsync(() => ViewModel.RemoveProfileAsync());
        private void CancelProfile_OnClick(object? sender, RoutedEventArgs e) => ViewModel.CancelProfile();

        private async void About_OnClick(object? sender, RoutedEventArgs e) =>
            await RunUiActionAsync(async () =>
            {
                var close = new Button { Content = "Close", HorizontalAlignment = HorizontalAlignment.Right };
                var dialog = new Window
                {
                    Title = "About FocusRGB", Width = 360, Height = 180, CanResize = false,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Content = new StackPanel
                    {
                        Margin = new Thickness(20), Spacing = 16,
                        Children = { new TextBlock { Text = ApplicationVersion.DisplayName }, close }
                    }
                };
                close.Click += (_, _) => dialog.Close();
                await dialog.ShowDialog(this);
            });

        private async void SaveApplicationSettings_OnClick(object? sender, RoutedEventArgs e)
        {
            if (await RunUiActionAsync(() => ViewModel.SaveApplicationSettingsAsync()))
            {
                (Application.Current as App)?.ApplyTheme(ViewModel.ApplicationTheme);
                AppLog.Configure(_dataDirectory, ViewModel.SavedVerboseLogging);
                AppLog.Write("Application preferences saved.");
                _pluginsLoaded = false;
            }
        }

        private void ApplicationTheme_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (sender is ComboBox { SelectedItem: string theme })
                (Application.Current as App)?.ApplyTheme(theme);
        }

        private async void McpEnabled_OnClick(object? sender, RoutedEventArgs e)
        {
            if (Application.Current is not App app) return;
            McpEnabledCheckBox.IsEnabled = false;
            try
            {
                var state = await app.SetMcpEnabledAsync(McpEnabledCheckBox.IsChecked == true);
                UpdateMcpControls(state);
            }
            catch (Exception exception)
            {
                McpStatusText.Text = $"Could not change local MCP: {exception.Message}";
                UpdateMcpControls(app.McpState);
            }
        }

        private void UpdateMcpControls() =>
            UpdateMcpControls((Application.Current as App)?.McpState ?? McpUiState.Unavailable);

        private void UpdateMcpControls(McpUiState state)
        {
            McpEnabledCheckBox.IsVisible = state.IsAvailable;
            McpEnabledCheckBox.IsEnabled = state.IsAvailable;
            McpEnabledCheckBox.IsChecked = state.IsEnabled;
            McpStatusText.Text = state.Message;
        }

        private void CancelApplicationSettings_OnClick(object? sender, RoutedEventArgs e)
        {
            ViewModel.CancelApplicationSettings();
            (Application.Current as App)?.ApplyTheme(ViewModel.SavedApplicationTheme);
        }

        private void ReloadSettings_OnClick(object? sender, RoutedEventArgs e) =>
            ViewModel.ReloadSettingsAfterConflict();

        private void OpenSettingsFolder_OnClick(object? sender, RoutedEventArgs e) =>
            OpenFolder(_dataDirectory, "Opened the settings folder.");

        private void OpenLogsFolder_OnClick(object? sender, RoutedEventArgs e) =>
            OpenFolder(Path.Combine(_dataDirectory, "logs"), "Opened the logs folder.");

        private void StartWithWindows_OnCheckedChanged(object? sender, RoutedEventArgs e)
        {
            if (_updatingStartupControls || _startupRegistration is null)
            {
                return;
            }
            UpdateStartupControls(_startupRegistration.SetEnabled(StartWithWindowsCheckBox.IsChecked == true));
        }

        private void UpdateStartupControls(StartupRegistrationStatus? status = null)
        {
            if (StartWithWindowsCheckBox is null)
            {
                return;
            }
            _updatingStartupControls = true;
            try
            {
                status ??= _startupRegistration?.Refresh()
                    ?? new StartupRegistrationStatus(false, false, "Start with Windows is unavailable in this session.");
                StartWithWindowsCheckBox.IsEnabled = status.Available;
                StartWithWindowsCheckBox.IsChecked = status.Enabled;
                StartupStatusText.Text = status.Message;
            }
            finally
            {
                _updatingStartupControls = false;
            }
        }

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
                await DiagnosticExport.WriteZipAsync(ViewModel, DateTimeOffset.UtcNow, output);
                ViewModel.ReportSettingsAction("Exported diagnostic metadata. Raw logs are omitted because they may contain secrets.");
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

        private async void SaveApplicationGroup_OnClick(object? sender, RoutedEventArgs e) =>
            await RunUiActionAsync(() => ViewModel.SaveApplicationGroupAsync());
        private async void SaveApplicationGroupAs_OnClick(object? sender, RoutedEventArgs e) =>
            await RunUiActionAsync(() => ViewModel.SaveApplicationGroupAsAsync());
        private async void RemoveApplicationGroup_OnClick(object? sender, RoutedEventArgs e) =>
            await RunUiActionAsync(() => ViewModel.RemoveApplicationGroupAsync());

        private void NewManualGame_OnClick(object? sender, RoutedEventArgs e) =>
            ViewModel.StartNewManualGame();
        private async void BrowseGameExecutable_OnClick(object? sender, RoutedEventArgs e)
        {
            var files = await RunUiActionAsync(() => StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Add Windows game executables",
                AllowMultiple = true,
                FileTypeFilter = [new FilePickerFileType("Windows executable") { Patterns = ["*.exe"] }]
            }));
            if (files is null) return;
            foreach (var file in files) ViewModel.AddDraftGameExecutable(file.Name);
        }
        private void AddDetectedGameExecutable_OnClick(object? sender, RoutedEventArgs e) =>
            ViewModel.AddDetectedGameExecutable();
        private void AddManualGameExecutable_OnClick(object? sender, RoutedEventArgs e) =>
            ViewModel.AddManualGameExecutable();
        private void RemoveGameExecutable_OnClick(object? sender, RoutedEventArgs e) =>
            ViewModel.RemoveDraftGameExecutable();
        private async void SaveManualGame_OnClick(object? sender, RoutedEventArgs e) =>
            await RunUiActionAsync(() => ViewModel.SaveManualGameAsync());
        private async void RemoveManualGame_OnClick(object? sender, RoutedEventArgs e) =>
            await RunUiActionAsync(() => ViewModel.RemoveManualGameAsync());

        private async void SaveMappingAs_OnClick(object? sender, RoutedEventArgs e) =>
            await SaveFocusAsync(saveAs: true);

        private void CreateFocus_OnClick(object? sender, RoutedEventArgs e)
        {
            ResetFocusStatus();
            ViewModel.MappingEditor.BeginCreate();
        }

        private void EditSelectedFocus_OnClick(object? sender, RoutedEventArgs e)
        {
            if (ViewModel.MappingEditor.SelectedMapping is { } item) ViewModel.MappingEditor.BeginEdit(item);
            ResetFocusStatus();
        }

        private void MoveMappingUp_OnClick(object? sender, RoutedEventArgs e) =>
            MoveFromButton(sender, -1);

        private void MoveMappingDown_OnClick(object? sender, RoutedEventArgs e) =>
            MoveFromButton(sender, 1);

        private void MoveFromButton(object? sender, int offset)
        {
            if (sender is Button { CommandParameter: MappingEditorItemViewModel item })
            {
                AnimateFocusMove(() => ViewModel.MappingEditor.Move(item, offset));
            }
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

        private void AddDetectedExecutable_OnClick(object? sender, RoutedEventArgs e) =>
            ViewModel.AddDetectedExecutable();

        private void AddManualExecutable_OnClick(object? sender, RoutedEventArgs e) =>
            ViewModel.AddManualExecutable();

        private void EditMapping_OnClick(object? sender, RoutedEventArgs e)
        {
            if (sender is Button { CommandParameter: MappingEditorItemViewModel item })
                ViewModel.MappingEditor.BeginEdit(item);
            ResetFocusStatus();
        }

        private void RemoveMapping_OnClick(object? sender, RoutedEventArgs e)
        {
            if (sender is Button { CommandParameter: MappingEditorItemViewModel item })
                ViewModel.MappingEditor.Remove(item);
        }

        private async void ApplyMappings_OnClick(object? sender, RoutedEventArgs e) => await SaveFocusAsync(false);
        private void CancelMappings_OnClick(object? sender, RoutedEventArgs e)
        {
            ViewModel.MappingEditor.Cancel();
            ResetFocusStatus();
        }

        private async Task SaveFocusAsync(bool saveAs)
        {
            ResetFocusStatus();
            await RunUiActionAsync(() => ViewModel.MappingEditor.HasEditor
                ? ViewModel.SaveEditingFocusAsync(saveAs) : ViewModel.SaveMappingsAsync());
            if (!ViewModel.MappingEditor.IsDirty)
                _focusStatusTimer = DispatcherTimer.RunOnce(() => this.FindControl<TextBlock>("FocusSaveStatus")!.Opacity = 0,
                    TimeSpan.FromSeconds(3));
        }

        private void ResetFocusStatus()
        {
            _focusStatusTimer?.Dispose();
            this.FindControl<TextBlock>("FocusSaveStatus")!.Opacity = 1;
        }

        private void AnimateFocusMove(Action move)
        {
            ResetFocusStatus();
            var positions = FocusRowContainers()
                .ToDictionary(control => ((MappingEditorItemViewModel)control.DataContext!).Id,
                    control => control.TranslatePoint(default, this)?.Y ?? 0);
            move();
            foreach (var container in FocusRowContainers()) container.RenderTransform = null;
            void AfterLayout(object? sender, EventArgs args)
            {
                LayoutUpdated -= AfterLayout;
                foreach (var row in FocusRowContainers())
                {
                    if (row.DataContext is not MappingEditorItemViewModel item || !positions.TryGetValue(item.Id, out var before)) continue;
                    var transform = new TranslateTransform(0, before - (row.TranslatePoint(default, this)?.Y ?? before));
                    row.RenderTransform = transform;
                    transform.Transitions = [new DoubleTransition { Property = TranslateTransform.YProperty, Duration = TimeSpan.FromMilliseconds(180) }];
                    transform.Y = 0;
                }
            }
            LayoutUpdated += AfterLayout;
            InvalidateArrange();
        }

        protected override void OnClosed(EventArgs e)
        {
            FinishMappingDrag(false);
            _focusStatusTimer?.Dispose();
            base.OnClosed(e);
        }

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
