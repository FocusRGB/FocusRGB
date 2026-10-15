using System.Text;
using System.Text.Json;
using FocusRgb.Contracts;
using FocusRgb.PluginSdk;

namespace FocusRgb.App.Tests
{
    public sealed class PluginServerTests
    {
        [Theory]
        [InlineData("{\"jsonrpc\":2,\"id\":1,\"method\":\"status/get\",\"params\":{}}")]
        [InlineData("{\"jsonrpc\":\"2.0\",\"id\":\"a\",\"method\":\"status/get\",\"params\":{}}")]
        [InlineData("[]")]
        public async Task RunAsync_ReturnsInvalidRequestWithNullId_WhenFieldsHaveWrongTypes(string request)
        {
            using var input = new StringReader(request);
            using var output = new StringWriter();

            await PluginServer.RunAsync(
                new StubPlugin(), input, output, TextWriter.Null, TestContext.Current.CancellationToken);

            using var response = JsonDocument.Parse(output.ToString());
            Assert.Equal(JsonValueKind.Null, response.RootElement.GetProperty("id").ValueKind);
            Assert.Equal(-32600, response.RootElement.GetProperty("error").GetProperty("code").GetInt32());
        }

        [Fact]
        public async Task RunAsync_ReturnsParseErrorWithNullId_WhenJsonIsMalformed()
        {
            using var output = new StringWriter();

            await PluginServer.RunAsync(
                new StubPlugin(), new StringReader("{"), output, TextWriter.Null,
                TestContext.Current.CancellationToken);

            using var response = JsonDocument.Parse(output.ToString());
            Assert.Equal(JsonValueKind.Null, response.RootElement.GetProperty("id").ValueKind);
            Assert.Equal(-32700, response.RootElement.GetProperty("error").GetProperty("code").GetInt32());
        }

        [Fact]
        public async Task RunAsync_ReturnsCancelledError_WhenLiveRequestIsCancelled()
        {
            var responseStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var flushFinished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var input = new CancelRaceReader(Task.CompletedTask, flushFinished.Task);
            using var output = new CompletionRaceWriter(responseStarted, flushFinished);

            await PluginServer.RunAsync(
                new CancellablePlugin(), input, output, TextWriter.Null,
                TestContext.Current.CancellationToken);

            using var response = JsonDocument.Parse(Assert.Single(output.Lines));
            Assert.Equal(-32800, response.RootElement.GetProperty("error").GetProperty("code").GetInt32());
        }

        [Fact]
        public async Task RunAsync_WritesOneResponse_WhenCancellationRacesCompletedRequest()
        {
            var responseStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var flushFinished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var input = new CancelRaceReader(responseStarted.Task, flushFinished.Task);
            using var output = new CompletionRaceWriter(responseStarted, flushFinished);

            await PluginServer.RunAsync(
                new CompletingPlugin(), input, output, TextWriter.Null,
                TestContext.Current.CancellationToken);

            var line = Assert.Single(output.Lines);
            using var response = JsonDocument.Parse(line);
            Assert.True(response.RootElement.TryGetProperty("result", out _));
        }

        private class StubPlugin : IFocusRgbPlugin
        {
            public Task<InitializeResult> InitializeAsync(
                InitializeParameters parameters,
                CancellationToken cancellationToken) => throw new NotSupportedException();

            public virtual Task<PluginStatusResult> GetStatusAsync(CancellationToken cancellationToken) =>
                throw new NotSupportedException();

            public Task<PluginCatalogResult> ListCatalogAsync(
                CatalogListParameters parameters,
                CancellationToken cancellationToken) => throw new NotSupportedException();

            public Task<BindingApplyResult> ApplyBindingAsync(
                BindingApplyParameters parameters,
                CancellationToken cancellationToken) => throw new NotSupportedException();

            public Task<BindingReleaseResult> ReleaseBindingAsync(
                BindingReleaseParameters parameters,
                CancellationToken cancellationToken) => throw new NotSupportedException();

            public Task ShutdownAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        }

        private sealed class CancellablePlugin : StubPlugin
        {
            public override async Task<PluginStatusResult> GetStatusAsync(CancellationToken cancellationToken)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                throw new InvalidOperationException();
            }
        }

        private sealed class CompletingPlugin : StubPlugin
        {
            public override Task<PluginStatusResult> GetStatusAsync(CancellationToken cancellationToken) =>
                Task.FromResult(new PluginStatusResult { Availability = "available" });
        }

        private sealed class CancelRaceReader(Task cancelAfter, Task finishAfter) : TextReader
        {
            private int _line;

            public override async ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken)
            {
                switch (_line++)
                {
                    case 0:
                        return "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"status/get\",\"params\":{}}";
                    case 1:
                        await cancelAfter.WaitAsync(cancellationToken);
                        return "{\"jsonrpc\":\"2.0\",\"method\":\"$/cancel\",\"params\":{\"id\":1}}";
                    default:
                        await finishAfter.WaitAsync(cancellationToken);
                        return null;
                }
            }
        }

        private sealed class CompletionRaceWriter(
            TaskCompletionSource responseStarted,
            TaskCompletionSource flushFinished) : TextWriter
        {
            public override Encoding Encoding => Encoding.UTF8;
            public List<string> Lines { get; } = [];

            public override Task WriteLineAsync(
                ReadOnlyMemory<char> buffer,
                CancellationToken cancellationToken = default)
            {
                Lines.Add(buffer.ToString());
                responseStarted.TrySetResult();
                return Task.CompletedTask;
            }

            public override async Task FlushAsync(CancellationToken cancellationToken)
            {
                try
                {
                    await Task.Delay(50, cancellationToken);
                }
                finally
                {
                    flushFinished.TrySetResult();
                }
            }
        }
    }
}
