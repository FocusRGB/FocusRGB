using System.Text.Json;
using FocusRgb.App.Plugins;
using FocusRgb.Contracts;

namespace FocusRgb.App.Tests
{
    /// <summary>Checks package trust boundaries before an external executable can be launched.</summary>
    public sealed class PluginPackageTests : IDisposable
    {
        private readonly string _directory = Path.Combine(
            Path.GetTempPath(), "FocusRgb.PackageTests", Guid.NewGuid().ToString("N"));

        [Fact]
        public void Load_ReturnsLaunchSpec_WhenManifestAndEntryPointAreValid()
        {
            WritePackage(Manifest());

            var result = PluginPackage.Load(_directory, "fake-output");

            Assert.True(result.IsSuccess, result.ErrorMessage);
            Assert.Equal("fake-output", result.Manifest!.Id);
            Assert.EndsWith("fake.dll", Assert.Single(result.Launch!.Arguments), StringComparison.Ordinal);
        }

        [Fact]
        public void Load_RejectsEntryPointOutsidePackage()
        {
            WritePackage(Manifest() with { EntryPoint = "..\\outside.dll" });

            var result = PluginPackage.Load(_directory, "fake-output");

            Assert.False(result.IsSuccess);
            Assert.Equal("plugin-absent", result.ErrorCode);
        }

        [Fact]
        public void Load_RejectsInvalidEntryPointPath()
        {
            var manifest = Manifest() with { EntryPoint = "bad\0path.exe" };
            WritePackage(manifest);

            var result = PluginPackage.Load(_directory, manifest.Id);

            Assert.False(result.IsSuccess);
            Assert.Equal("manifest-invalid", result.ErrorCode);
        }

        [Fact]
        public void Load_RejectsIncompatibleContractBeforeLaunch()
        {
            WritePackage(Manifest() with { ContractVersion = "1.0" });

            var result = PluginPackage.Load(_directory, "fake-output");

            Assert.False(result.IsSuccess);
            Assert.Equal("manifest-invalid", result.ErrorCode);
        }

        public void Dispose()
        {
            if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
        }

        private void WritePackage(ManifestData manifest)
        {
            var package = Path.Combine(_directory, "fake-output");
            Directory.CreateDirectory(package);
            File.WriteAllText(Path.Combine(package, "fake.dll"), string.Empty);
            File.WriteAllText(Path.Combine(package, PluginPackage.ManifestFileName),
                JsonSerializer.Serialize(manifest, PluginJson.Options));
        }

        private static ManifestData Manifest() => new(
            1,
            "fake-output",
            "0.1.0",
            ContractVersion.Current,
            "fake.dll",
            [OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "macos" : "linux"],
            [System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant()],
            ["output"],
            [],
            "MIT");

        private sealed record ManifestData(
            int SchemaVersion,
            string Id,
            string Version,
            string ContractVersion,
            string EntryPoint,
            IReadOnlyList<string> OperatingSystems,
            IReadOnlyList<string> Architectures,
            IReadOnlyList<string> Roles,
            IReadOnlyList<string> Resources,
            string License);
    }
}
