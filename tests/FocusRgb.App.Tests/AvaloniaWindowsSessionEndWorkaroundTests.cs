namespace FocusRgb.App.Tests
{
    public sealed class AvaloniaWindowsSessionEndWorkaroundTests
    {
        [Fact]
        public void SuppressQueryShutdown_RemovesPlatformLifetimeEvents()
        {
            Assert.True(AvaloniaWindowsSessionEndWorkaround.SuppressQueryShutdown());
        }
    }
}
