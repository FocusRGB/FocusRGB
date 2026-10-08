# Focus-based RGB controller: delivery plan

Planning only. Updated 2026-10-08. Replaces v2's exclusive `signalrgb` / `native` profile
types with composable plugin actions. End goal: replace SignalRGB, retain other app integrations.

Read `app-brief.md`, `plugin-architecture.md`, and `integration-evidence.md` first.
`focus-profiles-plan-v2.md` is historical, not current guidance.

## Milestones

| Milestone | Deliverable | Acceptance |
|---|---|---|
| 0: documentation | Shared brief, architecture, evidence, handoff | Another model can continue without the chat; completed by this update |
| 1: dry run | Avalonia/.NET 10 tray shell, background core, focus detector, manual catalog, rule engine, fake output | Correct debounced media/code/game selection, editable deterministic priority, manual override, ignored/unmatched windows do not churn, no lights change |
| 2: plugin SDK | External-process protocol, manifest validation, .NET SDK/template, sample fake plugin | A third-party plugin can be installed locally, reports health/capabilities, times out safely, and cannot crash the core process |
| 3: SignalRGB | Capability discovery, built-in URL selection, overrides if transport established | Actual effect selection observed; unavailable actions explained; overrides visibly checked and cleared on exit/reconnect |
| 4: WLED/ownership | Presets/static zones then streaming, transition coordinator | One writer, no flicker, offline boards do not block core, state restored where supported; automatic handover proven or fixed native ownership chosen |
| 5: games | Steam scan, manual entries, generic/per-game maps, first supported game API | Catalog matches installed games; background games do not falsely claim focus; telemetry drives an effect when available and static profiles work without it |
| 6: native effects/media | Own effects, layouts, screen mirror, calibration | Correct display/strip direction, desk exclusion, user validates colours step by step; media/code strips work without SignalRGB |
| 7: native peripherals | Keyboard and mouse backends in chosen order | Actual device support/maps confirmed; no competing writers; macros/special keys preserved |
| 8: local MCP | Authenticated local server over shared command services | Model can explain selection and safely modify profiles/priorities/manual mode without bypassing validation |
| 9: reliability/startup | Diagnostics, recovery, optional startup | Predictable reconnect/startup, restart recovers stale overrides, visible selection/ownership reasons |
| 10: retire SignalRGB | Replace remaining dependent effects/actions | Required profiles work with SignalRGB stopped; other integrations remain; user confirms before uninstall/startup cleanup |

This is proposed order, not authorization for live changes. Cleanup/recovery belongs with
the first live output, even if full reliability is a later milestone. API acceptance alone
does not satisfy visual acceptance.

## First implementation slice

Create the Avalonia/.NET 10 shell and UI-independent background core. Build detector, rules,
and fake output before hardware or third-party plugin loading. Check these behaviors:

1. Code.exe held beyond debounce selects `code` once.
2. Media foreground held beyond debounce selects `media` once.
3. Brief focus changes do not switch profiles.
4. Ignored/unmatched apps keep current profile.
5. Background Steam game does not become foreground just from its running ID.
6. Tied priorities resolve deterministically.
7. Missing/unsupported plugin actions produce a reason without output.
8. Manual mode prevents automatic switching until automatic mode is explicitly resumed.
9. Changing rule priority changes the winner predictably and persists across restart.

Before hardware work, establish standalone override transport and WLED ownership. Black
SignalRGB output does not release devices. `enabled` writes are unresolved. Avoid competing
streams while investigating ownership.

Open game style/API, capture display, interim game effect, ownership fallback, plugin
distribution, and native-peripheral order are in `../.ai/STATUS.md`. About two seconds debounce and
keep-current unmatched behavior are inherited proposed defaults, adjustable by the user.
