# Focus-based RGB controller: delivery plan

Active delivery plan. Updated 2026-10-16. Replaces the earlier exclusive `signalrgb` / `native`
profile types with composable plugin actions. End goal: replace SignalRGB, retain other app
integrations.

Read `app-brief.md`, `plugin-architecture.md`, and `integration-evidence.md` first.

Use [implementation-slices.md](implementation-slices.md) for the ordered status index and links
to each detailed task, plus session handoff prompts. This document remains the product-level
acceptance plan.

## Implementation goals

1. **Working FocusRGB desktop app.** Finish persistent profile management, manual selection,
   tray/background behavior, application preferences, restart restoration, and useful local
   status before depending on any other desktop application.
2. **External applications and automation.** Add process observation and application mappings,
   then SignalRGB using the minimum plugin runtime and coordination required for that workflow.
3. **Games and external control.** Stabilize the proven public SDK, add local-model MCP control,
   game discovery, and one game API adapter.
4. **Native effects, layouts, and outputs.** Implement the renderer, layout editor, grouping,
   screen ambience, calibration, WLED output, and one peripheral backend behind the same
   profile model. Fake output comes before live hardware.

The plugin protocol/SDK is introduced when SignalRGB needs it. Native output consumes the same
boundary later and cannot block the external-application or game workflows.

## Detailed milestones

| Milestone | Deliverable | Acceptance |
|---|---|---|
| 0: documentation | Shared brief, architecture, evidence, handoff | Another model can continue without the chat; completed by this update |
| 1: desktop app | Windows Avalonia/.NET 10 tray/background app, profile CRUD, persistence, manual mode, settings | Default is protected; profile changes and the last manual selection survive restart without an external application |
| 2: desktop automation | Windows observations and persisted detected/manual application mappings | Priority/focus behavior selects profiles; process failures never break manual operation |
| 3: SignalRGB | Minimal plugin host, ownership/recovery, verified capabilities, and a separate SignalRGB plugin | Core/app have no SignalRGB dependency; supported effects switch visibly and failures are explained |
| 4: external control and SDK | Local MCP plus stabilized public plugin SDK/template | External commands use validated app services; third-party plugins build without an app checkout |
| 5: game profiles and APIs | Steam/manual discovery, assignments, and one supported telemetry adapter | Static profiles work without telemetry; stale/missing events degrade safely |
| 6: native engine | Built-in effect renderer, layout editor, groups, screen ambience, calibration | Effects render to fake output with correct geometry, exclusions, direction, and reproducible sampling |
| 7: native outputs | WLED and one keyboard/mouse backend | One writer per device, offline hardware does not block the app, macros/special keys are preserved |
| 8: reliability/startup | Packaging, diagnostics, recovery, optional startup | Install/uninstall preserves user state; restart recovers stale output and reports ownership honestly |
| 9: retire SignalRGB | Replace remaining dependent effects/layouts | Required profiles work with SignalRGB stopped; other integrations remain; user confirms before uninstall/startup cleanup |

This is proposed order, not authorization for live changes. Cleanup/recovery belongs with
the first live output, even if full reliability is a later milestone. API acceptance alone
does not satisfy visual acceptance.

## External-first acceptance path

Finish the FocusRGB app, then its external-application and game workflows before the standalone
native RGB system:

1. First start selects protected Default in manual mode.
2. Users can create, rename, duplicate, and delete profiles; Default cannot be removed or renamed.
3. Profile bindings, fallbacks, and the last manual selection survive restart.
4. Save/Cancel and optional Live preview never silently persist a draft.
5. The tray keeps the engine alive, while Quit performs a clean save and shutdown.
6. Close behavior, theme, start-minimized, and diagnostics follow the application settings.
7. Windows observations and persisted mappings drive automatic profile selection.
8. The separate SignalRGB plugin applies the selected provider effect without coupling Core or App.
9. The fake and SignalRGB plugins prove the public SDK before game discovery and one game adapter use it.
10. Local MCP commands use the same validated application services as the UI.

After that gate, add the standalone native renderer, layouts, WLED output, peripheral output,
packaging, and SignalRGB-independence checks. Existing selection-engine work remains reusable but
does not dictate delivery order.

Native layout/effect acceptance includes: stacked strips with different LED counts sample
the same area; group transforms preserve LED identity; excluded wall strips receive no frames;
Live preview updates on pointer release and Cancel restores applied state; unchecked preview
waits for Apply; direct-only keyboard maps work without canvas placement; direct key layers
override the canvas only on their assigned targets.

For the first tests without SignalRGB, add a separate WLED integration/output plugin and a
placement layout, optionally imported from an available SignalRGB layout through verified
read/export support. Confirm SignalRGB has released the target device before native output.

Before hardware work, establish standalone override transport and WLED ownership. Black
SignalRGB output does not release devices. `enabled` writes are unresolved. Avoid competing
streams while investigating ownership.

## Retained findings from the earlier plan

The earlier v2 plan mixed delivery direction with machine-specific experiments. Its useful
findings are retained here at planning level; [integration-evidence.md](integration-evidence.md)
holds the dated details, versions, addresses, device IDs, commands, and confidence limits.

- Windows foreground lookup was demonstrated, while Steam `RunningAppID` indicates a running
  game rather than foreground ownership. Detection remains passive: no injection, hooks,
  overlays, memory reads, or promises of universal anti-cheat compatibility.
- SignalRGB's URL launcher switched installed free effects in the recorded tests. Paid/custom
  effects did not switch, and the tested MCP/REST lighting paths were Pro-rejected. The plugin
  must discover capabilities, verify outcomes, and never launch SignalRGB implicitly.
- Only one writer may control a physical device. Forced colour, black output, or provider
  disconnection does not prove release; the proposed `enabled` handover was never verified.
  Direct WLED output therefore starts only after ownership is established.
- Static named-key overrides were visually confirmed on the keyboard, but their tool transport
  is not an application API. The BlackWidow appeared through LampArray; the Basilisk did not.
  Native control, animation performance, cleanup after failure, and preservation of macros and
  special keys still require their dedicated slices.
- Recorded executable names, installed games, LED maps, and hardware inventory are discovery
  leads, not configuration defaults. Rescan and confirm them before implementation.

The removed v2-only profile schema, direct SignalRGB runner, scheduled-task design, and phase
numbers are intentionally not retained: they conflict with the provider-neutral contracts and
the numbered slice roadmap that now govern implementation.

Open game style/API, capture display, interim game effect, ownership fallback, plugin
distribution, and native-peripheral order are in `../.ai/STATUS.md`. About two seconds debounce
is the proposed default; unmatched automatic selection uses the protected Default profile.
