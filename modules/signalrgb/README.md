# SignalRGB module

Everything specific to the interim SignalRGB backend lives in this directory. The future
controller accesses it only through the provider-neutral integration interface and loads the
SignalRGB implementation as a separate plugin. SignalRGB code must not be compiled into or
referenced by the core or Avalonia app projects. Removing this plugin must not require
rewriting detection, persistence, rules, layouts, or other application integrations.

The controller plugin must expose SignalRGB's available effects and layouts through the shared
catalog contract where supported and apply the user's effect with provider rendering authority.
An explicit layout is optional: omission retains SignalRGB's existing LED placements. While
active, SignalRGB's effect/layout wins and the controller must not run its native renderer on
the same owned devices. Catalog discovery and application success must be reported honestly;
do not invent resources or treat a submitted URL as proof of visible output.

SignalRGB already outputs to WLED. Its authority supersedes app rendering, placements, routing,
and calibration on managed devices; a separate WLED plugin is used after release for native
tests. Optional layout export supplies placement data only and does not grant device ownership.

## Layout

```text
effects/    active custom lightscripts; currently empty
plugins/    active SignalRGB user device plugins; currently empty
templates/  starters that are not installed automatically
examples/   integration examples that are not installed automatically
archive/    retained experiments that are not part of the live setup
tools/      SignalRGB install, Canvas API, and MCP launch utilities
docs/       SignalRGB reference and security research
```

Only files deliberately placed in `effects/` or `plugins/` are installed into SignalRGB.
The module is an organizational boundary today; the controller runtime and module loader
have not been implemented.

## Where SignalRGB looks

| Content | Path |
|---|---|
| Custom effects | `%USERPROFILE%\Documents\WhirlwindFX\Effects\` |
| User plugins | `%USERPROFILE%\Documents\WhirlwindFX\Plugins\` |
| Components | `%USERPROFILE%\Documents\WhirlwindFX\Components\` |
| Built-ins | `%LOCALAPPDATA%\VortxEngine\app-<ver>\Signal-x64\` |
| Logs | `%LOCALAPPDATA%\VortxEngine\app-<ver>\Signal-x64\Logs\` |

The installer detects redirected Documents folders such as OneDrive. User plugins override
built-in plugins with the same VID/PID and survive SignalRGB updates.

## Content workflow

1. Check the [recorded limitations](../../docs/integration-evidence.md). Available features
   depend on the effect, device, account, and interface.
2. Copy a starter from `templates/` or `examples/` into `effects/` or `plugins/` only when it
   should become active SignalRGB user content.
3. From the repository root, run:

   ```powershell
   modules\signalrgb\tools\install-signalrgb-content.ps1
   ```

   Add `-Watch`, `-Effects`, or `-Plugins` when needed.
4. Restart SignalRGB for a new effect file. Existing effects reload when reselected; plugins
   reload after device reconnect or SignalRGB restart.

## Canvas API example

Effects receive external events at:

```text
POST http://localhost:16034/canvas/event?sender=<app>&event=<event>
```

```powershell
modules\signalrgb\tools\send-signalrgb-canvas-event.ps1 -Sender demo -Event hit
python modules\signalrgb\tools\send-signalrgb-canvas-event.py demo hit
```

[`examples/signalrgb-canvas-event-demo.html`](examples/signalrgb-canvas-event-demo.html)
handles `hit`, `heal`, `low`, and `idle`. Copy it into `effects/`, install it, and select it
manually before sending events.

## MCP launcher

`tools/signalrgb-mcp.cmd` resolves the newest installed SignalRGB version instead of pinning
an `app-*` directory. The workspace-level `E:\AI Playground\.mcp.json` points to it; that
discovery file must remain at the workspace root for the MCP client.

## Local setup recorded on 2026-10-08

- Razer BlackWidow V4 (`0x1532:0x0287`) and Basilisk V3 (`0x1532:0x0099`) use SignalRGB's
  built-in Razer plugin. Windows Dynamic Lighting was disabled.
- Synapse 4 was installed without Chroma and started by the delayed `Synapse after SignalRGB`
  task so its macros and special keys could coexist with SignalRGB.
- WLED Desk at `10.0.0.36` reported WLED 16.0.1 and 300 LEDs split 215/85. WLED Wall at
  `10.0.0.37` did not respond. Treat these as dated observations.

SignalRGB discovers WLED over the network; no user device plugin is required. The WS2815
strips were not colour-corrected, so full RGB white appeared blue.

## References

- [Developer cheat sheet](docs/developer-cheatsheet.md)
- [Security findings](docs/security-findings.md) and [security report](docs/security-report.md)
- [SignalRGB developer hub](https://docs.signalrgb.com/developer/)
- [Lightscripts](https://docs.signalrgb.com/developer/lightscripts/)
- [Game/app integrations](https://docs.signalrgb.com/developer/lightscripts/creating-dev-integrations/)
- [Plugin exports](https://docs.signalrgb.com/developer/plugins/plugin-exports/)
- [Plugin tutorial](https://docs.signalrgb.com/developer/plugins/tutorial/)
