# Current status

Updated: 2026-10-09.

## Completed

- Provider-independent brief, plugin design, roadmap, evidence record, and AI instructions.
- Final goal confirmed: replace SignalRGB, retain integrations with other apps.
- Historical v2 plan archived; new plan supports profiles combining plugin actions.
- Keyboard visual confirmation distinguished from mouse API acceptance and unresolved handover.
- SignalRGB-only starters, examples, and the Pro-blocked experiment separated from the empty
  active `effects/` and `plugins/` install folders; tools now have explicit names.
- SignalRGB-specific content, tools, MCP launcher, reference notes, and security research are
  grouped under `modules/signalrgb/`; module ownership is documented for future integrations.
- Generated builds and tool artifacts are consolidated under the ignored `build/` directory;
  repository Graphify output is stored in `build/graphify-out/`.
- Runtime selected: .NET 10 with an Avalonia tray/settings UI and UI-independent background
  core, targeting Windows, macOS, and Linux where platform integrations permit it.
- Plugin direction selected: open manifests/protocol/schemas and official .NET SDK/templates;
  plugins run out of process over versioned JSON-RPC and declare platform/resource needs.
- Selection requirements now include editable deterministic rule priority and a manual mode
  that suppresses detection until automatic selection is resumed.
- Game API plugins feed versioned normalized and namespaced events to effects. A local MCP
  adapter will use the same validated command services as the UI.
- Implementation order confirmed: first deliver a working core and real SignalRGB vertical
  slice; next add game profiles composed from an effect and LED layout; then implement native
  effects/layouts and outputs to remove the SignalRGB dependency. The public plugin SDK must
  be extracted from working internal contracts instead of blocking the first integration.
- The repository uses the MIT License. Assistants may use scoped branches, commits, pushes,
  and pull requests for requested work while preserving unrelated changes and history.

## What exists

The SignalRGB module contains templates, a Canvas API example, and an archived Pro-blocked
effect. The shared screen colour test and Arduino sketch remain available. No custom
SignalRGB effects or user device plugins are active in the repo or Documents folders.
There is no controller app, plugin loader,
implemented profile schema, or new startup task. Architecture JSON is illustrative.

Read-only checks on 2026-10-08 confirmed SignalRGB 2.5.74, WLED Desk 16.0.1 with its 215/85
split, Dynamic Lighting disabled, and the Synapse delayed task still present. WLED Wall did
not respond. No live settings, presets, layouts, registry values, services, or tasks changed.

## Next step

When implementation is requested, scaffold the Avalonia/.NET 10 solution and build milestone
1: focus detection, deterministic/manual profile selection, fake-output tests, and real
SignalRGB effect switching. Do not begin native device ownership or the complete public plugin
SDK first. Before SignalRGB LED overrides or WLED streaming, establish standalone transport
and device ownership. Claude tool availability does not itself establish an app API.

## Open choices

- Generic game profile first or individual game profiles.
- First supported game API and its normalized event capabilities.
- Interim SignalRGB background effect during games.
- Capture display and monitor/desk layout editor.
- Accept native-only WLED ownership while automatic handover is unresolved?
- Native keyboard first or mouse first?
- Plugin signing/distribution and installation UX after local packages work.

These remain pending; they do not block documentation work.
