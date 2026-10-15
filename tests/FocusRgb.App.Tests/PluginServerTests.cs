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

        private sealed class StubPlugin : IFocusRgbPlugin
        {
            public Task<InitializeResult> InitializeAsync(
                InitializeParameters parameters,
                CancellationToken cancellationToken) => throw new NotSupportedException();

            public Task<PluginStatusResult> GetStatusAsync(CancellationToken cancellationToken) =>
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
    }
}
