using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using FocusRgb.Contracts;

namespace FocusRgb.PluginSdk
{
    /// <summary>Serves newline-delimited JSON-RPC while reserving stdout exclusively for protocol data.</summary>
    public static class PluginServer
    {
        public static async Task RunAsync(
            IFocusRgbPlugin plugin,
            TextReader input,
            TextWriter output,
            TextWriter error,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(plugin);
            ArgumentNullException.ThrowIfNull(input);
            ArgumentNullException.ThrowIfNull(output);
            ArgumentNullException.ThrowIfNull(error);

            using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var pending = new ConcurrentDictionary<long, CancellationTokenSource>();
            var tasks = new ConcurrentBag<Task>();
            using var writeGate = new SemaphoreSlim(1, 1);
            using var errorGate = new SemaphoreSlim(1, 1);
            plugin.Connect(new PluginHost(output, error, writeGate, errorGate));

            try
            {
                while (!lifetime.IsCancellationRequested)
                {
                    var line = await input.ReadLineAsync(lifetime.Token).ConfigureAwait(false);
                    if (line is null)
                    {
                        break;
                    }

                    JsonDocument document;
                    try
                    {
                        document = JsonDocument.Parse(line);
                    }
                    catch (JsonException exception)
                    {
                        await WriteErrorAsync(output, writeGate, null, -32700, exception.Message, lifetime.Token)
                            .ConfigureAwait(false);
                        continue;
                    }

                    using (document)
                    {
                        var root = document.RootElement;
                        if (!TryReadMethod(root, out var method))
                        {
                            await WriteErrorAsync(output, writeGate, null, -32600,
                                "Invalid JSON-RPC request.", lifetime.Token).ConfigureAwait(false);
                            continue;
                        }

                        if (method == PluginMethods.Cancel)
                        {
                            if (TryReadCancelId(root, out var cancelledId)
                                && pending.TryRemove(cancelledId, out var cancelledRequest))
                            {
                                try
                                {
                                    cancelledRequest.Cancel();
                                }
                                catch (ObjectDisposedException)
                                {
                                }
                            }
                            continue;
                        }

                        if (!root.TryGetProperty("id", out var idValue)
                            || idValue.ValueKind != JsonValueKind.Number
                            || !idValue.TryGetInt64(out var id))
                        {
                            await WriteErrorAsync(output, writeGate, null, -32600,
                                "Invalid JSON-RPC request.", lifetime.Token).ConfigureAwait(false);
                            continue;
                        }

                        var parameters = root.TryGetProperty("params", out var value)
                            ? value.Clone()
                            : JsonSerializer.SerializeToElement(new { }, PluginJson.Options);
                        var requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                        pending[id] = requestCancellation;
                        var task = HandleRequestAsync(
                            plugin,
                            output,
                            error,
                            writeGate,
                            errorGate,
                            id,
                            method,
                            parameters,
                            requestCancellation.Token,
                            lifetime.Token);
                        tasks.Add(task);

                        if (method == PluginMethods.Shutdown)
                        {
                            await task.ConfigureAwait(false);
                            break;
                        }

                        _ = task.ContinueWith(
                            completed =>
                            {
                                pending.TryRemove(id, out _);
                                requestCancellation.Dispose();
                            },
                            CancellationToken.None,
                            TaskContinuationOptions.ExecuteSynchronously,
                            TaskScheduler.Default);
                    }
                }
            }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
            {
            }
            finally
            {
                lifetime.Cancel();
                try
                {
                    await Task.WhenAll(tasks).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }
                foreach (var request in pending.ToArray())
                {
                    if (pending.TryRemove(request.Key, out var cancellation)) cancellation.Dispose();
                }
            }
        }

        private static async Task HandleRequestAsync(
            IFocusRgbPlugin plugin,
            TextWriter output,
            TextWriter error,
            SemaphoreSlim writeGate,
            SemaphoreSlim errorGate,
            long id,
            string method,
            JsonElement parameters,
            CancellationToken requestCancellationToken,
            CancellationToken responseCancellationToken)
        {
            try
            {
                object result = method switch
                {
                    PluginMethods.Initialize => await InitializeAsync(
                        plugin, Deserialize<InitializeParameters>(parameters), requestCancellationToken)
                        .ConfigureAwait(false),
                    PluginMethods.StatusGet => await plugin.GetStatusAsync(requestCancellationToken).ConfigureAwait(false),
                    PluginMethods.CatalogList => await plugin.ListCatalogAsync(
                        Deserialize<CatalogListParameters>(parameters), requestCancellationToken).ConfigureAwait(false),
                    PluginMethods.BindingApply => await plugin.ApplyBindingAsync(
                        Deserialize<BindingApplyParameters>(parameters), requestCancellationToken).ConfigureAwait(false),
                    PluginMethods.BindingRelease => await plugin.ReleaseBindingAsync(
                        Deserialize<BindingReleaseParameters>(parameters), requestCancellationToken).ConfigureAwait(false),
                    PluginMethods.Shutdown => await ShutdownAsync(plugin, requestCancellationToken).ConfigureAwait(false),
                    _ => throw new JsonRpcDispatchException(-32601, $"Unknown method '{method}'.")
                };
                await WriteResultAsync(output, writeGate, id, result, responseCancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (requestCancellationToken.IsCancellationRequested)
            {
                await WriteErrorAsync(
                    output, writeGate, id, -32800, "Request cancelled.", responseCancellationToken)
                    .ConfigureAwait(false);
            }
            catch (JsonException exception)
            {
                await WriteErrorAsync(output, writeGate, id, -32602, exception.Message, responseCancellationToken)
                    .ConfigureAwait(false);
            }
            catch (JsonRpcDispatchException exception)
            {
                await WriteErrorAsync(
                    output, writeGate, id, exception.Code, exception.Message, responseCancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                await WriteLogAsync(error, errorGate, exception.ToString(), CancellationToken.None)
                    .ConfigureAwait(false);
                await WriteErrorAsync(
                    output, writeGate, id, -32603, "Plugin request failed.", responseCancellationToken)
                    .ConfigureAwait(false);
            }
        }

        private static async Task<InitializeResult> InitializeAsync(
            IFocusRgbPlugin plugin,
            InitializeParameters parameters,
            CancellationToken cancellationToken)
        {
            var result = await plugin.InitializeAsync(parameters, cancellationToken).ConfigureAwait(false);
            var version = typeof(PluginServer).Assembly.GetName().Version
                ?? throw new InvalidOperationException("The SDK assembly version is unavailable.");
            return result with
            {
                Sdk = new PluginSdkInfo
                {
                    Name = "FocusRgb.PluginSdk",
                    Language = "dotnet",
                    Version = $"{version.Major}.{version.Minor}.{version.Build}"
                }
            };
        }

        private static async Task<object> ShutdownAsync(
            IFocusRgbPlugin plugin,
            CancellationToken cancellationToken)
        {
            await plugin.ShutdownAsync(cancellationToken).ConfigureAwait(false);
            return new { };
        }

        private static T Deserialize<T>(JsonElement parameters) =>
            parameters.Deserialize<T>(PluginJson.Options)
            ?? throw new JsonException("Request parameters are required.");

        private static bool TryReadMethod(JsonElement root, out string method)
        {
            method = string.Empty;
            return root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("jsonrpc", out var version)
                && version.ValueKind == JsonValueKind.String
                && version.GetString() == "2.0"
                && root.TryGetProperty("method", out var methodValue)
                && methodValue.ValueKind == JsonValueKind.String
                && (method = methodValue.GetString() ?? string.Empty).Length > 0;
        }

        private static bool TryReadCancelId(JsonElement root, out long id)
        {
            id = 0;
            return root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("params", out var parameters)
                && parameters.ValueKind == JsonValueKind.Object
                && parameters.TryGetProperty("id", out var idValue)
                && idValue.ValueKind == JsonValueKind.Number
                && idValue.TryGetInt64(out id);
        }

        private static async Task WriteResultAsync(
            TextWriter output,
            SemaphoreSlim writeGate,
            long id,
            object result,
            CancellationToken cancellationToken) =>
            await WriteAsync(output, writeGate, new { jsonrpc = "2.0", id, result }, cancellationToken)
                .ConfigureAwait(false);

        private static async Task WriteErrorAsync(
            TextWriter output,
            SemaphoreSlim writeGate,
            long? id,
            int code,
            string message,
            CancellationToken cancellationToken) =>
            await WriteAsync(output, writeGate, new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = id,
                ["error"] = new JsonObject { ["code"] = code, ["message"] = message }
            }, cancellationToken).ConfigureAwait(false);

        private static async Task WriteAsync(
            TextWriter output,
            SemaphoreSlim writeGate,
            object response,
            CancellationToken cancellationToken)
        {
            var json = JsonSerializer.Serialize(response, PluginJson.Options);
            await writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await output.WriteLineAsync(json.AsMemory(), cancellationToken).ConfigureAwait(false);
                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                writeGate.Release();
            }
        }

        private static async Task WriteLogAsync(
            TextWriter error,
            SemaphoreSlim errorGate,
            string message,
            CancellationToken cancellationToken)
        {
            await errorGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await error.WriteLineAsync(message.AsMemory(), cancellationToken).ConfigureAwait(false);
                await error.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                errorGate.Release();
            }
        }

        private sealed class JsonRpcDispatchException(int code, string message) : Exception(message)
        {
            public int Code { get; } = code;
        }

        private sealed class PluginHost(
            TextWriter output,
            TextWriter error,
            SemaphoreSlim writeGate,
            SemaphoreSlim errorGate) : IPluginHost
        {
            public Task NotifyStatusChangedAsync(CancellationToken cancellationToken = default) =>
                WriteAsync(output, writeGate, new
                {
                    jsonrpc = "2.0",
                    method = PluginMethods.StatusChanged,
                    @params = new { }
                }, cancellationToken);

            public async Task LogAsync(string message, CancellationToken cancellationToken = default)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(message);
                await WriteLogAsync(error, errorGate, message, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
