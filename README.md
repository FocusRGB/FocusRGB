# FocusRGB

A planned desktop RGB controller with an Avalonia UI and .NET 10 background core.
The first executable targets Windows; shared code remains portable for future platforms.
**Early implementation:** the contract (slice 00) and solution scaffold (slice 01) exist;
the app currently opens a placeholder window.

The app will select lighting profiles manually or from applications using a user-defined
priority list. Rules can remain active while an app is running or only while it has focus.
First launch uses manual mode; subsequent launches restore the saved state.

## Implementation roadmap

1. Build the portable core and a separately packaged SignalRGB integration plugin.
2. Add game profiles and supported game API integrations.
3. Build native effects, LED placement layouts, and output plugins to operate without SignalRGB.

SignalRGB owns rendering and its existing layout while its integration is active, including
its WLED output. A separate WLED plugin supports native operation. Application mappings are
customizable; the core and UI have no dependency on either provider's implementation.

Native layouts will support a 2D placement canvas, stacked device groups, and device exclusion.
Game effects can combine canvas animation, semantic zones, and direct key/LED mappings.
A local MCP interface is planned for controlling the same commands used by the UI.

## Start here

- [Current status](.ai/STATUS.md): completed work, active slice, and next action.
- [Implementation slices](docs/implementation-slices.md): bounded tasks, dependencies,
  acceptance checks, and start/resume prompts for working across models or session limits.
- [App brief](docs/app-brief.md): product requirements and user decisions.
- [Minimal contract](docs/minimal-contract.md): frozen settings, state, status, command,
  and plugin protocol shapes for the first implementation goal.
- [Plugin architecture](docs/plugin-architecture.md): integration contracts, selection,
  rendering authority, fallback, and layouts.
- [Delivery plan](docs/focus-profiles-plan.md): product goals and acceptance gates.
- [Integration evidence](docs/integration-evidence.md): dated observations and unverified capabilities.

Changes reach `develop` (the default branch) through pull requests; see the
[Git workflow](docs/git-workflow.md). For AI-assisted development, follow the [agent instructions](.ai/AGENTS.md) and
[handoff guide](.ai/README.md). Use the [slice checkpoint template](.ai/SLICE-HANDOFF.md)
to preserve progress when switching models or reaching a session limit.

## Repository conventions

Integration-specific source and research live under `modules/<integration>/`. The existing
[SignalRGB module](modules/signalrgb/README.md) contains research and tools; its controller
plugin is still planned. The [Arduino module](modules/arduino/README.md)
holds the earlier LED strip sketch and a future Arduino plugin. Application projects will
live under [src/](src/README.md); the shared screen colour test remains under `tools/`.

## Build and test

Requires the .NET SDK pinned in `global.json` (10.0.401 or a later 10.0 feature band).

```powershell
dotnet build FocusRGB.slnx -c Release
dotnet test --solution FocusRGB.slnx -c Release
.\build\artifacts\bin\FocusRgb.App\release\FocusRGB.exe
```

Package versions are pinned centrally in `Directory.Packages.props`. CI runs the same build
and tests on pull requests into `develop` and `main`.

Generated builds, packages, reports, caches, and tool artifacts belong under the Git-ignored
`build/` directory. Maintained source and documentation stay outside it. Generated Graphify
data lives at `build/graphify-out/` and is optional for development.

## License

[MIT](LICENSE).
