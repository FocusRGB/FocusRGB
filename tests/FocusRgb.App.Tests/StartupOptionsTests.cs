namespace FocusRgb.App.Tests
{
    public sealed class StartupOptionsTests
    {
        [Theory]
        [InlineData(false, "--minimized", true)]
        [InlineData(false, "--MINIMIZED", true)]
        [InlineData(false, "--other", false)]
        [InlineData(true, "--other", true)]
        public void ShouldStartMinimized_CombinesLaunchFlagWithSavedPreference(bool saved, string argument, bool expected)
        {
            Assert.Equal(expected, App.ShouldStartMinimized([argument], saved));
            Assert.Equal(saved, App.ShouldStartMinimized(null, saved));
        }
    }
}
