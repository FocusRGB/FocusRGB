# rgb-playground

This repo contains existing RGB experiments and the plan for a cross-platform Avalonia/.NET
10 RGB controller. The background app will select profiles from focused or running
applications, support user-prioritized rules and manual selection, expose an open plugin SDK,
eventually replace SignalRGB with native effects, and retain integrations with other apps and
games. A local MCP adapter is planned for model-driven configuration through the same
validated commands as the UI.
**The controller is not implemented yet.**

Implementation proceeds in three product goals: a working portable core exercised through a
separate SignalRGB plugin; game profiles that combine an RGB effect with an LED layout; then
native effects, layouts, and outputs that remove the SignalRGB dependency. The first executable
targets Windows while the core remains portable.

## Start here

- [App brief](docs/app-brief.md): requirements and desired behavior.
- [Plugin architecture](docs/plugin-architecture.md): detection, integrations, output, modules, and ownership.
- [Delivery plan](docs/focus-profiles-plan.md): milestones and acceptance checks.
- [Integration evidence](docs/integration-evidence.md): historical results and unresolved support.
- [AI handoff](.ai/README.md): reading order for any AI model.
- [Agent instructions](.ai/AGENTS.md): shared working rules for coding assistants.
- [Claude instructions](.ai/CLAUDE.md): points Claude to the shared handoff.

## Projects

| Project | Location | What it is |
|---|---|---|
| SignalRGB module | [`modules/signalrgb/`](modules/signalrgb/README.md) | Interim backend, content, examples, tools, and research kept behind one module boundary |
| Screen colour test | [`tools/screen-color-test.html`](tools/screen-color-test.html) | Shared visual calibration aid for current and future RGB backends |
| Arduino WS2812B tester | `arduino/LEDStripTester/` | Standalone Arduino Uno strip test sketch |

Integration-specific files belong under `modules/<integration>/`. Shared controller code,
documentation, and hardware-independent tools stay at the project level. A module may expose
one or more plugin roles to the future controller.

## Generated output

All generated builds, reports, caches, and tool output belong under `build/`, which is ignored
by Git. Keep source files and maintained documentation outside it. Graphify output lives at
`build/graphify-out/`; generate and query it with:

```powershell
graphify extract . --out build
graphify query "question" --graph build/graphify-out/graph.json
```

## Arduino WS2812B tester

`arduino/LEDStripTester/LEDStripTester.ino` drives a WS2812B addressable strip from an
Arduino Uno through several demonstration patterns.

| Setting | Value | Constant |
|---|---|---|
| Data pin | 5 | `LEDSPIN` |
| LED count | 32 | `LEDSIZE` |
| Refresh rate | 20 | `REFRESHRATE` |

It requires the `WS2812BStrip` library (`#include <WS2812BStrip.h>`, namespace `WS2812B`).
Adjust the constants before flashing. Build output goes to `build/` and is not tracked.

This sketch is independent of the ESP32-based WLED controllers and the SignalRGB module.

## License

This project is available under the [MIT License](LICENSE).
