namespace FocusRgb.Contracts;

/// <summary>Plugin protocol version defined in docs/minimal-contract.md.</summary>
public static class ContractVersion
{
    public const int Major = 0;
    public const int Minor = 1;

    public static string Current => $"{Major}.{Minor}";
}
