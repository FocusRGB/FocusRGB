# Focus-based RGB controller: delivery plan

Planning only. Updated 2026-10-08. Replaces v2's exclusive `signalrgb` / `native` profile
types with composable plugin actions. End goal: replace SignalRGB, retain other app integrations.

Read `app-brief.md`, `plugin-architecture.md`, and `integration-evidence.md` first.
`focus-profiles-plan-v2.md` is historical, not current guidance.

## Milestones

| Milestone | Deliverable | Acceptance |
|---|---|---|
| 0: documentation | Shared brief, architecture, evidence, handoff | Another model can continue without the chat; completed by this update |
| 1: dry run | Focus detector, manual catalog, rule engine, fake output | Correct debounced media/code/game selection, stable priority, ignored/unmatched windows do not churn, no lights change |
| 2: SignalRGB | Capability discovery, built-in URL selection, overrides if transport established | Actual effect selection observed; unavailable actions explained; overrides visibly checked and cleared on exit/reconnect |
| 3: WLED/ownership | Presets/static zones then streaming, transition coordinator | One writer, no flicker, offline boards do not block core, state restored where supported; automatic handover proven or fixed native ownership chosen |
| 4: games | Steam scan, manual entries, generic/per-game maps | Catalog matches installed games; running background game does not falsely claim focus; static profiles need no telemetry |
| 5: native effects/media | Own effects, layouts, screen mirror, calibration | Correct display/strip direction, desk exclusion, user validates colours step by step; media/code strips work without SignalRGB |
| 6: native peripherals | Keyboard and mouse backends in chosen order | Actual device support/maps confirmed; no competing writers; macros/special keys preserved |
| 7: reliability/startup | Chosen UI, diagnostics, manual pause, recovery, optional startup | Predictable reconnect/startup, restart recovers stale overrides, visible selection/ownership reasons |
| 8: retire SignalRGB | Replace remaining dependent effects/actions | Required profiles work with SignalRGB stopped; other integrations remain; user confirms before uninstall/startup cleanup |

This is proposed order, not authorization for live changes. Cleanup/recovery belongs with
the first live output, even if full reliability is a later milestone. API acceptance alone
does not satisfy visual acceptance.

## First implementation slice

Choose runtime and initial interface when implementation is requested. Build detector,
rules, and fake output before hardware. Check these behaviors:

1. Code.exe held beyond debounce selects `code` once.
2. Media foreground held beyond debounce selects `media` once.
3. Brief focus changes do not switch profiles.
4. Ignored/unmatched apps keep current profile.
5. Background Steam game does not become foreground just from its running ID.
6. Tied priorities resolve deterministically.
7. Missing/unsupported plugin actions produce a reason without output.

Before hardware work, establish standalone override transport and WLED ownership. Black
SignalRGB output does not release devices. `enabled` writes are unresolved. Avoid competing
streams while investigating ownership.

Open runtime/UI, game style, capture display, interim game effect, ownership fallback,
and native-peripheral order are in `../.ai/STATUS.md`. About two seconds debounce and
keep-current unmatched behavior are inherited proposed defaults, adjustable by the user.
