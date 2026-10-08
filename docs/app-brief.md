# RGB controller: product brief

Planning only. Updated 2026-10-08 from the RGB chat and user clarification.

## Goal

Build a Windows RGB controller that selects profiles based on the focused application and
controls lighting through plugins. Ultimately it should supply its own effects and replace
SignalRGB, while retaining integrations with other applications through supported APIs and
interfaces. Removing SignalRGB must not require rewriting detection, rules, or layouts.

## Desired behavior

| Focus/context | Profile behavior |
|---|---|
| Stremio, PotPlayer, Windows Media Player (classic or new) | Media: SignalRGB Screen Ambience initially, own screen mirror later |
| VS Code | Code: SignalRGB Aurora initially, native equivalent later |
| Recognized game | Generic or per-game profile with custom keyboard/mouse maps and strip zones |
| Brief focus change or ignored system window | Avoid disruptive switches |
| Unmatched app | Keep current profile by default |
| Missing or unsupported backend | Explain the limitation; only use an explicitly configured fallback |
| Game loses focus, profile exits, or app shuts down | Clear app-owned overrides and release output safely |

Profiles can combine backends: direct WLED strip output and SignalRGB keyboard overrides,
for example. They are not restricted to one exclusive `signalrgb` or `native` type.
Game detection is separate from telemetry: recognizing Dota 2 does not provide health or
cooldowns. Reactive effects require a separate supported integration.

## Design requirements

- Plugins cover focus/app detection, installed-app/game discovery, external integrations,
  RGB output, and extensible native effects. Integration-specific files are packaged under
  `modules/<integration>/`; a module may provide several plugin roles.
- Core owns rule arbitration, debounce, transitions, settings, logging, and device ownership.
- Match executable names, launcher-scoped game IDs, and manually added apps/games.
- Define named keyboard LEDs, mouse zones, strip ranges, and screen zones explicitly.
- Media mirroring can exclude desk LEDs and respect monitor strip direction.
- Native output can support per-device colour calibration, validated with user feedback.
- Local profile selection and WLED output should not require cloud services.
- Respect available backend capabilities and account restrictions. The URL scheme is not
  a paid-effect bypass.
- Preserve Synapse macros and special keys during the transition.
- Prefer passive detection without game injection or memory reads; do not promise universal
  anti-cheat compatibility from historical tests.
- Show selection reasons, actual device ownership, and degraded actions.
- Store versioned settings/profiles and keep documentation usable by any AI model.

## Scope

First: dry-run detection and rule selection. Next: interim SignalRGB adapter, WLED output,
and game discovery. Later: native screen mirror/effects and native keyboard/mouse output,
then retire SignalRGB. Other application integrations remain supported in the final app.

Runtime, language, UI, plugin packaging, and installation method are undecided. Startup
automation follows reliable cleanup/recovery. The Arduino tester is independent. Existing
`modules/signalrgb/plugins/` contains SignalRGB device plugins, not this controller's
extension system.

Current authorization is documentation, not implementation or device reconfiguration.
Open product/implementation choices are tracked in `../.ai/STATUS.md`.
