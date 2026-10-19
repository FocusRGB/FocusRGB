using System.Reflection;

namespace FocusRgb.TestSupport
{
    /// <summary>Provides repository and built fake-plugin paths from one build-stamped root.</summary>
    public static class RepositoryPaths
    {
        public static string Root { get; } = typeof(RepositoryPaths).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(attribute => attribute.Key == "RepositoryRoot").Value!;

        public static string FakePlugin
        {
            get
            {
                var local = Path.Combine(AppContext.BaseDirectory, "FocusRgb.FakePlugin.dll");
                if (File.Exists(local)) return local;
                var configuration = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(
                    Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)).Name;
                return Path.Combine(Root, "build", "artifacts", "bin", "FocusRgb.FakePlugin",
                    configuration, "FocusRgb.FakePlugin.dll");
            }
        }
    }
}
