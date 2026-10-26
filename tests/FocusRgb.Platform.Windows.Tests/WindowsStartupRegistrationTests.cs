using FocusRgb.Platform.Windows;

namespace FocusRgb.Platform.Windows.Tests
{
    public sealed class WindowsStartupRegistrationTests : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "focusrgb-startup-" + Guid.NewGuid().ToString("N"));

        [Fact]
        public void Refresh_MissingEntryStaysOff_AndEnablingQuotesTheCurrentExecutable()
        {
            var executable = Executable("Application folder", "FocusRGB.exe");
            var key = new FakeRunKey();
            var registration = new WindowsStartupRegistration(key, executable);

            Assert.False(registration.Refresh().Enabled);
            Assert.Equal(0, key.Writes);
            var enabled = registration.SetEnabled(true);

            Assert.True(enabled.Enabled);
            Assert.True(enabled.Available);
            Assert.Equal($"\"{executable}\" --minimized", key.Value);
            Assert.True(registration.Refresh().Enabled);
            Assert.Equal(1, key.Writes);
            Assert.False(registration.SetEnabled(false).Enabled);
            Assert.Null(key.Value);
            Assert.Equal(1, key.Deletes);
        }

        [Theory]
        [InlineData("\"C:\\Old folder\\FocusRGB.exe\"")]
        [InlineData("\"C:\\Old folder\\FocusRGB.exe\" --other")]
        public void Refresh_RepairsEnabledEntryToCurrentPathAndMinimizedArgument(string stale)
        {
            var executable = Executable("Moved folder", "FocusRGB.exe");
            var key = new FakeRunKey { Value = stale };

            var status = new WindowsStartupRegistration(key, executable).Refresh();

            Assert.True(status.Enabled);
            Assert.Equal($"\"{executable}\" --minimized", key.Value);
            Assert.Equal(1, key.Writes);
        }

        [Fact]
        public void SetEnabled_DisablesEvenWhenExecutableHasBeenRemoved()
        {
            var key = new FakeRunKey { Value = "old.exe" };
            var registration = new WindowsStartupRegistration(key, Path.Combine(_directory, "gone.exe"));
            Assert.True(registration.Refresh().Available);
            Assert.True(registration.Refresh().Enabled);

            var status = registration.SetEnabled(false);

            Assert.False(status.Enabled);
            Assert.Null(key.Value);
            Assert.False(status.Available);
        }

        [Theory]
        [InlineData("dotnet.exe")]
        [InlineData("FocusRGB.dll")]
        public void SetEnabled_RejectsHostOrNonExecutablePaths(string fileName)
        {
            var path = Executable("App", fileName);
            var key = new FakeRunKey();

            var status = new WindowsStartupRegistration(key, path).SetEnabled(true);

            Assert.False(status.Available);
            Assert.False(status.Enabled);
            Assert.Equal(0, key.Writes);
        }

        [Fact]
        public void SetEnabled_ReportsPermissionFailureAndKeepsPreviousRegistryState()
        {
            var executable = Executable("App", "FocusRGB.exe");
            var key = new FakeRunKey { FailWrites = true };
            var registration = new WindowsStartupRegistration(key, executable);

            var status = registration.SetEnabled(true);

            Assert.False(status.Available);
            Assert.False(status.Enabled);
            Assert.Null(key.Value);
            Assert.Contains("permissions", status.Message, StringComparison.Ordinal);
        }

        private string Executable(string directory, string fileName)
        {
            var folder = Path.Combine(_directory, directory);
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, fileName);
            File.WriteAllBytes(path, []);
            return path;
        }

        public void Dispose()
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, true);
            }
        }

        private sealed class FakeRunKey : IStartupRunKey
        {
            public object? Value { get; set; }
            public int Writes { get; private set; }
            public int Deletes { get; private set; }
            public bool FailWrites { get; init; }
            public object? Read() => Value;
            public void Write(string command)
            {
                if (FailWrites)
                {
                    throw new UnauthorizedAccessException();
                }
                Value = command;
                Writes++;
            }
            public void Delete() { Value = null; Deletes++; }
        }
    }
}
