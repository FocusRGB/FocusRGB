using System.Reflection;
using Avalonia;
using Avalonia.Platform;

namespace FocusRgb.App
{
    internal static class AvaloniaWindowsSessionEndWorkaround
    {
        public static bool SuppressQueryShutdown()
        {
            // shortcut: Avalonia 12.1.4 exposes this only as private API; recheck when Avalonia is upgraded.
            var mutable = typeof(AvaloniaLocator).GetProperty(
                "CurrentMutable", BindingFlags.Public | BindingFlags.Static)?.GetValue(null)
                ?? throw new InvalidOperationException("Avalonia's mutable service locator is unavailable.");
            var bind = mutable.GetType().GetMethods()
                .Single(method => method.Name == "Bind" && method.IsGenericMethodDefinition)
                .MakeGenericMethod(typeof(IPlatformLifetimeEventsImpl))
                .Invoke(mutable, null)
                ?? throw new InvalidOperationException("Avalonia's lifetime service could not be selected.");
            var toFunc = bind.GetType().GetMethods()
                .Single(method => method.Name == "ToFunc" && method.IsGenericMethodDefinition)
                .MakeGenericMethod(typeof(IPlatformLifetimeEventsImpl));
            Func<IPlatformLifetimeEventsImpl> noLifetimeEvents = static () => null!;
            toFunc.Invoke(bind, [noLifetimeEvents]);
            var current = typeof(AvaloniaLocator).GetProperty(
                "Current", BindingFlags.Public | BindingFlags.Static)?.GetValue(null)
                ?? throw new InvalidOperationException("Avalonia's service locator is unavailable.");
            return current.GetType().GetMethod("GetService")?.Invoke(
                current, [typeof(IPlatformLifetimeEventsImpl)]) is null;
        }
    }
}
