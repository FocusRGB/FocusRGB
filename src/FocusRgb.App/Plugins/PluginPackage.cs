using System.Runtime.InteropServices;
using System.Text.Json;
using FocusRgb.Core.Protocol;

namespace FocusRgb.App.Plugins
{
    /// <summary>Resolves one explicitly enabled package without allowing paths outside its directory.</summary>
    public static class PluginPackage
    {
        public const string ManifestFileName = "focusrgb-plugin.json";
        private const string RunnerContractError = "Runner plugins require contract version 1.1 or later.";

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
            return LoadDirectory(packageDirectory, pluginId);
        }

        internal static PluginPackageResult LoadDirectory(string packageDirectory, string pluginId)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(packageDirectory);
            ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
            if (!IsId(pluginId))
            {
                return PluginPackageResult.Fail("manifest-invalid", "The enabled plugin ID is invalid.");
            }

            packageDirectory = Path.GetFullPath(packageDirectory);
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

            var error = ValidateManifest(manifest, pluginId);
            if (error is not null)
            {
                return PluginPackageResult.Fail(
                    error == RunnerContractError ? "contract-incompatible" : "manifest-invalid", error);
            }
            if (!SupportsCurrentPlatform(manifest!))
            {
                return PluginPackageResult.Fail("plugin-incompatible", "The package does not support this platform.");
            }

            string entryPoint;
            try
            {
                entryPoint = Path.GetFullPath(Path.Combine(packageDirectory, manifest!.EntryPoint!));
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

            if (manifest.Runtime is not null)
            {
                var launch = ResolveRunner(manifest.Runtime, entryPoint);
                return launch is null
                    ? PluginPackageResult.Fail("plugin-absent",
                        $"The {manifest.Runtime} runtime or named FocusRGB runner was not found in the bundled runners directory or PATH.")
                    : PluginPackageResult.Ok(manifest, launch);
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

        internal static string? ValidateManifest(PluginManifest? manifest, string? expectedId)
        {
            if (manifest is null || manifest.SchemaVersion != 1) return "Only manifest schema version 1 is supported.";
            if (expectedId is not null && manifest.Id != expectedId || !IsId(manifest.Id))
                return "Manifest ID does not match the enabled package ID.";
            if (!TryExactVersion(manifest.Version)) return "Plugin version must contain three numeric parts.";
            if (!IsContractCompatible(manifest.ContractVersion, ContractVersion.Major, ContractVersion.Minor))
                return "Plugin contract version is incompatible.";
            if (manifest.Runtime is not null && !IsContractAtLeast(manifest.ContractVersion, 1, 1))
                return RunnerContractError;
            if (string.IsNullOrWhiteSpace(manifest.EntryPoint)) return "Entry point is required.";
            if (manifest.Runtime is not null && manifest.Runtime is not ("node" or "python"))
                return "Runtime must be node or python.";
            if (manifest.Runtime == "node"
                && !new[] { ".js", ".mjs", ".cjs" }.Contains(
                    Path.GetExtension(manifest.EntryPoint), StringComparer.OrdinalIgnoreCase))
                return "Node runner entry points must be JavaScript modules.";
            if (manifest.Runtime == "python"
                && !manifest.EntryPoint.EndsWith(".py", StringComparison.OrdinalIgnoreCase))
                return "Python runner entry points must be Python modules.";
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

        internal static bool SupportsCurrentPlatform(PluginManifest manifest) =>
            manifest.OperatingSystems!.Contains(CurrentOperatingSystem(), StringComparer.Ordinal)
            && manifest.Architectures!.Contains(
                RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant(), StringComparer.Ordinal);

        internal static bool IsContractCompatible(string? version, int hostMajor, int hostMinor)
        {
            if (!TryContractVersion(version, out var major, out var minor)) return false;
            return major == hostMajor && minor <= hostMinor;
        }

        private static bool IsContractAtLeast(string? version, int requiredMajor, int requiredMinor) =>
            TryContractVersion(version, out var major, out var minor)
            && (major > requiredMajor || major == requiredMajor && minor >= requiredMinor);

        private static bool TryContractVersion(string? version, out int major, out int minor)
        {
            var parts = version?.Split('.');
            major = 0;
            minor = 0;
            return parts is { Length: 2 }
                && int.TryParse(parts[0], System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out major)
                && int.TryParse(parts[1], System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out minor);
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

        private static PluginLaunchSpec? ResolveRunner(string runtime, string entryPoint)
        {
            var pathDirectories = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (runtime == "node" && OperatingSystem.IsWindows())
            {
                var node = FindFile(pathDirectories, OperatingSystem.IsWindows() ? "node.exe" : "node");
                var nodeRunner = FindFile(
                    new[] { Path.Combine(AppContext.BaseDirectory, "runners", "node") }
                        .Concat(pathDirectories.Select(directory => Path.Combine(
                            directory, "node_modules", "@focusrgb", "plugin-sdk", "dist"))),
                    "focusrgb-node-runner.js");
                return node is null || nodeRunner is null ? null : new PluginLaunchSpec
                {
                    FileName = node,
                    Arguments = [nodeRunner, entryPoint]
                };
            }

            var name = $"focusrgb-{runtime}-runner";
            var fileName = OperatingSystem.IsWindows() ? name + ".exe" : name;
            var directories = new[] { Path.Combine(AppContext.BaseDirectory, "runners") }
                .Concat(pathDirectories);
            var runner = FindFile(directories, fileName);
            return runner is null ? null : new PluginLaunchSpec
            {
                FileName = runner,
                Arguments = [entryPoint]
            };
        }

        private static string? FindFile(IEnumerable<string> directories, string fileName)
        {
            foreach (var directory in directories)
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
