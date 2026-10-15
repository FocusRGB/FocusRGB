using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FocusRgb.Contracts;
using FocusRgb.PluginSdk;

await PluginServer.RunAsync(new ConsumerPlugin(), new StringReader(string.Empty), TextWriter.Null, TextWriter.Null);

internal sealed class ConsumerPlugin : IFocusRgbPlugin
{
    public Task<InitializeResult> InitializeAsync(InitializeParameters parameters, CancellationToken cancellationToken) =>
        Task.FromResult(new InitializeResult
        {
            PluginId = "consumer-fixture",
            PluginVersion = "0.1.0",
            ContractVersion = ContractVersion.Current,
            Capabilities = []
        });

    public Task<PluginStatusResult> GetStatusAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new PluginStatusResult { Availability = "available" });

    public Task<PluginCatalogResult> ListCatalogAsync(
        CatalogListParameters parameters,
        CancellationToken cancellationToken) => Task.FromResult(new PluginCatalogResult
        {
            Support = "unsupported",
            Items = []
        });

    public Task<BindingApplyResult> ApplyBindingAsync(
        BindingApplyParameters parameters,
        CancellationToken cancellationToken) => Task.FromResult(new BindingApplyResult
        {
            Outcome = "unsupported"
        });

    public Task<BindingReleaseResult> ReleaseBindingAsync(
        BindingReleaseParameters parameters,
        CancellationToken cancellationToken) => Task.FromResult(new BindingReleaseResult
        {
            Outcome = "unsupported"
        });

    public Task ShutdownAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
