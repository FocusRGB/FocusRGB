using System.Text;
using FocusRgb.Contracts;
using FocusRgb.PluginSdk;

Console.InputEncoding = new UTF8Encoding(false);
Console.OutputEncoding = new UTF8Encoding(false);

if (args.Contains("--malformed", StringComparer.Ordinal))
{
    await Console.In.ReadLineAsync();
    await Console.Out.WriteLineAsync("not-json");
    await Console.Out.FlushAsync();
    return;
}

var delay = ReadOption(args, "--delay-ms", 0);
var contractVersion = ReadStringOption(args, "--contract-version") ?? ContractVersion.Current;
var exitOnStatus = args.Contains("--exit-on-status", StringComparer.Ordinal);
await PluginServer.RunAsync(
    new FakePlugin(delay, contractVersion, exitOnStatus),
    Console.In,
    Console.Out,
    Console.Error);

static int ReadOption(string[] values, string name, int fallback) =>
    int.TryParse(ReadStringOption(values, name), out var parsed) ? parsed : fallback;

static string? ReadStringOption(string[] values, string name)
{
    var index = Array.IndexOf(values, name);
    return index >= 0 && index + 1 < values.Length ? values[index + 1] : null;
}

internal sealed class FakePlugin(int delayMilliseconds, string contractVersion, bool exitOnStatus)
    : IFocusRgbPlugin
{
    private PluginSelection? _current;

    public Task<InitializeResult> InitializeAsync(
        InitializeParameters parameters,
        CancellationToken cancellationToken) => Task.FromResult(new InitializeResult
        {
            PluginId = "fake-output",
            PluginVersion = "0.1.0",
            ContractVersion = contractVersion,
            Capabilities =
            [
                new PluginCapability { Id = "provider.effect.catalog", Support = "supported" },
                new PluginCapability { Id = "provider.effect.select", Support = "supported" },
                new PluginCapability { Id = "provider.selection.observe", Support = "supported" },
                new PluginCapability { Id = "provider.layout.catalog", Support = "unsupported" }
            ]
        });

    public async Task<PluginStatusResult> GetStatusAsync(CancellationToken cancellationToken)
    {
        if (exitOnStatus) Environment.Exit(23);
        if (delayMilliseconds > 0)
            await Task.Delay(delayMilliseconds, cancellationToken);
        return new PluginStatusResult { Availability = "available", Current = _current };
    }

    public Task<PluginCatalogResult> ListCatalogAsync(
        CatalogListParameters parameters,
        CancellationToken cancellationToken) => Task.FromResult(parameters.Kind == "effect"
            ? new PluginCatalogResult
            {
                Support = "supported",
                RefreshedAt = DateTimeOffset.UnixEpoch,
                Items =
                [
                    new PluginCatalogItem
                    {
                        ResourceId = "fake-aurora",
                        DisplayName = "Aurora",
                        Available = true
                    }
                ]
            }
            : new PluginCatalogResult
            {
                Support = "unsupported",
                Items = [],
                Reason = new PluginReason
                {
                    Code = "capability-unsupported",
                    Message = "The fake plugin does not provide layouts."
                }
            });

    public Task<BindingApplyResult> ApplyBindingAsync(
        BindingApplyParameters parameters,
        CancellationToken cancellationToken)
    {
        _current = new PluginSelection { Effect = parameters.Effect, Layout = parameters.Layout };
        return Task.FromResult(new BindingApplyResult { Outcome = "applied", Actual = _current });
    }

    public Task<BindingReleaseResult> ReleaseBindingAsync(
        BindingReleaseParameters parameters,
        CancellationToken cancellationToken)
    {
        _current = null;
        return Task.FromResult(new BindingReleaseResult { Outcome = "nothing-to-release" });
    }

    public Task ShutdownAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
