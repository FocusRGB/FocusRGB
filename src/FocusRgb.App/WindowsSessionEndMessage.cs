namespace FocusRgb.App
{
    internal static class WindowsSessionEndMessage
    {
        private const uint QueryEndSession = 0x0011;
        private const uint EndSession = 0x0016;

        public static IntPtr Handle(
            uint message,
            IntPtr wParam,
            Action completeSessionEnd,
            ref bool handled)
        {
            if (message == QueryEndSession)
            {
                handled = true;
                return new IntPtr(1);
            }
            if (message == EndSession && wParam != IntPtr.Zero) completeSessionEnd();
            return IntPtr.Zero;
        }
    }
}
