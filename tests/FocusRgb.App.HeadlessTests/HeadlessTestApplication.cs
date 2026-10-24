using Avalonia;
using Avalonia.Headless;
using Avalonia.Themes.Fluent;

[assembly: AvaloniaTestApplication(typeof(FocusRgb.App.HeadlessTests.HeadlessTestApplication))]

namespace FocusRgb.App.HeadlessTests
{
    public static class HeadlessTestApplication
    {
        public static AppBuilder BuildAvaloniaApp() => AppBuilder
            .Configure<TestApplication>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());

        private sealed class TestApplication : Application
        {
            public override void Initialize() => Styles.Add(new FluentTheme());
        }
    }
}
