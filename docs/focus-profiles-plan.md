# Focus-based RGB controller: delivery plan

Planning only. Updated 2026-10-09. Replaces v2's exclusive `signalrgb` / `native` profile
types with composable plugin actions. End goal: replace SignalRGB, retain other app integrations.

Read `app-brief.md`, `plugin-architecture.md`, and `integration-evidence.md` first.
`focus-profiles-plan-v2.md` is historical, not current guidance.

## Implementation goals

1. **Working core with a SignalRGB plugin.** Build the Windows Avalonia tray/background shell,
   portable core, detection, persisted rules and priorities, manual mode, profile selection,
   fake integration tests, and a separately packaged SignalRGB plugin. Finish when a detected
   or manually selected profile visibly changes the configured SignalRGB effect and failures
   are explained. Neither the core nor app project may reference SignalRGB code.
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
| 1: core + SignalRGB plugin | Windows Avalonia/.NET 10 tray/background app, portable core, detector, persistence, manual mode, fake integration, separate SignalRGB plugin | Core/app have no SignalRGB dependency; automatic and manual selection visibly switch an effect through the plugin; restart restores the last state |
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

Create the Windows Avalonia/.NET 10 shell and UI-independent portable core. Build detector,
rules, fake integration, and the narrow separately packaged SignalRGB effect-selection plugin
before hardware ownership or general third-party plugin loading. Check these behaviors:

1. First start selects `default` in manual mode, independent of the foreground app, and maps
   it to provider-owned SignalRGB Aurora.
2. Restart restores and applies the last mode and selected profile.
3. The user can choose detected/manual applications and effects/layouts returned by the
   SignalRGB plugin; each mapping persists.
4. `Code.exe` held beyond debounce selects `code` → Aurora once in automatic mode.
5. Windows Media Player held beyond debounce selects `media-player` → Logarithmic Visualizer.
6. Stremio held beyond debounce selects `stremio` → Screen Ambient.
7. Brief focus changes do not switch profiles.
8. Ignored/unmatched apps keep the current profile.
9. Tied priorities resolve deterministically.
10. Missing/unsupported plugin actions produce a reason without output.
11. Manual mode prevents automatic switching until automatic mode is explicitly resumed.
12. Changing rule priority changes the winner predictably and persists across restart.
13. Provider authority prevents the app's effect/layout renderer from writing to devices
    governed by the SignalRGB selection.
14. Initial SignalRGB mappings apply only the effect and retain its current placement layout.
    The UI supports optional explicit layout selection when the plugin verifies that capability.
15. SignalRGB absent, incompatible, or rejected actions are reported without launching it or
    pretending the profile was applied.
16. SignalRGB-managed WLED devices receive no direct commands from the app's WLED plugin.

For the first tests without SignalRGB, add a separate WLED integration/output plugin and a
placement layout, optionally imported from an available SignalRGB layout through verified
read/export support. Confirm SignalRGB has released the target device before native output.

Before hardware work, establish standalone override transport and WLED ownership. Black
SignalRGB output does not release devices. `enabled` writes are unresolved. Avoid competing
streams while investigating ownership.

Open game style/API, capture display, interim game effect, ownership fallback, plugin
distribution, and native-peripheral order are in `../.ai/STATUS.md`. About two seconds debounce and
keep-current unmatched behavior are inherited proposed defaults, adjustable by the user.
