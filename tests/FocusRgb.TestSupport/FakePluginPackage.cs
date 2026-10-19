using System.Text.Json;
using FocusRgb.Contracts;

namespace FocusRgb.TestSupport
{
    /// <summary>Builds the cross-platform fake-plugin manifest and package used by host tests.</summary>
    public static class FakePluginPackage
    {
        public static PluginManifest Manifest(
            string entryPoint = "FocusRgb.FakePlugin.dll",
            string? contractVersion = null) => new()
        {
            SchemaVersion = 1,
            Id = "fake-output",
            Version = "1.0.0",
            ContractVersion = contractVersion ?? ContractVersion.Current,
            EntryPoint = entryPoint,
            OperatingSystems = [OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "macos" : "linux"],
            Architectures = [System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant()],
            Roles = ["output"],
            Resources = [],
            License = "MIT"
        };

        public static async Task CreateAsync(
            string packagesDirectory,
            CancellationToken cancellationToken = default)
        {
            var packageDirectory = Path.Combine(packagesDirectory, "fake-output");
            Directory.CreateDirectory(packageDirectory);
            var sourceDirectory = Path.GetDirectoryName(RepositoryPaths.FakePlugin)!;
            foreach (var fileName in new[]
            {
                "FocusRgb.FakePlugin.dll",
                "FocusRgb.FakePlugin.deps.json",
                "FocusRgb.FakePlugin.runtimeconfig.json",
                "FocusRgb.PluginSdk.dll",
                "FocusRgb.Contracts.dll"
            })
            {
                File.Copy(Path.Combine(sourceDirectory, fileName), Path.Combine(packageDirectory, fileName));
            }
            await File.WriteAllTextAsync(
                Path.Combine(packageDirectory, "focusrgb-plugin.json"),
                JsonSerializer.Serialize(Manifest(), PluginJson.Options),
                cancellationToken).ConfigureAwait(false);
        }
    }
}
