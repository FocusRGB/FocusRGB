# FocusRGB

<p align="center">
  <img src="assets/focusrgb-logo.svg" alt="FocusRGB logo" width="128">
  <br>
  <strong>Lighting that follows what you're doing.</strong>
  <br>
  An open-source, plugin-driven desktop RGB controller for Windows and beyond.
</p>

---

FocusRGB automatically switches lighting profiles based on the active application or game in focus. Built with an Avalonia UI and a portable .NET 10 core, FocusRGB coordinates hardware lighting across process-isolated plugins without vendor lock-in.

> 📖 **User Guides & Documentation:** For installation steps, plugin setup, and user guides, visit the **[FocusRGB Wiki](https://github.com/FocusRGB/FocusRGB/wiki)**:
> - [**Installing & Managing Plugins**](https://github.com/FocusRGB/FocusRGB/wiki/Installing-plugins): Official one-click discovery, updates, and custom packages.
> - [**SignalRGB Plugin Guide**](https://github.com/FocusRGB/FocusRGB/wiki/SignalRGB-plugin): Prerequisites, effect selection, and provider output status.

---

## Key Features

- **Context-Aware Lighting:** Seamlessly switches profiles using ordered priority rules—either while an application is running or only while it has focus.
- **Process-Isolated Plugins:** Output providers, game integrators, and discovery services run as independent processes communicating over versioned stdio JSON-RPC. If a plugin crashes, the host stays responsive and safe.
- **Single-Writer Safety:** Explicit rendering authority coordinates one writer per physical device, with fallback and pending status when output is unavailable.
- **Local MCP Integration:** Optional local Model Context Protocol server allows AI assistants and IDEs to query state and propose profile changes—with every mutation requiring explicit, single-use in-app approval.
- **Windows Tray Integration:** Supports optional minimized startup and Start with Windows, and persists the desired mode and profile across restarts.

---

## Repositories & Architecture

FocusRGB is organized as a modular ecosystem:

| Repository | Purpose | Status |
|---|---|---|
| [**FocusRGB**](https://github.com/FocusRGB/FocusRGB) | Desktop app (Avalonia), portable core host, and normative JSON-RPC protocol specification | Active (`develop`) |
| [**FocusRGB-SDK**](https://github.com/FocusRGB/FocusRGB-SDK) | Official plugin development SDKs: .NET (`FocusRgb.PluginSdk` on nuget.org), with Node and Python planned | v1.2.0 on NuGet |
| [**FocusRGB-Plugin-SignalRGB**](https://github.com/FocusRGB/FocusRGB-Plugin-SignalRGB) | Official output plugin rendering FocusRGB profiles through SignalRGB | Released v1.0.0; `develop` uses SDK 1.2 |
| [**FocusRGB-Plugin-Steam**](https://github.com/FocusRGB/FocusRGB-Plugin-Steam) | Official discovery plugin scanning installed Steam games and launch executables | SDK 1.2 |

---

## Developer Quickstart

### Prerequisites

- Windows 10/11 (for the desktop shell)
- [.NET 10 SDK](https://dotnet.microsoft.com/download) (pinned in `global.json`)

### Build and Test

```powershell
# Restore and build the solution
dotnet build FocusRGB.slnx -c Release --disable-build-servers

# Run the test suite
$env:MSBUILDDISABLENODEREUSE = '1'
dotnet test --solution FocusRGB.slnx -c Release --no-build
dotnet build-server shutdown

# Launch the desktop application
.\build\artifacts\bin\FocusRgb.App\release\FocusRGB.exe
```

*All generated build output is kept under the git-ignored `build/` directory.*

---

## Project Structure

```text
FocusRGB.slnx
sdk/
  spec/                        Normative JSON-RPC protocol & manifest schemas
  conformance/                 Language-neutral protocol test harness
  examples/python-plugin/      Standalone no-SDK plugin example
src/
  FocusRgb.Core/               Portable host logic: profiles, selection engine, persistence
  FocusRgb.Platform.Windows/   Windows-specific APIs: process & foreground observation
  FocusRgb.App/                Avalonia desktop application & system tray host
  FocusRgb.LocalMcp/           Authenticated local named-pipe transport
  FocusRgb.Mcp/                Stdio MCP server adapter (FocusRgb.Mcp.exe)
tests/
  FocusRgb.App.Tests/          App and view model unit tests
  FocusRgb.App.HeadlessTests/  Avalonia UI headless interaction tests
  FocusRgb.Core.Tests/         Core selection, persistence, and boundary tests
  FocusRgb.FakePlugin/         Protocol fixture for integration testing
```

---

## Protocols & Plugin Development

- **Writing a Plugin:** Plugins can be developed in any language using stdin/stdout JSON-RPC. Read the [Normative Plugin Protocol](sdk/spec/plugin-protocol.md) and examine the schemas under `sdk/spec/`.
- **.NET Authors:** Use [`FocusRgb.PluginSdk`](https://www.nuget.org/packages/FocusRgb.PluginSdk) on nuget.org to build plugins with strongly-typed message models and automated lifecycle management.
- **Generic Settings:** Plugins can provide a `focusrgb-settings.schema.json` beside their manifest to automatically gain a native configuration window in the host.

---

## Contributing & Roadmap

- **Roadmap:** Track delivery milestones on the [FocusRGB Project Board](https://github.com/orgs/FocusRGB/projects/1).
- **Archived Slices:** Browse historical specifications in the [Archive Index](https://github.com/FocusRGB/FocusRGB/issues/83).
- **Issues & Contributions:** Report bugs or suggest enhancements via [GitHub Issues](https://github.com/FocusRGB/FocusRGB/issues). All code changes arrive via pull requests into `develop`.

---

## License

FocusRGB is developed and maintained by **Devantix** under the [MIT License](LICENSE).

Assisted-by: OpenAI Codex
