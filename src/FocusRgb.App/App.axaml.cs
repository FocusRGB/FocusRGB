using System.Collections.Concurrent;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using FocusRgb.App.Diagnostics;
using FocusRgb.App.Mcp;
using FocusRgb.App.Plugins;
using FocusRgb.App.ViewModels;
using FocusRgb.Core.Commands;
using FocusRgb.Core.Coordination;
using FocusRgb.Core.Persistence;
using FocusRgb.Core.Protocol;
using FocusRgb.LocalMcp;
using FocusRgb.Platform.Windows;

namespace FocusRgb.App
{
    public sealed class App : Application, IDisposable
    {
        private TrayIcon? _trayIcon;
        private IClassicDesktopStyleApplicationLifetime? _desktop;
        private MainWindow? _mainWindow;
        private Window? _startupErrorWindow;
        private ProfileChangePopupWindow? _profileChangePopup;
        private LocalStateStore? _store;
        private PluginRuntime? _plugins;
        private PluginRuntimeGateway? _pluginGateway;
        private MainWindowViewModel? _viewModel;
        private string? _packagesDirectory;
        private OfficialPluginDiscovery? _pluginDiscovery;
        private PluginPackageInstaller? _pluginInstaller;
        private PluginPackageDownloader? _pluginDownloader;
        private PluginPackageCache? _pluginPackageCache;
        private PluginInventory? _pluginInventory;
        private PluginConfigurationStore? _pluginConfigurationStore;
        private readonly HttpClient _pluginDiscoveryHttpClient = new();
        private readonly HttpClient _pluginDownloadHttpClient = new() { Timeout = Timeout.InfiniteTimeSpan };
        private OutputCoordinator? _outputCoordinator;
        private McpConfigurationStore? _mcpConfigurationStore;
        private LocalMcpPipeServer? _mcpServer;
        private McpReadQueryService? _mcpQueries;
        private McpCommandService? _mcpCommands;
        private ApplicationStateService? _applicationState;
        private McpUiState _mcpState = McpUiState.Unavailable;
        private NativeMenuItem? _reviewMcpRequestMenuItem;
        private CancellationTokenSource? _pluginStartupCancellation;
        private Task? _pluginStartupTask;
        private CancellationTokenSource? _observationCancellation;
        private Task? _observationTask;
        private bool _quitPending;
        private bool _quitWithoutSaving;
        private bool _shutdownApproved;
        private readonly ConcurrentDictionary<string, byte> _pluginRestartsPending = new(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, int> _pluginRestartAttempts = new(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, byte> _pluginRestartLimits = new(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, byte> _pluginPackageChanges = new(StringComparer.Ordinal);
        private Win32Properties.CustomWndProcHookCallback? _windowsMessageHook;

        public override void Initialize() => AvaloniaXamlLoader.Load(this);

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                _desktop = desktop;
                desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                if (!OperatingSystem.IsWindows())
                    desktop.ShutdownRequested += Desktop_OnShutdownRequested;
                desktop.Exit += (_, _) =>
                {
                    _trayIcon?.Dispose();
                    Dispose();
                };
                InitializeDesktopAsync(desktop);
            }

            base.OnFrameworkInitializationCompleted();
        }

        private async void InitializeDesktopAsync(IClassicDesktopStyleApplicationLifetime desktop)
        {
            try
            {
                var directory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "FocusRGB");
                _store = new LocalStateStore(directory);
                var startup = await _store.InitializeAsync(DateTimeOffset.UtcNow);
                if (!startup.IsSuccess)
                {
                    ShowStartupError(desktop, $"Could not initialize {startup.Error!.FileName}: {startup.Error.Message}");
                    return;
                }

                var trayAvailable = OperatingSystem.IsWindows();
                AppLog.Configure(directory, startup.Snapshot!.Settings.Application!.VerboseLogging == true);
                AppLog.Write($"Started; settings source={startup.Snapshot.SettingsSource}.");
                var pluginGateway = new PluginRuntimeGateway([]);
                _pluginGateway = pluginGateway;
                var packagesDirectory = Path.Combine(directory, "plugins");
                _packagesDirectory = packagesDirectory;
                foreach (var warning in PluginTemporaryDirectory.CleanupContents(
                    Path.Combine(directory, "plugin-downloads"), Path.Combine(packagesDirectory, ".staging")))
                {
                    AppLog.Write($"Plugin temporary-file cleanup failed: {warning}");
                }
                _pluginDiscovery = new OfficialPluginDiscovery(
                    _pluginDiscoveryHttpClient, Path.Combine(directory, "cache", "official-plugins.json"));
                _pluginInstaller = new PluginPackageInstaller(packagesDirectory);
                _pluginDownloader = new PluginPackageDownloader(
                    _pluginDownloadHttpClient, Path.Combine(directory, "plugin-downloads"));
                _pluginPackageCache = new PluginPackageCache(Path.Combine(directory, "cache", "plugin-packages"));
                try
                {
                    _pluginPackageCache.Prune();
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    AppLog.Write($"Plugin cache cleanup warning: {exception.Message}");
                }
                _pluginInventory = new PluginInventory(packagesDirectory);
                _pluginConfigurationStore = new PluginConfigurationStore(
                    packagesDirectory, Path.Combine(directory, "plugin-data"));
                var enabledPluginIds = startup.Snapshot.Settings.Plugins!.Enabled!.ToArray();
                var outputCoordinator = new OutputCoordinator(pluginGateway, startup.Snapshot.NativeSetup);
                _outputCoordinator = outputCoordinator;
                var initialOutput = await outputCoordinator.ApplyAsync(
                    startup.Snapshot.Settings,
                    startup.Snapshot.State,
                    startup.Snapshot.Desired.Reason);
                var stateService = new ApplicationStateService(
                    startup.Snapshot.Settings,
                    startup.Snapshot.State,
                    startup.Snapshot.Desired.Reason,
                    _store,
                    outputCoordinator,
                    initialOutput,
                    startup.Snapshot.GameCatalog);
                _applicationState = stateService;
                var popupDecision = new FocusRgb.Core.Selection.ProfileChangePopupDecision();
                popupDecision.Update(stateService.Current);
                stateService.SnapshotChanged += (_, snapshot) => Dispatcher.UIThread.Post(() =>
                {
                    if (_shutdownApproved) return;
                    var announcement = popupDecision.Update(snapshot);
                    if (snapshot.Settings.Application?.ShowProfileChangePopup == false)
                        _profileChangePopup?.Dismiss();
                    if (announcement is not null)
                    {
                        _profileChangePopup ??= new ProfileChangePopupWindow();
                        _profileChangePopup.Present(announcement);
                    }
                });
                ApplyTheme(startup.Snapshot.Settings.Application.Theme!);
                var viewModel = MainWindowViewModel.CreatePersistent(
                    startup.Snapshot,
                    _store,
                    trayAvailable,
                    outputCoordinator,
                    initialOutput,
                    pluginGateway,
                    applicationState: stateService,
                    dispatch: action => Dispatcher.UIThread.Post(action));
                _viewModel = viewModel;
                _mcpConfigurationStore = new McpConfigurationStore(directory);
                _mcpQueries = new McpReadQueryService(
                    stateService,
                    () => _pluginInventory.List(
                        stateService.Current.Settings.Plugins!.Enabled!, _plugins?.Entries ?? []));
                _mcpCommands = new McpCommandService(stateService, _mcpQueries);
                _mcpCommands.ApprovalRequested += McpCommands_OnApprovalRequested;
                _mcpCommands.ApprovalCleared += McpCommands_OnApprovalCleared;
                await InitializeMcpAsync();
                pluginGateway.AvailabilityChanged += async (_, change) =>
                {
                    if (_shutdownApproved) return;
                    try
                    {
                        await outputCoordinator.ReportAvailabilityAsync(
                            change.PluginId,
                            change.IsAvailable,
                            change.ReasonCode,
                            change.Message);
                        await Dispatcher.UIThread.InvokeAsync(() => UpdateIntegrationStatuses(viewModel));
                        var gameCatalog = await pluginGateway.ListGameCatalogAsync(change.PluginId);
                        await await Dispatcher.UIThread.InvokeAsync<Task>(
                            () => viewModel.RefreshGameCatalogAsync(change.PluginId, gameCatalog));
                        if (change.ReasonCode is "plugin-exited" or "timeout"
                            && !_pluginPackageChanges.ContainsKey(change.PluginId))
                        {
                            AppLog.Write($"Plugin {change.PluginId} became unavailable: {change.Message}");
                            _ = RestartPluginAsync(packagesDirectory, change.PluginId, pluginGateway, viewModel, outputCoordinator);
                        }
                        await await Dispatcher.UIThread.InvokeAsync<Task>(
                            () => viewModel.RefreshProfileResourcesAsync());
                    }
                    catch (Exception exception)
                    {
                        AppLog.Write($"Plugin availability update failed: {exception.Message}");
                    }
                };
                var mainWindow = new MainWindow(viewModel, directory,
                    OperatingSystem.IsWindows() ? new WindowsStartupRegistration(Environment.ProcessPath) : null);
                AttachWindowsSessionEnd(mainWindow);
                _mainWindow = mainWindow;
                var icon = CreateIcon();
                mainWindow.Icon = icon;
                desktop.MainWindow = mainWindow;
                if (trayAvailable)
                {
                    _trayIcon = CreateTrayIcon(mainWindow, icon);
                    TrayIcon.SetIcons(this, new TrayIcons { _trayIcon });
                }
                if (_mcpCommands.Pending is { } pendingApproval)
                    McpCommands_OnApprovalRequested(_mcpCommands, pendingApproval);
                mainWindow.ExitRequested += (_, _) => RequestQuit();
                mainWindow.McpApprovalDecided += (_, decision) =>
                {
                    if (decision.Approved) _mcpCommands?.Approve(decision.ApprovalId);
                    else _mcpCommands?.Decline(decision.ApprovalId);
                };
                mainWindow.HiddenToTray += (_, _) =>
                {
                    if (_trayIcon is not null)
                        _trayIcon.ToolTipText = $"{ApplicationVersion.DisplayName} is running in the system tray.";
                };
                if (trayAvailable && ShouldStartMinimized(desktop.Args, startup.Snapshot.Settings.Application.StartMinimized == true))
                {
                    _trayIcon!.ToolTipText = $"{ApplicationVersion.DisplayName} is running in the system tray.";
                    mainWindow.CreateHiddenPlatformWindow();
                    mainWindow.NotifyStartedMinimized();
                }
                else
                    mainWindow.Show();
                StartApplicationObservation(viewModel);
                var pluginStartup = StartPluginsAsync(packagesDirectory, enabledPluginIds);
                _pluginStartupTask = pluginStartup;
                try
                {
                    await pluginStartup;
                    if (_plugins is not null)
                    {
                        pluginGateway.Attach(_plugins.Entries);
                        UpdateIntegrationStatuses(viewModel);
                        UpdateInstalledPlugins(viewModel);
                        await viewModel.ReapplyOutputAsync();
                        await viewModel.RefreshProfileResourcesAsync();
                        foreach (var entry in _plugins.Entries.Where(entry => entry.IsRunning))
                        {
                            var gameCatalog = await pluginGateway.ListGameCatalogAsync(entry.PluginId);
                            await viewModel.RefreshGameCatalogAsync(entry.PluginId, gameCatalog);
                        }
                        foreach (var entry in _plugins.Entries.Where(entry => !entry.IsRunning))
                            _ = RestartPluginAsync(packagesDirectory, entry.PluginId, pluginGateway, viewModel, outputCoordinator);
                    }
                }
                finally
                {
                    if (ReferenceEquals(_pluginStartupTask, pluginStartup)) _pluginStartupTask = null;
                }
            }
            catch (Exception exception)
            {
                ShowStartupError(desktop, $"FocusRGB could not start: {exception.Message}");
            }
        }

        private void ShowStartupError(IClassicDesktopStyleApplicationLifetime desktop, string message)
        {
            var errorWindow = new Window
            {
                Title = "FocusRGB startup error",
                Width = 520,
                Height = 180,
                Content = new TextBlock { Text = message, Margin = new Thickness(24), TextWrapping = Avalonia.Media.TextWrapping.Wrap }
            };
            _startupErrorWindow = errorWindow;
            AttachWindowsSessionEnd(errorWindow);
            desktop.MainWindow = errorWindow;
            errorWindow.Closed += (_, _) =>
            {
                _startupErrorWindow = null;
                desktop.Shutdown(1);
            };
            errorWindow.Show();
        }

        public void ApplyTheme(string theme) => RequestedThemeVariant = theme switch
        {
            "Light" or "light" => ThemeVariant.Light,
            "Dark" or "dark" => ThemeVariant.Dark,
            _ => ThemeVariant.Default
        };

        public void Dispose()
        {
            _profileChangePopup?.Close();
            _pluginDiscovery?.Dispose();
            _pluginInventory?.Dispose();
            _pluginConfigurationStore?.Dispose();
            _pluginDiscoveryHttpClient.Dispose();
            _pluginDownloadHttpClient.Dispose();
        }

        public McpUiState McpState => _mcpState;

        public async Task RequestProfilePreviewAsync(string profileId, CancellationToken cancellationToken)
        {
            if (_mcpCommands is null) return;
            var result = await _mcpCommands.DispatchAsync(new LocalMcpRequest
            {
                Method = "previewProfile",
                ClientName = "FocusRGB app",
                Parameters = System.Text.Json.JsonSerializer.SerializeToElement(new { profile = profileId, durationSeconds = 10 })
            }, cancellationToken);
            if (result.TryGetProperty("error", out var error))
                _viewModel?.ReportUnexpectedError(error.GetProperty("message").GetString()!);
        }

        public Task CancelProfilePreviewAsync() => _applicationState?.CancelPreviewAsync() ?? Task.CompletedTask;

        public async Task<McpUiState> SetMcpEnabledAsync(
            bool enabled,
            CancellationToken cancellationToken = default)
        {
            if (!OperatingSystem.IsWindows() || _mcpConfigurationStore is null || _mcpQueries is null)
                return _mcpState = McpUiState.Unavailable;
            if (!enabled)
            {
                _mcpCommands?.DeclinePending();
                await StopMcpAsync();
                var disabled = await _mcpConfigurationStore.SetEnabledAsync(
                    false, cancellationToken: cancellationToken);
                return _mcpState = disabled.IsSuccess
                    ? new McpUiState(true, false, "Local MCP is off; no pipe is listening.")
                    : new McpUiState(true, false, disabled.ErrorMessage!);
            }

            var saved = await _mcpConfigurationStore.SetEnabledAsync(
                true, Environment.ProcessPath, cancellationToken);
            if (!saved.IsSuccess)
                return _mcpState = new McpUiState(true, false, saved.ErrorMessage!);
            try
            {
                StartMcp(saved.Configuration!);
                return _mcpState = new McpUiState(true, true, "Local MCP is on for reads and approval-gated commands.");
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return _mcpState = new McpUiState(true, false, $"Could not start local MCP: {exception.Message}");
            }
        }

        public void ShowMainWindow()
        {
            if (_mainWindow is not null)
                _mainWindow.ShowFromTray();
            else
            {
                if (_startupErrorWindow is null) return;
                _startupErrorWindow.WindowState = WindowState.Normal;
                _startupErrorWindow.Activate();
            }
        }

        private TrayIcon CreateTrayIcon(
            MainWindow mainWindow,
            WindowIcon icon)
        {
            var show = new NativeMenuItem("Show FocusRGB");
            show.Click += (_, _) => mainWindow.ShowFromTray();
            var reviewMcp = new NativeMenuItem("Review command request") { IsEnabled = false };
            reviewMcp.Click += (_, _) => ReviewMcpApprovalFromTray();
            _reviewMcpRequestMenuItem = reviewMcp;
            var quit = new NativeMenuItem("Quit");
            quit.Click += (_, _) => RequestQuit();
            var menu = new NativeMenu();
            menu.Items.Add(new NativeMenuItem(ApplicationVersion.DisplayName) { IsEnabled = false });
            menu.Items.Add(show);
            menu.Items.Add(reviewMcp);
            menu.Items.Add(new NativeMenuItemSeparator());
            menu.Items.Add(quit);
            var trayIcon = new TrayIcon
            {
                Icon = icon,
                ToolTipText = ApplicationVersion.DisplayName,
                Menu = menu,
                IsVisible = true
            };
            trayIcon.Clicked += (_, _) => mainWindow.ShowFromTray();
            return trayIcon;
        }

        internal static bool ShouldStartMinimized(string[]? args, bool savedPreference) =>
            savedPreference || args?.Contains("--minimized", StringComparer.OrdinalIgnoreCase) == true;

        private async Task QuitAsync()
        {
            if (_quitPending || _mainWindow is null || _desktop is null) return;
            _quitPending = true;
            if (!await _mainWindow.ConfirmUnsavedChangesAsync())
            {
                _quitPending = false;
                _mainWindow.CancelExitRequest();
                return;
            }
            if (_quitWithoutSaving)
            {
                _shutdownApproved = true;
                await StopPluginsAsync();
                Shutdown();
                return;
            }
            if (!await _mainWindow.PrepareQuitAsync())
            {
                _quitPending = false;
                _quitWithoutSaving = true;
                _mainWindow.CancelExitRequest();
                _mainWindow.ShowFromTray();
                _mainWindow.ReportQuitFailure();
                return;
            }

            _shutdownApproved = true;
            await StopPluginsAsync();
            Shutdown();
        }

        private async Task StopPluginsAsync()
        {
            await CancelProfilePreviewAsync();
            await StopMcpAsync();
            _observationCancellation?.Cancel();
            if (_observationTask is { } observationTask)
            {
                try
                {
                    await observationTask;
                }
                catch (OperationCanceledException)
                {
                }
                _observationTask = null;
            }
            _observationCancellation?.Dispose();
            _observationCancellation = null;
            _pluginStartupCancellation?.Cancel();
            var startup = _pluginStartupTask;
            if (startup is not null)
            {
                try
                {
                    await startup;
                }
                catch (OperationCanceledException)
                {
                }
            }
            if (_plugins is not null)
            {
                await _plugins.DisposeAsync();
                _plugins = null;
            }
            _outputCoordinator?.Dispose();
            _outputCoordinator = null;
        }

        private async Task InitializeMcpAsync()
        {
            if (!OperatingSystem.IsWindows())
            {
                _mcpState = McpUiState.Unavailable;
                return;
            }
            var loaded = await _mcpConfigurationStore!.LoadAsync();
            if (!loaded.IsSuccess)
            {
                _mcpState = new McpUiState(true, false, $"Local MCP is off: {loaded.ErrorMessage}");
                AppLog.Write(_mcpState.Message);
                return;
            }
            if (!loaded.Configuration!.Enabled)
            {
                _mcpState = new McpUiState(true, false, "Local MCP is off; no pipe is listening.");
                return;
            }
            var configuration = loaded.Configuration;
            if (!string.Equals(
                configuration.ApplicationPath,
                Environment.ProcessPath,
                StringComparison.OrdinalIgnoreCase))
            {
                var updated = await _mcpConfigurationStore.SetEnabledAsync(true, Environment.ProcessPath);
                if (!updated.IsSuccess)
                {
                    _mcpState = new McpUiState(true, false, $"Local MCP is off: {updated.ErrorMessage}");
                    AppLog.Write(_mcpState.Message);
                    return;
                }
                configuration = updated.Configuration!;
            }
            try
            {
                StartMcp(configuration);
                _mcpState = new McpUiState(true, true, "Local MCP is on for reads and approval-gated commands.");
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                _mcpState = new McpUiState(
                    true, false, $"Local MCP is off because its pipe could not start: {exception.Message}");
                AppLog.Write(_mcpState.Message);
            }
        }

        private void StartMcp(McpConfiguration configuration)
        {
            if (_mcpServer is not null) return;
            _mcpServer = new LocalMcpPipeServer(configuration.Credential!, _mcpCommands!.DispatchAsync);
            _ = _mcpServer.RunAsync().ContinueWith(
                task => AppLog.Write($"Local MCP listener stopped unexpectedly: {task.Exception?.GetBaseException().Message}"),
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default);
        }

        private async Task StopMcpAsync()
        {
            _mcpCommands?.DeclinePending();
            if (_mcpServer is null) return;
            await _mcpServer.DisposeAsync();
            _mcpServer = null;
        }

        private void McpCommands_OnApprovalRequested(object? sender, McpApprovalRequest request) =>
            Dispatcher.UIThread.Post(() =>
            {
                if (_reviewMcpRequestMenuItem is not null) _reviewMcpRequestMenuItem.IsEnabled = true;
                if (_trayIcon is not null) _trayIcon.ToolTipText = $"{ApplicationVersion.DisplayName}: command approval requested.";
                _mainWindow?.ShowMcpApproval(request, openedFromTray: false);
            });

        private void McpCommands_OnApprovalCleared(object? sender, EventArgs e) =>
            Dispatcher.UIThread.Post(() =>
            {
                if (_reviewMcpRequestMenuItem is not null) _reviewMcpRequestMenuItem.IsEnabled = false;
                _mainWindow?.ClearMcpApproval();
                if (_trayIcon is not null)
                    _trayIcon.ToolTipText = _mainWindow?.IsVisible == true
                        ? ApplicationVersion.DisplayName
                        : $"{ApplicationVersion.DisplayName} is running in the system tray.";
            });

        private void ReviewMcpApprovalFromTray()
        {
            if (_mainWindow is null || _mcpCommands?.Pending is not { } pending) return;
            _mainWindow.ShowFromTray();
            _mainWindow.ShowMcpApproval(pending, openedFromTray: true);
        }

        private void StartApplicationObservation(MainWindowViewModel viewModel)
        {
            var cancellation = new CancellationTokenSource();
            _observationCancellation = cancellation;
            _observationTask = Task.Run(async () =>
            {
                var observer = new WindowsApplicationObserver();
                while (true)
                {
                    cancellation.Token.ThrowIfCancellationRequested();
                    try
                    {
                        var observation = observer.Observe();
                        await await Dispatcher.UIThread.InvokeAsync<Task>(() =>
                            viewModel.ObserveApplicationsAsync(observation, cancellation.Token));
                    }
                    catch (Exception exception) when (exception is not OperationCanceledException)
                    {
                        AppLog.Write($"Application observation failed: {exception.Message}");
                    }
                    var interval = await Dispatcher.UIThread.InvokeAsync(() =>
                        viewModel.ApplicationObservationInterval);
                    await Task.Delay(interval, cancellation.Token);
                }
            }, cancellation.Token);
        }

        private async Task StartPluginsAsync(string packagesDirectory, IEnumerable<string> enabledPluginIds)
        {
            using var cancellation = new CancellationTokenSource();
            _pluginStartupCancellation = cancellation;
            var startup = PluginRuntime.StartAsync(packagesDirectory, enabledPluginIds, cancellation.Token);
            try
            {
                var runtime = await startup;
                if (cancellation.IsCancellationRequested)
                {
                    await runtime.DisposeAsync();
                    return;
                }
                _plugins = runtime;
                ConfigurePluginLogging(runtime.Entries);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
            }
            catch (Exception exception)
            {
                AppLog.Write($"Plugin startup failed: {exception.Message}");
            }
            finally
            {
                if (ReferenceEquals(_pluginStartupCancellation, cancellation))
                {
                    _pluginStartupCancellation = null;
                }
            }
        }

        private async Task RestartPluginAsync(
            string packagesDirectory,
            string pluginId,
            PluginRuntimeGateway gateway,
            MainWindowViewModel viewModel,
            OutputCoordinator coordinator)
        {
            if (_shutdownApproved
                || _plugins is not { } runtime
                || _pluginPackageChanges.ContainsKey(pluginId)
                || !_pluginRestartsPending.TryAdd(pluginId, 0)) return;
            var retry = false;
            var attempt = 0;
            try
            {
                attempt = _pluginRestartAttempts.AddOrUpdate(pluginId, 1, static (_, current) => current + 1);
                if (attempt > 3)
                {
                    await ShowRestartLimitAsync(viewModel, pluginId);
                    return;
                }
                await Task.Delay(attempt switch { 1 => 1000, 2 => 5000, _ => 30000 });
                if (_shutdownApproved || !ReferenceEquals(_plugins, runtime)
                    || _pluginPackageChanges.ContainsKey(pluginId)) return;
                var entry = await runtime.RestartAsync(packagesDirectory, pluginId);
                if (_shutdownApproved || !ReferenceEquals(_plugins, runtime)) return;
                _pluginRestartLimits.TryRemove(pluginId, out _);
                ConfigurePluginLogging([entry]);
                gateway.Attach([entry]);
                await Dispatcher.UIThread.InvokeAsync(() => UpdateIntegrationStatuses(viewModel));
                if (entry.IsRunning)
                    await coordinator.ReportAvailabilityAsync(entry.PluginId, true);
                await RefreshManagedGameCatalogAsync(
                    viewModel,
                    pluginId,
                    cancellationToken => gateway.ListGameCatalogAsync(pluginId, cancellationToken),
                    CancellationToken.None);
                retry = !entry.IsRunning;
                if (retry && attempt < 3)
                    AppLog.Write($"Plugin {pluginId} failed to restart; another attempt will be made.");
                else if (!retry)
                    _ = ResetRestartAttemptsAfterStableRunAsync(runtime, entry);
                await await Dispatcher.UIThread.InvokeAsync<Task>(
                    () => viewModel.RefreshProfileResourcesAsync());
            }
            catch (ObjectDisposedException) when (_shutdownApproved)
            {
            }
            catch (Exception exception)
            {
                AppLog.Write($"Plugin {pluginId} restart failed: {exception.Message}");
                retry = true;
            }
            finally
            {
                _pluginRestartsPending.TryRemove(pluginId, out _);
            }
            if (retry && !_shutdownApproved)
            {
                if (attempt < 3)
                    _ = RestartPluginAsync(packagesDirectory, pluginId, gateway, viewModel, coordinator);
                else
                    await ShowRestartLimitAsync(viewModel, pluginId);
            }
        }

        private async Task ShowRestartLimitAsync(MainWindowViewModel viewModel, string pluginId)
        {
            _pluginRestartLimits.TryAdd(pluginId, 0);
            AppLog.Write($"Plugin {pluginId} restart limit reached; restart FocusRGB to try again.");
            await Dispatcher.UIThread.InvokeAsync(() => UpdateIntegrationStatuses(viewModel));
        }

        private async Task ResetRestartAttemptsAfterStableRunAsync(
            PluginRuntime runtime,
            PluginRuntimeEntry entry)
        {
            await Task.Delay(TimeSpan.FromMinutes(1));
            if (ReferenceEquals(_plugins, runtime)
                && runtime.Entries.Any(current => ReferenceEquals(current, entry) && current.IsRunning))
                _pluginRestartAttempts.TryRemove(entry.PluginId, out _);
        }

        private void UpdateIntegrationStatuses(MainWindowViewModel viewModel)
        {
            var entries = _plugins?.Entries ?? [];
            viewModel.SetIntegrationStatuses(entries.Select(entry => entry.IsRunning
                ? $"{entry.PluginId}: running"
                    + (entry.Handshake?.Sdk is { } sdk
                        ? $" — SDK {sdk.Name} {sdk.Version} ({sdk.Language})"
                        : string.Empty)
                : $"{entry.PluginId}: {entry.ErrorCode ?? entry.Client?.Health ?? "unavailable"} — "
                    + (entry.ErrorMessage ?? "The plugin is not available.")
                    + (_pluginRestartLimits.ContainsKey(entry.PluginId) ? " Automatic restart stopped." : string.Empty)));
        }

        private static void ConfigurePluginLogging(IEnumerable<PluginRuntimeEntry> entries)
        {
            foreach (var plugin in entries)
            {
                if (plugin.Client is { } client)
                {
                    client.LogReceived += (_, message) => AppLog.Write($"Plugin {plugin.PluginId}: {message}");
                    if (plugin.Handshake?.Sdk is { } sdk)
                        AppLog.Write($"Plugin {plugin.PluginId} SDK: {sdk.Name} {sdk.Version} ({sdk.Language}).");
                }
                else
                    AppLog.Write($"Plugin {plugin.PluginId} did not start: {plugin.ErrorCode}: {plugin.ErrorMessage}");
            }
        }

        public async Task LoadPluginPageAsync(bool refresh, CancellationToken cancellationToken = default)
        {
            if (_pluginDiscovery is null || _viewModel is null) return;
            var catalog = await _pluginDiscovery.DiscoverAsync(refresh,
                includePrereleases: _viewModel.SavedIncludeBetaPlugins, cancellationToken: cancellationToken);
            _viewModel.SetOfficialPlugins(catalog, listing => _pluginPackageCache?.Contains(listing) == true);
            UpdateInstalledPlugins(_viewModel);
        }

        public async Task RefreshGameCatalogsAsync(CancellationToken cancellationToken = default)
        {
            if (_viewModel is null || _pluginGateway is null
                || await GetPluginRuntimeAsync() is not { } runtime) return;
            foreach (var entry in runtime.Entries.Where(entry => entry.IsRunning))
            {
                var catalog = await _pluginGateway.ListGameCatalogAsync(entry.PluginId, cancellationToken);
                await _viewModel.RefreshGameCatalogAsync(entry.PluginId, catalog, cancellationToken);
            }
        }

        public Task<PluginArchiveResult> InspectCustomPluginAsync(
            string archivePath,
            CancellationToken cancellationToken = default) =>
            _pluginInstaller?.InspectAsync(archivePath, cancellationToken: cancellationToken)
            ?? Task.FromResult(PluginArchiveResult.Fail("runtime-unavailable", "Plugin management is not ready."));

        public Task<DownloadedPluginPackage> DownloadCustomPluginAsync(
            string repository,
            CancellationToken cancellationToken = default) =>
            _pluginDownloader?.DownloadCustomGitHubAsync(repository, cancellationToken)
            ?? throw new InvalidOperationException("Plugin management is not ready.");

        public async Task<PluginManagementResult> InstallOfficialPluginAsync(
            OfficialPluginListing listing,
            bool enable,
            PluginArchiveResult approval,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(approval);
            if (!approval.IsSuccess || approval.Manifest!.Id != listing.Id || approval.Manifest.Version != listing.Version)
                return PluginManagementResult.Fail("The approved package does not match this release.");
            if (_pluginPackageCache is null || _pluginInstaller is null)
                return PluginManagementResult.Fail("Plugin management is not ready.");
            var staged = await _pluginPackageCache.StageAsync(listing, _pluginInstaller, approval, cancellationToken);
            if (!staged.IsSuccess)
            {
                _viewModel?.RefreshPluginLists();
                return PluginManagementResult.Fail(staged.ErrorMessage ?? "The official package could not be staged.");
            }
            try
            {
                return await CommitPluginAsync(
                    staged.StagedPackage!, enable, isOfficial: true, cancellationToken);
            }
            finally
            {
                _pluginInstaller.Discard(staged.StagedPackage!);
            }
        }

        public async Task<PluginManagementResult> DownloadOfficialPluginAsync(
            OfficialPluginListing listing,
            CancellationToken cancellationToken = default)
        {
            if (_pluginPackageCache is null || _pluginDownloader is null || _pluginInstaller is null)
                return PluginManagementResult.Fail("Plugin management is not ready.");
            await _pluginPackageCache.DownloadAsync(listing, _pluginDownloader, _pluginInstaller, cancellationToken);
            _viewModel?.RefreshPluginLists();
            return PluginManagementResult.Ok($"Downloaded and verified {listing.Id} {listing.Version}.");
        }

        public Task<PluginArchiveResult> InspectOfficialPluginAsync(
            OfficialPluginListing listing,
            CancellationToken cancellationToken = default) =>
            _pluginPackageCache is not null && _pluginDownloader is not null && _pluginInstaller is not null
                ? _pluginPackageCache.InspectOfficialAsync(listing, _pluginDownloader, cancellationToken)
                : Task.FromResult(PluginArchiveResult.Fail("runtime-unavailable", "Plugin management is not ready."));

        public async Task<PluginManagementResult> InstallCustomPluginAsync(
            string archivePath,
            string pluginId,
            string sha256,
            bool enable,
            CancellationToken cancellationToken = default)
        {
            if (_pluginInstaller is null)
                return PluginManagementResult.Fail("Plugin management is not ready.");
            var staged = await _pluginInstaller.StageAsync(
                archivePath, pluginId, sha256, cancellationToken);
            if (!staged.IsSuccess)
                return PluginManagementResult.Fail(staged.ErrorMessage ?? "The custom package could not be staged.");
            try
            {
                return await CommitPluginAsync(
                    staged.StagedPackage!, enable, isOfficial: false, cancellationToken);
            }
            finally
            {
                _pluginInstaller.Discard(staged.StagedPackage!);
            }
        }

        public async Task<PluginManagementResult> SetPluginEnabledAsync(
            string pluginId,
            bool enabled,
            CancellationToken cancellationToken = default)
        {
            if (_viewModel is null || await GetPluginRuntimeAsync() is not { } runtime)
                return PluginManagementResult.Fail("Plugin runtime is not ready.");
            if (!_pluginPackageChanges.TryAdd(pluginId, 0))
                return PluginManagementResult.Fail($"Plugin '{pluginId}' already has an action in progress.");
            try
            {
                if (enabled)
                {
                    var error = await _viewModel.SetPluginEnabledAsync(pluginId, true, cancellationToken);
                    if (error is not null) return PluginManagementResult.Fail(error);
                    var started = await StartManagedPluginAsync(runtime, pluginId, cancellationToken);
                    return started.IsRunning
                        ? PluginManagementResult.Ok($"Enabled and started {pluginId}.")
                        : PluginManagementResult.Fail(started.ErrorMessage ?? $"Enabled {pluginId}, but it did not start.");
                }

                var hadEntry = runtime.Entries.Any(entry => entry.PluginId == pluginId);
                await StopManagedPluginAsync(runtime, pluginId, cancellationToken);
                var saveError = await _viewModel.SetPluginEnabledAsync(pluginId, false, cancellationToken);
                if (saveError is null) return PluginManagementResult.Ok($"Disabled {pluginId}.");
                if (hadEntry) await StartManagedPluginAsync(runtime, pluginId, cancellationToken);
                return PluginManagementResult.Fail(saveError);
            }
            finally
            {
                _pluginPackageChanges.TryRemove(pluginId, out _);
                UpdatePluginViews();
            }
        }

        public PluginConfigurationLoadResult LoadPluginConfiguration(string pluginId) =>
            _pluginConfigurationStore?.Load(pluginId)
            ?? PluginConfigurationLoadResult.Fail("Plugin configuration is not ready.");

        public async Task<PluginManagementResult> SavePluginConfigurationAsync(
            string pluginId,
            PluginConfigurationSchema schema,
            IReadOnlyDictionary<string, System.Text.Json.JsonElement> values,
            CancellationToken cancellationToken = default)
        {
            if (_pluginConfigurationStore is null || await GetPluginRuntimeAsync() is not { } runtime)
                return PluginManagementResult.Fail("Plugin configuration is not ready.");
            if (!_pluginPackageChanges.TryAdd(pluginId, 0))
                return PluginManagementResult.Fail($"Plugin '{pluginId}' already has an action in progress.");
            var wasRunning = runtime.Entries.Any(entry => entry.PluginId == pluginId && entry.IsRunning);
            var stopped = false;
            try
            {
                if (wasRunning)
                {
                    await StopManagedPluginAsync(runtime, pluginId, cancellationToken);
                    stopped = true;
                }
                var error = await _pluginConfigurationStore.SaveAsync(
                    pluginId, schema, values, cancellationToken);
                if (error is not null)
                {
                    if (wasRunning) await StartManagedPluginAsync(runtime, pluginId, cancellationToken);
                    return PluginManagementResult.Fail(error);
                }
                if (!wasRunning) return PluginManagementResult.Ok($"Saved settings for {pluginId}.");
                var restarted = await StartManagedPluginAsync(runtime, pluginId, cancellationToken);
                return restarted.IsRunning
                    ? PluginManagementResult.Ok($"Saved settings and restarted {pluginId}.")
                    : PluginManagementResult.Fail(
                        $"Saved settings, but {pluginId} did not restart: {restarted.ErrorMessage}");
            }
            catch (OperationCanceledException) when (stopped)
            {
                await StartManagedPluginAsync(runtime, pluginId, CancellationToken.None);
                throw;
            }
            finally
            {
                _pluginPackageChanges.TryRemove(pluginId, out _);
                UpdatePluginViews();
            }
        }

        public async Task<PluginManagementResult> UninstallPluginAsync(
            string pluginId,
            CancellationToken cancellationToken = default)
        {
            if (_viewModel is null || _pluginInstaller is null || _pluginInventory is null
                || await GetPluginRuntimeAsync() is not { } runtime)
                return PluginManagementResult.Fail("Plugin management is not ready.");
            if (!_pluginPackageChanges.TryAdd(pluginId, 0))
                return PluginManagementResult.Fail($"Plugin '{pluginId}' already has an action in progress.");
            var wasEnabled = _viewModel.EnabledPluginIds.Contains(pluginId, StringComparer.Ordinal);
            var hadEntry = runtime.Entries.Any(entry => entry.PluginId == pluginId);
            try
            {
                await StopManagedPluginAsync(runtime, pluginId, cancellationToken);
                var saveError = await _viewModel.SetPluginEnabledAsync(pluginId, false, cancellationToken);
                if (saveError is not null)
                {
                    if (hadEntry) await StartManagedPluginAsync(runtime, pluginId, cancellationToken);
                    return PluginManagementResult.Fail(saveError);
                }
                var removed = _pluginInstaller.Uninstall(pluginId);
                if (!removed.IsSuccess)
                {
                    if (wasEnabled) await _viewModel.SetPluginEnabledAsync(pluginId, true, cancellationToken);
                    if (hadEntry) await StartManagedPluginAsync(runtime, pluginId, cancellationToken);
                    return PluginManagementResult.Fail(removed.ErrorMessage ?? $"Could not uninstall {pluginId}.");
                }
                var warning = await _pluginInventory.SetOfficialAsync(pluginId, false, cancellationToken);
                return PluginManagementResult.Ok($"Uninstalled {pluginId}."
                    + (warning is null ? "" : $" Origin metadata warning: {warning}"));
            }
            finally
            {
                _pluginPackageChanges.TryRemove(pluginId, out _);
                UpdatePluginViews();
            }
        }

        private async Task<PluginManagementResult> CommitPluginAsync(
            StagedPluginPackage staged,
            bool enable,
            bool isOfficial,
            CancellationToken cancellationToken)
        {
            if (_viewModel is null || _pluginInstaller is null || _pluginInventory is null
                || await GetPluginRuntimeAsync() is not { } runtime)
                return PluginManagementResult.Fail("Plugin management is not ready.");
            var pluginId = staged.Manifest.Id!;
            if (!_pluginPackageChanges.TryAdd(pluginId, 0))
                return PluginManagementResult.Fail($"Plugin '{pluginId}' already has an action in progress.");
            var hadEntry = runtime.Entries.Any(entry => entry.PluginId == pluginId);
            try
            {
                await StopManagedPluginAsync(runtime, pluginId, cancellationToken);
                var committed = _pluginInstaller.Commit(staged);
                if (!committed.IsSuccess)
                {
                    if (hadEntry) await StartManagedPluginAsync(runtime, pluginId, cancellationToken);
                    return PluginManagementResult.Fail(committed.ErrorMessage ?? $"Could not install {pluginId}.");
                }

                var originWarning = await _pluginInventory.SetOfficialAsync(
                    pluginId, isOfficial, cancellationToken);
                var saveError = await _viewModel.SetPluginEnabledAsync(pluginId, enable, cancellationToken);
                var shouldRun = _viewModel.EnabledPluginIds.Contains(pluginId, StringComparer.Ordinal);
                PluginRuntimeEntry? started = null;
                if (shouldRun) started = await StartManagedPluginAsync(runtime, pluginId, cancellationToken);
                if (saveError is not null)
                    return PluginManagementResult.Fail($"Installed {pluginId}, but settings could not be saved: {saveError}");
                if (started is not null && !started.IsRunning)
                    return PluginManagementResult.Fail($"Installed and enabled {pluginId}, but it did not start: {started.ErrorMessage}");
                var warning = committed.Warning ?? originWarning;
                return PluginManagementResult.Ok($"Installed {pluginId} {staged.Manifest.Version}."
                    + (warning is null ? "" : $" Warning: {warning}"));
            }
            finally
            {
                _pluginPackageChanges.TryRemove(pluginId, out _);
                UpdatePluginViews();
            }
        }

        private async Task<PluginRuntime?> GetPluginRuntimeAsync()
        {
            if (_pluginStartupTask is { } startup) await startup;
            return _plugins;
        }

        private async Task StopManagedPluginAsync(
            PluginRuntime runtime,
            string pluginId,
            CancellationToken cancellationToken)
        {
            if (!runtime.Entries.Any(entry => entry.PluginId == pluginId)) return;
            if (_outputCoordinator is not null)
                await _outputCoordinator.ReportAvailabilityAsync(
                    pluginId, false, "plugin-package-change", "The plugin is being changed.", cancellationToken);
            if (_viewModel is not null)
                await RefreshManagedGameCatalogAsync(
                    _viewModel, pluginId, null, cancellationToken);
            _pluginGateway?.Detach(pluginId);
            await runtime.StopAsync(pluginId, cancellationToken);
        }

        private async Task<PluginRuntimeEntry> StartManagedPluginAsync(
            PluginRuntime runtime,
            string pluginId,
            CancellationToken cancellationToken)
        {
            var entry = await runtime.StartPluginAsync(_packagesDirectory!, pluginId, cancellationToken);
            ConfigurePluginLogging([entry]);
            _pluginGateway?.Attach([entry]);
            if (entry.IsRunning && _outputCoordinator is not null)
                await _outputCoordinator.ReportAvailabilityAsync(pluginId, true, cancellationToken: cancellationToken);
            if (_viewModel is not null && _pluginGateway is not null)
                await RefreshManagedGameCatalogAsync(
                    _viewModel,
                    pluginId,
                    token => _pluginGateway.ListGameCatalogAsync(pluginId, token),
                    cancellationToken);
            return entry;
        }

        internal static async Task RefreshManagedGameCatalogAsync(
            MainWindowViewModel viewModel,
            string pluginId,
            Func<CancellationToken, Task<PluginGatewayResult<PluginCatalogResult>>>? load,
            CancellationToken cancellationToken)
        {
            var result = load is null
                ? new PluginGatewayResult<PluginCatalogResult>(
                    "plugin-stopped", "The discovery plugin is not running.")
                : await load(cancellationToken);
            await viewModel.RefreshGameCatalogAsync(pluginId, result, cancellationToken);
        }

        private void UpdatePluginViews()
        {
            if (_viewModel is null) return;
            UpdateIntegrationStatuses(_viewModel);
            UpdateInstalledPlugins(_viewModel);
        }

        private void UpdateInstalledPlugins(MainWindowViewModel viewModel)
        {
            if (_pluginInventory is null) return;
            viewModel.SetInstalledPlugins(_pluginInventory.List(
                viewModel.EnabledPluginIds, _plugins?.Entries ?? []));
        }

        private async void RequestQuit()
        {
            try
            {
                await QuitAsync();
            }
            catch (Exception exception)
            {
                _quitPending = false;
                _mainWindow?.ReportUnexpectedError($"Could not quit: {exception.Message}");
            }
        }

        private void Desktop_OnShutdownRequested(object? sender, ShutdownRequestedEventArgs e)
        {
            if (_shutdownApproved || _mainWindow is null) return;
            CompleteSessionEnd();
        }

        private void AttachWindowsSessionEnd(Window window)
        {
            if (!OperatingSystem.IsWindows()) return;
            _windowsMessageHook ??= OnWindowsMessage;
            Win32Properties.AddWndProcHookCallback(window, _windowsMessageHook);
        }

        private IntPtr OnWindowsMessage(
            IntPtr hWnd,
            uint message,
            IntPtr wParam,
            IntPtr lParam,
            ref bool handled) => WindowsSessionEndMessage.Handle(
                message, wParam, CompleteSessionEnd, ref handled);

        private void CompleteSessionEnd()
        {
            var cleanup = CompleteSessionEndAsync();
            if (!cleanup.IsCompleted)
            {
                var frame = new DispatcherFrame();
                _ = cleanup.ContinueWith(
                    _ => Dispatcher.UIThread.Post(() => frame.Continue = false),
                    TaskScheduler.Default);
                Dispatcher.UIThread.PushFrame(frame);
            }
            try
            {
                cleanup.GetAwaiter().GetResult();
            }
            catch (Exception exception)
            {
                AppLog.Write($"Best-effort session-end cleanup failed: {exception.Message}");
            }
        }

        private async Task CompleteSessionEndAsync()
        {
            if (_shutdownApproved || _desktop is null) return;
            _shutdownApproved = true;
            if (_mainWindow is not null)
            {
                using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                try
                {
                    if (!await _mainWindow.PrepareQuitAsync(cancellation.Token))
                        AppLog.Write("Could not save runtime state during session end.");
                }
                catch (Exception exception)
                {
                    AppLog.Write($"Could not save runtime state during session end: {exception.Message}");
                }
                _mainWindow.AllowClose();
            }
            await StopPluginsForSessionEndAsync();
            _desktop.Shutdown();
        }

        private async Task StopPluginsForSessionEndAsync()
        {
            try
            {
                await StopPluginsAsync().WaitAsync(TimeSpan.FromSeconds(1));
            }
            catch (Exception exception)
            {
                AppLog.Write($"Best-effort session-end cleanup failed: {exception.Message}");
            }
        }

        private void Shutdown()
        {
            AppLog.Write("Clean shutdown requested.");
            _shutdownApproved = true;
            _mainWindow!.AllowClose();
            _desktop!.Shutdown();
        }

        private static WindowIcon CreateIcon()
        {
            using var stream = AssetLoader.Open(new Uri("avares://FocusRGB/Assets/focusrgb.ico"));
            return new WindowIcon(stream);
        }
    }

    public sealed record McpUiState(bool IsAvailable, bool IsEnabled, string Message)
    {
        public static McpUiState Unavailable { get; } = new(
            false, false, "Local MCP is available on Windows only.");
    }
}
