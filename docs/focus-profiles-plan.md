# Focus-based RGB controller: delivery plan

Planning only. Updated 2026-10-09. Replaces v2's exclusive `signalrgb` / `native` profile
types with composable plugin actions. End goal: replace SignalRGB, retain other app integrations.

Read `app-brief.md`, `plugin-architecture.md`, and `integration-evidence.md` first.
`focus-profiles-plan-v2.md` is historical, not current guidance.

Use [implementation-slices.md](implementation-slices.md) for the ordered status index and links
to each detailed task, plus session handoff prompts. This document remains the product-level
acceptance plan.

## Implementation goals

1. **Working FocusRGB desktop app.** Finish persistent profile management, manual selection,
   tray/background behavior, application preferences, restart restoration, and useful local
   status before depending on any other desktop application.
2. **Native effects, layouts, and outputs.** Implement the renderer, layout editor, grouping,
   screen ambience, calibration, WLED output, and one peripheral backend behind the same
   profile model. Fake output comes before live hardware.
3. **External applications and automation.** Add process observation and application mappings,
   then SignalRGB, games/game APIs, and local-model MCP control. External failures must never
   make manual profiles or the native app unusable.

The plugin protocol/SDK is introduced when native output needs it. SignalRGB-specific and game
work stays late in the roadmap and cannot block the FocusRGB app or native engine.

## Detailed milestones

| Milestone | Deliverable | Acceptance |
|---|---|---|
| 0: documentation | Shared brief, architecture, evidence, handoff | Another model can continue without the chat; completed by this update |
| 1: desktop app | Windows Avalonia/.NET 10 tray/background app, profile CRUD, persistence, manual mode, settings | Default is protected; profile changes and the last manual selection survive restart without an external application |
| 2: native engine | Built-in effect renderer, layout editor, groups, screen ambience, calibration | Effects render to fake output with correct geometry, exclusions, direction, and reproducible sampling |
| 3: native outputs | Minimal plugin host, ownership/recovery, WLED, then one keyboard/mouse backend | One writer per device, offline hardware does not block the app, macros/special keys are preserved |
| 4: reliability/startup | Packaging, diagnostics, recovery, optional startup | Install/uninstall preserves user state; restart recovers stale output and reports ownership honestly |
| 5: desktop automation | Windows observations and persisted detected/manual application mappings | Priority/focus behavior selects profiles; process failures never break manual operation |
| 6: SignalRGB | Verified capabilities and a separate SignalRGB plugin | Core/app have no SignalRGB dependency; supported effects switch visibly and failures are explained |
| 7: game profiles and APIs | Steam/manual discovery, assignments, and one supported telemetry adapter | Static profiles work without telemetry; stale/missing events degrade safely |
| 8: external control and SDK | Local MCP plus stabilized public plugin SDK/template | External commands use validated app services; third-party plugins build without an app checkout |
| 9: retire SignalRGB | Replace remaining dependent effects/layouts | Required profiles work with SignalRGB stopped; other integrations remain; user confirms before uninstall/startup cleanup |

This is proposed order, not authorization for live changes. Cleanup/recovery belongs with
the first live output, even if full reliability is a later milestone. API acceptance alone
does not satisfy visual acceptance.

## App-first acceptance path

Finish the FocusRGB app and native path before desktop-application integrations:

1. First start selects protected Default in manual mode.
2. Users can create, rename, duplicate, and delete profiles; Default cannot be removed or renamed.
3. Profile bindings, fallbacks, and the last manual selection survive restart.
4. Apply/Cancel and optional Live preview never silently persist a draft.
5. The tray keeps the engine alive, while Quit performs a clean save and shutdown.
6. Close behavior, theme, start-minimized, and diagnostics follow the application settings.
7. Built-in static/gradient effects render through stable LED identities to fake output.
8. Layouts support placement, transforms, exclusions, and stacked groups with unequal LED counts.
9. Screen ambience uses selected-display capture and releases capture on cancellation.
10. WLED and the first native peripheral backend enforce one writer, bounded failure, and cleanup.
11. Packaging, optional startup, and recovery work without SignalRGB or observed applications.

After that gate, add Windows application observations and persisted mappings, then SignalRGB,
games, and MCP. Existing selection-engine work remains reusable but does not dictate delivery order.

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

Open game style/API, capture display, interim game effect, ownership fallback, plugin
distribution, and native-peripheral order are in `../.ai/STATUS.md`. About two seconds debounce
is the proposed default; unmatched automatic selection uses the protected Default profile.
