# FocusRGB source

Application source for FocusRGB, created by slice 01 in `../docs/implementation-slices.md`.
The solution file is `../FocusRGB.slnx`.

| Project | Purpose | May reference |
|---|---|---|
| `FocusRgb.Contracts/` | Provider-neutral plugin messages, capabilities, results, and resource IDs | Nothing project-specific |
| `FocusRgb.PluginSdk/` (slice 10) | Plugin-side protocol/runtime helpers published as a versioned package | Contracts |
| `FocusRgb.Core/` | Profiles, selection, persistence, commands, fallback, and ownership | Contracts |
| `FocusRgb.Platform.Windows/` | Windows focus/process detection, later screen capture (`net10.0-windows`) | Contracts, Core |
| `FocusRgb.App/` | Avalonia tray/settings UI and plugin host wiring; builds `FocusRGB.exe` (`net10.0-windows`) | Contracts, Core, Platform |

Contracts, PluginSdk, and Core target portable .NET 10 without Windows APIs. No project here
references an integration. Production plugins live in independent
`FocusRGB-Plugin-<Name>` repositories and consume the published SDK; the main repository keeps
only its fake conformance fixture under tests. Build output goes to the ignored
`../build/artifacts/` directory (set by `../Directory.Build.props`).
`../tests/FocusRgb.Core.Tests/DependencyBoundaryTests.cs` fails the build's tests if Contracts
or Core gain a platform target, a package, or a forbidden project reference.
