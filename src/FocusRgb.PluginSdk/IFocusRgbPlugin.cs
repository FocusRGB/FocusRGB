using FocusRgb.Contracts;

namespace FocusRgb.PluginSdk
{
    /// <summary>Implements only the methods in the FocusRGB 0.1 process-plugin contract.</summary>
    public interface IFocusRgbPlugin
    {
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
