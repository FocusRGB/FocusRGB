using Avalonia;
using Avalonia.Threading;
using System.Security.Principal;

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
                showSignal.Set();
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
                .WithInterFont()
                .LogToTrace();
    }
}
