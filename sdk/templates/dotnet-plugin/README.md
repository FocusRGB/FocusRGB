# FocusRgb.Plugin.Template

Standalone FocusRGB 1.0 process-plugin starter. It depends only on the public
`FocusRgb.PluginSdk` package and does not require an app source checkout.

The 1.0 package is not on nuget.org until the protected SDK publication workflow completes;
restore will fail before that release is published.

## Create a plugin repository

```powershell
dotnet new install path/to/dotnet-plugin
dotnet new focusrgb-plugin --name FocusRgb.Plugin.MyDevice --PluginId my-device
cd FocusRgb.Plugin.MyDevice
dotnet build -c Release
```

Use a lowercase `PluginId` containing only letters, digits, and hyphens. Replace the example
catalog and binding behavior, narrow the manifest's platforms/resources, set its `source`, and
add provider tests before publishing. Protocol messages belong on standard output; use standard
error for logs.

Run the language-neutral conformance check before packaging:

```powershell
python path/to/plugin-conformance.py --plugin-id my-device -- dotnet bin/Release/net10.0/FocusRgb.Plugin.MyDevice.dll
```

See the [FocusRGB plugin protocol](https://github.com/FocusRGB/FocusRGB/blob/sdk-dotnet-v1.0.0/sdk/spec/plugin-protocol.md).
