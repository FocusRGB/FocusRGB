using FocusRgb.Contracts;

namespace FocusRgb.PluginSdk
{
    /// <summary>Lets a plugin publish protocol notifications and stderr logs through the server.</summary>
    public interface IPluginHost
    {
        Task NotifyStatusChangedAsync(CancellationToken cancellationToken = default);
        Task LogAsync(string message, CancellationToken cancellationToken = default);
    }

    /// <summary>Implements the stable methods in the FocusRGB 1.0 process-plugin contract.</summary>
    public interface IFocusRgbPlugin
    {
        void Connect(IPluginHost host)
        {
        }

        Task<InitializeResult> InitializeAsync(
            InitializeParameters parameters,
            CancellationToken cancellationToken);

        Task<PluginStatusResult> GetStatusAsync(CancellationToken cancellationToken);

        Task<PluginCatalogResult> ListCatalogAsync(
            CatalogListParameters parameters,
            CancellationToken cancellationToken);

        Task<BindingApplyResult> ApplyBindingAsync(
            BindingApplyParameters parameters,
            CancellationToken cancellationToken);

        Task<BindingReleaseResult> ReleaseBindingAsync(
            BindingReleaseParameters parameters,
            CancellationToken cancellationToken);

        Task ShutdownAsync(CancellationToken cancellationToken);
    }
}
