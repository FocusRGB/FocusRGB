# Implementation slices

Updated 2026-10-10. Slice 00 is complete (`minimal-contract.md`); later slices are pending. This is the execution breakdown of
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

### 03 — Pure selection engine

Depends on: 02. Scope: supplied observations select a profile with deterministic ordered
priority and per-rule eligibility. Implement manual override, ignored windows, debounce,
process exit, and unmatched policy. Inject time/observations for tests; no OS or lighting calls.
Acceptance: higher-priority running A beats focused B; focus-only A yields; A exiting removes
eligibility; manual mode wins; ties and brief focus changes are stable. Return selection reason.

### 04 — Saved state and startup restoration

Depends on: 03. Scope: versioned local settings, atomic save, preserved prior valid data on
failure, first-run manual Default, restart restoration, and saved native fallback snapshot.
Acceptance: restart restores desired mode/profile; preview/provider changes never overwrite
native fallback; corrupt/missing files have explicit recovery behavior without silently
destroying the previous file. Desired state can persist while applied output is unavailable.

### 05 — Windows application observations

Depends on: 03. Scope: foreground and running-process observations behind the detector
boundary. Handle disappearing processes/access denial; identify actual Stremio window owner.
Acceptance: observation fixtures cover app close/helpers/ignored windows; a read-only local
run reports VS Code, Media Player, and Stremio identities where installed. No lights change.

### 06 — Minimal external plugin host and fake plugin

Depends on: 02. Scope: explicitly enabled local package manifest, version handshake,
JSON-RPC stdin/stdout requests, cancellation/timeouts, health, and process shutdown. Implement
one fake executable using the shared contract. Keep protocol logs off stdout.
Acceptance: fake catalog/apply works; unsupported version, malformed responses, timeout, and
plugin exit do not crash the app or hang a request. Removing fake plugin leaves core usable.

### 07 — Command coordination and fallback state

Depends on: 03, 04, 06. Scope: connect commands/selection to fake plugin results; track desired,
applied, pending, and native fallback states. Validate ownership and authority before output.
Implement reconnect notification state with Accept/Decline and coalescing.
Acceptance: failure retains desired state; no fallback cycle or competing writer; reconnect
does not resume automatically; Accept uses current intent; native fallback lacking an output
reports pending. No native renderer needed yet.

### 08 — SignalRGB capability investigation

Depends on: 00. Scope: read-only investigation and evidence notes for process/launcher discovery,
effect catalog/current selection, optional layout catalog/export, account restrictions, and
external application endpoints. Record exact interface/version and reproducible checks.
Acceptance: each capability is verified, unavailable, or unresolved with evidence; resolve
fixture names against actual catalog (including Screen Ambient versus historical Screen
Ambience). No light switching, provider settings edits, or device reconfiguration in this slice.

### 09 — Separate SignalRGB plugin

Depends on: 06, 08. Scope: plugin under its module, status/catalog requests, effect application,
optional supported layout selection, and provider authority/device scope. Omitted layout keeps
current provider placements. Never launch SignalRGB implicitly. Mock external calls in tests.
Acceptance: encoded identifiers and failure paths checked; app/core have no provider project
reference; unsupported APIs return honest errors; removing package keeps core operational.
Live effect application waits for the end-to-end gate in 12.

### 10 — Tray shell and status/manual controls

Depends on: 05, 07. Scope: wire background engine into Avalonia; tray show/quit, profile picker,
manual/automatic control, desired/applied/reason display, errors, and reconnect notification.
Acceptance: closing window leaves engine running; Quit shuts down plugin processes cleanly;
UI stays responsive during timeout; accepting/declining recovery reaches shared commands.
No startup registration or full editor.

### 11 — Editable applications and profile mappings

Depends on: 09, 10. Scope: choose detected/manual apps, available provider effects, optional
layouts, priority order and eligibility. Draft editor with Apply/Cancel and Live preview after
pointer release/completed edits; capability-aware disabled controls.
Acceptance: mappings persist, preview does not save, Cancel restores supported prior output,
unchecked preview sends nothing before Apply. Fake catalogs test unavailable resources;
unsupported SignalRGB catalogs remain a visible limitation, not a fabricated list.

### 12 — First product acceptance gate

Depends on: 11. Scope: run clean build/tests, then the explicitly requested live SignalRGB
test session using the four mappings. Retain current SignalRGB layout and WLED routing.
Acceptance: first-start/manual, restart, automatic selection, priority eligibility, absent
plugin, and recovery notification match requirements. User verifies visible effect switching;
catalog or application limitations are recorded as remaining gates. No native WLED writes.

## Goal 2: game profiles

### 13 — Installed game catalog and assignments

Depends on: 12. Scope: separate Steam discovery integration, manual game entries, scoped IDs,
and generic/per-game profile assignments. Choose the first game with the user before real
fixtures are required. Reuse the existing editor/rules.
Acceptance: duplicate titles across launchers do not collide; running state never invents focus;
static game profiles work without telemetry.

### 14 — One game API adapter

Depends on: 13. Scope: choose one supported API, document its event contract, implement one
separate plugin, and record replay fixtures with source/freshness. Share only proven common
fields; preserve game-specific data namespaced.
Acceptance: replay changes effect inputs; stale/disconnected telemetry degrades predictably;
static selection remains usable. No injection/memory reads or universal game schema.

## Goal 3: native effects, layouts, and outputs

### 15 — Placement model and offline renderer

Depends on: 02; schedule after Goal 2 by default. Scope: coordinates/transforms, stable device
and LED identities, optional direct-only participation, exclusion, and one static/gradient
canvas effect producing frames to fake output. A minimal default native setup is created here.
Acceptance: transform/reversal sampling preserves indices; unassigned devices get no frames;
direct-only devices need no canvas coordinates; unequal LED counts sample normalized positions.

### 16 — WLED plugin and native output gate

Depends on: 06, 07, 15. Scope: WLED status/device capabilities, configured network targets,
static output then frame streaming, bounded timeouts, cleanup/realtime expiry, and ownership.
Use fake network checks first. Before requested live tests, prove target release from SignalRGB.
Acceptance: visible native WLED output with one writer, offline device isolation, stop/reconnect
behavior, and working saved native fallback where dependencies/ownership are available.
No implicit changes to SignalRGB device settings or existing WLED presets.

### 17 — Layout canvas editor

Depends on: 11, 15. Scope: assign/unassign devices, move/rotate/stretch/compress, direct-only
participation, persistence, and existing preview controls.
Acceptance: monitor-only layout excludes wall strips; changing inclusion stops outgoing app
streams through coordinator; editing keeps hardware LED counts unchanged. Check pointer and
keyboard interaction. Native preview can use fake output before live WLED is available.

### 18 — Stacked groups

Depends on: 17. Scope: group/ungroup, shared footprint/transforms, member orientation, and
normalized sampling across different LED counts. Group state belongs to each layout.
Acceptance: 30- and 100-LED strips reproduce the same spatial effect at different resolution;
ungroup preserves identities and documented placement; named mappings still address members.

### 19 — Hybrid game layers

Depends on: 14, 15, 17. Scope: canvas base, named semantic zones/ranges, direct key/LED layer,
deterministic composition, and a small binding editor. Use fake keyboard/device capabilities.
Acceptance: named keys work without precise placement; a direct key overrides only its target;
strip/fan zones work without per-LED edits; exclusions and provider authority remain enforced.

### 20 — Native screen ambience and calibration

Depends on: 16, 17. Scope: Windows capture input, selected monitor, canvas mapping, native
per-device color calibration. Split capture and calibration into children if needed.
Acceptance: monitor direction and desk exclusion are correct; cancellation releases capture;
fake images check sampling; user validates actual colors. Capture is independent of app focus.

### 21 — One native peripheral backend

Depends on: 19. Scope: choose keyboard or mouse and one actual supported model, then implement
its separate output plugin and semantic/geometry map. Repeat as new slices for other models.
Acceptance: ownership/release verified, actual LEDs checked, macros/special keys preserved;
no support claims for untested devices.

## Follow-on slices

### 22 — Public SDK and contributor example

Depends on: 09, 16. Scope: extract the proven protocol/SDK, compatibility guidance, template,
and standalone example package. Acceptance: build/install the example without changing app
code; incompatible contracts fail clearly. Signing/marketplace remains a separate future task.

### 23 — Local MCP commands

Depends on: 07, 11. Scope: local server/client boundary using shared query/command services,
profiles/rules/priority/manual mode/preview tools and existing recovery acceptance.
Acceptance: tool calls follow the same validation/persistence/authority paths as UI; local
access restrictions work; no direct configuration-file edits. Model choice is not required.

### 24 — Startup, packaging, and recovery gate

Depends on: 12; repeat relevant gate checks after native output ships. Scope: packaging,
optional user-controlled startup, crash journal recovery, and clean shutdown/reconnect checks.
Acceptance: install/run/uninstall checks preserve user settings; recovered stale output is
reported honestly; startup applies saved intent without granting recovered plugins authority
against the user's reconnect preference. No unrelated task/service changes.

### 25 — SignalRGB independence gate

Depends on: required native slices 16–21 and 24. Scope: inventory remaining provider-dependent
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
00 | complete | see git log for docs/minimal-contract.md | first run, restart, absent plugin, explicit selection, priority examples; JSON parsed; no provider types
```
