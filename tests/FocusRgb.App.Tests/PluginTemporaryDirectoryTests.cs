using FocusRgb.App.Plugins;
using FocusRgb.TestSupport;

namespace FocusRgb.App.Tests
{
    public sealed class PluginTemporaryDirectoryTests : IDisposable
    {
        private readonly TempDirectory _temporaryDirectory = new(nameof(PluginTemporaryDirectoryTests));

        [Fact]
        public void CleanupContents_RemovesEntriesFromOnlyRequestedRoots()
        {
            var downloads = Path.Combine(_temporaryDirectory.Path, "plugin-downloads");
            var staging = Path.Combine(_temporaryDirectory.Path, "plugins", ".staging");
            var installed = Path.Combine(_temporaryDirectory.Path, "plugins", "signalrgb");
            Directory.CreateDirectory(Path.Combine(downloads, "abandoned"));
            Directory.CreateDirectory(Path.Combine(staging, "abandoned"));
            Directory.CreateDirectory(installed);
            File.WriteAllText(Path.Combine(downloads, "orphan.tmp"), "remove");
            File.WriteAllText(Path.Combine(installed, "plugin.exe"), "keep");

            var warnings = PluginTemporaryDirectory.CleanupContents(downloads, staging);

            Assert.Empty(warnings);
            Assert.Empty(Directory.EnumerateFileSystemEntries(downloads));
            Assert.Empty(Directory.EnumerateFileSystemEntries(staging));
            Assert.True(File.Exists(Path.Combine(installed, "plugin.exe")));
        }

        public void Dispose() => _temporaryDirectory.Dispose();
    }
}
