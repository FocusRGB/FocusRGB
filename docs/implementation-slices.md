# FocusRGB implementation slices

This is the compact roadmap and status index. Each linked slice file contains its dependency,
scope, and acceptance criteria. The active slice's exact resume state belongs in
[`../.ai/STATUS.md`](../.ai/STATUS.md), not here.
GitHub issue and milestone mappings are recorded in [`github-roadmap.md`](github-roadmap.md).

Requirements live in [`app-brief.md`](app-brief.md), contracts and behavior in
[`plugin-architecture.md`](plugin-architecture.md), and verified external facts in
[`integration-evidence.md`](integration-evidence.md).

Status: **00–12 implemented. 12 is awaiting review and merge; 13 is next afterward.**

## 1 — Foundation

- ✅ [00 — Minimal contract](slices/1-Foundation/00-freeze-the-minimal-data-and-command-contract.md):
  freeze settings, state, commands, plugin protocol, and fallback shapes.
- ✅ [01 — Solution scaffold](slices/1-Foundation/01-solution-scaffold-and-repeatable-checks.md):
  create the .NET 10/Avalonia solution, portable boundaries, and repeatable checks.
- ✅ [02 — Profiles and validation](slices/1-Foundation/02-profiles-and-configuration-validation.md):
  model and validate Default, Programming, Movie, Music, and their rules.
- ✅ [03 — UI shell](slices/1-Foundation/03-ui-shell-with-preview-state.md):
  add the mode-driven Home tab, profile controls, navigation, and explicit preview-only status.
- ✅ [04 — Mapping editor](slices/1-Foundation/04-profile-mapping-editor-with-preview-state.md):
  edit named application groups and ordered mappings with Save/Cancel and preview state.

## 2 — Core Engine

- ✅ [05 — Selection engine](<slices/2-Core Engine/05-pure-selection-engine.md>):
  arbitrate saved-list priority, manual override, focus debounce, ignored windows, and Default fallback.
- ✅ [06 — Persistence](<slices/2-Core Engine/06-saved-state-and-startup-restoration.md>):
  atomically save and restore settings, desired state, and the native fallback snapshot.

## 3 — FocusRGB App

- ✅ [07 — Profile editor and persistence](<slices/3-FocusRGB App/07-profile-editor-and-persistence.md>):
  create, edit, duplicate, and remove persisted profiles while protecting Default.
- ✅ [08 — Desktop runtime and tray](<slices/3-FocusRGB App/08-desktop-runtime-and-tray.md>):
  run manual profile state in the tray app with real local status and clean shutdown.
- ✅ [09 — Application settings](<slices/3-FocusRGB App/09-application-settings-and-lifecycle-preferences.md>):
  persist desktop lifecycle, theme, and diagnostic preferences in a small Settings page.

## 4 — Plugin Runtime and Coordination

- ✅ [10 — Plugin runtime, SDK, and fake output](<slices/4-Plugin Runtime and Coordination/10-minimal-plugin-runtime-and-fake-output.md>):
  package the minimal SDK and host a JSON-RPC fake output plugin required by provider plugins.
- ✅ [11 — Command coordination and fallback](<slices/4-Plugin Runtime and Coordination/11-command-coordination-and-fallback-state.md>):
  track desired/applied/pending state, fallback, ownership, and reconnect approval.

## 5 — External Applications

- ✅ [12 — Windows application observations](<slices/5-External Applications/12-windows-application-observations.md>):
  detect foreground and running applications behind the platform boundary.
- ✅ [13 — Application mappings](<slices/5-External Applications/13-application-mappings-and-persistence.md>):
  add detected/manual applications, persist automation rules, and wire automatic profile selection. **Awaiting review.**
- ⏳ [14 — SignalRGB investigation](<slices/5-External Applications/14-signalrgb-capability-investigation.md>):
  verify supported APIs, catalogs, selection, layouts, and limitations.
- ⏳ [15 — External SignalRGB plugin](<slices/5-External Applications/15-separate-signalrgb-plugin.md>):
  create its repository and implement provider status, catalogs, and effect/layout application.
- ⏳ [16 — SignalRGB acceptance gate](<slices/5-External Applications/16-signalrgb-acceptance-gate.md>):
  connect the already-selected profile to SignalRGB and verify the complete switching path with the user.

## 6 — Games and External Control

- ⏳ [17 — Stabilize the public plugin SDK](<slices/6-Games and External Control/17-public-sdk-and-contributor-example.md>):
  promote the fake- and SignalRGB-proven SDK to version 1 with templates and compatibility guidance.
- ⏳ [18 — Local MCP commands](<slices/6-Games and External Control/18-local-mcp-commands.md>):
  expose the same validated UI commands to a local model.
- ⏳ [19 — Installed game catalog](<slices/6-Games and External Control/19-installed-game-catalog-and-assignments.md>):
  discover installed games through plugins and assign static generic or per-game profiles.
- ⏳ [20 — One game API adapter](<slices/6-Games and External Control/20-one-game-api-adapter.md>):
  implement one game-data plugin using the public SDK with namespaced, replayable events.

## 7 — Native RGB System

- ⏳ [21 — Placement model and renderer](<slices/7-Native RGB System/21-placement-model-and-offline-renderer.md>):
  model LED geometry, built-in effects, exclusions, and offline canvas rendering.
- ⏳ [22 — WLED plugin and output gate](<slices/7-Native RGB System/22-wled-plugin-and-native-output-gate.md>):
  add safe native WLED frame output without making it a prerequisite for games.
- ⏳ [23 — Layout canvas editor](<slices/7-Native RGB System/23-layout-canvas-editor.md>):
  add the native layout editing required by hybrid canvas layers.
- ⏳ [24 — Hybrid game layers](<slices/7-Native RGB System/24-hybrid-game-layers.md>):
  combine canvas effects, semantic zones, and direct LED/key mappings.
- ⏳ [25 — Stacked groups](<slices/7-Native RGB System/25-stacked-groups.md>):
  group similar devices at shared positions with normalized sampling.
- ⏳ [26 — Screen ambience and calibration](<slices/7-Native RGB System/26-native-screen-ambience-and-calibration.md>):
  capture a display, sample layouts, and calibrate native output against fake devices first.
- ⏳ [27 — Native peripheral backend](<slices/7-Native RGB System/27-one-native-peripheral-backend.md>):
  support one verified keyboard or mouse model through a separate output plugin.
- ⏳ [28 — Packaging and recovery gate](<slices/7-Native RGB System/28-startup-packaging-and-recovery-gate.md>):
  verify install, optional startup, crash recovery, shutdown, and uninstall after external work.
- ⏳ [29 — SignalRGB independence gate](<slices/7-Native RGB System/29-signalrgb-independence-gate.md>):
  verify required profiles, effects, layouts, and devices without SignalRGB.

## Shared execution rules

- Work one slice at a time and verify its dependencies against actual code and tests.
- A slice may span sessions; record partial work and resume it instead of advancing early.
- Keep every completed slice independently reviewable on a scoped branch and pull request.
- Use multiple purpose-focused commits per pull request; do not collapse a whole slice into one commit.
- If a slice grows, split it into lettered children with explicit dependencies and acceptance checks.
- Preserve unrelated changes and use mock integrations until a live check is explicitly requested.
- Record API acceptance separately from visible lighting evidence; never invent unavailable capability data.
- Mark external capability gaps blocked or unsupported and continue independent work where possible.
- Do not rebuild the repository graph for every slice; use it only when dependency discovery needs it.

## Source boundaries

- `src/FocusRgb.Contracts/`: provider-neutral plugin messages, capabilities, results, and IDs.
- `src/FocusRgb.PluginSdk/`: plugin-side protocol helpers, packaged from the main repository.
- `src/FocusRgb.Core/`: profiles, arbitration, persistence, commands, fallback, and ownership.
- `src/FocusRgb.Platform.Windows/`: Windows process/focus detection and later screen capture.
- `src/FocusRgb.App/`: Avalonia tray/settings UI and generic plugin-host wiring.
- `tests/`: core, contract, integration, platform, UI, and fake-plugin conformance checks.
- `FocusRGB-Plugin-<Name>` repositories: production integration executables and releases.
- `modules/signalrgb/`: temporary research/tool staging until slice 15 migration.
- `build/`: ignored generated output, reports, packages, and tool artifacts.

Core may reference shared contracts; neither Core nor App references provider projects. Portable
Contracts/Core target .NET 10 without Windows APIs. The first app executable runs on Windows.
Use one background engine hosted by the tray app initially; a separate OS service is not required.

## Copy-paste prompts

Start:

> Implement slice NN from `docs/implementation-slices.md`. Read `.ai/AGENTS.md` and
> `.ai/STATUS.md`, then open the linked slice file and its relevant requirement/architecture
> sections. Verify dependencies against actual code and checks. Implement only this slice, run
> its acceptance checks, and update `.ai/STATUS.md` using `.ai/SLICE-HANDOFF.md`. Use multiple
> purpose-focused commits on a slice branch, push it, and open a pull request into its immediate
> dependency branch (or `develop` when it has no unmerged dependency).
> Report external gates honestly and do not run live hardware tests unless I request them.

Resume after a limit:

> Resume the active slice recorded in `.ai/STATUS.md`. Read `.ai/AGENTS.md`, the handoff, and
> the slice file linked from `docs/implementation-slices.md`. Inspect git status/diff and continue
> from the recorded next action. Preserve partial work; do not restart, redesign completed code,
> or advance to another slice. Re-run checks only where unfinished work or new edits require it.

Review:

> Review completed slice NN against its linked acceptance criteria and connected code. Check
> dependency boundaries, failure cases, and test evidence. Report concrete issues; do not expand
> scope or implement another slice.
