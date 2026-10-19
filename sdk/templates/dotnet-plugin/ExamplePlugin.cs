using FocusRgb.Contracts;
using FocusRgb.PluginSdk;

namespace FocusRgb.Plugin.Template
{
    /// <summary>Demonstrates the stable provider lifecycle without accessing real devices.</summary>
    public sealed class ExamplePlugin : IFocusRgbPlugin
    {
        private static readonly PluginResourceReference Effect = new()
        {
            ResourceId = "example-static-blue",
            DisplayName = "Static Blue"
        };
        private PluginSelection? _current;

        public Task<InitializeResult> InitializeAsync(
            InitializeParameters parameters,
            CancellationToken cancellationToken) => Task.FromResult(new InitializeResult
            {
                PluginId = "focusrgb-plugin-template",
                PluginVersion = "1.0.0",
                ContractVersion = ContractVersion.Current,
                Capabilities =
                [
                    new PluginCapability { Id = "provider.effect.catalog", Support = "supported" },
                    new PluginCapability { Id = "provider.effect.select", Support = "supported" },
                    new PluginCapability
                    {
                        Id = "provider.layout.catalog",
                        Support = "unsupported",
                        Reason = new PluginReason
                        {
                            Code = "capability-unsupported",
                            Message = "The example plugin has no layouts."
                        }
                    }
                ]
            });

        public Task<PluginStatusResult> GetStatusAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new PluginStatusResult
            {
                Availability = "available",
                Current = _current
            });

        public Task<PluginCatalogResult> ListCatalogAsync(
            CatalogListParameters parameters,
            CancellationToken cancellationToken) => Task.FromResult(parameters.Kind == "effect"
                ? new PluginCatalogResult
                {
                    Support = "supported",
                    RefreshedAt = DateTimeOffset.UtcNow,
                    Items =
                    [
                        new PluginCatalogItem
                        {
                            ResourceId = Effect.ResourceId!,
                            DisplayName = Effect.DisplayName,
                            Available = true
                        }
                    ]
                }
                : new PluginCatalogResult
                {
                    Support = "unsupported",
                    Reason = new PluginReason
                    {
                        Code = "capability-unsupported",
                        Message = "The example plugin has no layouts."
                    }
                });

        public Task<BindingApplyResult> ApplyBindingAsync(
            BindingApplyParameters parameters,
            CancellationToken cancellationToken)
        {
            _current = new PluginSelection { Effect = parameters.Effect, Layout = parameters.Layout };
            return Task.FromResult(new BindingApplyResult
            {
                Outcome = "applied",
                Actual = _current
            });
        }

        public Task<BindingReleaseResult> ReleaseBindingAsync(
            BindingReleaseParameters parameters,
            CancellationToken cancellationToken)
        {
            _current = null;
            return Task.FromResult(new BindingReleaseResult { Outcome = "released" });
        }

        public Task ShutdownAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
