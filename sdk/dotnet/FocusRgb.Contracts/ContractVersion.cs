namespace FocusRgb.Contracts
{
    /// <summary>
    /// Version of the plugin protocol defined in sdk/spec/plugin-protocol.md. The host and a
    /// plugin must share the major version; a plugin may report a lower minor version.
    /// </summary>
    public static class ContractVersion
    {
        public const int Major = 1;
        public const int Minor = 0;

        /// <summary>Wire form sent in the <c>initialize</c> handshake, e.g. "1.0".</summary>
        public static string Current => $"{Major}.{Minor}";
    }
}
