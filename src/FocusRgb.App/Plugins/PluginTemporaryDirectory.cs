namespace FocusRgb.App.Plugins
{
    internal static class PluginTemporaryDirectory
    {
        public static IReadOnlyList<string> CleanupContents(params string[] roots)
        {
            var warnings = new List<string>();
            foreach (var root in roots)
            {
                try
                {
                    if (!Directory.Exists(root)) continue;
                    if (File.GetAttributes(root).HasFlag(FileAttributes.ReparsePoint))
                        throw new IOException($"Refusing to clean reparse point '{root}'.");
                    foreach (var entry in Directory.EnumerateFileSystemEntries(root))
                    {
                        try { DeleteTree(entry); }
                        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                        {
                            warnings.Add($"{entry}: {exception.Message}");
                        }
                    }
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    warnings.Add($"{root}: {exception.Message}");
                }
            }
            return warnings;
        }

        private static void DeleteTree(string path)
        {
            var attributes = File.GetAttributes(path);
            if (!attributes.HasFlag(FileAttributes.Directory))
            {
                File.Delete(path);
                return;
            }
            if (!attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                foreach (var entry in Directory.EnumerateFileSystemEntries(path)) DeleteTree(entry);
            }
            Directory.Delete(path);
        }
    }
}
