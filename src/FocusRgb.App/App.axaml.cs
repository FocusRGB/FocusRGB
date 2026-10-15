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

namespace FocusRgb.App
{
    public sealed class App : Application
    {
        private TrayIcon? _trayIcon;
        private IClassicDesktopStyleApplicationLifetime? _desktop;
        private MainWindow? _mainWindow;
        private LocalStateStore? _store;
        private PluginRuntime? _plugins;
        private OutputCoordinator? _outputCoordinator;
        private CancellationTokenSource? _pluginStartupCancellation;
        private Task<PluginRuntime>? _pluginStartupTask;
        private bool _quitPending;
        private bool _quitWithoutSaving;
        private bool _shutdownApproved;

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
                var outputCoordinator = new OutputCoordinator(pluginGateway, startup.Snapshot.NativeSetup);
                _outputCoordinator = outputCoordinator;
                var initialOutput = await outputCoordinator.ApplyAsync(
                    startup.Snapshot.Settings,
                    startup.Snapshot.State,
                    startup.Snapshot.Desired.Reason);
                pluginGateway.AvailabilityChanged += async (_, change) =>
                {
                    try
                    {
                        await outputCoordinator.ReportAvailabilityAsync(change.PluginId, change.IsAvailable);
                    }
                    catch (Exception exception)
                    {
                        AppLog.Write($"Plugin availability update failed: {exception.Message}");
                    }
                };
                ApplyTheme(startup.Snapshot.Settings.Application.Theme!);
                var mainWindow = new MainWindow(
                    MainWindowViewModel.CreatePersistent(
                        startup.Snapshot, _store, trayAvailable, outputCoordinator, initialOutput),
                    directory);
                outputCoordinator.StatusChanged += (_, status) =>
                    Dispatcher.UIThread.Post(() => mainWindow.ApplyOutputStatus(status));
                _mainWindow = mainWindow;
                var icon = CreateIcon();
                mainWindow.Icon = icon;
                desktop.MainWindow = mainWindow;
                if (trayAvailable) _trayIcon = CreateTrayIcon(mainWindow, icon);
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
                await StartPluginsAsync(
                    Path.Combine(directory, "plugins"),
                    startup.Snapshot.Settings.Plugins!.Enabled!);
                if (_plugins is not null)
                {
                    pluginGateway.Attach(_plugins.Entries);
                    await outputCoordinator.ApplyAsync(
                        startup.Snapshot.Settings,
                        startup.Snapshot.State,
                        startup.Snapshot.Desired.Reason);
                }
            }
            catch (Exception exception)
            {
                ShowStartupError(desktop, $"FocusRGB could not start: {exception.Message}");
            }
        }

        private static void ShowStartupError(IClassicDesktopStyleApplicationLifetime desktop, string message)
        {
            var errorWindow = new Window
            {
                Title = "FocusRGB startup error",
                Width = 520,
                Height = 180,
                Content = new TextBlock { Text = message, Margin = new Thickness(24), TextWrapping = Avalonia.Media.TextWrapping.Wrap }
            };
            desktop.MainWindow = errorWindow;
            desktop.ShutdownMode = ShutdownMode.OnLastWindowClose;
            errorWindow.Show();
        }

        public void ApplyTheme(string theme) => RequestedThemeVariant = theme switch
        {
            "Light" or "light" => ThemeVariant.Light,
            "Dark" or "dark" => ThemeVariant.Dark,
            _ => ThemeVariant.Default
        };

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

            await StopPluginsAsync();
            Shutdown();
        }

        private async Task StopPluginsAsync()
        {
            _pluginStartupCancellation?.Cancel();
            if (_pluginStartupTask is not null)
            {
                try
                {
                    await _pluginStartupTask;
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

        private async Task StartPluginsAsync(string packagesDirectory, IEnumerable<string> enabledPluginIds)
        {
            using var cancellation = new CancellationTokenSource();
            _pluginStartupCancellation = cancellation;
            var startup = PluginRuntime.StartAsync(packagesDirectory, enabledPluginIds, cancellation.Token);
            _pluginStartupTask = startup;
            try
            {
                var runtime = await startup;
                if (cancellation.IsCancellationRequested)
                {
                    await runtime.DisposeAsync();
                    return;
                }
                _plugins = runtime;
                foreach (var plugin in runtime.Entries)
                {
                    if (plugin.Client is { } client)
                        client.LogReceived += (_, message) => AppLog.Write($"Plugin {plugin.PluginId}: {message}");
                    else
                        AppLog.Write($"Plugin {plugin.PluginId} did not start: {plugin.ErrorCode}: {plugin.ErrorMessage}");
                }
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
                    _pluginStartupTask = null;
                }
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

        private async void Desktop_OnShutdownRequested(object? sender, ShutdownRequestedEventArgs e)
        {
            if (_shutdownApproved || _mainWindow is null) return;
            e.Cancel = true;
            try
            {
                await QuitAsync();
            }
            catch (Exception exception)
            {
                _quitPending = false;
                _mainWindow?.ReportUnexpectedError($"Could not save during shutdown: {exception.Message}");
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
            const string png =
                "iVBORw0KGgoAAAANSUhEUgAAABAAAAAQCAYAAAAf8/9hAAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8YQUAAAAJcEhZcwAADsMAAA7DAcdvqGQAAAA8SURBVDhPY1BNfv2fEsyALkAqHrYGzPv5HwNc/IipDr8Bv/8vbsIiR0cDkAFuw/AYgFvTUDCABDwMDAAA67aNBCCjJCwAAAAASUVORK5CYII=";
            using var stream = new MemoryStream(Convert.FromBase64String(png));
            return new WindowIcon(stream);
        }
    }
}
