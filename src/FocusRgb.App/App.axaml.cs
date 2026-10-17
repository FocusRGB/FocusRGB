using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Avalonia.Threading;
using FocusRgb.App.Diagnostics;
using FocusRgb.App.Plugins;
using FocusRgb.App.ViewModels;
using FocusRgb.Core.Coordination;
using FocusRgb.Core.Persistence;
using FocusRgb.Platform.Windows;

namespace FocusRgb.App
{
    public sealed class App : Application
    {
        private TrayIcon? _trayIcon;
        private IClassicDesktopStyleApplicationLifetime? _desktop;
        private MainWindow? _mainWindow;
        private Window? _startupErrorWindow;
        private LocalStateStore? _store;
        private PluginRuntime? _plugins;
        private OutputCoordinator? _outputCoordinator;
        private CancellationTokenSource? _pluginStartupCancellation;
        private Task? _pluginStartupTask;
        private CancellationTokenSource? _observationCancellation;
        private Task? _observationTask;
        private bool _quitPending;
        private bool _quitWithoutSaving;
        private bool _shutdownApproved;
        private int _pluginRestartPending;
        private int _pluginRestartAttempts;
        private bool _pluginRestartLimitReached;

        public override void Initialize() => AvaloniaXamlLoader.Load(this);

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                _desktop = desktop;
                desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                desktop.ShutdownRequested += Desktop_OnShutdownRequested;
                desktop.Exit += (_, _) =>
                {
                    _trayIcon?.Dispose();
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
                var packagesDirectory = Path.Combine(directory, "plugins");
                var enabledPluginIds = startup.Snapshot.Settings.Plugins!.Enabled!.ToArray();
                var outputCoordinator = new OutputCoordinator(pluginGateway, startup.Snapshot.NativeSetup);
                _outputCoordinator = outputCoordinator;
                var initialOutput = await outputCoordinator.ApplyAsync(
                    startup.Snapshot.Settings,
                    startup.Snapshot.State,
                    startup.Snapshot.Desired.Reason);
                ApplyTheme(startup.Snapshot.Settings.Application.Theme!);
                var viewModel = MainWindowViewModel.CreatePersistent(
                    startup.Snapshot, _store, trayAvailable, outputCoordinator, initialOutput);
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
                        if (change.ReasonCode is "plugin-exited" or "timeout")
                        {
                            AppLog.Write($"Plugin {change.PluginId} became unavailable: {change.Message}");
                            _ = RestartPluginsAsync(packagesDirectory, enabledPluginIds, pluginGateway, viewModel, outputCoordinator);
                        }
                    }
                    catch (Exception exception)
                    {
                        AppLog.Write($"Plugin availability update failed: {exception.Message}");
                    }
                };
                var mainWindow = new MainWindow(viewModel, directory);
                outputCoordinator.StatusChanged += (_, status) =>
                    Dispatcher.UIThread.Post(() => mainWindow.ApplyOutputStatus(status));
                _mainWindow = mainWindow;
                var icon = CreateIcon();
                mainWindow.Icon = icon;
                desktop.MainWindow = mainWindow;
                if (trayAvailable)
                {
                    _trayIcon = CreateTrayIcon(mainWindow, icon);
                    TrayIcon.SetIcons(this, new TrayIcons { _trayIcon });
                }
                mainWindow.ExitRequested += (_, _) => RequestQuit();
                mainWindow.HiddenToTray += (_, _) =>
                {
                    if (_trayIcon is not null)
                        _trayIcon.ToolTipText = "FocusRGB is running in the system tray.";
                };
                if (trayAvailable && startup.Snapshot.Settings.Application.StartMinimized == true)
                {
                    _trayIcon!.ToolTipText = "FocusRGB is running in the system tray.";
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
                        await viewModel.ReapplyOutputAsync();
                        if (_plugins.Entries.Any(entry => !entry.IsRunning))
                            _ = RestartPluginsAsync(packagesDirectory, enabledPluginIds, pluginGateway, viewModel, outputCoordinator);
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
            var quit = new NativeMenuItem("Quit");
            quit.Click += (_, _) => RequestQuit();
            var menu = new NativeMenu();
            menu.Items.Add(show);
            menu.Items.Add(new NativeMenuItemSeparator());
            menu.Items.Add(quit);
            var trayIcon = new TrayIcon
            {
                Icon = icon,
                ToolTipText = "FocusRGB",
                Menu = menu,
                IsVisible = true
            };
            trayIcon.Clicked += (_, _) => mainWindow.ShowFromTray();
            return trayIcon;
        }

        private async Task QuitAsync()
        {
            if (_quitPending || _mainWindow is null || _desktop is null) return;
            _quitPending = true;
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
                    await Task.Delay(TimeSpan.FromSeconds(1), cancellation.Token);
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

        private async Task RestartPluginsAsync(
            string packagesDirectory,
            IReadOnlyList<string> enabledPluginIds,
            PluginRuntimeGateway gateway,
            MainWindowViewModel viewModel,
            OutputCoordinator coordinator)
        {
            if (Interlocked.Exchange(ref _pluginRestartPending, 1) != 0 || _shutdownApproved) return;
            var retry = false;
            var attempt = 0;
            try
            {
                attempt = Interlocked.Increment(ref _pluginRestartAttempts);
                if (attempt > 3)
                {
                    await ShowRestartLimitAsync(viewModel);
                    return;
                }
                await Task.Delay(attempt switch { 1 => 1000, 2 => 5000, _ => 30000 });
                if (_shutdownApproved) return;
                if (_plugins is not null) await _plugins.DisposeAsync();
                var runtime = await PluginRuntime.StartAsync(packagesDirectory, enabledPluginIds);
                _plugins = runtime;
                if (_shutdownApproved)
                {
                    await runtime.DisposeAsync();
                    if (ReferenceEquals(_plugins, runtime)) _plugins = null;
                    return;
                }
                _pluginRestartLimitReached = false;
                ConfigurePluginLogging(_plugins.Entries);
                gateway.Attach(_plugins.Entries);
                await Dispatcher.UIThread.InvokeAsync(() => UpdateIntegrationStatuses(viewModel));
                foreach (var entry in _plugins.Entries.Where(entry => entry.IsRunning))
                    await coordinator.ReportAvailabilityAsync(entry.PluginId, true);
                retry = _plugins.Entries.Any(entry => !entry.IsRunning);
                if (retry)
                {
                    if (attempt < 3)
                        AppLog.Write("One or more plugins failed to restart; another attempt will be made.");
                }
                else
                    _ = ResetRestartAttemptsAfterStableRunAsync(_plugins);
            }
            catch (Exception exception)
            {
                AppLog.Write($"Plugin restart failed: {exception.Message}");
                retry = true;
            }
            finally
            {
                Interlocked.Exchange(ref _pluginRestartPending, 0);
            }
            if (retry && !_shutdownApproved)
            {
                if (attempt < 3)
                    _ = RestartPluginsAsync(packagesDirectory, enabledPluginIds, gateway, viewModel, coordinator);
                else
                    await ShowRestartLimitAsync(viewModel);
            }
        }

        private async Task ShowRestartLimitAsync(MainWindowViewModel viewModel)
        {
            _pluginRestartLimitReached = true;
            AppLog.Write("Plugin restart limit reached; restart FocusRGB to try again.");
            await Dispatcher.UIThread.InvokeAsync(() => UpdateIntegrationStatuses(viewModel));
        }

        private async Task ResetRestartAttemptsAfterStableRunAsync(PluginRuntime runtime)
        {
            await Task.Delay(TimeSpan.FromMinutes(1));
            if (ReferenceEquals(_plugins, runtime) && runtime.Entries.All(entry => entry.IsRunning))
                Interlocked.Exchange(ref _pluginRestartAttempts, 0);
        }

        private void UpdateIntegrationStatuses(MainWindowViewModel viewModel)
        {
            var entries = _plugins?.Entries ?? [];
            viewModel.SetIntegrationStatuses(entries.Select(entry => entry.IsRunning
                ? $"{entry.PluginId}: running"
                : $"{entry.PluginId}: {entry.ErrorCode ?? entry.Client?.Health ?? "unavailable"} — "
                    + (entry.ErrorMessage ?? "The plugin is not available.")
                    + (_pluginRestartLimitReached ? " Automatic restart stopped." : string.Empty)));
        }

        private static void ConfigurePluginLogging(IEnumerable<PluginRuntimeEntry> entries)
        {
            foreach (var plugin in entries)
            {
                if (plugin.Client is { } client)
                    client.LogReceived += (_, message) => AppLog.Write($"Plugin {plugin.PluginId}: {message}");
                else
                    AppLog.Write($"Plugin {plugin.PluginId} did not start: {plugin.ErrorCode}: {plugin.ErrorMessage}");
            }
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
            _shutdownApproved = true;
            _mainWindow.AllowClose();
            _ = StopPluginsForSessionEndAsync();
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
            // shortcut: temporary generated glyph, replace when project branding is defined.
            const string ico =
                "AAABAAEAEBAAAAEAIACnAAAAFgAAAIlQTkcNChoKAAAADUlIRFIAAAAQAAAAEAgGAAAAH/P/YQAAAAFzUkdCAK7OHOkAAAAEZ0FNQQAAsY8L/GEFAAAACXBIWXMAAA7DAAAOwwHHb6hkAAAAPElEQVQ4T2NQTX79nxLMgC5AKh62Bsz7+R8DXPyIqQ6/Ab//L27CIkdHA5ABbsPwGIBb01AwgAQ8DAwAAOu2jQQgoyQsAAAAAElFTkSuQmCC";
            using var stream = new MemoryStream(Convert.FromBase64String(ico));
            return new WindowIcon(stream);
        }
    }
}
