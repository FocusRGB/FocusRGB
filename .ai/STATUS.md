# Current status

Updated: 2026-10-13.

## Completed

- Persistence, selection, and mapping review findings were resolved (PR #17): missing/malformed
  schema versions recover as corrupt while numeric future versions remain untouched; JSON accepts
  comments/trailing commas; automatic startup retains restored intent until focus stabilizes;
  manual-to-automatic clears old focus; effects remain profile-owned; and zero-binding profiles
  no longer crash preview projection. Full solution tests passed 107/107. Last-known-good settings
  recovery is intentionally scheduled for slice 12a because partial linked-setting recovery is
  unsafe.
- Plugin repository ownership is decided: contracts, the minimal .NET SDK, host, schemas,
  conformance tests, template, and fake plugin stay in FocusRGB; production plugins use peer
  `FocusRGB-Plugin-<Name>` repositories and published SDK versions. Empty repositories are not
  created early: SignalRGB is created in slice 11, the selected game adapter in 16, and WLED
  in 18. Slice 24 stabilizes the proven `0.x` SDK as version 1 (PR #16).
- Application settings are now specified with researched defaults and ownership boundaries.
  Close-button behavior defaults to minimize to tray and can be changed to real exit; startup
  registration remains opt-in work for slice 26. Implementation is isolated in new slice 12a
  after tray support (PR #15).
- The implementation roadmap is now a compact status index; all 29 dependency/scope/acceptance
  definitions live in numbered group folders under `../docs/slices/` (PR #14).
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
- Initial acceptance profiles are protected Default, Programming, Movie, and Music. Programming
  maps VS Code and Visual Studio; Movie maps PotPlayer and Stremio; Music maps Media Player and
  Spotify. First start uses Default in manual mode; automatic mode returns to Default when no
  mapping is eligible. Later starts restore and apply the last mode and active profile.
- Application/profile mappings are user-configurable. Integrations expose available provider
  effects/layouts. Bindings select `provider` or `application` rendering authority; SignalRGB
  defaults to provider authority, so its selected effect/layout suppresses the app renderer on
  overlapping devices.
- Initial SignalRGB effect fixtures: Default → Aurora, Programming → Aurora, Movie → Screen
  Ambient, and Music → Logarithmic Visualizer. Initial SignalRGB tests omit the layout and
  retain its existing LED placements; explicit provider layout selection remains optional.
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

The SignalRGB staging module contains templates, a Canvas API example, and an archived
Pro-blocked effect; maintained content moves to its external plugin repository in slice 11.
The shared screen colour test remains under `tools/`; the Arduino sketch moved to
`../modules/arduino/sketches/`. `../src/` contains the app scaffold and portable configuration
code. No custom
SignalRGB effects or user device plugins are active in the repo or Documents folders.
The controller app now opens a minimal preview shell with Overview, Profiles, Mappings, and
Integrations navigation. It loads the validated defaults, starts in manual Default, and labels
desired/applied values as preview-only. The in-memory mapping editor supports application and
executable identity, profile/effect choices, eligibility, numeric priority, ordered movement,
Apply/Cancel, and optional Live preview. Grouped mappings retain all comma-separated executable
names. Layout selection is visibly disabled because the fake catalog does not expose layouts.
The portable selection engine now evaluates supplied observations with manual override,
while-running/while-focused eligibility, deterministic priority, focus debounce, ignored
windows, process-exit handling, protected Default fallback, and explicit reasons. The portable
local store validates and atomically persists settings, desired selection state, and an
independent native fallback snapshot; it seeds first run, restores restarts, and archives
malformed or invalid-reference documents with explicit recovery details. No tray behavior,
detector wiring, app persistence wiring, plugin loader, hardware output, or startup task exists yet.
The planned Settings page is documented in `../docs/application-settings.md`; no preference
schema, settings UI, tray behavior, startup registration, or diagnostics implementation was added.

Read-only checks on 2026-10-08 confirmed SignalRGB 2.5.74, WLED Desk 16.0.1 with its 215/85
split, Dynamic Lighting disabled, and the Synapse delayed task still present. WLED Wall did
not respond. No live settings, presets, layouts, registry values, services, or tasks changed.

## Active implementation slice

- Slice: 07 — Windows application observations
- State: in progress
- Branch, latest relevant code commit, and pull request target:
  `slice/07-windows-observations`, `5d6f12f`; PR not opened yet, target
  `fix/reviewed-persistence-selection`.
- Scope and dependencies verified: uses the slice 05 selector boundary and the updated
  `ApplicationObservation`/`Select` contract from PR #17; no process APIs enter Core.
- Implemented files and entry points:
  `../src/FocusRgb.Platform.Windows/WindowsApplicationObserver.cs`,
  `WindowsProcessSource.cs`, and focused platform tests under
  `../tests/FocusRgb.Platform.Windows.Tests/`.
- Verification: `dotnet build FocusRGB.slnx -c Release` passed with 0 warnings/errors;
  `dotnet test --solution FocusRGB.slnx -c Release --no-build` passed 113/113. Fixtures cover
  Stremio shell/helper identity, disappearing foreground processes, access denial, ignored
  foreground windows, and executable-name normalization.
- External/live evidence: not run yet; user requested approval immediately before the read-only
  process and foreground-window check. No lights, settings, or integrations were touched.
- Remaining acceptance criteria: with the configured apps open, report installed Programming,
  Movie, and Music identities and confirm the real Stremio foreground-window owner.
- Current blocker or required product input: user must open the desired apps and confirm the
  read-only local observation check can run.
- Uncommitted work to preserve: status/index updates only.
- Next action: run the approved local observation check, record evidence, finish documentation,
  push the branch, and open the stacked pull request.
- Next slice: 08 only after the live acceptance check passes.


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
priority 100 in list order Programming, Movie, Music; protected Default handles unmatched
automatic selection. Fixture effects carry display names with `resourceId: null`, resolved by
exact catalog match; SignalRGB bindings use a
`provider-managed` device scope; JSON-RPC requests time out after 5000 ms by default.

## Next step

Slices 07–27 are pending. Continue with 07 (Windows application observations). Supply portable
observations to the slice 05 selector; do not move process APIs into Core.

Build milestone 1 around the existing Windows Avalonia/.NET 10 shell, portable core,
integration contracts, fake test integration, and separate SignalRGB plugin. Keep protected
Default and the Programming, Movie, and Music mappings customizable, with provider catalog
discovery/authority and restart restoration.
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
