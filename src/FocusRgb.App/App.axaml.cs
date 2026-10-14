using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using FocusRgb.App.ViewModels;
using FocusRgb.Core.Persistence;

namespace FocusRgb.App
{
    public sealed class App : Application
    {
        private TrayIcon? _trayIcon;

        public override void Initialize() => AvaloniaXamlLoader.Load(this);

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
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

                var mainWindow = new MainWindow(
                    MainWindowViewModel.CreatePersistent(startup.Snapshot!, store));
                var icon = CreateIcon();
                mainWindow.Icon = icon;
                desktop.MainWindow = mainWindow;
                _trayIcon = CreateTrayIcon(mainWindow, desktop, icon);
                desktop.ShutdownRequested += (_, _) => mainWindow.AllowClose();
                desktop.Exit += (_, _) => _trayIcon?.Dispose();
            }

            base.OnFrameworkInitializationCompleted();
        }

        private static TrayIcon CreateTrayIcon(
            MainWindow mainWindow,
            IClassicDesktopStyleApplicationLifetime desktop,
            WindowIcon icon)
        {
            var show = new NativeMenuItem("Show FocusRGB");
            show.Click += (_, _) => mainWindow.ShowFromTray();
            var quit = new NativeMenuItem("Quit");
            quit.Click += async (_, _) =>
            {
                if (!await mainWindow.PrepareQuitAsync())
                {
                    mainWindow.ShowFromTray();
                    return;
                }

                mainWindow.AllowClose();
                desktop.Shutdown();
            };
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
