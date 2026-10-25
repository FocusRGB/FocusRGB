namespace FocusRgb.Core.Protocol
{
    /// <summary>
    /// Latest plugin protocol implemented by the host. The host and a plugin must share the major
    /// version; a plugin may report a lower minor version. A new spec minor can land before its
    /// host routing implementation.
    /// </summary>
    public static class ContractVersion
    {
        public const int Major = 1;
        public const int Minor = 2;

        /// <summary>Wire form sent in the <c>initialize</c> handshake, e.g. "1.2".</summary>
        public static string Current => $"{Major}.{Minor}";
    }
}
