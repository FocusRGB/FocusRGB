using FocusRgb.LocalMcp;
using FocusRgb.Mcp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

var dataDirectory = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "FocusRGB");
var configuration = await new McpConfigurationStore(dataDirectory).LoadAsync();
if (!configuration.IsSuccess || configuration.Configuration is not { Enabled: true, Credential: not null } enabled)
{
    await Console.Error.WriteLineAsync(configuration.ErrorMessage ?? "Local MCP is disabled in FocusRGB.");
    return 1;
}

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
builder.Services.AddSingleton(new LocalMcpPipeClient(enabled.Credential, enabled.ApplicationPath!));
builder.Services.AddMcpServer()
    .WithStdioServerTransport()
    .WithTools<FocusRgbReadTools>();
await builder.Build().RunAsync();
return 0;
