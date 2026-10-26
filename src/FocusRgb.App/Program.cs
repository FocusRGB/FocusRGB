using System.Security.Principal;
using Avalonia;
using Avalonia.Threading;

namespace FocusRgb.App
{
    internal static class Program
    {
        [STAThread]
        public static int Main(string[] args)
        {
            if (!OperatingSystem.IsWindows()) return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            var user = WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName;
            using var showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, $"Local\\FocusRGB.Show.{user}");
            using var instance = new Mutex(true, $"Local\\FocusRGB.Instance.{user}", out var firstInstance);
            if (!firstInstance)
            {
                if (!App.ShouldStartMinimized(args, savedPreference: false)) showSignal.Set();
                return 0;
            }

            using var listenerCancellation = new CancellationTokenSource();
            var listener = Task.Run(() =>
            {
                while (!listenerCancellation.IsCancellationRequested)
                {
                    showSignal.WaitOne();
                    if (!listenerCancellation.IsCancellationRequested)
                        Dispatcher.UIThread.Post(() => (Application.Current as App)?.ShowMainWindow());
                }
            });
            try
            {
                return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            }
            finally
            {
                listenerCancellation.Cancel();
                showSignal.Set();
                listener.GetAwaiter().GetResult();
                instance.ReleaseMutex();
            }
        }

        /// <summary>Configures Avalonia; also used by the Avalonia previewer.</summary>
        public static AppBuilder BuildAvaloniaApp() =>
            AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .AfterSetup(_ =>
                {
                    if (OperatingSystem.IsWindows())
                    {
                        // Avalonia treats WM_QUERYENDSESSION as final; the window hook waits for WM_ENDSESSION.
                        if (!AvaloniaWindowsSessionEndWorkaround.SuppressQueryShutdown())
                            throw new InvalidOperationException("Avalonia query shutdown could not be suppressed.");
                    }
                })
                .WithInterFont()
                .LogToTrace();
    }
}
