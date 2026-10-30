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

Use the **Focus** tab to select a profile manually or manage automatic **Focuses** in the **Focus Library**.
Each Focus links an application or game to a lighting profile. See the [user guides](https://github.com/FocusRGB/FocusRGB/wiki).

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

## Focus notifications

Automatic Focus notifications use an opaque, bordered box. Under **Settings → Appearance**, choose
Small/Medium/Large, a Dark/Light/Blue background, and a 2/3/5-second duration. Defaults are Medium,
Dark and 3 seconds. Save applies these preferences; Cancel restores saved values. Notifications
remain passive and click-through, and are suppressed for manual selection and startup.
Settings schema v8 adds `application.notification` and preserves existing debugging, logging and
switching-speed preferences, including last-known-good recovery.

## Local MCP and isolated instances

For an isolated desktop instance, run `FocusRGB.exe --data-dir "D:\FocusRGB-test"` (or set
`FOCUSRGB_DATA_DIR`). The command-line path takes precedence; otherwise the default remains
`%LOCALAPPDATA%\FocusRGB`. Settings, state, native setup, plugins and their data, caches, logs and
`mcp.json` all use the selected folder. The instance lock, show-window signal and authenticated MCP
pipe are scoped to the normalized folder and Windows user. Different folders can run side by side;
a second launch for the same folder reuses the existing instance. Use the same `--data-dir` or
environment variable with `FocusRgb.Mcp.exe` to connect to that instance. MCP remains off by default;
disposable test folders may be pre-seeded with their own `mcp.json` before launch. The adapter cannot
enable MCP or approve mutations. Do not copy a live instance's credentials into a test folder.

Enable **Settings → Local MCP → Enable local MCP tools** in the selected instance before connecting
the `FocusRgb.Mcp.exe` stdio adapter. Read tools require no approval; every mutation still requires
explicit, single-use approval in the app. MCP cannot enable itself or approve its own requests.

**Settings → Diagnostics → Debugging mode** is a separate switch, off by default. Save Settings to
apply it immediately without restarting. Only the five debugging tools below require it: with the
mode off they return `status: "disabled"`, their trace/start history and recent-log buffer are cleared
and stop recording, and **Enable basic diagnostic logging** is off and unavailable. The Local MCP
switch/listener, normal read tools and approval-gated control tools are unaffected. MCP cannot
change Debugging mode. With the mode on, file logging remains optional; its choice is kept while the
mode is off and applies again when the mode is turned back on. Settings schema v7 migrates
existing diagnostic logging on to Debugging mode on; otherwise it starts off, including recovery
through `settings.last-known-good.json`.
The existing reads are `listProfiles`, `listApplicationGroups`, `listMappings`, `listPlugins`,
`listDevices`, `getStatus` and `explainActiveSelection`. Diagnostic reads add:

| Tool | Read-only result |
|---|---|
| `getSelectionTrace(limit=20)` | Recent observed process/class, retained foreground, transient filtering, actual debounce state, matched rule/profile and reason. No window titles. |
| `getPluginRuntime(limit=20)` | Plugin origin, health, PID, start time, successful restarts observed this session, last error code, recovery approval, and the last queried official catalog's status/cache age. |
| `getRecentLogs(limit=20, level=null, source=null)` | Redacted in-memory host events, even with file logging off. Level/source filters are exact (level ignores case). Raw plugin log payloads are omitted. |
| `getUiState()` | Selected tab, dirty Settings/Focus Library/Profiles editors, visibility, dialog/toast names and latest status messages. |
| `getOutputState(limit=20)` | Current writer scopes, rendering authority, active profile/effect and last binding acknowledgement time. It never controls devices or claims physical output readback. |

Trace and log buffers each retain at most 200 entries per process; restart counters retain at most
128 plugin IDs. `limit` is clamped to 1–50;
responses are capped at 32 KiB, text at 256 characters (identifiers have smaller caps), dialogs and
toasts at 8 each, and UI status messages at 10. Truncation is reported; request fewer entries if the
response-size cap is reached. Paths/URLs, credential-bearing messages and opaque token-like strings
are conservatively redacted, so some resource identifiers may be omitted. No window titles, command
lines, raw settings, credentials, plugin configuration or log files are returned. Catalog age is
unknown until discovery is queried; this read does not trigger network discovery. Output timestamps
describe acknowledged binding calls, not RGB-frame or hardware observations; provider-managed scopes
do not enumerate physical devices. Diagnostic adapters return `status: "disabled"` with MCP or Debugging mode off and
`status: "unavailable"` when the instance cannot be reached, without exposing local paths or errors.
These diagnostics add no mutation or plugin-protocol surface.

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
