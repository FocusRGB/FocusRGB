using System.Security;
using Microsoft.Win32;

namespace FocusRgb.Platform.Windows
{
    public sealed record StartupRegistrationStatus(bool Enabled, bool Available, string Message);

    public interface IStartupRegistration
    {
        StartupRegistrationStatus Refresh();
        StartupRegistrationStatus SetEnabled(bool enabled);
    }

    internal interface IStartupRunKey
    {
        object? Read();
        void Write(string command);
        void Delete();
    }

    /// <summary>Uses only this application's per-user Run value; no settings document or elevation.</summary>
    public sealed class WindowsStartupRegistration : IStartupRegistration
    {
        private readonly IStartupRunKey _key;
        private readonly string? _executablePath;

        public WindowsStartupRegistration(string? executablePath)
            : this(new WindowsRunKey(), executablePath)
        {
        }

        internal WindowsStartupRegistration(IStartupRunKey key, string? executablePath)
        {
            _key = key;
            _executablePath = executablePath;
        }

        public StartupRegistrationStatus Refresh() => Update(null);

        public StartupRegistrationStatus SetEnabled(bool enabled) => Update(enabled);

        private StartupRegistrationStatus Update(bool? enabled)
        {
            var current = false;
            try
            {
                var value = _key.Read();
                current = value is not null;
                if (enabled == false)
                {
                    _key.Delete();
                    current = false;
                }
                var executable = _executablePath;
                if (string.IsNullOrWhiteSpace(executable) || !Path.IsPathFullyQualified(executable)
                    || !executable.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                    || Path.GetFileName(executable).Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase)
                    || executable.Contains('"') || executable.Any(char.IsControl) || !File.Exists(executable))
                {
                    // Keep an existing entry disableable even when creating a new one would be unsafe.
                    return new(current, current, "The application executable is unavailable. You can disable an existing startup entry, but cannot enable a new one.");
                }
                var command = $"\"{Path.GetFullPath(executable)}\" --minimized";
                if ((enabled == true || current) && !string.Equals(value as string, command, StringComparison.Ordinal))
                {
                    _key.Write(command);
                    current = true;
                }
                return new(current, true, current
                    ? "On. FocusRGB will start in the tray when you sign in."
                    : "Off. FocusRGB will not start with Windows.");
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                or SecurityException or ArgumentException or NotSupportedException)
            {
                return new(current, false, "Could not read or update Start with Windows. Check your Windows user permissions and try again.");
            }
        }

        private sealed class WindowsRunKey : IStartupRunKey
        {
            private const string RunPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
            private const string ValueName = "FocusRGB";

            public object? Read()
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunPath);
                return key?.GetValue(ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            }

            public void Write(string command)
            {
                using var key = Registry.CurrentUser.CreateSubKey(RunPath);
                key.SetValue(ValueName, command, RegistryValueKind.String);
            }

            public void Delete()
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunPath, writable: true);
                key?.DeleteValue(ValueName, throwOnMissingValue: false);
            }
        }
    }
}
