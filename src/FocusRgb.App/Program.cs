using Avalonia;

namespace FocusRgb.App
{
    internal static class Program
    {
        [STAThread]
        public static int Main(string[] args) =>
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

        /// <summary>Configures Avalonia; also used by the Avalonia previewer.</summary>
        public static AppBuilder BuildAvaloniaApp() =>
            AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .WithInterFont()
                .LogToTrace();
    }
}
