using System.Reflection;

namespace FocusRgb.App
{
    /// <summary>Uses the packaged version consistently in the desktop UI and discovery defaults.</summary>
    public static class ApplicationVersion
    {
        public static string Current { get; } = typeof(App).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Split('+')[0];
        public static bool IsPrerelease => Current.Contains('-', StringComparison.Ordinal);
        public static string DisplayName => Format(Current);

        internal static string Format(string version) =>
            $"FocusRGB v{version}" + (version.Contains('-', StringComparison.Ordinal) ? " (Beta)" : "");
    }
}
