# Implementation slices

Updated 2026-10-12. Slices 00–04 are complete; later slices are pending. This is the execution breakdown of
`focus-profiles-plan.md`. Requirements live in `app-brief.md`, contracts and behavior in
`plugin-architecture.md`, and verified external-system facts in `integration-evidence.md`.

## Working one slice at a time

Ask a model to implement one numbered slice. Its dependencies must be complete first. Read
only the relevant files and connected callers/tests; do not rebuild the repository graph for
every slice. Implement the stated scope, run its checks, and update `.ai/STATUS.md` using
`.ai/SLICE-HANDOFF.md`. A slice can span sessions: record partial work and resume the same ID.
Do not advance the ID just because a session or token allowance ended.

Keep each completed slice independently reviewable, with a scoped commit and a clean build.
Preserve unrelated user changes. If a slice grows, split it into lettered children (for example
08a/08b) with explicit dependencies and acceptance checks; do not silently expand its scope.
Use mock integrations until a live check is explicitly part of the user's requested run.
Record API acceptance separately from observed lighting. Mark unavailable external capabilities
as blocked/unsupported and continue independent work; never replace a missing capability with
invented data or pretend an acceptance gate passed.

## Planned source boundaries

- `src/FocusRgb.Contracts/`: provider-neutral plugin messages, capabilities, results, and resource IDs.
- `src/FocusRgb.Core/`: profiles, arbitration, persistence, commands, fallback, and ownership.
- `src/FocusRgb.Platform.Windows/`: Windows process/focus detection and later screen capture.
- `src/FocusRgb.App/`: Avalonia tray/settings UI and generic plugin host wiring.
- `modules/signalrgb/controller-plugin/`: separate controller plugin executable/package;
  existing `modules/signalrgb/plugins/` remains SignalRGB USB plugin content.
- `modules/wled/controller-plugin/`: separate WLED controller plugin executable/package.
- `tests/`: relevant core, contract, integration, and UI checks.
- `build/`: all generated .NET output, test reports, packages, and tool artifacts (ignored).

The core may reference shared contracts; neither core nor app references provider projects.
Portable contracts/core target .NET 10 without Windows APIs. The first app executable runs on
Windows. Use one background engine hosted by the tray app initially; separate OS services are
not prerequisites. Names above are proposed until slice 01 creates them; record actual paths
in the handoff and reuse those thereafter.

## Goal 1: working core with separate SignalRGB plugin

### 00 — Freeze the minimal data and command contract

Depends on: none. Scope: one small design document with JSON examples for profiles, ordered
rules, mode, desired/applied state, saved native fallback, and integration capabilities.
Include optional layout, rendering authority, while-running/while-focused eligibility,
catalog results, omitted-layout behavior, and versioning. Define requests/results needed by
the first plugin only, including unsupported/unverified outcomes and device scope.
Acceptance: examples cover first run, restart, absent plugin, and an explicit user selection;
no SignalRGB types enter shared contracts. No runtime code or exhaustive future schema.

### 01 — Solution scaffold and repeatable checks

Depends on: 00. Scope: .NET 10 solution, contracts/core/Windows/app/test projects, minimal
Avalonia window, central output paths under `build/`, and documented build/test commands.
Acceptance: restore/build succeeds, one useful scaffold smoke check runs, core/contracts have
no Windows or provider references, generated files remain ignored. Pin actual package versions.

### 02 — Profiles and configuration validation

Depends on: 01. Scope: implement the minimal model from 00; validate IDs, rule targets,
authority, and required fields. Seed user-editable fixtures: Default/Aurora, VS Code/Aurora,
Media Player/Logarithmic Visualizer, Stremio/Screen Ambient; provider layout stays unset.
Acceptance: valid fixtures load; duplicates/missing references fail with usable errors;
SignalRGB display names are fixtures, not core constants. Native setup is a separate snapshot.

### 03 — UI shell with preview state

Depends on: 02. Scope: replace the placeholder window with the minimal settings shell,
navigation, manual/automatic control, profile picker, desired/applied/reason status, and clear
preview-only integration status. Populate it from validated default settings through a small
UI-facing state model; do not implement detection, persistence, tray behavior, or outputs.
Acceptance: the Windows app builds and opens with Default in manual mode; all four fixture
profiles are visible; changing the preview mode/profile updates displayed state; state behavior
has focused tests; the UI never suggests that mock output reached hardware.

### 04 — Profile mapping editor with preview state

Depends on: 03. Scope: build the applications/profile mapping editor against draft in-memory
state: application identity, profile, priority order, while-running/while-focused eligibility,
Apply/Cancel, and optional Live preview after completed edits. Show provider effect and optional
layout choices from a fake capability catalog, with unavailable controls visibly disabled.
Acceptance: edits remain drafts until Apply; Cancel restores the prior draft and preview output;
unchecked preview sends no UI-state apply before Apply; priority reorder and eligibility are
keyboard accessible. No file writes, process detection, plugin process, or hardware output.

### 05 — Pure selection engine

Depends on: 02. Scope: supplied observations select a profile with deterministic ordered
priority and per-rule eligibility. Implement manual override, ignored windows, debounce,
process exit, and unmatched policy. Inject time/observations for tests; no OS or lighting calls.
Acceptance: higher-priority running A beats focused B; focus-only A yields; A exiting removes
eligibility; manual mode wins; ties and brief focus changes are stable. Return selection reason.

### 06 — Saved state and startup restoration

Depends on: 05. Scope: versioned local settings, atomic save, preserved prior valid data on
failure, first-run manual Default, restart restoration, and saved native fallback snapshot.
Acceptance: restart restores desired mode/profile; preview/provider changes never overwrite
native fallback; corrupt/missing files have explicit recovery behavior without silently
destroying the previous file. Desired state can persist while applied output is unavailable.

### 07 — Windows application observations

Depends on: 05. Scope: foreground and running-process observations behind the detector
boundary. Handle disappearing processes/access denial; identify actual Stremio window owner.
Acceptance: observation fixtures cover app close/helpers/ignored windows; a read-only local
run reports VS Code, Media Player, and Stremio identities where installed. No lights change.

### 08 — Minimal external plugin host and fake plugin

Depends on: 02. Scope: explicitly enabled local package manifest, version handshake,
JSON-RPC stdin/stdout requests, cancellation/timeouts, health, and process shutdown. Implement
one fake executable using the shared contract. Keep protocol logs off stdout.
Acceptance: fake catalog/apply works; unsupported version, malformed responses, timeout, and
plugin exit do not crash the app or hang a request. Removing fake plugin leaves core usable.

### 09 — Command coordination and fallback state

Depends on: 05, 06, 08. Scope: connect commands/selection to fake plugin results; track desired,
applied, pending, and native fallback states. Validate ownership and authority before output.
Implement reconnect notification state with Accept/Decline and coalescing.
Acceptance: failure retains desired state; no fallback cycle or competing writer; reconnect
does not resume automatically; Accept uses current intent; native fallback lacking an output
reports pending. No native renderer needed yet.

### 10 — SignalRGB capability investigation

Depends on: 00. Scope: read-only investigation and evidence notes for process/launcher discovery,
effect catalog/current selection, optional layout catalog/export, account restrictions, and
external application endpoints. Record exact interface/version and reproducible checks.
Acceptance: each capability is verified, unavailable, or unresolved with evidence; resolve
fixture names against actual catalog (including Screen Ambient versus historical Screen
Ambience). No light switching, provider settings edits, or device reconfiguration in this slice.

### 11 — Separate SignalRGB plugin

Depends on: 08, 10. Scope: plugin under its module, status/catalog requests, effect application,
optional supported layout selection, and provider authority/device scope. Omitted layout keeps
current provider placements. Never launch SignalRGB implicitly. Mock external calls in tests.
Acceptance: encoded identifiers and failure paths checked; app/core have no provider project
reference; unsupported APIs return honest errors; removing package keeps core operational.
Live effect application waits for the end-to-end gate in 14.

### 12 — Tray runtime and real status wiring

Depends on: 07, 09. Scope: wire the background engine into the existing Avalonia shell; add tray
show/quit and connect the previewed profile picker, manual/automatic control,
desired/applied/reason display, errors, and reconnect notification to shared commands.
Acceptance: closing window leaves engine running; Quit shuts down plugin processes cleanly;
UI stays responsive during timeout; accepting/declining recovery reaches shared commands.
No startup registration or full editor.

### 13 — Wire profile mappings to catalogs and persistence

Depends on: 04, 11, 12. Scope: connect the existing editor to detected/manual apps, persisted
settings, and real provider catalogs. Keep Apply/Cancel and Live preview behavior; replace fake
capabilities with capability-aware controls and explicit unavailable states.
Acceptance: mappings persist, preview does not save, Cancel restores supported prior output,
unchecked preview sends nothing before Apply. Fake catalogs test unavailable resources;
unsupported SignalRGB catalogs remain a visible limitation, not a fabricated list.

### 14 — First product acceptance gate

Depends on: 13. Scope: run clean build/tests, then the explicitly requested live SignalRGB
test session using the four mappings. Retain current SignalRGB layout and WLED routing.
Acceptance: first-start/manual, restart, automatic selection, priority eligibility, absent
plugin, and recovery notification match requirements. User verifies visible effect switching;
catalog or application limitations are recorded as remaining gates. No native WLED writes.

## Goal 2: game profiles

### 15 — Installed game catalog and assignments

Depends on: 14. Scope: separate Steam discovery integration, manual game entries, scoped IDs,
and generic/per-game profile assignments. Choose the first game with the user before real
fixtures are required. Reuse the existing editor/rules.
Acceptance: duplicate titles across launchers do not collide; running state never invents focus;
static game profiles work without telemetry.

### 16 — One game API adapter

Depends on: 15. Scope: choose one supported API, document its event contract, implement one
separate plugin, and record replay fixtures with source/freshness. Share only proven common
fields; preserve game-specific data namespaced.
Acceptance: replay changes effect inputs; stale/disconnected telemetry degrades predictably;
static selection remains usable. No injection/memory reads or universal game schema.

## Goal 3: native effects, layouts, and outputs

### 17 — Placement model and offline renderer

Depends on: 02; schedule after Goal 2 by default. Scope: coordinates/transforms, stable device
and LED identities, optional direct-only participation, exclusion, and one static/gradient
canvas effect producing frames to fake output. A minimal default native setup is created here.
Acceptance: transform/reversal sampling preserves indices; unassigned devices get no frames;
direct-only devices need no canvas coordinates; unequal LED counts sample normalized positions.

### 18 — WLED plugin and native output gate

Depends on: 08, 09, 17. Scope: WLED status/device capabilities, configured network targets,
static output then frame streaming, bounded timeouts, cleanup/realtime expiry, and ownership.
Use fake network checks first. Before requested live tests, prove target release from SignalRGB.
Acceptance: visible native WLED output with one writer, offline device isolation, stop/reconnect
behavior, and working saved native fallback where dependencies/ownership are available.
No implicit changes to SignalRGB device settings or existing WLED presets.

### 19 — Layout canvas editor

Depends on: 13, 17. Scope: assign/unassign devices, move/rotate/stretch/compress, direct-only
participation, persistence, and existing preview controls.
Acceptance: monitor-only layout excludes wall strips; changing inclusion stops outgoing app
streams through coordinator; editing keeps hardware LED counts unchanged. Check pointer and
keyboard interaction. Native preview can use fake output before live WLED is available.

### 20 — Stacked groups

Depends on: 19. Scope: group/ungroup, shared footprint/transforms, member orientation, and
normalized sampling across different LED counts. Group state belongs to each layout.
Acceptance: 30- and 100-LED strips reproduce the same spatial effect at different resolution;
ungroup preserves identities and documented placement; named mappings still address members.

### 21 — Hybrid game layers

Depends on: 16, 17, 19. Scope: canvas base, named semantic zones/ranges, direct key/LED layer,
deterministic composition, and a small binding editor. Use fake keyboard/device capabilities.
Acceptance: named keys work without precise placement; a direct key overrides only its target;
strip/fan zones work without per-LED edits; exclusions and provider authority remain enforced.

### 22 — Native screen ambience and calibration

Depends on: 18, 19. Scope: Windows capture input, selected monitor, canvas mapping, native
per-device color calibration. Split capture and calibration into children if needed.
Acceptance: monitor direction and desk exclusion are correct; cancellation releases capture;
fake images check sampling; user validates actual colors. Capture is independent of app focus.

### 23 — One native peripheral backend

Depends on: 21. Scope: choose keyboard or mouse and one actual supported model, then implement
its separate output plugin and semantic/geometry map. Repeat as new slices for other models.
Acceptance: ownership/release verified, actual LEDs checked, macros/special keys preserved;
no support claims for untested devices.

## Follow-on slices

### 24 — Public SDK and contributor example

Depends on: 11, 18. Scope: extract the proven protocol/SDK, compatibility guidance, template,
and standalone example package. Acceptance: build/install the example without changing app
code; incompatible contracts fail clearly. Signing/marketplace remains a separate future task.

### 25 — Local MCP commands

Depends on: 09, 13. Scope: local server/client boundary using shared query/command services,
profiles/rules/priority/manual mode/preview tools and existing recovery acceptance.
Acceptance: tool calls follow the same validation/persistence/authority paths as UI; local
access restrictions work; no direct configuration-file edits. Model choice is not required.

### 26 — Startup, packaging, and recovery gate

Depends on: 14; repeat relevant gate checks after native output ships. Scope: packaging,
optional user-controlled startup, crash journal recovery, and clean shutdown/reconnect checks.
Acceptance: install/run/uninstall checks preserve user settings; recovered stale output is
reported honestly; startup applies saved intent without granting recovered plugins authority
against the user's reconnect preference. No unrelated task/service changes.

### 27 — SignalRGB independence gate

Depends on: required native slices 18–23 and 26. Scope: inventory remaining provider-dependent
profiles, migrate required ones with user review, and run with SignalRGB stopped.
Acceptance: required effects/layouts/devices and fallback work independently; other integration
plugins remain usable. Removing/uninstalling SignalRGB or modifying its startup is a separate
explicit user action.

## Copy-paste prompts

Start:

> Implement slice NN from docs/implementation-slices.md. Read .ai/AGENTS.md and .ai/STATUS.md,
> then that slice and its relevant requirement/architecture sections. Verify dependencies
> against actual code and checks. Implement only this slice, run its acceptance checks, and
> update .ai/STATUS.md using .ai/SLICE-HANDOFF.md. Commit on a slice branch from develop, push it,
> and open a pull request into develop when complete.
> Report external gates honestly and do not run live hardware tests unless I request them.

Resume after a limit:

> Resume the active slice recorded in .ai/STATUS.md. Read .ai/AGENTS.md, the handoff, and the
> matching slice in docs/implementation-slices.md. Inspect git status/diff and continue from
> the recorded next action. Preserve partial work; do not restart, redesign completed code,
> or advance to another slice. Re-run checks only where unfinished work or new edits require it.

Review:

> Review the completed slice NN against its acceptance criteria and connected code. Check
> dependency boundaries, failure cases, and test evidence. Report concrete issues; do not
> expand scope or implement another slice.

## Completion ledger

Maintain this compact ledger as implementation progresses:
`ID | pending/in-progress/blocked/complete | commit | acceptance evidence or remaining gate`.
The active slice's detailed resume notes belong only in `.ai/STATUS.md`. Unlisted slices are pending.

```text
00 | complete | 0c9a8c3 (PR #1) | first run, restart, absent plugin, explicit selection, priority examples; JSON parsed; no provider types
01 | complete | PR from slice/01-solution-scaffold | build passes; 7 boundary/smoke tests pass; app window opens and closes with exit 0; output only under build/
02 | complete | PR from slice/02-profile-validation (depends on PR #6) | build passes; 52 tests pass including 45 settings cases; default fixtures, validation, unknown-field preservation, separate native snapshot
03 | complete | 2104f40 (PR #9, depends on PR #8) | build passes; 56 tests pass; app opens with four fixture profiles and explicit preview-only state
04 | complete | dc32b07 (PR from slice/04-profile-mapping-editor, depends on PR #9) | build passes; 62 tests pass; draft/apply/cancel/live-preview, priority order, fake effects, disabled layout catalog
```
