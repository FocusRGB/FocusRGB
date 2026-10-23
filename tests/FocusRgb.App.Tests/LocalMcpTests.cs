using System.IO.Pipes;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using FocusRgb.App.Mcp;
using FocusRgb.App.Plugins;
using FocusRgb.Core.Commands;
using FocusRgb.Core.Persistence;
using FocusRgb.LocalMcp;
using FocusRgb.Mcp;

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

            var missingPath = await store.SetEnabledAsync(
                true, cancellationToken: TestContext.Current.CancellationToken);
            var enabled = await store.SetEnabledAsync(
                true, Environment.ProcessPath, TestContext.Current.CancellationToken);
            var disabled = await store.SetEnabledAsync(
                false, cancellationToken: TestContext.Current.CancellationToken);
            var reloaded = await store.LoadAsync(TestContext.Current.CancellationToken);

            Assert.False(missingPath.IsSuccess);
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
        public async Task ReadTools_QueryApplicationOwnedStateAcrossEveryTool()
        {
            var stateStore = new LocalStateStore(_directory);
            var startup = await stateStore.InitializeAsync(
                new DateTimeOffset(2026, 10, 22, 12, 0, 0, TimeSpan.Zero),
                TestContext.Current.CancellationToken);
            var state = new ApplicationStateService(
                startup.Snapshot!.Settings,
                startup.Snapshot.State,
                startup.Snapshot.Desired.Reason,
                stateStore);
            var queries = new McpReadQueryService(state, () =>
            [
                new InstalledPluginInfo
                {
                    Id = "fake-output",
                    DisplayName = "Fake output",
                    Version = "1.0.0",
                    IsOfficial = true,
                    IsEnabled = true,
                    IsRunning = true
                }
            ]);
            var credential = Credential();
            await using var server = new LocalMcpPipeServer(credential, queries.QueryAsync);
            _ = server.RunAsync();
            var client = new LocalMcpPipeClient(credential, Environment.ProcessPath!);
            var tools = new FocusRgbReadTools(client);

            var profiles = await tools.ListProfilesAsync(TestContext.Current.CancellationToken);
            var groups = await tools.ListApplicationGroupsAsync(TestContext.Current.CancellationToken);
            var mappings = await tools.ListMappingsAsync(TestContext.Current.CancellationToken);
            var plugins = await tools.ListPluginsAsync(TestContext.Current.CancellationToken);
            var devices = await tools.ListDevicesAsync(TestContext.Current.CancellationToken);
            var status = await tools.GetStatusAsync(TestContext.Current.CancellationToken);
            var explanation = await tools.ExplainActiveSelectionAsync(TestContext.Current.CancellationToken);
            var unknown = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                client.QueryAsync("unknown", TestContext.Current.CancellationToken));

            Assert.NotEmpty(profiles.EnumerateArray());
            Assert.NotEmpty(groups.EnumerateArray());
            Assert.Equal(0, mappings.EnumerateArray().First().GetProperty("order").GetInt32());
            Assert.Equal("fake-output", plugins.EnumerateArray().Single().GetProperty("id").GetString());
            Assert.NotEmpty(devices.EnumerateArray());
            Assert.Equal("manual", status.GetProperty("selectionState").GetProperty("mode").GetString());
            Assert.Equal("default", explanation.GetProperty("desiredProfile").GetString());
            Assert.Contains("method-not-found", unknown.Message, StringComparison.Ordinal);
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
        public async Task Pipe_AclAllowsOnlyTheCurrentUser()
        {
            await using var server = new LocalMcpPipeServer(
                Credential(),
                (_, _) => Task.FromResult(JsonSerializer.SerializeToElement(true)));
            var currentUser = WindowsIdentity.GetCurrent().User!;

            var rules = server.GetAccessControl()
                .GetAccessRules(includeExplicit: true, includeInherited: false, typeof(SecurityIdentifier))
                .Cast<PipeAccessRule>()
                .ToList();

            Assert.NotEmpty(rules);
            Assert.All(rules, rule => Assert.Equal(currentUser, rule.IdentityReference));
        }

        [Fact]
        public async Task Pipe_SurvivesAbruptDisconnectAndReadTimeout()
        {
            var credential = Credential();
            await using var server = new LocalMcpPipeServer(
                credential,
                (method, _) => Task.FromResult(JsonSerializer.SerializeToElement(method)));
            _ = server.RunAsync();
            using (var disconnected = new NamedPipeClientStream(
                ".", LocalMcpProtocol.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous))
            {
                await disconnected.ConnectAsync(TestContext.Current.CancellationToken);
            }
            var client = new LocalMcpPipeClient(credential, Environment.ProcessPath!);
            Assert.Equal(
                "after-disconnect",
                (await client.QueryAsync("after-disconnect", TestContext.Current.CancellationToken)).GetString());

            using var stalled = new NamedPipeClientStream(
                ".", LocalMcpProtocol.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await stalled.ConnectAsync(TestContext.Current.CancellationToken);
            Assert.Equal(
                "after-timeout",
                (await client.QueryAsync("after-timeout", TestContext.Current.CancellationToken)).GetString());
        }

        [Fact]
        public async Task Client_ReloadsTheUiControlledConfigurationForEveryQuery()
        {
            var store = new McpConfigurationStore(_directory);
            var enabled = await store.SetEnabledAsync(
                true, Environment.ProcessPath, TestContext.Current.CancellationToken);
            await using var server = new LocalMcpPipeServer(
                enabled.Configuration!.Credential!,
                (_, _) => Task.FromResult(JsonSerializer.SerializeToElement(true)));
            _ = server.RunAsync();
            var client = new LocalMcpPipeClient(store);

            Assert.True((await client.QueryAsync(
                "getStatus", TestContext.Current.CancellationToken)).GetBoolean());
            await store.SetEnabledAsync(false, cancellationToken: TestContext.Current.CancellationToken);
            var disabled = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                client.QueryAsync("getStatus", TestContext.Current.CancellationToken));

            Assert.Contains("disabled", disabled.Message, StringComparison.OrdinalIgnoreCase);
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
