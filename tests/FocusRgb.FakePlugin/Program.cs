using System.Diagnostics;
using System.Text;
using System.Text.Json;
using FocusRgb.Contracts;
using FocusRgb.PluginSdk;

Console.InputEncoding = new UTF8Encoding(false);
Console.OutputEncoding = new UTF8Encoding(false);

if (Environment.GetEnvironmentVariable("FOCUSRGB_FAKE_PLUGIN_ARGS") is { Length: > 0 } environmentArguments)
    args = [.. args, .. environmentArguments.Split(' ', StringSplitOptions.RemoveEmptyEntries)];

if (ReadStringOption(args, "--grandchild") is { } grandchildPidFile)
{
    await File.WriteAllTextAsync(
        grandchildPidFile,
        Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
    await Task.Delay(Timeout.InfiniteTimeSpan);
    return;
}

if (ReadStringOption(args, "--spawn-grandchild") is { } childPidFile)
{
    _ = Task.Run(async () =>
    {
        await Task.Delay(250);
        Process.Start(new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            ArgumentList = { System.Reflection.Assembly.GetExecutingAssembly().Location, "--grandchild", childPidFile }
        });
    });
}

var pidFile = ReadStringOption(args, "--pid-file");
if (pidFile is not null)
{
    var pendingPidFile = pidFile + ".pending";
    await File.WriteAllTextAsync(
        pendingPidFile,
        Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
    File.Move(pendingPidFile, pidFile, overwrite: true);
}

var rawResponse = ReadStringOption(args, "--invalid-response");
if (args.Contains("--null-capabilities", StringComparer.Ordinal)) rawResponse = "null-capabilities";
if (args.Contains("--stall-after-initialize", StringComparer.Ordinal)) rawResponse = "stalled-stdin";
if (rawResponse is not null)
{
    await RunRawProbeAsync(rawResponse);
    return;
}

if (args.Contains("--malformed", StringComparer.Ordinal))
{
    await Console.In.ReadLineAsync();
    await Console.Out.WriteLineAsync("not-json");
    await Console.Out.FlushAsync();
    return;
}

var delay = ReadOption(args, "--delay-ms", 0);
var initializeDelay = ReadOption(args, "--initialize-delay-ms", 0);
var contractVersion = ReadStringOption(args, "--contract-version") ?? ContractVersion.Current;
var exitOnStatus = args.Contains("--exit-on-status", StringComparer.Ordinal);
var stderrBytes = ReadOption(args, "--stderr-bytes", 0);
var stderrLineBytes = ReadOption(args, "--stderr-line-bytes", 0);
if (stderrLineBytes > 0)
{
    await Console.Error.WriteLineAsync(new string('x', stderrLineBytes));
    await Console.Error.FlushAsync();
}
if (stderrBytes > 0)
{
    while (stderrBytes > 0)
    {
        var count = Math.Min(stderrBytes, 1024);
        await Console.Error.WriteLineAsync(new string('x', count));
        stderrBytes -= count;
    }
    await Console.Error.FlushAsync();
}
await PluginServer.RunAsync(
    new FakePlugin(
        delay,
        initializeDelay,
        contractVersion,
        exitOnStatus,
        ReadStringOption(args, "--catalog-name") ?? "Aurora",
        ReadOption(args, "--notify-count", 0),
        args.Contains("--notify-during-status", StringComparer.Ordinal)),
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

static async Task RunRawProbeAsync(string invalidResponse)
{
    var delayNextRead = invalidResponse == "delayed-read";
    var initialized = false;
    while (await ReadRequestAsync() is { } line)
    {
        using var request = JsonDocument.Parse(line);
        var root = request.RootElement;
        var method = root.GetProperty("method").GetString();
        var id = root.TryGetProperty("id", out var idValue) ? idValue.GetInt64() : 0;
        if (method == PluginMethods.Initialize)
        {
            if (invalidResponse == "null-capabilities")
            {
                await Console.Out.WriteLineAsync(JsonSerializer.Serialize(new
                {
                    jsonrpc = "2.0",
                    id,
                    result = new
                    {
                        pluginId = "fake-output",
                        pluginVersion = "0.1.0",
                        contractVersion = ContractVersion.Current,
                        capabilities = (object?)null
                    }
                }));
                await Console.Out.FlushAsync();
                continue;
            }
            await WriteAsync(new
            {
                jsonrpc = "2.0",
                id,
                result = new
                {
                    pluginId = "fake-output",
                    pluginVersion = "0.1.0",
                    contractVersion = ContractVersion.Current,
                    capabilities = Array.Empty<object>()
                }
            });
            initialized = true;
            if (invalidResponse == "stalled-stdin") await Task.Delay(Timeout.InfiniteTimeSpan);
            continue;
        }
        if (method == PluginMethods.StatusGet)
        {
            if (invalidResponse == "delayed-read")
            {
                await WriteAsync(new { jsonrpc = "2.0", id, result = new { availability = "available" } });
                continue;
            }
            if (invalidResponse == "unknown-notification")
            {
                await WriteAsync(new { jsonrpc = "2.0", method = "future/changed", @params = new { } });
                await WriteAsync(new { jsonrpc = "2.0", id, result = new { availability = "available" } });
                continue;
            }
            var malformed = invalidResponse switch
            {
                "string-id" => "{\"jsonrpc\":\"2.0\",\"id\":\"abc\",\"result\":{}}",
                "null-id" => "{\"jsonrpc\":\"2.0\",\"id\":null,\"result\":{}}",
                "array" => "[]",
                "error-string" => $$"""{"jsonrpc":"2.0","id":{{id}},"error":"bad"}""",
                "oversize" => new string('x', 1024 * 1024 + 1),
                "stray" => "plugin diagnostic output",
                "method-not-found" => JsonSerializer.Serialize(new
                {
                    jsonrpc = "2.0",
                    id,
                    error = new { code = -32601, message = "Unknown method." }
                }),
                "request-cancelled" => JsonSerializer.Serialize(new
                {
                    jsonrpc = "2.0",
                    id,
                    error = new { code = -32800, message = "Cancelled." }
                }),
                _ => "not-json"
            };
            await Console.Out.WriteLineAsync(malformed);
            await Console.Out.FlushAsync();
            if (invalidResponse is not ("error-string" or "method-not-found" or "request-cancelled"))
                await WriteAsync(new { jsonrpc = "2.0", id, result = new { availability = "available" } });
            continue;
        }
        if (method == PluginMethods.CatalogList)
        {
            await WriteAsync(new { jsonrpc = "2.0", id, result = new { support = "supported", items = Array.Empty<object>() } });
            continue;
        }
        if (method == PluginMethods.Shutdown)
        {
            await WriteAsync(new { jsonrpc = "2.0", id, result = new { } });
            return;
        }
    }

    async Task<string?> ReadRequestAsync()
    {
        if (!initialized || !delayNextRead) return await Console.In.ReadLineAsync();
        delayNextRead = false;
        var first = new char[1];
        if (await Console.In.ReadAsync(first.AsMemory()) == 0) return null;
        await Task.Delay(500);
        return first[0] + await Console.In.ReadLineAsync();
    }

    static async Task WriteAsync(object value)
    {
        await Console.Out.WriteLineAsync(JsonSerializer.Serialize(value, PluginJson.Options));
        await Console.Out.FlushAsync();
    }
}

internal sealed class FakePlugin(
    int delayMilliseconds,
    int initializeDelayMilliseconds,
    string contractVersion,
    bool exitOnStatus,
    string catalogName,
    int notifyCount,
    bool notifyDuringStatus)
    : IFocusRgbPlugin
{
    private PluginSelection? _current;
    private IPluginHost? _host;

    public void Connect(IPluginHost host) => _host = host;

    public async Task<InitializeResult> InitializeAsync(
        InitializeParameters parameters,
        CancellationToken cancellationToken)
    {
        if (initializeDelayMilliseconds > 0)
            await Task.Delay(initializeDelayMilliseconds, cancellationToken);
        return new InitializeResult
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
        };
    }

    public async Task<PluginStatusResult> GetStatusAsync(CancellationToken cancellationToken)
    {
        if (exitOnStatus) Environment.Exit(23);
        if (notifyDuringStatus) await _host!.NotifyStatusChangedAsync(cancellationToken);
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
                        DisplayName = catalogName,
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

    public async Task<BindingApplyResult> ApplyBindingAsync(
        BindingApplyParameters parameters,
        CancellationToken cancellationToken)
    {
        _current = new PluginSelection { Effect = parameters.Effect, Layout = parameters.Layout };
        for (var index = 0; index < notifyCount; index++)
            await _host!.NotifyStatusChangedAsync(cancellationToken);
        await _host!.LogAsync("Applied fake output.", cancellationToken);
        return new BindingApplyResult { Outcome = "applied", Actual = _current };
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
