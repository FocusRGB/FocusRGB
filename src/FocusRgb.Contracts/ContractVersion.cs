namespace FocusRgb.Contracts
{
    /// <summary>
    /// Version of the plugin protocol defined in docs/minimal-contract.md. The host and a
    /// plugin must share the major version; a plugin may report a lower minor version.
    /// </summary>
    public static class ContractVersion
    {
        public const int Major = 0;
        public const int Minor = 1;

        /// <summary>Wire form sent in the <c>initialize</c> handshake, e.g. "0.1".</summary>
        public static string Current => $"{Major}.{Minor}";
    }
}
