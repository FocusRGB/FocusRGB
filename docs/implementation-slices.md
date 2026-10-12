# FocusRGB implementation slices

This is the compact roadmap and status index. Each linked slice file contains its dependency,
scope, and acceptance criteria. The active slice's exact resume state belongs in
[`../.ai/STATUS.md`](../.ai/STATUS.md), not here.

Requirements live in [`app-brief.md`](app-brief.md), contracts and behavior in
[`plugin-architecture.md`](plugin-architecture.md), and verified external facts in
[`integration-evidence.md`](integration-evidence.md).

Status: **00–06 complete on the stacked pull-request chain. 07 is next.**

## 1 — Foundation

- ✅ [00 — Minimal contract](slices/1-Foundation/00-freeze-the-minimal-data-and-command-contract.md):
  freeze settings, state, commands, plugin protocol, and fallback shapes.
- ✅ [01 — Solution scaffold](slices/1-Foundation/01-solution-scaffold-and-repeatable-checks.md):
  create the .NET 10/Avalonia solution, portable boundaries, and repeatable checks.
- ✅ [02 — Profiles and validation](slices/1-Foundation/02-profiles-and-configuration-validation.md):
  model and validate Default, Programming, Movie, Music, and their rules.
- ✅ [03 — UI shell](slices/1-Foundation/03-ui-shell-with-preview-state.md):
  add navigation, mode/profile controls, and explicit preview-only status.
- ✅ [04 — Mapping editor](slices/1-Foundation/04-profile-mapping-editor-with-preview-state.md):
  edit grouped executables, priority, eligibility, Apply/Cancel, and preview state.

## 2 — Core Engine

- ✅ [05 — Selection engine](<slices/2-Core Engine/05-pure-selection-engine.md>):
  arbitrate priority, manual override, focus debounce, ignored windows, and Default fallback.
- ✅ [06 — Persistence](<slices/2-Core Engine/06-saved-state-and-startup-restoration.md>):
  atomically save and restore settings, desired state, and the native fallback snapshot.

## 3 — SignalRGB Product Path

- ▶️ [07 — Windows application observations](<slices/3-SignalRGB Product Path/07-windows-application-observations.md>):
  detect foreground and running applications behind the platform boundary. **Next.**
- ⏳ [08 — Plugin host and fake plugin](<slices/3-SignalRGB Product Path/08-minimal-external-plugin-host-and-fake-plugin.md>):
  host an external JSON-RPC plugin with health, timeouts, and shutdown.
- ⏳ [09 — Command coordination and fallback](<slices/3-SignalRGB Product Path/09-command-coordination-and-fallback-state.md>):
  track desired/applied/pending state, fallback, ownership, and reconnect approval.
- ⏳ [10 — SignalRGB investigation](<slices/3-SignalRGB Product Path/10-signalrgb-capability-investigation.md>):
  verify supported APIs, catalogs, selection, layouts, and limitations.
- ⏳ [11 — SignalRGB plugin](<slices/3-SignalRGB Product Path/11-separate-signalrgb-plugin.md>):
  implement provider status, catalogs, effect/layout application, and honest unsupported results.
- ⏳ [12 — Tray runtime and real status](<slices/3-SignalRGB Product Path/12-tray-runtime-and-real-status-wiring.md>):
  run the engine in the tray app and connect real status and recovery commands.
- ⏳ [13 — Wire mappings](<slices/3-SignalRGB Product Path/13-wire-profile-mappings-to-catalogs-and-persistence.md>):
  connect the editor to persistence, detected apps, and provider catalogs.
- ⏳ [14 — First acceptance gate](<slices/3-SignalRGB Product Path/14-first-product-acceptance-gate.md>):
  verify the complete SignalRGB profile-switching path with the user.

## 4 — Game Support

- ⏳ [15 — Installed game catalog](<slices/4-Game Support/15-installed-game-catalog-and-assignments.md>):
  discover installed games and assign static generic or per-game profiles.
- ⏳ [16 — One game API adapter](<slices/4-Game Support/16-one-game-api-adapter.md>):
  implement one game-data plugin with namespaced, replayable events.

## 5 — Native RGB System

- ⏳ [17 — Placement model and renderer](<slices/5-Native RGB System/17-placement-model-and-offline-renderer.md>):
  model LED geometry, transforms, exclusions, and offline canvas rendering.
- ⏳ [18 — WLED plugin and output gate](<slices/5-Native RGB System/18-wled-plugin-and-native-output-gate.md>):
  control WLED safely with one writer, cleanup, and recovery.
- ⏳ [19 — Layout canvas editor](<slices/5-Native RGB System/19-layout-canvas-editor.md>):
  assign, place, transform, and exclude devices.
- ⏳ [20 — Stacked groups](<slices/5-Native RGB System/20-stacked-groups.md>):
  group similar devices at shared positions with normalized sampling.
- ⏳ [21 — Hybrid game layers](<slices/5-Native RGB System/21-hybrid-game-layers.md>):
  combine canvas effects, semantic zones, and direct LED/key mappings.
- ⏳ [22 — Screen ambience and calibration](<slices/5-Native RGB System/22-native-screen-ambience-and-calibration.md>):
  capture a display, sample the layout, and calibrate native output.
- ⏳ [23 — Native peripheral backend](<slices/5-Native RGB System/23-one-native-peripheral-backend.md>):
  support one verified keyboard or mouse model through a separate plugin.

## 6 — Follow-on Delivery

- ⏳ [24 — Public plugin SDK](<slices/6-Follow-on Delivery/24-public-sdk-and-contributor-example.md>):
  extract stable contracts, templates, compatibility guidance, and an example.
- ⏳ [25 — Local MCP commands](<slices/6-Follow-on Delivery/25-local-mcp-commands.md>):
  expose the same validated UI commands to a local model.
- ⏳ [26 — Packaging and recovery gate](<slices/6-Follow-on Delivery/26-startup-packaging-and-recovery-gate.md>):
  verify install, optional startup, crash recovery, shutdown, and uninstall.
- ⏳ [27 — SignalRGB independence gate](<slices/6-Follow-on Delivery/27-signalrgb-independence-gate.md>):
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
- `src/FocusRgb.Core/`: profiles, arbitration, persistence, commands, fallback, and ownership.
- `src/FocusRgb.Platform.Windows/`: Windows process/focus detection and later screen capture.
- `src/FocusRgb.App/`: Avalonia tray/settings UI and generic plugin-host wiring.
- `modules/signalrgb/controller-plugin/`: separate SignalRGB controller plugin.
- `modules/wled/controller-plugin/`: separate WLED controller plugin.
- `tests/`: core, contract, integration, platform, and UI checks.
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
> purpose-focused commits on a slice branch, push it, and open a pull request into `develop`.
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
