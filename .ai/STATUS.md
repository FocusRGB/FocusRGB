# Current status

Updated: 2026-10-10.

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
effect. The shared screen colour test remains under `tools/`; the Arduino sketch moved to
`../modules/arduino/sketches/` for a future Arduino plugin. `../src/` holds only a README
until slice 01 creates the projects. No custom
SignalRGB effects or user device plugins are active in the repo or Documents folders.
There is no controller app, plugin loader,
implemented profile schema, or new startup task. Architecture JSON is illustrative.

Read-only checks on 2026-10-08 confirmed SignalRGB 2.5.74, WLED Desk 16.0.1 with its 215/85
split, Dynamic Lighting disabled, and the Synapse delayed task still present. WLED Wall did
not respond. No live settings, presets, layouts, registry values, services, or tasks changed.

## Active implementation slice

- Slice: 01 — Solution scaffold and repeatable checks
- State: complete (pending pull request review into `develop`)
- Branch, latest relevant commit, and pull request into `develop`: `slice/01-solution-scaffold`
  from `develop` at 022b0bb; commit "Scaffold .NET 10 solution with boundary checks"
- Scope and dependencies verified: slice 00 merged (PR #1); .NET SDK 10.0.401 installed
- Implemented files and entry points: `../FocusRGB.slnx`, `../global.json`,
  `../Directory.Build.props` (output to `build/artifacts`), `../Directory.Packages.props`
  (Avalonia 12.1.4, xunit.v3 4.0.2), `../src/FocusRgb.{Contracts,Core,Platform.Windows,App}`,
  `../tests/FocusRgb.Core.Tests`, `../.github/workflows/ci.yml`
- Verification: `dotnet build FocusRGB.slnx -c Release` succeeded with warnings as errors;
  `dotnet test --solution FocusRGB.slnx -c Release` passed 7/7. Mutation checks: adding an
  Avalonia package to Core failed `PortableProjectHasOnlyAllowedReferences`; retargeting Core to
  `net10.0-windows` failed the build (NU1201). `FocusRGB.exe` opened a "FocusRGB" window and
  exited 0 on close. `git status` showed no generated files outside `build/`.
- External/live evidence: none required; no SignalRGB or device calls
- Remaining acceptance criteria: none
- Current blocker or required product input: none
- Uncommitted work to preserve: none
- Next action: slice 02 — implement the `settings.json` model and validation from
  `../docs/minimal-contract.md` in `FocusRgb.Core`, with fixture tests
- Next slice: 02 (03 and 06 follow; 08 can run in parallel as read-only investigation)

Toolchain notes: `xunit.v3` 4.x requires the Microsoft Testing Platform runner, enabled in
`global.json`; use `dotnet test --solution …`, not the VSTest form. The App and Windows
platform projects target `net10.0-windows`; Contracts and Core stay `net10.0`.

Project name (2026-10-10): FocusRGB. The repository is `MAJ224/FocusRGB`; .NET projects
use the `FocusRgb.*` prefix (`FocusRgb.Contracts`, `FocusRgb.Core`, `FocusRgb.Platform.Windows`,
`FocusRgb.App`). The archived SignalRGB effect keeps its historical publisher metadata.

Git workflow (2026-10-10): `develop` is the default branch; all work reaches it through pull
requests (`../docs/git-workflow.md`). Rulesets protect `develop` and `main`; `develop` requires
the `branch-policy` check. Add `branch-policy` to `main` after the first release merge, and the
CI `build` check to both rulesets after CI first runs on `develop`.

Proposed defaults frozen in slice 00 for the user to confirm or change: three persisted files
(`settings.json`, `state.json`, `native-setup.json`); seeded rules are `while-focused` at equal
priority 100 in list order Media Player, Stremio, VS Code; fixture effects carry display names
with `resourceId: null`, resolved by exact catalog match; SignalRGB bindings use a
`provider-managed` device scope; JSON-RPC requests time out after 5000 ms by default.

## Next step

Slices 01–25 are pending. Continue with 01 (solution scaffold), then 02 (profiles and
configuration validation) implementing `../docs/minimal-contract.md`.

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
