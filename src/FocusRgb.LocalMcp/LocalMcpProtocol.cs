using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace FocusRgb.LocalMcp
{
    /// <summary>Defines the private app-to-adapter protocol; MCP itself remains on adapter stdio.</summary>
    public static class LocalMcpProtocol
    {
        public const int Version = 1;
        public const int MaximumMessageBytes = 64 * 1024;
        internal static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(1);
        internal static readonly TimeSpan WriteTimeout = TimeSpan.FromSeconds(2);

        public static string PipeName
        {
            get
            {
                var user = WindowsIdentity.GetCurrent().User?.Value
                    ?? throw new InvalidOperationException("The current Windows user SID is unavailable.");
                return $"FocusRGB.Mcp.{user}";
            }
        }
    }

    public sealed class LocalMcpRequest
    {
        public int ProtocolVersion { get; init; }
        public string? Id { get; init; }
        public string? Credential { get; init; }
        public string? ClientName { get; init; }
        public string? Method { get; init; }
        public JsonElement? Parameters { get; init; }
    }

    public sealed class LocalMcpResponse
    {
        public string? Id { get; init; }
        public JsonElement? Result { get; init; }
        public LocalMcpError? Error { get; init; }
    }

    public sealed class LocalMcpError
    {
        public required string Code { get; init; }
        public required string Message { get; init; }
    }

    public sealed class LocalMcpMethodNotFoundException(string message) : ArgumentException(message);

    /// <summary>Hosts one authenticated, current-user-only named-pipe listener owned by the app.</summary>
    public sealed class LocalMcpPipeServer : IAsyncDisposable
    {
        private const int MaximumServerInstances = 4;
        private readonly byte[] _credential;
        private readonly Func<LocalMcpRequest, CancellationToken, Task<JsonElement>> _dispatch;
        private readonly CancellationTokenSource _stopping = new();
        private readonly SemaphoreSlim _connectionSlots = new(MaximumServerInstances, MaximumServerInstances);
        private readonly PipeSecurity _security;
        private readonly NamedPipeServerStream _firstPipe;
        private readonly object _pipeGate = new();
        private NamedPipeServerStream? _acceptingPipe;
        private Task? _runTask;

        public LocalMcpPipeServer(
            string credential,
            Func<LocalMcpRequest, CancellationToken, Task<JsonElement>> dispatch)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(credential);
            ArgumentNullException.ThrowIfNull(dispatch);
            _credential = Convert.FromBase64String(credential);
            _dispatch = dispatch;
            var user = WindowsIdentity.GetCurrent().User
                ?? throw new InvalidOperationException("The current Windows user SID is unavailable.");
            _security = new PipeSecurity();
            _security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            _security.AddAccessRule(new PipeAccessRule(user, PipeAccessRights.FullControl, AccessControlType.Allow));
            _firstPipe = CreatePipe(firstInstance: true);
        }

        public Task RunAsync()
        {
            if (_runTask is not null) throw new InvalidOperationException("The MCP listener is already running.");
            _runTask = RunCoreAsync(_stopping.Token);
            return _runTask;
        }

        public async ValueTask DisposeAsync()
        {
            await _stopping.CancelAsync().ConfigureAwait(false);
            lock (_pipeGate) _acceptingPipe?.Dispose();
            _firstPipe.Dispose();
            if (_runTask is not null)
            {
                try { await _runTask.ConfigureAwait(false); }
                catch (OperationCanceledException) { }
                catch (ObjectDisposedException) { }
                catch (IOException) { }
            }
            _stopping.Dispose();
            _connectionSlots.Dispose();
        }

        private async Task RunCoreAsync(CancellationToken cancellationToken)
        {
            var first = true;
            var connections = new List<Task>();
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    connections.RemoveAll(task => task.IsCompleted);
                    await _connectionSlots.WaitAsync(cancellationToken).ConfigureAwait(false);
                    NamedPipeServerStream? pipe = null;
                    try
                    {
                        pipe = first ? _firstPipe : CreatePipe(firstInstance: false);
                        first = false;
                        lock (_pipeGate) _acceptingPipe = pipe;
                        await pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        pipe?.Dispose();
                        _connectionSlots.Release();
                        break;
                    }
                    catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
                    {
                        _connectionSlots.Release();
                        break;
                    }
                    catch (IOException)
                    {
                        pipe?.Dispose();
                        _connectionSlots.Release();
                        if (cancellationToken.IsCancellationRequested) break;
                        continue;
                    }
                    finally
                    {
                        lock (_pipeGate)
                        {
                            if (ReferenceEquals(_acceptingPipe, pipe)) _acceptingPipe = null;
                        }
                    }
                    connections.Add(ProcessConnectionAsync(pipe, cancellationToken));
                }
            }
            finally
            {
                await Task.WhenAll(connections).ConfigureAwait(false);
            }
        }

        private async Task ProcessConnectionAsync(
            NamedPipeServerStream pipe,
            CancellationToken cancellationToken)
        {
            try
            {
                await HandleConnectionAsync(pipe, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (IOException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
            finally
            {
                try
                {
                    if (pipe.IsConnected) pipe.Disconnect();
                }
                catch (IOException)
                {
                }
                catch (InvalidOperationException)
                {
                }
                pipe.Dispose();
                _connectionSlots.Release();
            }
        }

        private async Task HandleConnectionAsync(
            NamedPipeServerStream pipe,
            CancellationToken cancellationToken)
        {
            using var reader = new StreamReader(pipe, new UTF8Encoding(false), false, 4096, leaveOpen: true);
            using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, leaveOpen: true)
            {
                AutoFlush = true
            };
            using var readTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            readTimeout.CancelAfter(LocalMcpProtocol.ReadTimeout);
            string? line;
            try
            {
                line = await ReadBoundedLineAsync(reader, readTimeout.Token).ConfigureAwait(false);
            }
            catch (InvalidDataException exception)
            {
                await WriteResponseAsync(writer, Error(null, "invalid-request", exception.Message), cancellationToken)
                    .ConfigureAwait(false);
                return;
            }
            if (line is null) return;
            using var dispatchCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var disconnect = CancelOnDisconnectAsync(reader, dispatchCancellation);
            var response = await HandleRequestAsync(line, dispatchCancellation.Token).ConfigureAwait(false);
            await dispatchCancellation.CancelAsync().ConfigureAwait(false);
            await disconnect.ConfigureAwait(false);
            await WriteResponseAsync(writer, response, cancellationToken).ConfigureAwait(false);
        }

        private static async Task WriteResponseAsync(
            StreamWriter writer, LocalMcpResponse response, CancellationToken cancellationToken)
        {
            using var writeTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            writeTimeout.CancelAfter(LocalMcpProtocol.WriteTimeout);
            // Cancel the I/O itself and await it before the caller disposes its writer.
            await writer.WriteLineAsync(JsonSerializer.Serialize(response).AsMemory(), writeTimeout.Token)
                .ConfigureAwait(false);
        }

        private static async Task CancelOnDisconnectAsync(
            StreamReader reader,
            CancellationTokenSource dispatchCancellation)
        {
            try
            {
                var buffer = new char[1];
                while (await reader.ReadAsync(buffer, dispatchCancellation.Token).ConfigureAwait(false) != 0)
                {
                }
                await dispatchCancellation.CancelAsync().ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (IOException)
            {
                await dispatchCancellation.CancelAsync().ConfigureAwait(false);
            }
        }

        internal PipeSecurity GetAccessControl() => _firstPipe.GetAccessControl();

        private NamedPipeServerStream CreatePipe(bool firstInstance) => NamedPipeServerStreamAcl.Create(
            LocalMcpProtocol.PipeName,
            PipeDirection.InOut,
            MaximumServerInstances,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | (firstInstance ? PipeOptions.FirstPipeInstance : PipeOptions.None),
            4096,
            4096,
            _security);

        private async Task<LocalMcpResponse> HandleRequestAsync(string json, CancellationToken cancellationToken)
        {
            LocalMcpRequest? request;
            try
            {
                request = JsonSerializer.Deserialize<LocalMcpRequest>(json);
            }
            catch (JsonException)
            {
                return Error(null, "invalid-request", "The local request is not valid JSON.");
            }
            if (request?.ProtocolVersion != LocalMcpProtocol.Version
                || string.IsNullOrWhiteSpace(request.Id)
                || string.IsNullOrWhiteSpace(request.Method))
                return Error(request?.Id, "invalid-request", "The local request shape or protocol version is invalid.");
            if (!Authenticate(request.Credential))
                return Error(request.Id, "unauthorized", "The local MCP credential was rejected.");
            try
            {
                return new LocalMcpResponse
                {
                    Id = request.Id,
                    Result = await _dispatch(request, cancellationToken).ConfigureAwait(false)
                };
            }
            catch (LocalMcpMethodNotFoundException exception)
            {
                return Error(request.Id, "method-not-found", exception.Message);
            }
            catch (ArgumentException exception)
            {
                return Error(request.Id, "invalid-request", exception.Message);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                return Error(request.Id, "internal-error", "FocusRGB could not complete the local MCP request.");
            }
        }

        private bool Authenticate(string? supplied)
        {
            byte[] bytes;
            try { bytes = Convert.FromBase64String(supplied ?? string.Empty); }
            catch (FormatException) { return false; }
            return bytes.Length == _credential.Length
                && CryptographicOperations.FixedTimeEquals(bytes, _credential);
        }

        private static async Task<string?> ReadBoundedLineAsync(
            StreamReader reader,
            CancellationToken cancellationToken)
        {
            var buffer = new char[1];
            var result = new StringBuilder();
            var byteCount = 0;
            while (await reader.ReadAsync(buffer, cancellationToken).ConfigureAwait(false) != 0)
            {
                if (buffer[0] == '\n') return result.ToString().TrimEnd('\r');
                result.Append(buffer[0]);
                byteCount += buffer[0] switch { <= '\u007f' => 1, <= '\u07ff' => 2, _ => 3 };
                if (byteCount > LocalMcpProtocol.MaximumMessageBytes)
                    throw new InvalidDataException("The local MCP request is too large.");
            }
            return result.Length == 0 ? null : result.ToString();
        }

        private static LocalMcpResponse Error(string? id, string code, string message) => new()
        {
            Id = id,
            Error = new LocalMcpError { Code = code, Message = message }
        };
    }

    /// <summary>Connects the stdio adapter to the authenticated app listener after verifying its process.</summary>
    public sealed partial class LocalMcpPipeClient
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
        private readonly McpConfigurationStore? _configurationStore;
        private readonly string? _credential;
        private readonly string? _expectedServerPath;

        public LocalMcpPipeClient(McpConfigurationStore configurationStore)
        {
            ArgumentNullException.ThrowIfNull(configurationStore);
            _configurationStore = configurationStore;
        }

        internal LocalMcpPipeClient(string credential, string expectedServerPath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(credential);
            ArgumentException.ThrowIfNullOrWhiteSpace(expectedServerPath);
            _credential = credential;
            _expectedServerPath = Path.GetFullPath(expectedServerPath);
        }

        public async Task<JsonElement> QueryAsync(string method, CancellationToken cancellationToken = default)
            => await RequestAsync(method, null, cancellationToken).ConfigureAwait(false);

        public async Task<JsonElement> RequestAsync(
            string method,
            object? parameters,
            CancellationToken cancellationToken = default)
            => await RequestAsync(method, parameters, "FocusRgb.Mcp", cancellationToken).ConfigureAwait(false);

        public async Task<JsonElement> RequestAsync(
            string method,
            object? parameters,
            string clientName,
            CancellationToken cancellationToken = default)
        {
            var (credential, expectedServerPath) = await GetConnectionAsync(cancellationToken).ConfigureAwait(false);
            using var pipe = new NamedPipeClientStream(
                ".", LocalMcpProtocol.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            try
            {
                await pipe.ConnectAsync(2000, cancellationToken).ConfigureAwait(false);
            }
            catch (TimeoutException exception)
            {
                throw new InvalidOperationException(
                    "FocusRGB is not running or local MCP is disabled.", exception);
            }
            VerifyServer(pipe, expectedServerPath);
            using var reader = new StreamReader(pipe, new UTF8Encoding(false), false, 4096, leaveOpen: true);
            using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, leaveOpen: true)
            {
                AutoFlush = true
            };
            var id = Guid.NewGuid().ToString("N");
            await writer.WriteLineAsync(JsonSerializer.Serialize(new LocalMcpRequest
            {
                ProtocolVersion = LocalMcpProtocol.Version,
                Id = id,
                Credential = credential,
                ClientName = clientName,
                Method = method,
                Parameters = parameters is null ? null : JsonSerializer.SerializeToElement(parameters, JsonOptions)
            })).ConfigureAwait(false);
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)
                ?? throw new IOException("FocusRGB closed the local MCP connection without a response.");
            var response = JsonSerializer.Deserialize<LocalMcpResponse>(line)
                ?? throw new InvalidDataException("FocusRGB returned an invalid local MCP response.");
            if (response.Id != id) throw new InvalidDataException("FocusRGB returned a mismatched response.");
            if (response.Error is not null)
                throw new InvalidOperationException($"{response.Error.Code}: {response.Error.Message}");
            return response.Result ?? throw new InvalidDataException("FocusRGB returned no result.");
        }

        private async Task<(string Credential, string ApplicationPath)> GetConnectionAsync(
            CancellationToken cancellationToken)
        {
            if (_configurationStore is null) return (_credential!, _expectedServerPath!);
            var loaded = await _configurationStore.LoadAsync(cancellationToken).ConfigureAwait(false);
            if (!loaded.IsSuccess)
                throw new InvalidOperationException(loaded.ErrorMessage);
            if (loaded.Configuration is not { Enabled: true, Credential: not null, ApplicationPath: not null } enabled)
                throw new InvalidOperationException("Local MCP is disabled in FocusRGB.");
            return (enabled.Credential, enabled.ApplicationPath);
        }

        private static void VerifyServer(NamedPipeClientStream pipe, string expectedPath)
        {
            if (!GetNamedPipeServerProcessId(pipe.SafePipeHandle, out var processId))
                throw new InvalidOperationException("Could not identify the FocusRGB pipe server process.");
            using var process = Process.GetProcessById(checked((int)processId));
            var actualPath = process.MainModule?.FileName;
            if (actualPath is null || !Path.GetFullPath(actualPath).Equals(expectedPath, StringComparison.OrdinalIgnoreCase))
                throw new UnauthorizedAccessException("The named-pipe server is not the expected FocusRGB executable.");
        }

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool GetNamedPipeServerProcessId(
            Microsoft.Win32.SafeHandles.SafePipeHandle pipe,
            out uint serverProcessId);
    }
}
