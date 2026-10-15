using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using FocusRgb.Contracts;

namespace FocusRgb.App.Plugins
{
    /// <summary>Owns one plugin process and correlates bounded JSON-RPC requests over standard IO.</summary>
    public sealed class PluginProcessClient : IAsyncDisposable
    {
        private const int MaxLineLength = 64 * 1024;
        private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan InitializeTimeout = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan ExitGrace = TimeSpan.FromMilliseconds(500);
        private const string WriteFailureMessage = "The plugin stopped reading requests.";
        private readonly Process _process;
        private readonly StreamWriter _input;
        private readonly SemaphoreSlim _writeGate = new(1, 1);
        private readonly ConcurrentDictionary<long, TaskCompletionSource<RpcResponse>> _pending = new();
        private readonly CancellationTokenSource _lifetime = new();
        private readonly object _logGate = new();
        private readonly Queue<string> _bufferedLogs = new();
        private readonly Task _reader;
        private readonly Task _errorReader;
        private readonly Task _exitObserver;
        private EventHandler<string>? _logReceived;
        private long _nextId;
        private int _statusChangePending;
        private int _writeFailed;
        private bool _disposed;

        private PluginProcessClient(Process process)
        {
            _process = process;
            _input = process.StandardInput;
            _input.AutoFlush = true;
            Health = "starting";
            _reader = ReadResponsesAsync();
            _errorReader = ReadErrorsAsync();
            _exitObserver = ObserveExitAsync();
        }

        public string Health { get; private set; }
        public string? PluginId { get; private set; }
        public event EventHandler? StatusChanged;
        public event EventHandler<string>? LogReceived
        {
            add
            {
                List<string> buffered;
                lock (_logGate)
                {
                    _logReceived += value;
                    buffered = [.. _bufferedLogs];
                    _bufferedLogs.Clear();
                }
                foreach (var message in buffered) value?.Invoke(this, message);
            }
            remove
            {
                lock (_logGate) _logReceived -= value;
            }
        }

        public static async Task<PluginStartResult> StartAsync(
            PluginLaunchSpec launch,
            PluginManifest manifest,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(launch);
            ArgumentNullException.ThrowIfNull(manifest);
            var utf8 = new UTF8Encoding(false);
            var startInfo = new ProcessStartInfo
            {
                FileName = launch.FileName,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardInputEncoding = utf8,
                StandardOutputEncoding = utf8,
                StandardErrorEncoding = utf8,
                CreateNoWindow = true
            };
            foreach (var argument in launch.Arguments) startInfo.ArgumentList.Add(argument);

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
            try
            {
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
                    || handshake.Capabilities is null
                    || !PluginPackage.TryContractMajor(handshake.ContractVersion, out var major)
                    || major != ContractVersion.Major)
                {
                    await client.DisposeAsync().ConfigureAwait(false);
                    return PluginStartResult.Fail("contract-incompatible", "The plugin handshake does not match its manifest.");
                }

                client.PluginId = handshake.PluginId;
                client.Health = "healthy";
                return PluginStartResult.Ok(client, handshake);
            }
            catch
            {
                await client.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        public async Task<PluginCallResult<PluginStatusResult>> GetStatusAsync(
            CancellationToken cancellationToken = default)
        {
            Interlocked.Exchange(ref _statusChangePending, 0);
            return await CallAsync<PluginStatusResult>(
                PluginMethods.StatusGet, new { }, DefaultTimeout, cancellationToken).ConfigureAwait(false);
        }

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
            if (!_process.HasExited)
            {
                using var shutdown = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                try
                {
                    await CallAsync<JsonElement>(PluginMethods.Shutdown, new { }, DefaultTimeout, shutdown.Token)
                        .ConfigureAwait(false);
                    await _process.WaitForExitAsync().WaitAsync(ExitGrace).ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is OperationCanceledException
                    or TimeoutException or IOException or InvalidOperationException or ObjectDisposedException)
                {
                }
            }

            _disposed = true;
            _lifetime.Cancel();
            try
            {
                if (!_process.HasExited) _process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
            }

            await ObserveWithoutThrowingAsync(_reader).ConfigureAwait(false);
            await ObserveWithoutThrowingAsync(_errorReader).ConfigureAwait(false);
            await ObserveWithoutThrowingAsync(_exitObserver).ConfigureAwait(false);
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
            if (_disposed || _process.HasExited)
                return new PluginCallResult<T>("plugin-exited", "The plugin process exited.");
            if (Volatile.Read(ref _writeFailed) != 0)
                return new PluginCallResult<T>("timeout", WriteFailureMessage);

            var id = Interlocked.Increment(ref _nextId);
            var completion = new TaskCompletionSource<RpcResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
            while (!_pending.TryAdd(id, completion)) id = Interlocked.Increment(ref _nextId);

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
                    await CancelRequestAsync(id).ConfigureAwait(false);
                    return new PluginCallResult<T>("timeout", $"Plugin request '{method}' timed out.");
                }

                if (response.ErrorCode is not null)
                    return new PluginCallResult<T>(
                        response.ErrorCode, response.ErrorMessage!, response.JsonRpcErrorCode);
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
                await CancelRequestAsync(id).ConfigureAwait(false);
                throw;
            }
            catch (TimeoutException)
            {
                await CancelRequestAsync(id).ConfigureAwait(false);
                return new PluginCallResult<T>("timeout", $"Writing plugin request '{method}' timed out.");
            }
            catch (InvalidOperationException exception)
            {
                MarkWriteFailure(exception.Message);
                return new PluginCallResult<T>("timeout", WriteFailureMessage);
            }
            catch (Exception exception) when (exception is IOException or ObjectDisposedException)
            {
                return new PluginCallResult<T>("plugin-exited", exception.Message);
            }
            finally
            {
                _pending.TryRemove(id, out _);
            }
        }

        private async Task CancelRequestAsync(long id)
        {
            _pending.TryRemove(id, out _);
            try
            {
                await WriteAsync(new
                {
                    jsonrpc = "2.0",
                    method = PluginMethods.Cancel,
                    @params = new CancelParameters { Id = id }
                }, _lifetime.Token).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is OperationCanceledException
                or TimeoutException or IOException or InvalidOperationException or ObjectDisposedException)
            {
            }
        }

        private async Task WriteAsync(object message, CancellationToken cancellationToken)
        {
            var json = JsonSerializer.Serialize(message, PluginJson.Options);
            if (Volatile.Read(ref _writeFailed) != 0) throw new TimeoutException(WriteFailureMessage);
            if (!await _writeGate.WaitAsync(DefaultTimeout, cancellationToken).ConfigureAwait(false))
            {
                MarkWriteFailure(WriteFailureMessage);
                throw new TimeoutException("Writing to the plugin timed out.");
            }
            try
            {
                if (Volatile.Read(ref _writeFailed) != 0) throw new TimeoutException(WriteFailureMessage);
                var write = _input.WriteLineAsync(json.AsMemory(), cancellationToken);
                try
                {
                    await write.WaitAsync(DefaultTimeout, cancellationToken).ConfigureAwait(false);
                }
                catch (TimeoutException)
                {
                    _ = ObserveWithoutThrowingAsync(write);
                    MarkWriteFailure(WriteFailureMessage);
                    throw;
                }
                catch (InvalidOperationException exception)
                {
                    MarkWriteFailure(exception.Message);
                    throw;
                }
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
                    var line = await ReadLineAsync(
                        _process.StandardOutput, MaxLineLength, _lifetime.Token).ConfigureAwait(false);
                    if (line.EndOfStream) break;
                    if (line.WasTooLong)
                    {
                        Health = "unhealthy";
                        FailOnlyPendingMalformed("The plugin response exceeded the 64 KiB limit.");
                        continue;
                    }
                    ProcessResponse(line.Value!);
                }
            }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
            {
            }
            catch (Exception exception) when (exception is IOException or ObjectDisposedException)
            {
                FailPending("plugin-exited", exception.Message);
            }
            catch (Exception exception) when (exception is JsonException or InvalidOperationException)
            {
                Health = "unhealthy";
                FailPending("malformed-response", exception.Message);
            }
        }

        private void ProcessResponse(string line)
        {
            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(line);
            }
            catch (JsonException)
            {
                Health = "unhealthy";
                FailOnlyPendingMalformed("The plugin response was not valid JSON.");
                return;
            }

            using (document)
            {
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    Health = "unhealthy";
                    FailOnlyPendingMalformed("The plugin response was not a JSON object.");
                    return;
                }
                if (root.TryGetProperty("method", out var method)
                    && method.ValueKind == JsonValueKind.String
                    && !root.TryGetProperty("id", out _))
                {
                    if (method.GetString() == PluginMethods.StatusChanged
                        && Interlocked.Exchange(ref _statusChangePending, 1) == 0)
                    {
                        RaiseStatusChanged();
                    }
                    return;
                }
                if (!root.TryGetProperty("id", out var idValue)
                    || idValue.ValueKind != JsonValueKind.Number
                    || !idValue.TryGetInt64(out var id))
                {
                    Health = "unhealthy";
                    FailOnlyPendingMalformed("The plugin response had no valid request id.");
                    return;
                }
                if (!_pending.TryRemove(id, out var completion)) return;
                if (root.TryGetProperty("error", out var error))
                {
                    if (error.ValueKind != JsonValueKind.Object
                        || !error.TryGetProperty("code", out var code)
                        || code.ValueKind != JsonValueKind.Number
                        || !code.TryGetInt32(out var jsonRpcCode)
                        || !error.TryGetProperty("message", out var message)
                        || message.ValueKind != JsonValueKind.String)
                    {
                        CompleteMalformed(completion, "The plugin response contained an invalid error object.");
                        return;
                    }
                    completion.TrySetResult(new RpcResponse
                    {
                        ErrorCode = jsonRpcCode switch
                        {
                            -32601 => "capability-unsupported",
                            -32800 => "request-cancelled",
                            _ => "malformed-response"
                        },
                        ErrorMessage = $"JSON-RPC error {jsonRpcCode}: {message.GetString() ?? "Plugin request failed."}",
                        JsonRpcErrorCode = jsonRpcCode
                    });
                }
                else if (root.TryGetProperty("result", out var result))
                {
                    if (Volatile.Read(ref _writeFailed) == 0) Health = "healthy";
                    completion.TrySetResult(new RpcResponse { Result = result.Clone() });
                }
                else
                {
                    CompleteMalformed(completion, "The plugin response had neither result nor error.");
                }
            }
        }

        private void CompleteMalformed(TaskCompletionSource<RpcResponse> completion, string message)
        {
            Health = "unhealthy";
            completion.TrySetResult(new RpcResponse
            {
                ErrorCode = "malformed-response",
                ErrorMessage = message
            });
        }

        private void FailOnlyPendingMalformed(string message)
        {
            if (_pending.Count != 1) return;
            var pending = _pending.First();
            if (_pending.TryRemove(pending.Key, out var completion))
                CompleteMalformed(completion, message);
        }

        private async Task ReadErrorsAsync()
        {
            try
            {
                while (!_lifetime.IsCancellationRequested)
                {
                    var line = await ReadLineAsync(
                        _process.StandardError, MaxLineLength, _lifetime.Token).ConfigureAwait(false);
                    if (line.EndOfStream) break;
                    if (line.WasTooLong)
                        PublishLog("Plugin stderr line exceeded the 64 KiB limit and was truncated.");
                    else if (!string.IsNullOrWhiteSpace(line.Value))
                        PublishLog(line.Value);
                }
            }
            catch (Exception exception) when (exception is OperationCanceledException
                or IOException or ObjectDisposedException)
            {
            }
        }

        private void PublishLog(string message)
        {
            EventHandler<string>? handler;
            lock (_logGate)
            {
                handler = _logReceived;
                if (handler is null)
                {
                    _bufferedLogs.Enqueue(message);
                    return;
                }
            }
            try
            {
                handler.Invoke(this, message);
            }
            catch (Exception)
            {
            }
        }

        private async Task ObserveExitAsync()
        {
            try
            {
                await _process.WaitForExitAsync().ConfigureAwait(false);
                Health = "exited";
                FailPending("plugin-exited", "The plugin process exited.");
                RaiseStatusChanged();
            }
            catch (Exception exception) when (exception is InvalidOperationException or ObjectDisposedException)
            {
            }
        }

        private void FailPending(string code, string message)
        {
            foreach (var request in _pending.ToArray())
            {
                if (_pending.TryRemove(request.Key, out var completion))
                    completion.TrySetResult(new RpcResponse { ErrorCode = code, ErrorMessage = message });
            }
        }

        private void MarkWriteFailure(string message)
        {
            if (Interlocked.Exchange(ref _writeFailed, 1) != 0) return;
            Health = "unhealthy";
            FailPending("timeout", string.IsNullOrWhiteSpace(message) ? WriteFailureMessage : message);
            RaiseStatusChanged();
        }

        private void RaiseStatusChanged()
        {
            try
            {
                StatusChanged?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception)
            {
            }
        }

        private static async Task<BoundedLine> ReadLineAsync(
            StreamReader reader,
            int maxLength,
            CancellationToken cancellationToken)
        {
            var value = new StringBuilder(Math.Min(maxLength, 4096));
            var character = new char[1];
            var tooLong = false;
            while (await reader.ReadAsync(character.AsMemory(), cancellationToken).ConfigureAwait(false) > 0)
            {
                if (character[0] == '\n') return new BoundedLine(value.ToString(), tooLong, false);
                if (character[0] == '\r') continue;
                if (value.Length < maxLength) value.Append(character[0]);
                else tooLong = true;
            }
            return value.Length == 0 && !tooLong
                ? new BoundedLine(null, false, true)
                : new BoundedLine(value.ToString(), tooLong, false);
        }

        private static async Task ObserveWithoutThrowingAsync(Task task)
        {
            try
            {
                await task.ConfigureAwait(false);
            }
            catch (Exception)
            {
            }
        }

        private readonly record struct BoundedLine(string? Value, bool WasTooLong, bool EndOfStream);

        private sealed class RpcResponse
        {
            public JsonElement Result { get; init; }
            public string? ErrorCode { get; init; }
            public string? ErrorMessage { get; init; }
            public int? JsonRpcErrorCode { get; init; }
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

        internal PluginCallResult(string code, string message, int? jsonRpcErrorCode = null)
        {
            ErrorCode = code;
            ErrorMessage = message;
            JsonRpcErrorCode = jsonRpcErrorCode;
        }

        public bool IsSuccess => ErrorCode is null;
        public T? Value { get; private init; }
        public string? ErrorCode { get; private init; }
        public string? ErrorMessage { get; private init; }
        public int? JsonRpcErrorCode { get; private init; }
    }
}
