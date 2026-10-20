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
<https://github.com/FocusRGB/FocusRGB/blob/sdk-dotnet-v1.0.0/sdk/spec/plugin-protocol.md>.
`PluginServer` automatically reports `FocusRgb.PluginSdk`, `dotnet`, and its package version in
the optional diagnostic SDK identity; plugin implementations do not set that field themselves.
