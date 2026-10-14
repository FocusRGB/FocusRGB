using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using FocusRgb.App.ViewModels;
using FocusRgb.Core.Persistence;

namespace FocusRgb.App
{
    public sealed class App : Application
    {
        public override void Initialize() => AvaloniaXamlLoader.Load(this);

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
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

                desktop.MainWindow = new MainWindow(
                    MainWindowViewModel.CreatePersistent(startup.Snapshot!, store));
            }

            base.OnFrameworkInitializationCompleted();
        }
    }
}
