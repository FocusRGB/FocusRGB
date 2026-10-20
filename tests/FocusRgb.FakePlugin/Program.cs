using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

Console.InputEncoding = new UTF8Encoding(false);
Console.OutputEncoding = new UTF8Encoding(false);

if (Environment.GetEnvironmentVariable("FOCUSRGB_FAKE_PLUGIN_ARGS") is { Length: > 0 } environmentArguments)
    args = [.. args, .. environmentArguments.Split(' ', StringSplitOptions.RemoveEmptyEntries)];

if (ReadStringOption(args, "--grandchild") is { } grandchildPidFile)
{
    var pendingGrandchildPidFile = grandchildPidFile + ".pending";
    await File.WriteAllTextAsync(
        pendingGrandchildPidFile,
        Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
    File.Move(pendingGrandchildPidFile, grandchildPidFile, overwrite: true);
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
var contractVersion = ReadStringOption(args, "--contract-version") ?? Protocol.ContractVersion;
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
await RunFakePluginAsync(
    delay,
    initializeDelay,
    contractVersion,
    exitOnStatus,
    ReadStringOption(args, "--catalog-name") ?? "Aurora",
    ReadOption(args, "--notify-count", 0),
    args.Contains("--notify-during-status", StringComparer.Ordinal));

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
        if (method == Protocol.Initialize)
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
                        pluginVersion = "1.0.0",
                        contractVersion = Protocol.ContractVersion,
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
                    pluginVersion = "1.0.0",
                    contractVersion = Protocol.ContractVersion,
                    capabilities = Array.Empty<object>()
                }
            });
            initialized = true;
            if (invalidResponse == "stalled-stdin") await Task.Delay(Timeout.InfiniteTimeSpan);
            continue;
        }
        if (method == Protocol.StatusGet)
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
        if (method == Protocol.CatalogList)
        {
            await WriteAsync(new { jsonrpc = "2.0", id, result = new { support = "supported", items = Array.Empty<object>() } });
            continue;
        }
        if (method == Protocol.Shutdown)
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
        await Console.Out.WriteLineAsync(JsonSerializer.Serialize(value, Protocol.JsonOptions));
        await Console.Out.FlushAsync();
    }
}

static async Task RunFakePluginAsync(
    int delayMilliseconds,
    int initializeDelayMilliseconds,
    string contractVersion,
    bool exitOnStatus,
    string catalogName,
    int notifyCount,
    bool notifyDuringStatus)
{
    using var lifetime = new CancellationTokenSource();
    using var writeGate = new SemaphoreSlim(1, 1);
    var pending = new ConcurrentDictionary<long, CancellationTokenSource>();
    var tasks = new ConcurrentBag<Task>();
    JsonElement? currentEffect = null;
    JsonElement? currentLayout = null;

    while (await Console.In.ReadLineAsync(lifetime.Token) is { } line)
    {
        using var request = JsonDocument.Parse(line);
        var root = request.RootElement;
        var method = root.GetProperty("method").GetString()!;
        if (method == Protocol.Cancel)
        {
            var cancelledId = root.GetProperty("params").GetProperty("id").GetInt64();
            if (pending.TryRemove(cancelledId, out var cancelled)) cancelled.Cancel();
            continue;
        }

        var id = root.GetProperty("id").GetInt64();
        var parameters = root.TryGetProperty("params", out var value)
            ? value.Clone()
            : JsonSerializer.SerializeToElement(new { });
        var requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        pending[id] = requestCancellation;
        var task = HandleRequestAsync(id, method, parameters, requestCancellation.Token);
        tasks.Add(task);

        if (method == Protocol.Shutdown)
        {
            await task;
            pending.TryRemove(id, out var shutdownCancellation);
            (shutdownCancellation ?? requestCancellation).Dispose();
            break;
        }

        _ = task.ContinueWith(
            _ =>
            {
                pending.TryRemove(id, out var cancellation);
                (cancellation ?? requestCancellation).Dispose();
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    lifetime.Cancel();
    try
    {
        await Task.WhenAll(tasks);
    }
    catch (OperationCanceledException)
    {
    }

    async Task HandleRequestAsync(
        long id,
        string method,
        JsonElement parameters,
        CancellationToken cancellationToken)
    {
        try
        {
            object result;
            switch (method)
            {
                case Protocol.Initialize:
                    if (initializeDelayMilliseconds > 0)
                        await Task.Delay(initializeDelayMilliseconds, cancellationToken);
                    result = new
                    {
                        pluginId = "fake-output",
                        pluginVersion = "1.0.0",
                        contractVersion,
                        capabilities = new[]
                        {
                            new { id = "provider.effect.catalog", support = "supported" },
                            new { id = "provider.effect.select", support = "supported" },
                            new { id = "provider.selection.observe", support = "supported" },
                            new { id = "provider.layout.catalog", support = "unsupported" }
                        }
                    };
                    break;
                case Protocol.StatusGet:
                    if (exitOnStatus) Environment.Exit(23);
                    if (notifyDuringStatus) await WriteNotificationAsync(cancellationToken);
                    if (delayMilliseconds > 0) await Task.Delay(delayMilliseconds, cancellationToken);
                    result = new
                    {
                        availability = "available",
                        current = CurrentSelection()
                    };
                    break;
                case Protocol.CatalogList:
                    result = parameters.GetProperty("kind").GetString() == "effect"
                        ? new
                        {
                            support = "supported",
                            refreshedAt = DateTimeOffset.UnixEpoch,
                            items = new[]
                            {
                                new { resourceId = "fake-aurora", displayName = catalogName, available = true }
                            },
                            reason = (object?)null
                        }
                        : new
                        {
                            support = "unsupported",
                            refreshedAt = (DateTimeOffset?)null,
                            items = Array.Empty<object>(),
                            reason = (object)new
                            {
                                code = "capability-unsupported",
                                message = "The fake plugin does not provide layouts."
                            }
                        };
                    break;
                case Protocol.BindingApply:
                    currentEffect = CloneOptional(parameters, "effect");
                    currentLayout = CloneOptional(parameters, "layout");
                    for (var index = 0; index < notifyCount; index++)
                        await WriteNotificationAsync(cancellationToken);
                    await Console.Error.WriteLineAsync("Applied fake output.");
                    await Console.Error.FlushAsync(cancellationToken);
                    result = new { outcome = "applied", actual = CurrentSelection() };
                    break;
                case Protocol.BindingRelease:
                    currentEffect = null;
                    currentLayout = null;
                    result = new { outcome = "nothing-to-release" };
                    break;
                case Protocol.Shutdown:
                    result = new { };
                    break;
                default:
                    await WriteErrorAsync(id, -32601, $"Unknown method '{method}'.", cancellationToken);
                    return;
            }

            await WriteAsync(new { jsonrpc = "2.0", id, result }, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await WriteErrorAsync(id, -32800, "Request cancelled.", lifetime.Token);
        }
        catch (JsonException exception)
        {
            await WriteErrorAsync(id, -32602, exception.Message, lifetime.Token);
        }
    }

    object? CurrentSelection() => currentEffect is null && currentLayout is null
        ? null
        : new { effect = currentEffect, layout = currentLayout };

    static JsonElement? CloneOptional(JsonElement value, string name) =>
        value.TryGetProperty(name, out var property) && property.ValueKind != JsonValueKind.Null
            ? property.Clone()
            : null;

    Task WriteNotificationAsync(CancellationToken cancellationToken) => WriteAsync(new
    {
        jsonrpc = "2.0",
        method = Protocol.StatusChanged,
        @params = new { }
    }, cancellationToken);

    Task WriteErrorAsync(long? id, int code, string message, CancellationToken cancellationToken) =>
        WriteAsync(new { jsonrpc = "2.0", id, error = new { code, message } }, cancellationToken);

    async Task WriteAsync(object response, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(response, Protocol.JsonOptions);
        await writeGate.WaitAsync(cancellationToken);
        try
        {
            await Console.Out.WriteLineAsync(json.AsMemory(), cancellationToken);
            await Console.Out.FlushAsync(cancellationToken);
        }
        finally
        {
            writeGate.Release();
        }
    }
}

internal static class Protocol
{
    public const string ContractVersion = "1.0";
    public const string Initialize = "initialize";
    public const string StatusGet = "status/get";
    public const string CatalogList = "catalog/list";
    public const string BindingApply = "binding/apply";
    public const string BindingRelease = "binding/release";
    public const string Shutdown = "shutdown";
    public const string StatusChanged = "status/changed";
    public const string Cancel = "$/cancel";

    public static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}
