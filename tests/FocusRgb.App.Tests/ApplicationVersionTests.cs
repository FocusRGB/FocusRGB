namespace FocusRgb.App.Tests
{
    public sealed class ApplicationVersionTests
    {
        [Theory]
        [InlineData("0.4.0-beta.1", "FocusRGB v0.4.0-beta.1 (Beta)")]
        [InlineData("0.4.0", "FocusRGB v0.4.0")]
        public void Format_LabelsPrereleaseVersions(string version, string expected) =>
            Assert.Equal(expected, ApplicationVersion.Format(version));

        [Fact]
        public void Current_UsesBetaPackageVersionWithoutSourceRevision()
        {
            Assert.Equal("0.4.0-beta.1", ApplicationVersion.Current);
            Assert.True(ApplicationVersion.IsPrerelease);
        }
    }
}
