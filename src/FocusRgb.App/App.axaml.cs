using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using FocusRgb.App.ViewModels;
using FocusRgb.Core.Persistence;

namespace FocusRgb.App
{
    public sealed class App : Application
    {
        private TrayIcon? _trayIcon;
        private IClassicDesktopStyleApplicationLifetime? _desktop;
        private MainWindow? _mainWindow;
        private bool _quitPending;

        public override void Initialize() => AvaloniaXamlLoader.Load(this);

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                _desktop = desktop;
                desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                var directory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "FocusRGB");
                var store = new LocalStateStore(directory);
                var startup = store.InitializeAsync(DateTimeOffset.UtcNow).GetAwaiter().GetResult();
                if (!startup.IsSuccess)
                {
                    throw new InvalidOperationException(
                        $"Could not initialize {startup.Error!.FileName}: {startup.Error.Message}");
                }

                var trayAvailable = OperatingSystem.IsWindows();
                ApplyTheme(startup.Snapshot!.Settings.Application!.Theme!);
                var mainWindow = new MainWindow(
                    MainWindowViewModel.CreatePersistent(startup.Snapshot, store, trayAvailable),
                    directory);
                _mainWindow = mainWindow;
                var icon = CreateIcon();
                mainWindow.Icon = icon;
                desktop.MainWindow = mainWindow;
                if (trayAvailable) _trayIcon = CreateTrayIcon(mainWindow, icon);
                if (trayAvailable && startup.Snapshot.Settings.Application.StartMinimized == true)
                {
                    void HideOnFirstOpen(object? sender, EventArgs args)
                    {
                        mainWindow.Opened -= HideOnFirstOpen;
                        mainWindow.Hide();
                        mainWindow.NotifyStartedMinimized();
                    }
                    mainWindow.Opened += HideOnFirstOpen;
                }
                mainWindow.ExitRequested += async (_, _) => await QuitAsync();
                desktop.ShutdownRequested += (_, _) => mainWindow.AllowClose();
                desktop.Exit += (_, _) => _trayIcon?.Dispose();
            }

            base.OnFrameworkInitializationCompleted();
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
            quit.Click += async (_, _) => await QuitAsync();
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
            if (!await _mainWindow.PrepareQuitAsync())
            {
                _quitPending = false;
                _mainWindow.CancelExitRequest();
                _mainWindow.ShowFromTray();
                return;
            }

            _mainWindow.AllowClose();
            _desktop.Shutdown();
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
