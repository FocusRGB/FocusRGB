using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using FocusRgb.Contracts;

namespace FocusRgb.App.Plugins
{
    /// <summary>Owns one plugin process and correlates bounded JSON-RPC requests over standard IO.</summary>
    public sealed class PluginProcessClient : IAsyncDisposable
    {
        private const int MaxLineLength = 1024 * 1024;
        private const int MaxBufferedLogs = 500;
        private const int HealthStarting = 0;
        private const int HealthHealthy = 1;
        private const int HealthUnhealthy = 2;
        private const int HealthWriteFailed = 3;
        private const int HealthExited = 4;
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
        private IntPtr _job;
        private EventHandler<string>? _logReceived;
        private int _droppedLogs;
        private long _nextId;
        private int _statusChangePending;
        private int _health;
        private int _writeFailed;
        private int _disposeStarted;
        private bool _disposed;
        private readonly TaskCompletionSource _disposeCompleted = new(TaskCreationOptions.RunContinuationsAsynchronously);

        private PluginProcessClient(Process process)
        {
            _process = process;
            _job = CreateKillOnCloseJob(process);
            _input = process.StandardInput;
            _input.AutoFlush = true;
            _reader = ReadResponsesAsync();
            _errorReader = ReadErrorsAsync();
            _exitObserver = ObserveExitAsync();
        }

        public string Health => Volatile.Read(ref _health) switch
        {
            HealthStarting => "starting",
            HealthHealthy => "healthy",
            HealthExited => "exited",
            _ => "unhealthy"
        };
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
                    if (_droppedLogs > 0)
                    {
                        buffered.Insert(0, $"Dropped {_droppedLogs} earlier plugin log lines.");
                        _droppedLogs = 0;
                    }
                    foreach (var message in buffered) value?.Invoke(this, message);
                }
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
                    || handshake.ContractVersion != manifest.ContractVersion
                    || !PluginPackage.IsContractCompatible(
                        handshake.ContractVersion, ContractVersion.Major, ContractVersion.Minor))
                {
                    await client.DisposeAsync().ConfigureAwait(false);
                    return PluginStartResult.Fail("contract-incompatible", "The plugin handshake does not match its manifest.");
                }

                client.PluginId = handshake.PluginId;
                client.SetRecoverableHealth(HealthHealthy);
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
            if (Interlocked.Exchange(ref _disposeStarted, 1) != 0)
            {
                await _disposeCompleted.Task.ConfigureAwait(false);
                return;
            }
            try
            {
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
                try
                {
                    _input.Dispose();
                }
                catch (IOException)
                {
                }
                _process.Dispose();
                CloseJob();
                _writeGate.Dispose();
                _lifetime.Dispose();
            }
            finally
            {
                _disposeCompleted.TrySetResult();
            }
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
            var requestWritten = false;

            try
            {
                await WriteAsync(new { jsonrpc = "2.0", id, method, @params = parameters }, cancellationToken)
                    .ConfigureAwait(false);
                requestWritten = true;
                RpcResponse response;
                try
                {
                    response = await completion.Task.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
                }
                catch (TimeoutException)
                {
                    await CancelRequestAsync(id).ConfigureAwait(false);
                    SetRecoverableHealth(HealthUnhealthy);
                    RaiseStatusChanged();
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
                if (requestWritten) await CancelRequestAsync(id).ConfigureAwait(false);
                throw;
            }
            catch (TimeoutException)
            {
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
                throw new TimeoutException("Writing to the plugin timed out.");
            }
            try
            {
                if (Volatile.Read(ref _writeFailed) != 0) throw new TimeoutException(WriteFailureMessage);
                var write = _input.WriteLineAsync(json.AsMemory(), _lifetime.Token);
                try
                {
                    await write.WaitAsync(DefaultTimeout, _lifetime.Token).ConfigureAwait(false);
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
                        MarkUnhealthy();
                        FailOnlyPendingMalformed("The plugin response exceeded the 1 MiB limit.");
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
                MarkUnhealthy();
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
                PublishLog($"Ignored non-JSON plugin stdout: {line}");
                return;
            }

            using (document)
            {
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    MarkUnhealthy();
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
                    MarkUnhealthy();
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
            MarkUnhealthy();
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
                        PublishLog("Plugin stderr line exceeded the 1 MiB limit and was truncated.");
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
                    if (_bufferedLogs.Count == MaxBufferedLogs)
                    {
                        _bufferedLogs.Dequeue();
                        _droppedLogs++;
                    }
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
                Interlocked.Exchange(ref _health, HealthExited);
                CloseJob();
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
            SetWriteFailedHealth();
            FailPending("timeout", string.IsNullOrWhiteSpace(message) ? WriteFailureMessage : message);
            RaiseStatusChanged();
        }

        private void SetRecoverableHealth(int health)
        {
            while (true)
            {
                var current = Volatile.Read(ref _health);
                if (current >= HealthWriteFailed) return;
                if (Interlocked.CompareExchange(ref _health, health, current) == current) return;
            }
        }

        private void MarkUnhealthy()
        {
            var wasHealthy = Health == "healthy";
            SetRecoverableHealth(HealthUnhealthy);
            if (wasHealthy && Health == "unhealthy") RaiseStatusChanged();
        }

        private void SetWriteFailedHealth()
        {
            while (true)
            {
                var current = Volatile.Read(ref _health);
                if (current == HealthExited || current == HealthWriteFailed) return;
                if (Interlocked.CompareExchange(ref _health, HealthWriteFailed, current) == current) return;
            }
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

        private static IntPtr CreateKillOnCloseJob(Process process)
        {
            if (!OperatingSystem.IsWindows()) return IntPtr.Zero;
            var job = CreateJobObject(IntPtr.Zero, null);
            if (job == IntPtr.Zero) return IntPtr.Zero;
            var information = new JobObjectExtendedLimitInformation
            {
                BasicLimitInformation = new JobObjectBasicLimitInformation { LimitFlags = 0x2000 }
            };
            var size = Marshal.SizeOf<JobObjectExtendedLimitInformation>();
            var pointer = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(information, pointer, false);
                if (SetInformationJobObject(job, 9, pointer, (uint)size)
                    && AssignProcessToJobObject(job, process.Handle))
                    return job;
            }
            finally
            {
                Marshal.FreeHGlobal(pointer);
            }
            CloseHandle(job);
            return IntPtr.Zero;
        }

        private void CloseJob()
        {
            var job = Interlocked.Exchange(ref _job, IntPtr.Zero);
            if (job != IntPtr.Zero) CloseHandle(job);
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateJobObject(IntPtr securityAttributes, string? name);

        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetInformationJobObject(IntPtr job, int informationClass, IntPtr information, uint length);

        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr handle);

        [StructLayout(LayoutKind.Sequential)]
        private struct JobObjectBasicLimitInformation
        {
            public long PerProcessUserTimeLimit;
            public long PerJobUserTimeLimit;
            public uint LimitFlags;
            public nuint MinimumWorkingSetSize;
            public nuint MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public nuint Affinity;
            public uint PriorityClass;
            public uint SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct IoCounters
        {
            public ulong ReadOperationCount;
            public ulong WriteOperationCount;
            public ulong OtherOperationCount;
            public ulong ReadTransferCount;
            public ulong WriteTransferCount;
            public ulong OtherTransferCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct JobObjectExtendedLimitInformation
        {
            public JobObjectBasicLimitInformation BasicLimitInformation;
            public IoCounters IoInfo;
            public nuint ProcessMemoryLimit;
            public nuint JobMemoryLimit;
            public nuint PeakProcessMemoryUsed;
            public nuint PeakJobMemoryUsed;
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
