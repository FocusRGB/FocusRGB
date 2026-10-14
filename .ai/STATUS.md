# Current status

Updated: 2026-10-14.

## Completed

- Slice 09 adds saved application preferences for close behavior, start minimized, theme, and
  verbose logging. Settings schema 1 migrates to schema 2; invalid current settings are archived
  and restored from a validated last-known-good copy before defaults. The Settings page exposes
  capability-aware lifecycle choices and safe folder/diagnostic actions. Theme changes affect
  only the UI, and close-to-tray uses saved values rather than editor drafts. Build passes
  without warnings and all 135 tests pass.

- Slice 08 hosts selection state in the desktop runtime. Profile/mode changes use a portable
  command service that saves before updating visible state, local status distinguishes desired
  from unapplied output, the close button hides the window, and the tray provides Show and Quit.
  Quit flushes valid selection state and stays open with an actionable message on save failure.
  Build passes without warnings and all 122 tests pass.

- Slice 07 makes the Profiles page a real persisted editor. Save edits the selected profile,
  Save As creates a uniquely identified copy, Cancel restores the saved draft/preview, and Remove
  protects Default plus mapping and fallback references. Effect, provider-current or named layout,
  and primary fallback choices are validated and atomically stored under `%LOCALAPPDATA%\FocusRGB`.
  Persisted current/last selection references are reassigned to Default before a profile is
  removed. Solution build and all 117 tests pass.

- Roadmap reordered app-first: profile management, tray/settings, native rendering/layouts,
  and native outputs now precede desktop-application detection, SignalRGB, games, and MCP.
  The Windows observation fixture checkpoint remains preserved on
  `slice/07-windows-observations` for the renumbered Slice 19.
- Persistence, selection, and mapping review findings were resolved (PR #17): missing/malformed
  schema versions recover as corrupt while numeric future versions remain untouched; JSON accepts
  comments/trailing commas; automatic startup retains restored intent until focus stabilizes;
  manual-to-automatic clears old focus; effects remain profile-owned; and zero-binding profiles
  no longer crash preview projection. Full solution tests passed 107/107. Last-known-good settings
  recovery is intentionally scheduled for slice 09 because partial linked-setting recovery is
  unsafe.
- Plugin repository ownership is decided: contracts, the minimal .NET SDK, host, schemas,
  conformance tests, template, and fake plugin stay in FocusRGB; production plugins use peer
  `FocusRGB-Plugin-<Name>` repositories and published SDK versions. Empty repositories are not
  created early: WLED is created in slice 16, SignalRGB in 22, and the selected game adapter
  in 27. Slice 24 stabilizes the proven `0.x` SDK as version 1 (PR #16).
- Application settings are now specified with researched defaults and ownership boundaries.
  Close-button behavior defaults to minimize to tray and can be changed to real exit; startup
  registration remains opt-in work for slice 18. Implementation is isolated in slice 09 after
  tray support (PR #15).
- The implementation roadmap is now a compact status index; all 30 dependency/scope/acceptance
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
- Selection requirements now use editable deterministic mapping-list order and a manual mode
  that suppresses detection until automatic selection is resumed.
- Game API plugins feed versioned normalized and namespaced events to effects. A local MCP
  adapter will use the same validated command services as the UI.
- Implementation order confirmed: finish the FocusRGB desktop app first, then its native
  effects/layouts and outputs, and only then add external-application automation, SignalRGB,
  games, and MCP. The public plugin SDK must be extracted from working internal contracts.
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

Latest design decisions: mapping priority is the saved top-to-bottom order, with per-rule
while-running/while-focused eligibility; earlier running apps can retain control without focus. Profiles require
fallback/recovery policies and separate desired versus applied state. Editing supports optional
Live preview on pointer release, with explicit Save otherwise. Native placement layouts support
stacked groups with different LED counts, scaling, and unassigned-device exclusion. Game
rendering is designed as canvas, semantic-zone, and direct named-key/LED layers. Detailed
behavior is in `../docs/plugin-architecture.md`; only the selection/persistence and profile-editing
parts are implemented so far.

Fallback/reconnect choices are confirmed: use the app's default native setup until modified,
then its last applied native setup. Recovered plugins generate a notification and resume use
only after user acceptance. Decline/dismiss keeps the fallback active; acceptance revalidates
the current desired selection, capabilities, and ownership. Native fallback can remain pending
until native rendering/output exists or the target hardware is released.

The SignalRGB staging module contains templates, a Canvas API example, and an archived
Pro-blocked effect; maintained content moves to its external plugin repository in slice 22.
The shared screen colour test remains under `tools/`; the Arduino sketch moved to
`../modules/arduino/sketches/`. `../src/` contains the app scaffold and portable configuration
code. No custom
SignalRGB effects or user device plugins are active in the repo or Documents folders.
The controller app now opens a minimal tray-backed shell with Home, Profiles, Application Groups,
and Integrations navigation. Home always exposes the Mode control; Manual shows profile/current
state and Automatic shows the mapping editor. It loads the validated defaults, starts in manual
Default, restores later starts from the local state store, and labels desired/applied values as
unapplied until an output exists. Manual profile/mode commands persist asynchronously; closing
hides the window while the local runtime continues, and tray Quit saves before shutdown.
Profiles are persistently editable with protected references and validated fallback
choices. The remaining in-memory editor provides named executable groups with a Windows `.exe`
picker, mapping selection of one
group plus one profile, top-to-bottom priority, hover arrows, midpoint-based whole-row insertion
drag/drop, row Edit/Remove, Save/Save As/Remove item actions, eligibility, Save/Cancel, and optional
Live preview. Group selectors align with their editor bodies, and executable actions sit beside
the list. Persistent application groups remain part of slice 20.
The portable selection engine now evaluates supplied observations with manual override,
while-running/while-focused eligibility, deterministic priority, focus debounce, ignored
windows, process-exit handling, protected Default fallback, and explicit reasons. The portable
local store validates and atomically persists settings, desired selection state, and an
independent native fallback snapshot; it seeds first run, restores restarts, and archives
malformed or invalid-reference documents with explicit recovery details. No detector wiring,
plugin loader, hardware output, or startup task exists yet.
The Settings page persists application preferences in schema-version-2 `settings.json`, applies
System/Light/Dark only to the Avalonia UI, supports saved close-to-tray or clean-exit behavior,
and can start with the window hidden without registering OS startup. It can open local settings
and log folders and export a diagnostic ZIP that excludes profiles, mappings, paths, and secrets.
OS startup registration remains deferred to Slice 18.

Read-only checks on 2026-10-08 confirmed SignalRGB 2.5.74, WLED Desk 16.0.1 with its 215/85
split, Dynamic Lighting disabled, and the Synapse delayed task still present. WLED Wall did
not respond. No live settings, presets, layouts, registry values, services, or tasks changed.

## Active implementation slice

- Slice: 09 — Application settings and lifecycle preferences
- State: complete; awaiting review in the stacked pull-request chain
- Branch, latest relevant code commit, and pull request target:
  `slice/09-application-settings`; commits `89e95a5`, `69ea5bc`, `683b113`, and
  `fbaecfc`; PR pending against `slice/08-desktop-runtime-tray`.
- Scope and dependencies verified: follows persistence and tray lifecycle; no startup
  registration, process observation, plugin, SignalRGB, or hardware dependency.
- Implemented files and entry points: `ApplicationSettings` and `SettingsJson` own schema
  migration/validation; `LocalStateStore` owns last-known-good recovery; `MainWindowViewModel`
  owns saved-versus-draft preferences; `App` and `MainWindow` own lifecycle/theme/actions;
  `DiagnosticExport` owns the intentionally limited export shape.
- Verification: `dotnet build FocusRGB.slnx --no-restore` and
  `dotnet test FocusRGB.slnx --no-restore` pass, 135/135 tests.
- External/live evidence: none required; no live integrations, devices, startup registration, or
  machine preferences were changed. Interactive theme, file-picker, start-hidden, and close-menu
  behavior remains a manual packaging check rather than claimed live evidence.
- Remaining acceptance criteria: none for Slice 09.
- Current blocker or required product input: none.
- Uncommitted work to preserve: documentation and final verification before the Slice 09 PR.
- Next action: finish the Slice 09 documentation commit and open its stacked PR.
- Next slice: 10 — Placement model and offline renderer.


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
(`settings.json`, `state.json`, `native-setup.json`); seeded rules are `while-focused` in priority
order Programming, Movie, Music; protected Default handles unmatched
automatic selection. Fixture effects carry display names with `resourceId: null`, resolved by
exact catalog match; SignalRGB bindings use a
`provider-managed` device scope; JSON-RPC requests time out after 5000 ms by default.

## Next step

Slices 10–29 are pending. Continue with the native RGB path at Slice 10. Profile persistence,
the tray runtime, and application settings are complete; application-group/mapping persistence
remains intentionally scheduled with external-application work in Slice 20.

External application work starts at Slice 19. SignalRGB begins at 21, the public SDK is stabilized
at 24, MCP is 25, and game integrations start at 26. The preserved observation branch must be
integrated only when Slice 19 starts.
Claude tool availability does not itself establish an app API.

## Open choices

- Generic game profile first or individual game profiles.
- First supported game API and its normalized event capabilities.
- Interim SignalRGB background effect during games.
- Capture display, initial placement geometry, and keyboard calibration approach.
- Accept native-only WLED ownership while automatic handover is unresolved?
- Native keyboard first or mouse first?
- Plugin signing/distribution and installation UX after local packages work.

These remain pending; they do not block documentation work.
