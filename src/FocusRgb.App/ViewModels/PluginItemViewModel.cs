using FocusRgb.App.Plugins;

namespace FocusRgb.App.ViewModels
{
    /// <summary>Presents one official catalog entry without losing its verified release metadata.</summary>
    public sealed class OfficialPluginItemViewModel
    {
        public OfficialPluginItemViewModel(OfficialPluginListing listing)
        {
            Listing = listing;
        }

        public OfficialPluginListing Listing { get; }
        public string Id => Listing.Id;
        public string Name => Listing.Name;
        public string Version => Listing.Version;
        public string Description => Listing.Description ?? "No description provided.";
        public string Source => Listing.RepositoryUrl;
        public string Contract => $"Contract {Listing.ContractVersion}";
        public string Resources => Listing.Resources.Count == 0
            ? "Resources: none declared"
            : $"Resources: {string.Join(", ", Listing.Resources)}";
        public string Compatibility => Listing.IsCompatible
            ? "Compatible with this system"
            : "Incompatible with this operating system or architecture";
        public bool CanInstall => Listing.IsCompatible;
    }

    /// <summary>Presents one local plugin folder and its recorded install origin.</summary>
    public sealed class InstalledPluginItemViewModel
    {
        public InstalledPluginItemViewModel(InstalledPluginInfo plugin)
        {
            Plugin = plugin;
        }

        public InstalledPluginInfo Plugin { get; }
        public string Id => Plugin.Id;
        public string Name => Plugin.DisplayName;
        public string Version => Plugin.Version ?? "unknown version";
        public string Origin => Plugin.IsOfficial ? "Official" : "Custom, unverified";
        public string Status => Plugin.Error is not null
            ? Plugin.Error
            : Plugin.IsRunning ? "Running" : Plugin.IsEnabled ? "Enabled, not running" : "Disabled";
        public bool CanEnable => !Plugin.IsEnabled && Plugin.Error is null;
        public bool CanDisable => Plugin.IsEnabled;
        public bool CanConfigure => Plugin.HasConfiguration && Plugin.Error is null;
    }
}
