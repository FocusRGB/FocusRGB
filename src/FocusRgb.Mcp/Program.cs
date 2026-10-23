using FocusRgb.LocalMcp;
using FocusRgb.Mcp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

var dataDirectory = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "FocusRGB");
var builder = Host.CreateApplicationBuilder(args);
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
builder.Services.AddSingleton(new LocalMcpPipeClient(new McpConfigurationStore(dataDirectory)));
builder.Services.AddMcpServer()
    .WithStdioServerTransport()
    .WithTools<FocusRgbReadTools>();
await builder.Build().RunAsync();
