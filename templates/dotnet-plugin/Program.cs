using System.Text;
using FocusRgb.PluginSdk;
using FocusRgb.Plugin.Template;

Console.InputEncoding = new UTF8Encoding(false);
Console.OutputEncoding = new UTF8Encoding(false);

await PluginServer.RunAsync(
    new ExamplePlugin(),
    Console.In,
    Console.Out,
    Console.Error);
