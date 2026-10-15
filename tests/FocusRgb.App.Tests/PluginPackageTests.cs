using System.Text.Json;
using FocusRgb.App.Plugins;
using FocusRgb.Contracts;

namespace FocusRgb.App.Tests
{
    /// <summary>Checks package trust boundaries before an external executable can be launched.</summary>
    [Collection("Environment variables")]
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

        [Fact]
        public void Load_AcceptsLowerMinorContractVersion()
        {
            WritePackage(Manifest() with { ContractVersion = "0.0" });

            var result = PluginPackage.Load(_directory, "fake-output");

            Assert.True(result.IsSuccess, result.ErrorMessage);
        }

        [Theory]
        [InlineData("version")]
        [InlineData("operating-system")]
        [InlineData("duplicate-resource")]
        [InlineData("source")]
        public void Load_RejectsManifestThatDoesNotMatchSchemaRules(string invalid)
        {
            var manifest = invalid switch
            {
                "version" => Manifest() with { Version = "1.0" },
                "operating-system" => Manifest() with { OperatingSystems = ["plan9"] },
                "duplicate-resource" => Manifest() with { Resources = ["effect", "effect"] },
                _ => Manifest() with { Source = "relative" }
            };
            WritePackage(manifest);

            var result = PluginPackage.Load(_directory, "fake-output");

            Assert.False(result.IsSuccess);
            Assert.Equal("manifest-invalid", result.ErrorCode);
        }

        [Fact]
        public void Load_RejectsUnknownManifestProperty()
        {
            var package = WritePackage(Manifest());
            var path = Path.Combine(package, PluginPackage.ManifestFileName);
            var json = File.ReadAllText(path).TrimEnd('}');
            File.WriteAllText(path, json + ",\"unknown\":true}");

            var result = PluginPackage.Load(_directory, "fake-output");

            Assert.False(result.IsSuccess);
            Assert.Equal("manifest-invalid", result.ErrorCode);
        }

        [Fact]
        public void Load_ResolvesDllHostFromDotNetRootBeforePath()
        {
            var root = Path.Combine(_directory, "dotnet-root");
            Directory.CreateDirectory(root);
            var host = Path.Combine(root, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
            File.WriteAllText(host, string.Empty);
            WritePackage(Manifest());
            var previousHost = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
            var previousRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
            var previousPath = Environment.GetEnvironmentVariable("PATH");
            try
            {
                Environment.SetEnvironmentVariable("DOTNET_HOST_PATH", null);
                Environment.SetEnvironmentVariable("DOTNET_ROOT", root);
                Environment.SetEnvironmentVariable("PATH", string.Empty);

                var result = PluginPackage.Load(_directory, "fake-output");

                Assert.Equal(Path.GetFullPath(host), result.Launch!.FileName);
            }
            finally
            {
                Environment.SetEnvironmentVariable("DOTNET_HOST_PATH", previousHost);
                Environment.SetEnvironmentVariable("DOTNET_ROOT", previousRoot);
                Environment.SetEnvironmentVariable("PATH", previousPath);
            }
        }

        public void Dispose()
        {
            if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
        }

        private string WritePackage(ManifestData manifest)
        {
            var package = Path.Combine(_directory, "fake-output");
            Directory.CreateDirectory(package);
            File.WriteAllText(Path.Combine(package, "fake.dll"), string.Empty);
            File.WriteAllText(Path.Combine(package, PluginPackage.ManifestFileName),
                JsonSerializer.Serialize(manifest, PluginJson.Options));
            return package;
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
            string License,
            string? Source = null);
    }

    [CollectionDefinition("Environment variables", DisableParallelization = true)]
    public sealed class EnvironmentVariablesTests;
}
