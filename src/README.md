# FocusRGB source

The UI calls automatic application/game rules **Focuses** and presents them in the **Focus Library**.
Internal rule/mapping identifiers and settings serialization remain unchanged.

Application source for FocusRGB. Delivery status is tracked on the
[project board](https://github.com/orgs/FocusRGB/projects/1), with archived plans indexed by
[issue #83](https://github.com/FocusRGB/FocusRGB/issues/83). The solution file is
`../FocusRGB.slnx`; the normative plugin contract is `../sdk/spec/plugin-protocol.md`.

| Project | Purpose | May reference |
|---|---|---|
| `FocusRgb.Core/` | Internal host protocol types, profiles, selection, persistence, commands, fallback, and ownership | Nothing project-specific |
| `FocusRgb.Platform.Windows/` | Windows focus/process detection, later screen capture (`net10.0-windows`) | Core |
| `FocusRgb.LocalMcp/` | Authenticated Windows named-pipe boundary shared by the app and MCP adapter | Nothing project-specific |
| `FocusRgb.Mcp/` | Official-SDK stdio MCP adapter; builds `FocusRgb.Mcp.exe` (`net10.0-windows`) | LocalMcp |
| `FocusRgb.App/` | Avalonia tray/settings UI and plugin host wiring; builds `FocusRGB.exe` (`net10.0-windows`) | Core, Platform, LocalMcp |

Core targets portable .NET 10 without Windows APIs and is not packable. No project here
references an integration or SDK source. Production plugins live in independent
`FocusRGB-Plugin-<Name>` repositories and consume packages from [`FocusRGB-SDK`](https://github.com/FocusRGB/FocusRGB-SDK); the main
repository keeps only its raw fake conformance fixture under tests. Build output goes to the ignored
`../build/artifacts/` directory (set by `../Directory.Build.props`).
`../tests/FocusRgb.Core.Tests/DependencyBoundaryTests.cs` fails the build's tests if Core gains
a platform target, a package, or a forbidden project reference.
