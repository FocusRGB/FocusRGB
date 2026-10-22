using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FocusRgb.LocalMcp;

namespace FocusRgb.App.Tests
{
    [Collection(nameof(LocalMcpTestGroup))]
    public sealed class LocalMcpTests : IDisposable
    {
        private readonly string _directory = Path.Combine(
            Path.GetTempPath(), "FocusRgb.Tests", Guid.NewGuid().ToString("N"));

        [Fact]
        public async Task Configuration_MissingAndInvalidFilesFailClosed()
        {
            var store = new McpConfigurationStore(_directory);

            var missing = await store.LoadAsync(TestContext.Current.CancellationToken);
            Directory.CreateDirectory(_directory);
            await File.WriteAllTextAsync(
                Path.Combine(_directory, McpConfigurationStore.FileName),
                "{\"schemaVersion\":1,\"enabled\":true}",
                TestContext.Current.CancellationToken);
            var invalid = await store.LoadAsync(TestContext.Current.CancellationToken);

            Assert.True(missing.IsSuccess);
            Assert.False(missing.Configuration!.Enabled);
            Assert.False(invalid.IsSuccess);
        }

        [Fact]
        public async Task Configuration_EnableCreatesCredentialAndDisablePersistsOff()
        {
            var store = new McpConfigurationStore(_directory);

            var enabled = await store.SetEnabledAsync(
                true, Environment.ProcessPath, TestContext.Current.CancellationToken);
            var disabled = await store.SetEnabledAsync(
                false, cancellationToken: TestContext.Current.CancellationToken);
            var reloaded = await store.LoadAsync(TestContext.Current.CancellationToken);

            Assert.True(enabled.IsSuccess);
            Assert.Equal(32, Convert.FromBase64String(enabled.Configuration!.Credential!).Length);
            Assert.True(disabled.IsSuccess);
            Assert.False(reloaded.Configuration!.Enabled);
            Assert.Equal(enabled.Configuration.Credential, reloaded.Configuration.Credential);
        }

        [Fact]
        public async Task Pipe_AuthenticatesAndDispatchesAReadQuery()
        {
            var credential = Credential();
            await using var server = new LocalMcpPipeServer(
                credential,
                (method, _) => Task.FromResult(JsonSerializer.SerializeToElement(new { method })));
            _ = server.RunAsync();
            var client = new LocalMcpPipeClient(credential, Environment.ProcessPath!);

            var result = await client.QueryAsync("getStatus", TestContext.Current.CancellationToken);

            Assert.Equal("getStatus", result.GetProperty("method").GetString());
        }

        [Fact]
        public async Task Pipe_RejectsWrongCredentialBeforeDispatch()
        {
            var dispatched = false;
            await using var server = new LocalMcpPipeServer(
                Credential(),
                (_, _) =>
                {
                    dispatched = true;
                    return Task.FromResult(JsonSerializer.SerializeToElement(true));
                });
            _ = server.RunAsync();
            var client = new LocalMcpPipeClient(Credential(), Environment.ProcessPath!);

            var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                client.QueryAsync("getStatus", TestContext.Current.CancellationToken));

            Assert.Contains("unauthorized", error.Message, StringComparison.Ordinal);
            Assert.False(dispatched);
        }

        [Fact]
        public async Task Pipe_RejectsMalformedRequestAndUnexpectedServerPath()
        {
            var credential = Credential();
            await using var server = new LocalMcpPipeServer(
                credential,
                (_, _) => Task.FromResult(JsonSerializer.SerializeToElement(true)));
            _ = server.RunAsync();
            using (var pipe = new NamedPipeClientStream(
                ".", LocalMcpProtocol.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous))
            {
                await pipe.ConnectAsync(TestContext.Current.CancellationToken);
                using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
                using var reader = new StreamReader(pipe, new UTF8Encoding(false), leaveOpen: true);
                await writer.WriteLineAsync("not-json");
                var response = JsonSerializer.Deserialize<LocalMcpResponse>(
                    (await reader.ReadLineAsync(TestContext.Current.CancellationToken))!);
                Assert.Equal("invalid-request", response!.Error!.Code);
            }

            var wrongPath = Path.Combine(Path.GetTempPath(), "not-focusrgb.exe");
            var client = new LocalMcpPipeClient(credential, wrongPath);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                client.QueryAsync("getStatus", TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task Pipe_AllowsOnlyOneListenerAndStopsCleanly()
        {
            await using var first = new LocalMcpPipeServer(
                Credential(),
                (_, _) => Task.FromResult(JsonSerializer.SerializeToElement(true)));
            _ = first.RunAsync();

            Assert.Throws<IOException>(() => new LocalMcpPipeServer(
                Credential(),
                (_, _) => Task.FromResult(JsonSerializer.SerializeToElement(true))));
        }

        [Fact]
        public async Task Pipe_HonorsCancellationWhenTheAppIsNotListening()
        {
            using var cancellation = new CancellationTokenSource();
            await cancellation.CancelAsync();
            var client = new LocalMcpPipeClient(Credential(), Environment.ProcessPath!);

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                client.QueryAsync("getStatus", cancellation.Token));
        }

        [Fact]
        public async Task Pipe_ReportsWhenTheAppIsNotListening()
        {
            var client = new LocalMcpPipeClient(Credential(), Environment.ProcessPath!);

            var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                client.QueryAsync("getStatus", TestContext.Current.CancellationToken));

            Assert.Contains("not running", error.Message, StringComparison.OrdinalIgnoreCase);
        }

        private static string Credential() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

        public void Dispose()
        {
            if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
        }
    }

    [CollectionDefinition(nameof(LocalMcpTestGroup), DisableParallelization = true)]
    public sealed class LocalMcpTestGroup;
}
