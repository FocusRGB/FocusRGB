namespace FocusRgb.Core.Configuration
{
    public static class SwitchingSpeedPresets
    {
        public static int Milliseconds(string? speed) => speed switch
        {
            "fast" => 300,
            "normal" => 750,
            "smooth" => 2000,
            _ => throw new ArgumentException("Unknown switching speed.", nameof(speed))
        };
    }
}
