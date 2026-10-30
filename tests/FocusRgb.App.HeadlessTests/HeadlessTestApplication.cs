using Avalonia;
using Avalonia.Headless;
using Avalonia.Themes.Fluent;

[assembly: AvaloniaTestApplication(typeof(FocusRgb.App.HeadlessTests.HeadlessTestApplication))]

namespace FocusRgb.App.HeadlessTests
{
    public static class HeadlessTestApplication
    {
        private static readonly FocusRgb.TestSupport.TempDirectory TemporaryData = new(nameof(HeadlessTestApplication));
        internal static string DataDirectory => TemporaryData.Path;

        static HeadlessTestApplication()
        {
            Environment.SetEnvironmentVariable("FOCUSRGB_DATA_DIR", DataDirectory);
            AppDomain.CurrentDomain.ProcessExit += (_, _) => TemporaryData.Dispose();
        }

        public static AppBuilder BuildAvaloniaApp() => AppBuilder
            .Configure<TestApplication>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());

        private sealed class TestApplication : Application
        {
            public override void Initialize() => Styles.Add(new FluentTheme());
        }
    }
}
