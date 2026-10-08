# rgb-playground

This repo contains existing RGB experiments and the plan for a plugin-based RGB controller.

## Planned RGB controller

The desired Windows app selects profiles from the focused application and controls RGB
through plugins. Its final goal is to replace SignalRGB with its own effects while retaining
integrations with other apps. **The controller is not implemented yet.**

- [App brief](docs/app-brief.md): requirements and desired behavior.
- [Plugin architecture](docs/plugin-architecture.md): detection, integrations, output, and ownership.
- [Delivery plan](docs/focus-profiles-plan.md): milestones and acceptance checks.
- [Integration evidence](docs/integration-evidence.md): historical results and unresolved support.
- [AI handoff](.ai/README.md): start here to continue with any AI model.
- [Agent instructions](.ai/AGENTS.md): shared working rules for coding assistants.
- [Claude instructions](.ai/CLAUDE.md): points Claude to the shared handoff.

Existing projects:

| Project | Location | What it is |
|---------|----------|------------|
| **SignalRGB development** | `effects/` `plugins/` `tools/` `docs/` | Effects (lightscripts), game/app integrations, and device plugins for SignalRGB |
| **Arduino WS2812B tester** | `arduino/LEDStripTester/` | Standalone Arduino Uno sketch that drives a WS2812B strip through several patterns |

The two are independent — nothing in `arduino/` is needed to work on SignalRGB content, or vice versa.

---

# SignalRGB development

Workspace for writing SignalRGB **effects** (lightscripts), **game/app integrations**, and
**device plugins**. SignalRGB loads user content from your Documents folder; this repo is the
source of truth and `tools/sync.ps1` copies it there.

## Layout

```
effects/    HTML5 + JS lightscripts (one .html per effect, optional .png preview, same basename)
plugins/    JS device plugins (one .js per USB device, matched by VendorId/ProductId)
tools/      sync.ps1 (install into SignalRGB), send-event.ps1 / send-event.py (test integrations)
docs/       cheatsheet.md (API reference, paths, links)
```

## Where SignalRGB looks

| Content        | Path                                                   |
|----------------|--------------------------------------------------------|
| Custom effects | `%USERPROFILE%\Documents\WhirlwindFX\Effects\`         |
| User plugins   | `%USERPROFILE%\Documents\WhirlwindFX\Plugins\`         |
| Components     | `%USERPROFILE%\Documents\WhirlwindFX\Components\`      |
| Built-ins      | `%LOCALAPPDATA%\VortxEngine\app-<ver>\Signal-x64\`     |
| Logs           | `%LOCALAPPDATA%\VortxEngine\app-<ver>\Signal-x64\Logs\`|

If Documents is redirected to OneDrive the path is `...\OneDrive\Documents\WhirlwindFX\...`;
`sync.ps1` detects that automatically.

User plugins override built-in ones with the same VID/PID and survive SignalRGB updates.

## Workflow

1. Install SignalRGB. Available features depend on the effect, device, account, and interface;
   see [recorded limitations](docs/integration-evidence.md). Custom screen-reading effects
   on WLED were blocked on the tested free account.
2. Edit files in `effects/` or `plugins/`.
3. Run `tools\sync.ps1` (or `tools\sync.ps1 -Watch` to auto-copy on save).
4. New effect files need a SignalRGB restart to appear under Effects > Installed.
   Existing effects reload when you re-select them. Plugins reload on device reconnect
   or SignalRGB restart.
5. Open the effect in SignalRGB's Effect Editor to see its live canvas and console.

## Testing a game/app integration

Effects receive external events through the local Canvas API:

```
POST http://localhost:16034/canvas/event?sender=<app>&event=<event>
```

```powershell
tools\send-event.ps1 -Sender demo -Event hit
python tools\send-event.py demo hit
```

`effects/api-event-demo.html` reacts to `hit`, `heal`, `low`, `idle` from sender `demo`.

## Devices in this setup

- **Razer BlackWidow V4** (wired, VID `0x1532` PID `0x0287`) and **Razer Basilisk V3**
  (wired, VID `0x1532` PID `0x0099`) are both driven by SignalRGB's built-in Razer plugin.
  Windows Dynamic Lighting is **disabled** on this machine (`AmbientLightingEnabled = 0`) and
  the Razer Dynamic Lighting package has been removed, so the LampArray path is not in use.
- **Razer Synapse 4** is installed *without* the Chroma module — it provides macros and
  special-key mappings only, no lighting. SignalRGB terminates `RazerAppEngine` during its own
  startup, so Synapse is launched 90 s after logon by the scheduled task
  `Synapse after SignalRGB` rather than from `HKCU\...\Run`. Both then coexist.
- **WLED Wall** `10.0.0.37` — 300 LEDs (5 m @ 60/m, WS2815 12 V) on a single bus, GPIO 16,
  RGB colour order, 12 mA/LED, 2700 mA limiter against a 12 V 3 A supply. Realtime receive on,
  DDP port 4048, realtime timeout 2500 ms, boot preset 1 (warm white `[255,180,107]` @ 30 %).
  SignalRGB discovers it over the network — no plugin needed, just Link it under Devices.
- **WLED Desk** `10.0.0.36` — recorded working in the October 7 snapshot, with 215 LEDs
  on IO16 and 85 on IO2. Live availability and settings must be checked again.

These setup notes are historical. The user's later preferred Desk preset is **Blends**;
see [integration evidence](docs/integration-evidence.md) before restoring older presets.

> WS2815 strips are not colour-corrected: `[255,255,255]` renders visibly blue. Use reduced
> blue/green values (e.g. `[255,180,107]`) for a neutral or warm white.

## Docs

- Developer hub: https://docs.signalrgb.com/developer/
- Lightscripts: https://docs.signalrgb.com/developer/lightscripts/
- Game/app integrations: https://docs.signalrgb.com/developer/lightscripts/creating-dev-integrations/
- Plugin exports: https://docs.signalrgb.com/developer/plugins/plugin-exports/
- Plugin tutorial (USB capture -> plugin): https://docs.signalrgb.com/developer/plugins/tutorial/
- Community references: https://github.com/SRGBmods/public , https://github.com/Derek4aty1/signalrgb-extras

---

# Arduino WS2812B tester

`arduino/LEDStripTester/LEDStripTester.ino` — drives a WS2812B addressable strip from an
Arduino Uno through several demonstration patterns, showing different ways to manipulate
the LEDs.

| Setting | Value | Constant |
|---------|-------|----------|
| Data pin | 5 | `LEDSPIN` |
| LED count | 32 | `LEDSIZE` |
| Refresh rate | 20 | `REFRESHRATE` |

Requires the `WS2812BStrip` library (`#include <WS2812BStrip.h>`, namespace `WS2812B`).
Adjust the constants at the top of the sketch to match your strip before flashing.

Build output goes to `build/` and is **not** tracked — regenerate it with the Arduino IDE or
`arduino-cli` rather than committing binaries.

> This is unrelated to the WLED controllers above. WLED runs on ESP32 boards and is configured
> over its HTTP JSON API; this sketch is bare-metal AVR with the strip wired directly to the Uno.
