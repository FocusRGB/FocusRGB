using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

namespace FocusRgb.LocalMcp
{
    /// <summary>Shares data-folder selection and private instance identities between the app and adapter.</summary>
    public static class ApplicationDataDirectory
    {
        public static string Default => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FocusRGB");

        public static string Resolve(string[] args, string? environmentValue = null)
        {
            string? selected = null;
            for (var index = 0; index < args.Length; index++)
            {
                if (args[index] != "--data-dir") continue;
                if (selected is not null || ++index == args.Length || string.IsNullOrWhiteSpace(args[index])
                    || args[index].StartsWith("--", StringComparison.Ordinal))
                {
                    throw new ArgumentException("Use --data-dir <path> once with a nonempty path.");
                }
                selected = args[index];
            }
            var value = selected ?? environmentValue ?? Environment.GetEnvironmentVariable("FOCUSRGB_DATA_DIR");
            return Normalize(string.IsNullOrWhiteSpace(value) ? Default : value);
        }

        public static string Normalize(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

        public static FileStream AcquireInstanceLock(string dataDirectory)
        {
            Directory.CreateDirectory(dataDirectory);
            // A file lock also rejects directory aliases that have different named-mutex hashes.
            return new FileStream(Path.Combine(dataDirectory, ".instance.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }

        public static string Scope(string dataDirectory)
        {
            var path = Normalize(dataDirectory).ToUpperInvariant();
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path)));
            var user = WindowsIdentity.GetCurrent().User?.Value
                ?? throw new InvalidOperationException("The current Windows user SID is unavailable.");
            return $"{user}.{hash}";
        }
    }
}
