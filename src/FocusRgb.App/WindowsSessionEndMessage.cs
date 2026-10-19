namespace FocusRgb.App
{
    internal static class WindowsSessionEndMessage
    {
        private const uint QueryEndSession = 0x0011;
        private const uint EndSession = 0x0016;

        public static bool IsQuery(uint message) => message == QueryEndSession;

        public static bool IsCommitted(uint message, IntPtr wParam) =>
            message == EndSession && wParam != IntPtr.Zero;
    }
}
