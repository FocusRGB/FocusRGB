using System.Runtime.InteropServices;
using System.Text.Json;
using FocusRgb.Contracts;

namespace FocusRgb.App.Plugins
{
    /// <summary>Resolves one explicitly enabled package without allowing paths outside its directory.</summary>
    public static class PluginPackage
    {
        public const string ManifestFileName = "focusrgb-plugin.json";

        public static PluginPackageResult Load(string packagesDirectory, string pluginId)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(packagesDirectory);
            ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
            if (!IsId(pluginId))
            {
                return PluginPackageResult.Fail("manifest-invalid", "The enabled plugin ID is invalid.");
            }

            var root = Path.GetFullPath(packagesDirectory);
            var packageDirectory = Path.GetFullPath(Path.Combine(root, pluginId));
            var manifestPath = Path.Combine(packageDirectory, ManifestFileName);
            if (!File.Exists(manifestPath))
            {
                return PluginPackageResult.Fail("plugin-absent", $"Package '{pluginId}' is not installed.");
            }

            PluginManifest? manifest;
            try
            {
                manifest = JsonSerializer.Deserialize<PluginManifest>(
                    File.ReadAllText(manifestPath), PluginJson.Options);
            }
            catch (JsonException exception)
            {
                return PluginPackageResult.Fail("manifest-invalid", exception.Message);
            }
            catch (IOException exception)
            {
                return PluginPackageResult.Fail("plugin-absent", exception.Message);
            }
            catch (UnauthorizedAccessException exception)
            {
                return PluginPackageResult.Fail("plugin-absent", exception.Message);
            }

            var error = Validate(manifest, pluginId);
            if (error is not null)
            {
                return PluginPackageResult.Fail("manifest-invalid", error);
            }
            if (!manifest!.OperatingSystems!.Contains(CurrentOperatingSystem(), StringComparer.Ordinal)
                || !manifest.Architectures!.Contains(RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant(),
                    StringComparer.Ordinal))
            {
                return PluginPackageResult.Fail("plugin-incompatible", "The package does not support this platform.");
            }

            string entryPoint;
            try
            {
                entryPoint = Path.GetFullPath(Path.Combine(packageDirectory, manifest.EntryPoint!));
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
            {
                return PluginPackageResult.Fail("manifest-invalid", exception.Message);
            }
            var packagePrefix = packageDirectory.EndsWith(Path.DirectorySeparatorChar)
                ? packageDirectory
                : packageDirectory + Path.DirectorySeparatorChar;
            if (!entryPoint.StartsWith(packagePrefix, StringComparison.OrdinalIgnoreCase)
                || !File.Exists(entryPoint))
            {
                return PluginPackageResult.Fail("plugin-absent", "The package entry point is missing or outside the package.");
            }

            return PluginPackageResult.Ok(manifest, entryPoint.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                ? new PluginLaunchSpec { FileName = "dotnet", Arguments = [entryPoint] }
                : new PluginLaunchSpec { FileName = entryPoint });
        }

        private static string? Validate(PluginManifest? manifest, string expectedId)
        {
            if (manifest is null || manifest.SchemaVersion != 1) return "Only manifest schema version 1 is supported.";
            if (manifest.Id != expectedId || !IsId(manifest.Id)) return "Manifest ID does not match the enabled package ID.";
            if (!Version.TryParse(manifest.Version, out _)) return "Plugin version must be a numeric version.";
            if (!TryContractMajor(manifest.ContractVersion, out var major) || major != ContractVersion.Major)
                return "Plugin contract version is incompatible.";
            if (string.IsNullOrWhiteSpace(manifest.EntryPoint)) return "Entry point is required.";
            if (manifest.OperatingSystems is not { Count: > 0 }) return "At least one operating system is required.";
            if (manifest.Architectures is not { Count: > 0 }) return "At least one architecture is required.";
            if (manifest.Roles is not { Count: > 0 }) return "At least one role is required.";
            if (string.IsNullOrWhiteSpace(manifest.License)) return "License is required.";
            return null;
        }

        internal static bool TryContractMajor(string? version, out int major)
        {
            major = 0;
            var parts = version?.Split('.');
            return parts is { Length: 2 }
                && int.TryParse(parts[0], System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out major)
                && int.TryParse(parts[1], System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out _);
        }

        private static bool IsId(string? value) => value is { Length: >= 1 and <= 64 }
            && value[0] is >= 'a' and <= 'z' or >= '0' and <= '9'
            && value.All(character => character is >= 'a' and <= 'z' or >= '0' and <= '9' or '-');

        private static string CurrentOperatingSystem() =>
            OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "macos" : "linux";
    }

    /// <summary>Contains the executable and arguments resolved from a validated package.</summary>
    public sealed class PluginLaunchSpec
    {
        public required string FileName { get; init; }
        public IReadOnlyList<string> Arguments { get; init; } = [];
    }

    /// <summary>Returns either a validated manifest/launch specification or one package error.</summary>
    public sealed class PluginPackageResult
    {
        public bool IsSuccess => Manifest is not null;
        public PluginManifest? Manifest { get; private init; }
        public PluginLaunchSpec? Launch { get; private init; }
        public string? ErrorCode { get; private init; }
        public string? ErrorMessage { get; private init; }

        public static PluginPackageResult Ok(PluginManifest manifest, PluginLaunchSpec launch) => new()
        {
            Manifest = manifest,
            Launch = launch
        };

        public static PluginPackageResult Fail(string code, string message) => new()
        {
            ErrorCode = code,
            ErrorMessage = message
        };
    }
}
