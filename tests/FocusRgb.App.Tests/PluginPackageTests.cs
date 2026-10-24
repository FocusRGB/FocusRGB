using System.Text.Json;
using FocusRgb.App.Plugins;
using FocusRgb.Core.Protocol;
using FocusRgb.TestSupport;

namespace FocusRgb.App.Tests
{
    /// <summary>Checks package trust boundaries before an external executable can be launched.</summary>
    [Collection("Environment variables")]
    public sealed class PluginPackageTests : IDisposable
    {
        private readonly TempDirectory _tempDirectory = new(nameof(PluginPackageTests));
        private string _directory => _tempDirectory.Path;

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
        public void Load_RejectsPluginIdReservedForManualGames()
        {
            WritePackage(Manifest() with { Id = "manual" }, "manual");

            var result = PluginPackage.Load(_directory, "manual");

            Assert.False(result.IsSuccess);
            Assert.Equal("manifest-invalid", result.ErrorCode);
            Assert.Contains("reserved", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
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

        [Theory]
        [InlineData("0.1")]
        [InlineData("2.0")]
        public void Load_RejectsIncompatibleContractBeforeLaunch(string contractVersion)
        {
            WritePackage(Manifest() with { ContractVersion = contractVersion });

            var result = PluginPackage.Load(_directory, "fake-output");

            Assert.False(result.IsSuccess);
            Assert.Equal("manifest-invalid", result.ErrorCode);
        }

        [Fact]
        public void Load_RejectsRunnerPluginUsingContract10()
        {
            WritePackage(Manifest() with { ContractVersion = "1.0", Runtime = "node" });

            var result = PluginPackage.Load(_directory, "fake-output");

            Assert.False(result.IsSuccess);
            Assert.Equal("contract-incompatible", result.ErrorCode);
            Assert.Equal("Runner plugins require contract version 1.1 or later.", result.ErrorMessage);
        }

        [Theory]
        [InlineData("0.1", false)]
        [InlineData("1.0", true)]
        [InlineData("1.2", true)]
        [InlineData("1.3", false)]
        [InlineData("2.0", false)]
        public void IsContractCompatible_UsesMajorAndHostMinor(string pluginVersion, bool expected) =>
            Assert.Equal(expected, PluginPackage.IsContractCompatible(pluginVersion, 1, 2));

        [Theory]
        [InlineData("version")]
        [InlineData("operating-system")]
        [InlineData("duplicate-resource")]
        [InlineData("unsupported-resource")]
        [InlineData("source")]
        public void Load_RejectsManifestThatDoesNotMatchSchemaRules(string invalid)
        {
            var manifest = invalid switch
            {
                "version" => Manifest() with { Version = "1.0" },
                "operating-system" => Manifest() with { OperatingSystems = ["plan9"] },
                "duplicate-resource" => Manifest() with { Resources = ["effect", "effect"] },
                "unsupported-resource" => Manifest() with { Resources = ["provider-effects"] },
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

        [Fact]
        public void Load_ResolvesPythonRunnerFromPath()
        {
            var runnerDirectory = Path.Combine(_directory, "runners");
            Directory.CreateDirectory(runnerDirectory);
            var runner = Path.Combine(runnerDirectory,
                "focusrgb-python-runner" + (OperatingSystem.IsWindows() ? ".exe" : string.Empty));
            File.WriteAllText(runner, string.Empty);
            const string entryPoint = "plugin.py";
            var package = WritePackage(Manifest() with { EntryPoint = entryPoint, Runtime = "python" });
            File.WriteAllText(Path.Combine(package, entryPoint), string.Empty);
            var previousPath = Environment.GetEnvironmentVariable("PATH");
            try
            {
                Environment.SetEnvironmentVariable("PATH", runnerDirectory);

                var result = PluginPackage.Load(_directory, "fake-output");

                Assert.True(result.IsSuccess, result.ErrorMessage);
                Assert.Equal(Path.GetFullPath(runner), result.Launch!.FileName);
                Assert.EndsWith(entryPoint, Assert.Single(result.Launch.Arguments), StringComparison.Ordinal);
            }
            finally
            {
                Environment.SetEnvironmentVariable("PATH", previousPath);
            }
        }

        [Fact]
        public void Load_ResolvesNodeRunnerFromDocumentedWindowsNpmLayout()
        {
            if (!OperatingSystem.IsWindows()) return;

            var npmDirectory = Path.Combine(_directory, "npm");
            var runner = Path.Combine(npmDirectory, "node_modules", "@focusrgb", "plugin-sdk", "dist",
                "focusrgb-node-runner.js");
            Directory.CreateDirectory(Path.GetDirectoryName(runner)!);
            File.WriteAllText(runner, string.Empty);
            File.WriteAllText(Path.Combine(npmDirectory, "focusrgb-node-runner.cmd"), string.Empty);
            var nodeDirectory = Path.Combine(_directory, "nodejs");
            Directory.CreateDirectory(nodeDirectory);
            var node = Path.Combine(nodeDirectory, "node.exe");
            File.WriteAllText(node, string.Empty);
            const string entryPoint = "PLUGIN.JS";
            var package = WritePackage(Manifest() with { EntryPoint = entryPoint, Runtime = "node" });
            File.WriteAllText(Path.Combine(package, entryPoint), string.Empty);
            var previousPath = Environment.GetEnvironmentVariable("PATH");
            try
            {
                Environment.SetEnvironmentVariable("PATH", string.Join(Path.PathSeparator, nodeDirectory, npmDirectory));

                var result = PluginPackage.Load(_directory, "fake-output");

                Assert.True(result.IsSuccess, result.ErrorMessage);
                Assert.Equal(Path.GetFullPath(node), result.Launch!.FileName);
                Assert.Equal([Path.GetFullPath(runner), Path.GetFullPath(Path.Combine(package, entryPoint))],
                    result.Launch.Arguments);
            }
            finally
            {
                Environment.SetEnvironmentVariable("PATH", previousPath);
            }
        }

        [Fact]
        public void Load_RejectsUnsupportedRuntime()
        {
            WritePackage(Manifest() with { Runtime = "ruby" });

            var result = PluginPackage.Load(_directory, "fake-output");

            Assert.False(result.IsSuccess);
            Assert.Equal("manifest-invalid", result.ErrorCode);
            Assert.Equal("Runtime must be node or python.", result.ErrorMessage);
        }

        [Fact]
        public void Load_ReportsMissingNamedRunner()
        {
            var package = WritePackage(Manifest() with { EntryPoint = "plugin.mjs", Runtime = "node" });
            File.WriteAllText(Path.Combine(package, "plugin.mjs"), string.Empty);
            var previousPath = Environment.GetEnvironmentVariable("PATH");
            try
            {
                Environment.SetEnvironmentVariable("PATH", string.Empty);

                var result = PluginPackage.Load(_directory, "fake-output");

                Assert.False(result.IsSuccess);
                Assert.Equal("plugin-absent", result.ErrorCode);
                Assert.Contains("node runtime or named FocusRGB runner", result.ErrorMessage, StringComparison.Ordinal);
            }
            finally
            {
                Environment.SetEnvironmentVariable("PATH", previousPath);
            }
        }

        [Theory]
        [InlineData("node", "plugin.py", "Node runner entry points must be JavaScript modules.")]
        [InlineData("python", "plugin.mjs", "Python runner entry points must be Python modules.")]
        public void Load_RejectsEntryPointThatDoesNotMatchRuntime(
            string runtime,
            string entryPoint,
            string expectedMessage)
        {
            WritePackage(Manifest() with { EntryPoint = entryPoint, Runtime = runtime });

            var result = PluginPackage.Load(_directory, "fake-output");

            Assert.False(result.IsSuccess);
            Assert.Equal("manifest-invalid", result.ErrorCode);
            Assert.Equal(expectedMessage, result.ErrorMessage);
        }

        public void Dispose()
        {
            _tempDirectory.Dispose();
        }

        private string WritePackage(ManifestData manifest, string packageId = "fake-output")
        {
            var package = Path.Combine(_directory, packageId);
            Directory.CreateDirectory(package);
            File.WriteAllText(Path.Combine(package, "fake.dll"), string.Empty);
            File.WriteAllText(Path.Combine(package, PluginPackage.ManifestFileName),
                JsonSerializer.Serialize(manifest, PluginJson.Options));
            return package;
        }

        private static ManifestData Manifest()
        {
            var manifest = FakePluginPackage.Manifest("fake.dll");
            return new ManifestData(
                manifest.SchemaVersion,
                manifest.Id!,
                manifest.Version!,
                manifest.ContractVersion!,
                manifest.EntryPoint!,
                manifest.OperatingSystems!,
                manifest.Architectures!,
                manifest.Roles!,
                manifest.Resources!,
                manifest.License!);
        }

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
            string? Runtime = null,
            string? Source = null);
    }

    [CollectionDefinition("Environment variables", DisableParallelization = true)]
    public sealed class EnvironmentVariablesTests;
}
