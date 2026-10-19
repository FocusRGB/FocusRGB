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

            if (entryPoint.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                var dotnet = ResolveDotNetHost();
                return dotnet is null
                    ? PluginPackageResult.Fail("plugin-absent", "The dotnet host could not be resolved.")
                    : PluginPackageResult.Ok(manifest, new PluginLaunchSpec
                    {
                        FileName = dotnet,
                        Arguments = [entryPoint]
                    });
            }
            return PluginPackageResult.Ok(manifest, new PluginLaunchSpec { FileName = entryPoint });
        }

        private static string? Validate(PluginManifest? manifest, string expectedId)
        {
            if (manifest is null || manifest.SchemaVersion != 1) return "Only manifest schema version 1 is supported.";
            if (manifest.Id != expectedId || !IsId(manifest.Id)) return "Manifest ID does not match the enabled package ID.";
            if (!TryExactVersion(manifest.Version)) return "Plugin version must contain three numeric parts.";
            if (!IsContractCompatible(manifest.ContractVersion, ContractVersion.Major, ContractVersion.Minor))
                return "Plugin contract version is incompatible.";
            if (string.IsNullOrWhiteSpace(manifest.EntryPoint)) return "Entry point is required.";
            if (!ValidList(manifest.OperatingSystems, ["windows", "linux", "macos"]))
                return "Operating systems must be unique supported values.";
            if (!ValidList(manifest.Architectures, ["x64", "arm64"]))
                return "Architectures must be unique supported values.";
            if (!ValidList(manifest.Roles, ["detector", "discovery", "integration", "output", "effect"]))
                return "Roles must be unique supported values.";
            if (manifest.Resources is null
                || manifest.Resources.Count != manifest.Resources.Distinct(StringComparer.Ordinal).Count()
                || manifest.Resources.Any(resource => resource is not ("process.read" or "process.start" or "filesystem.read")))
                return "Resources must be unique supported values.";
            if (string.IsNullOrWhiteSpace(manifest.License)) return "License is required.";
            if (manifest.Source is not null
                && (!Uri.TryCreate(manifest.Source, UriKind.Absolute, out var source)
                    || source.Scheme is not ("http" or "https")))
                return "Source must be an absolute HTTP or HTTPS URI.";
            return null;
        }

        internal static bool IsContractCompatible(string? version, int hostMajor, int hostMinor)
        {
            var parts = version?.Split('.');
            if (parts is not { Length: 2 }
                || !int.TryParse(parts[0], System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out var major)
                || !int.TryParse(parts[1], System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out var minor))
                return false;
            // shortcut: accept the shipped 0.1 SignalRGB wire shape until its 1.0 package is published.
            return major == hostMajor && minor <= hostMinor
                || hostMajor == 1 && hostMinor == 0 && major == 0 && minor == 1;
        }

        private static bool IsId(string? value) => value is { Length: >= 1 and <= 64 }
            && value[0] is >= 'a' and <= 'z' or >= '0' and <= '9'
            && value.All(character => character is >= 'a' and <= 'z' or >= '0' and <= '9' or '-');

        private static bool TryExactVersion(string? value)
        {
            var parts = value?.Split('.');
            return parts is { Length: 3 }
                && parts.All(part => int.TryParse(part, System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out _));
        }

        private static bool ValidList(List<string>? values, IReadOnlyCollection<string> allowed) =>
            values is { Count: > 0 }
            && values.Count == values.Distinct(StringComparer.Ordinal).Count()
            && values.All(allowed.Contains);

        private static string? ResolveDotNetHost()
        {
            var configured = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
            if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
                return Path.GetFullPath(configured);
            var fileName = OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet";
            var root = Environment.GetEnvironmentVariable("DOTNET_ROOT");
            if (!string.IsNullOrWhiteSpace(root))
            {
                var candidate = Path.Combine(root, fileName);
                if (File.Exists(candidate)) return Path.GetFullPath(candidate);
            }
            foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var candidate = Path.Combine(directory, fileName);
                if (File.Exists(candidate)) return Path.GetFullPath(candidate);
            }
            return null;
        }

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
