using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using FocusRgb.Contracts;

namespace FocusRgb.App.Plugins
{
    /// <summary>Owns one plugin process and correlates bounded JSON-RPC requests over standard IO.</summary>
    public sealed class PluginProcessClient : IAsyncDisposable
    {
        private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan InitializeTimeout = TimeSpan.FromSeconds(10);
        private readonly Process _process;
        private readonly StreamWriter _input;
        private readonly SemaphoreSlim _writeGate = new(1, 1);
        private readonly ConcurrentDictionary<long, TaskCompletionSource<RpcResponse>> _pending = new();
        private readonly CancellationTokenSource _lifetime = new();
        private readonly Task _reader;
        private long _nextId;
        private bool _disposed;

        private PluginProcessClient(Process process)
        {
            _process = process;
            _input = process.StandardInput;
            _input.AutoFlush = true;
            Health = "starting";
            _reader = ReadResponsesAsync();
            _ = ObserveExitAsync();
        }

        public string Health { get; private set; }
        public string? PluginId { get; private set; }
        public event EventHandler? StatusChanged;
        public event EventHandler<string>? LogReceived;

        public static async Task<PluginStartResult> StartAsync(
            PluginLaunchSpec launch,
            PluginManifest manifest,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(launch);
            ArgumentNullException.ThrowIfNull(manifest);
            var startInfo = new ProcessStartInfo
            {
                FileName = launch.FileName,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            foreach (var argument in launch.Arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            Process process;
            try
            {
                process = Process.Start(startInfo)
                    ?? throw new InvalidOperationException("The plugin process did not start.");
            }
            catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                return PluginStartResult.Fail("plugin-absent", exception.Message);
            }

            var client = new PluginProcessClient(process);
            var initialized = await client.CallAsync<InitializeResult>(
                PluginMethods.Initialize,
                new InitializeParameters
                {
                    ContractVersion = ContractVersion.Current,
                    Host = new PluginHostInfo { Name = "FocusRGB", Version = "0.1.0" }
                },
                InitializeTimeout,
                cancellationToken).ConfigureAwait(false);
            if (!initialized.IsSuccess)
            {
                await client.DisposeAsync().ConfigureAwait(false);
                return PluginStartResult.Fail(initialized.ErrorCode!, initialized.ErrorMessage!);
            }

            var handshake = initialized.Value!;
            if (handshake.PluginId != manifest.Id
                || !PluginPackage.TryContractMajor(handshake.ContractVersion, out var major)
                || major != ContractVersion.Major)
            {
                await client.DisposeAsync().ConfigureAwait(false);
                return PluginStartResult.Fail("contract-incompatible", "The plugin handshake does not match its manifest.");
            }

            client.PluginId = handshake.PluginId;
            client.Health = "healthy";
            client.BeginErrorRead();
            return PluginStartResult.Ok(client, handshake);
        }

        public Task<PluginCallResult<PluginStatusResult>> GetStatusAsync(
            CancellationToken cancellationToken = default) =>
            CallAsync<PluginStatusResult>(PluginMethods.StatusGet, new { }, DefaultTimeout, cancellationToken);

        public Task<PluginCallResult<PluginCatalogResult>> ListCatalogAsync(
            string kind,
            CancellationToken cancellationToken = default) =>
            CallAsync<PluginCatalogResult>(PluginMethods.CatalogList,
                new CatalogListParameters { Kind = kind }, DefaultTimeout, cancellationToken);

        public Task<PluginCallResult<BindingApplyResult>> ApplyBindingAsync(
            BindingApplyParameters parameters,
            CancellationToken cancellationToken = default) =>
            CallAsync<BindingApplyResult>(PluginMethods.BindingApply, parameters, DefaultTimeout, cancellationToken);

        public Task<PluginCallResult<BindingReleaseResult>> ReleaseBindingAsync(
            BindingReleaseParameters parameters,
            CancellationToken cancellationToken = default) =>
            CallAsync<BindingReleaseResult>(PluginMethods.BindingRelease, parameters, DefaultTimeout, cancellationToken);

        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            _disposed = true;
            if (!_process.HasExited)
            {
                using var shutdown = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                try
                {
                    await CallAsync<JsonElement>(PluginMethods.Shutdown, new { }, DefaultTimeout, shutdown.Token)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }
            }
            _lifetime.Cancel();
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
            }
            try
            {
                await _reader.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            FailPending("plugin-exited", "The plugin process exited.");
            _input.Dispose();
            _process.Dispose();
            _writeGate.Dispose();
            _lifetime.Dispose();
        }

        private async Task<PluginCallResult<T>> CallAsync<T>(
            string method,
            object parameters,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            if (_process.HasExited)
                return new PluginCallResult<T>("plugin-exited", "The plugin process exited.");

            var id = Interlocked.Increment(ref _nextId);
            var completion = new TaskCompletionSource<RpcResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!_pending.TryAdd(id, completion))
                return new PluginCallResult<T>("host-error", "Could not allocate a plugin request.");

            try
            {
                await WriteAsync(new { jsonrpc = "2.0", id, method, @params = parameters }, cancellationToken)
                    .ConfigureAwait(false);
                RpcResponse response;
                try
                {
                    response = await completion.Task.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
                }
                catch (TimeoutException)
                {
                    _pending.TryRemove(id, out _);
                    await WriteAsync(new
                    {
                        jsonrpc = "2.0",
                        method = PluginMethods.Cancel,
                        @params = new CancelParameters { Id = id }
                    }, CancellationToken.None).ConfigureAwait(false);
                    return new PluginCallResult<T>("timeout", $"Plugin request '{method}' timed out.");
                }

                if (response.ErrorCode is not null)
                    return new PluginCallResult<T>(response.ErrorCode, response.ErrorMessage!);
                try
                {
                    var value = response.Result.Deserialize<T>(PluginJson.Options);
                    return value is null
                        ? new PluginCallResult<T>("malformed-response", "The plugin response had no result.")
                        : new PluginCallResult<T>(value);
                }
                catch (JsonException exception)
                {
                    return new PluginCallResult<T>("malformed-response", exception.Message);
                }
            }
            catch (OperationCanceledException)
            {
                _pending.TryRemove(id, out _);
                throw;
            }
            catch (IOException exception)
            {
                return new PluginCallResult<T>("plugin-exited", exception.Message);
            }
            finally
            {
                _pending.TryRemove(id, out _);
            }
        }

        private async Task WriteAsync(object message, CancellationToken cancellationToken)
        {
            var json = JsonSerializer.Serialize(message, PluginJson.Options);
            await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await _input.WriteLineAsync(json.AsMemory(), cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _writeGate.Release();
            }
        }

        private async Task ReadResponsesAsync()
        {
            try
            {
                while (!_lifetime.IsCancellationRequested)
                {
                    var line = await _process.StandardOutput.ReadLineAsync(_lifetime.Token).ConfigureAwait(false);
                    if (line is null) break;
                    JsonDocument document;
                    try
                    {
                        document = JsonDocument.Parse(line);
                    }
                    catch (JsonException exception)
                    {
                        Health = "unhealthy";
                        FailPending("malformed-response", exception.Message);
                        continue;
                    }
                    using (document)
                    {
                        var root = document.RootElement;
                        if (root.TryGetProperty("method", out var method)
                            && method.GetString() == PluginMethods.StatusChanged)
                        {
                            StatusChanged?.Invoke(this, EventArgs.Empty);
                            continue;
                        }
                        if (!root.TryGetProperty("id", out var idValue)
                            || !idValue.TryGetInt64(out var id)
                            || !_pending.TryRemove(id, out var completion))
                        {
                            continue;
                        }
                        if (root.TryGetProperty("error", out var error))
                        {
                            completion.TrySetResult(new RpcResponse
                            {
                                ErrorCode = "plugin-error",
                                ErrorMessage = error.TryGetProperty("message", out var message)
                                    ? message.GetString() ?? "Plugin request failed."
                                    : "Plugin request failed."
                            });
                        }
                        else if (root.TryGetProperty("result", out var result))
                        {
                            completion.TrySetResult(new RpcResponse { Result = result.Clone() });
                        }
                        else
                        {
                            completion.TrySetResult(new RpcResponse
                            {
                                ErrorCode = "malformed-response",
                                ErrorMessage = "The plugin response had neither result nor error."
                            });
                        }
                    }
                }
            }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
            {
            }
            catch (IOException exception)
            {
                FailPending("plugin-exited", exception.Message);
            }
        }

        private async Task ObserveExitAsync()
        {
            await _process.WaitForExitAsync().ConfigureAwait(false);
            Health = "exited";
            FailPending("plugin-exited", "The plugin process exited.");
        }

        private void BeginErrorRead()
        {
            _process.ErrorDataReceived += (_, args) =>
            {
                if (!string.IsNullOrWhiteSpace(args.Data)) LogReceived?.Invoke(this, args.Data);
            };
            _process.BeginErrorReadLine();
        }

        private void FailPending(string code, string message)
        {
            foreach (var request in _pending.ToArray())
            {
                if (_pending.TryRemove(request.Key, out var completion))
                {
                    completion.TrySetResult(new RpcResponse { ErrorCode = code, ErrorMessage = message });
                }
            }
        }

        private sealed class RpcResponse
        {
            public JsonElement Result { get; init; }
            public string? ErrorCode { get; init; }
            public string? ErrorMessage { get; init; }
        }
    }

    /// <summary>Returns either one initialized process client or one startup failure.</summary>
    public sealed class PluginStartResult
    {
        public bool IsSuccess => Client is not null;
        public PluginProcessClient? Client { get; private init; }
        public InitializeResult? Handshake { get; private init; }
        public string? ErrorCode { get; private init; }
        public string? ErrorMessage { get; private init; }

        public static PluginStartResult Ok(PluginProcessClient client, InitializeResult handshake) => new()
        {
            Client = client,
            Handshake = handshake
        };

        public static PluginStartResult Fail(string code, string message) => new()
        {
            ErrorCode = code,
            ErrorMessage = message
        };
    }

    /// <summary>Returns either one typed plugin response or one stable host/plugin error.</summary>
    public sealed class PluginCallResult<T>
    {
        internal PluginCallResult(T value) => Value = value;

        internal PluginCallResult(string code, string message)
        {
            ErrorCode = code;
            ErrorMessage = message;
        }

        public bool IsSuccess => ErrorCode is null;
        public T? Value { get; private init; }
        public string? ErrorCode { get; private init; }
        public string? ErrorMessage { get; private init; }

    }
}
