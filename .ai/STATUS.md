# Current status

Updated: 2026-10-09.

## Completed

- README refreshed around the current product roadmap, implementation slices, and shared
  handoff; old Arduino setup details and tool-specific commands removed from the entry page.

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
- SignalRGB must not be compiled into or referenced by the core/application projects. The app
  uses a provider-neutral integration interface and SignalRGB is a separate plugin. The first
  executable targets Windows while portable projects remain free of Windows-only APIs.
- Initial acceptance profiles are Default, VS Code, Media Player, and Stremio. First start uses
  Default in manual mode. Later starts restore and apply the last mode and active profile.
- Application/profile mappings are user-configurable. Integrations expose available provider
  effects/layouts. Bindings select `provider` or `application` rendering authority; SignalRGB
  defaults to provider authority, so its selected effect/layout suppresses the app renderer on
  overlapping devices.
- Initial SignalRGB effect fixtures: Default → Aurora, VS Code → Aurora, Media Player →
  Logarithmic Visualizer, and Stremio → Screen Ambient. Initial SignalRGB tests omit the layout
  and retain its existing LED placements; explicit provider layout selection remains optional.
- Layout means LED placement configuration. Native tests can import an available SignalRGB
  layout where read/export support is verified. A separate WLED plugin is required for native
  tests; SignalRGB already handles WLED while active. Provider authority supersedes app effect,
  layout, output routing, and native calibration on managed devices while preserving app rules,
  priorities, manual mode, and persistence. Never allow both writers on the same WLED device.
- The repository uses the MIT License. Assistants may use scoped branches, commits, pushes,
  and pull requests for requested work while preserving unrelated changes and history.

## What exists

Latest design decisions: profile priority lists use per-rule while-running/while-focused
eligibility; higher-priority running apps can retain control without focus. Profiles require
fallback/recovery policies and separate desired versus applied state. Editing supports optional
Live preview on pointer release, with explicit Apply otherwise. Native placement layouts support
stacked groups with different LED counts, scaling, and unassigned-device exclusion. Game
rendering is designed as canvas, semantic-zone, and direct named-key/LED layers. Detailed
behavior is in `../docs/plugin-architecture.md`; none of it is implemented yet.

Fallback/reconnect choices are confirmed: use the app's default native setup until modified,
then its last applied native setup. Recovered plugins generate a notification and resume use
only after user acceptance. Decline/dismiss keeps the fallback active; acceptance revalidates
the current desired selection, capabilities, and ownership. Native fallback can remain pending
until native rendering/output exists or the target hardware is released.

The SignalRGB module contains templates, a Canvas API example, and an archived Pro-blocked
effect. The shared screen colour test and Arduino sketch remain available. No custom
SignalRGB effects or user device plugins are active in the repo or Documents folders.
There is no controller app, plugin loader,
implemented profile schema, or new startup task. Architecture JSON is illustrative.

Read-only checks on 2026-10-08 confirmed SignalRGB 2.5.74, WLED Desk 16.0.1 with its 215/85
split, Dynamic Lighting disabled, and the Synapse delayed task still present. WLED Wall did
not respond. No live settings, presets, layouts, registry values, services, or tasks changed.

## Next step

Implementation is planned as slices 00–25 in `../docs/implementation-slices.md`. All are pending.
Start with 00 (minimal data/command contract) when implementation is requested; then 01
(solution scaffold). Use `SLICE-HANDOFF.md` for exact resume notes and the slice plan's start/
resume prompts to continue across models or session limits. This planning update adds no app.

When implementation is requested, scaffold the Windows Avalonia/.NET 10 app, portable core,
integration contracts, fake test integration, and separate SignalRGB plugin. Build milestone
1 around customizable Default, VS Code, Media Player, and Stremio mappings, provider catalog
discovery/authority, and restart restoration.
Do not begin native device ownership or the complete public plugin SDK first. Before SignalRGB
LED overrides or WLED streaming, establish standalone transport and device ownership. Claude
tool availability does not itself establish an app API.

## Open choices

- Generic game profile first or individual game profiles.
- First supported game API and its normalized event capabilities.
- Interim SignalRGB background effect during games.
- Capture display, initial placement geometry, and keyboard calibration approach.
- Accept native-only WLED ownership while automatic handover is unresolved?
- Native keyboard first or mouse first?
- Plugin signing/distribution and installation UX after local packages work.

These remain pending; they do not block documentation work.
