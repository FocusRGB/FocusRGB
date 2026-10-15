using System.Collections.Concurrent;
using System.Text.Json;
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
                            await WriteErrorAsync(output, writeGate, TryReadId(root), -32600,
                                "Invalid JSON-RPC request.", lifetime.Token).ConfigureAwait(false);
                            continue;
                        }

                        if (method == PluginMethods.Cancel)
                        {
                            if (TryReadCancelId(root, out var cancelledId)
                                && pending.TryGetValue(cancelledId, out var cancelledRequest))
                            {
                                cancelledRequest.Cancel();
                            }
                            continue;
                        }

                        var id = TryReadId(root);
                        if (id is null)
                        {
                            continue;
                        }

                        var parameters = root.TryGetProperty("params", out var value)
                            ? value.Clone()
                            : JsonSerializer.SerializeToElement(new { }, PluginJson.Options);
                        var requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                        pending[id.Value] = requestCancellation;
                        var task = HandleRequestAsync(
                            plugin,
                            output,
                            error,
                            writeGate,
                            id.Value,
                            method,
                            parameters,
                            requestCancellation.Token);
                        tasks.Add(task);

                        if (method == PluginMethods.Shutdown)
                        {
                            await task.ConfigureAwait(false);
                            break;
                        }

                        _ = task.ContinueWith(
                            _ =>
                            {
                                pending.TryRemove(id.Value, out var completedCancellation);
                                completedCancellation?.Dispose();
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
                foreach (var request in pending.Values)
                {
                    request.Cancel();
                }
                try
                {
                    await Task.WhenAll(tasks).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }
                foreach (var request in pending.Values)
                {
                    request.Dispose();
                }
            }
        }

        private static async Task HandleRequestAsync(
            IFocusRgbPlugin plugin,
            TextWriter output,
            TextWriter error,
            SemaphoreSlim writeGate,
            long id,
            string method,
            JsonElement parameters,
            CancellationToken cancellationToken)
        {
            try
            {
                object result = method switch
                {
                    PluginMethods.Initialize => await plugin.InitializeAsync(
                        Deserialize<InitializeParameters>(parameters), cancellationToken).ConfigureAwait(false),
                    PluginMethods.StatusGet => await plugin.GetStatusAsync(cancellationToken).ConfigureAwait(false),
                    PluginMethods.CatalogList => await plugin.ListCatalogAsync(
                        Deserialize<CatalogListParameters>(parameters), cancellationToken).ConfigureAwait(false),
                    PluginMethods.BindingApply => await plugin.ApplyBindingAsync(
                        Deserialize<BindingApplyParameters>(parameters), cancellationToken).ConfigureAwait(false),
                    PluginMethods.BindingRelease => await plugin.ReleaseBindingAsync(
                        Deserialize<BindingReleaseParameters>(parameters), cancellationToken).ConfigureAwait(false),
                    PluginMethods.Shutdown => await ShutdownAsync(plugin, cancellationToken).ConfigureAwait(false),
                    _ => throw new JsonRpcDispatchException(-32601, $"Unknown method '{method}'.")
                };
                await WriteResultAsync(output, writeGate, id, result, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                await WriteErrorAsync(output, writeGate, id, -32800, "Request cancelled.", CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (JsonException exception)
            {
                await WriteErrorAsync(output, writeGate, id, -32602, exception.Message, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (JsonRpcDispatchException exception)
            {
                await WriteErrorAsync(output, writeGate, id, exception.Code, exception.Message, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                await error.WriteLineAsync(exception.ToString()).ConfigureAwait(false);
                await WriteErrorAsync(output, writeGate, id, -32603, "Plugin request failed.", cancellationToken)
                    .ConfigureAwait(false);
            }
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
                && version.GetString() == "2.0"
                && root.TryGetProperty("method", out var methodValue)
                && (method = methodValue.GetString() ?? string.Empty).Length > 0;
        }

        private static long? TryReadId(JsonElement root) =>
            root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("id", out var id)
            && id.TryGetInt64(out var value)
                ? value
                : null;

        private static bool TryReadCancelId(JsonElement root, out long id)
        {
            id = 0;
            return root.TryGetProperty("params", out var parameters)
                && parameters.TryGetProperty("id", out var idValue)
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
            await WriteAsync(output, writeGate, new { jsonrpc = "2.0", id, error = new { code, message } },
                cancellationToken).ConfigureAwait(false);

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

        private sealed class JsonRpcDispatchException(int code, string message) : Exception(message)
        {
            public int Code { get; } = code;
        }
    }
}
