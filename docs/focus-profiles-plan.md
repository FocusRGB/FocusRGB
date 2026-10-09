# Focus-based RGB controller: delivery plan

Planning only. Updated 2026-10-09. Replaces v2's exclusive `signalrgb` / `native` profile
types with composable plugin actions. End goal: replace SignalRGB, retain other app integrations.

Read `app-brief.md`, `plugin-architecture.md`, and `integration-evidence.md` first.
`focus-profiles-plan-v2.md` is historical, not current guidance.

## Implementation goals

1. **Working core with SignalRGB.** Build the Avalonia tray/background shell, detection,
   persisted rules and priorities, manual mode, profile selection, fake output tests, and a
   real SignalRGB adapter. Finish when a detected or manually selected profile visibly changes
   the configured SignalRGB effect and failures are explained.
2. **Game profiles.** Define each profile as an effect plus LED layout, add game discovery and
   manual mappings, then add supported game APIs for reactive effects. Static game profiles
   must work without telemetry.
3. **Native effects and layouts.** Implement the renderer, mappings/editor, calibration, and
   native output plugins behind the same profile model. Migrate profiles incrementally until
   SignalRGB is no longer required.

The open plugin protocol/SDK, MCP adapter, recovery, and packaging support these goals. They
must not become speculative blockers for the first SignalRGB vertical slice.

## Detailed milestones

| Milestone | Deliverable | Acceptance |
|---|---|---|
| 0: documentation | Shared brief, architecture, evidence, handoff | Another model can continue without the chat; completed by this update |
| 1: core + SignalRGB | Avalonia/.NET 10 tray/background core, detector, persisted rules, manual mode, profile selection, fake output, SignalRGB adapter | Automatic and manual selection visibly switch a configured SignalRGB effect; priorities are deterministic; failures are explained |
| 2: game profiles | Effect-plus-layout profile model, Steam/manual discovery, generic and per-game assignments | Installed games can select distinct profiles; background games do not falsely claim focus; layouts and effects are independently reusable |
| 3: game APIs | First supported telemetry integration and event-driven effect inputs | Telemetry drives an effect when available; stale/missing data degrades safely to the static game profile |
| 4: native engine | Own effect renderer, layout mappings/editor, screen mirror, calibration | Effects render independently of SignalRGB with correct display/strip direction and validated colours |
| 5: native outputs | WLED ownership/streaming and keyboard/mouse backends in chosen order | One writer per device, offline hardware does not block core, macros/special keys are preserved |
| 6: public plugin SDK | Extracted external-process protocol, manifest validation, .NET SDK/template, sample plugin | A third-party plugin can be installed locally, reports health/capabilities, times out safely, and cannot crash the core process |
| 7: local MCP | Authenticated local server over shared command services | Model can explain selection and safely modify profiles/priorities/manual mode without bypassing validation |
| 8: reliability/startup | Diagnostics, recovery, optional startup | Predictable reconnect/startup, restart recovers stale overrides, visible selection/ownership reasons |
| 9: retire SignalRGB | Replace remaining dependent effects/layouts | Required profiles work with SignalRGB stopped; other integrations remain; user confirms before uninstall/startup cleanup |

This is proposed order, not authorization for live changes. Cleanup/recovery belongs with
the first live output, even if full reliability is a later milestone. API acceptance alone
does not satisfy visual acceptance.

## First implementation slice

Create the Avalonia/.NET 10 shell and UI-independent background core. Build detector, rules,
fake output, and the narrow SignalRGB effect-selection adapter before hardware ownership or
third-party plugin loading. Check these behaviors:

1. Code.exe held beyond debounce selects `code` once.
2. Media foreground held beyond debounce selects `media` once.
3. Brief focus changes do not switch profiles.
4. Ignored/unmatched apps keep current profile.
5. Background Steam game does not become foreground just from its running ID.
6. Tied priorities resolve deterministically.
7. Missing/unsupported plugin actions produce a reason without output.
8. Manual mode prevents automatic switching until automatic mode is explicitly resumed.
9. Changing rule priority changes the winner predictably and persists across restart.
10. The selected profile maps to a configured SignalRGB effect and visibly applies it.
11. SignalRGB absent, incompatible, or rejected actions are reported without launching it or
    pretending the profile was applied.

Before hardware work, establish standalone override transport and WLED ownership. Black
SignalRGB output does not release devices. `enabled` writes are unresolved. Avoid competing
streams while investigating ownership.

Open game style/API, capture display, interim game effect, ownership fallback, plugin
distribution, and native-peripheral order are in `../.ai/STATUS.md`. About two seconds debounce and
keep-current unmatched behavior are inherited proposed defaults, adjustable by the user.
