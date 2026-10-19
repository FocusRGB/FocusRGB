# FocusRgb.PluginSdk

Optional .NET helpers for implementing a FocusRGB process plugin. Plugins in other languages can
implement the newline-delimited JSON-RPC protocol directly.

```csharp
await PluginServer.RunAsync(
    new MyPlugin(),
    Console.In,
    Console.Out,
    Console.Error);
```

Implement `IFocusRgbPlugin`, write protocol messages only through `PluginServer`, and keep logs on
standard error. The language-neutral protocol and schemas are documented at
<https://github.com/FocusRGB/FocusRGB/blob/develop/docs/plugin-protocol.md>.
