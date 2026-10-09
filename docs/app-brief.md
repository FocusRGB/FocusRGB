# RGB controller: product brief

Planning only. Updated 2026-10-09 from the RGB chat and user clarification.

## Goal

Build a cross-platform desktop RGB controller with a minimal Avalonia UI on .NET 10 and an
always-running background process. It selects profiles from focused or running applications
and controls lighting through open extension contracts. Ultimately it should supply its own
effects and replace SignalRGB, while retaining integrations with other applications through
supported APIs and interfaces. Removing SignalRGB must not require rewriting detection,
rules, layouts, or game integrations.

## Desired behavior

| Focus/context | Profile behavior |
|---|---|
| First start | Enter manual mode with the Default profile |
| Default profile | Keep the same effect/layout regardless of the focused application; test with SignalRGB Aurora |
| VS Code | Test with SignalRGB Aurora |
| Media Player | Test with SignalRGB Logarithmic Visualizer |
| Stremio | Test with SignalRGB Screen Ambient |
| Recognized game | Generic or per-game profile with custom keyboard/mouse maps and strip zones |
| Brief focus change or ignored system window | Avoid disruptive switches |
| Unmatched app | Keep current profile by default |
| Manual mode | Keep the user-selected profile active until automatic selection is resumed |
| Restart | Restore and apply the last mode and selected profile before normal detection continues |
| Missing or unsupported backend | Explain the limitation; only use an explicitly configured fallback |
| Game loses focus, profile exits, or app shuts down | Clear app-owned overrides and release output safely |

Profiles can combine backends: direct WLED strip output and SignalRGB keyboard overrides,
for example. They are not restricted to one exclusive `signalrgb` or `native` type.
Game detection is separate from telemetry: recognizing Dota 2 does not provide health or
cooldowns. Reactive effects require a separate supported integration.

Conceptually, every profile combines an RGB effect with an LED layout. The effect describes
which colours to produce; the layout maps logical LEDs and zones onto actual devices. During
the SignalRGB phase the effect can select a SignalRGB resource, while the layout is stored as
part of our profile and only applied where the adapter has a verified capability. The later
native engine must preserve the profile concept so users can replace the provider without
rebuilding their rules and game assignments.

Application matches and provider resources are user-configurable. The UI lists detected or
manually entered applications and asks the active integration for its available effects and
layouts. Profiles store provider resource IDs when available and display names for the user;
missing resources remain visible as unavailable instead of silently selecting another one.

Each integration binding declares rendering authority. With `provider` authority, the
provider's effect and layout take priority and the app must not render its own effect/layout
to the same owned devices. With `application` authority, the app's native effect/layout is
used and normal device-ownership rules apply. SignalRGB profiles default to `provider`.

## Design requirements

- Plugins cover focus/app detection, installed-app/game discovery, external integrations,
  RGB output, and extensible native effects. Integration-specific files are packaged under
  `modules/<integration>/`; a module may provide several plugin roles.
- The core references only integration contracts. SignalRGB-specific code is a separate
  plugin and must never be referenced by the core or Avalonia application projects.
- Integration contracts expose provider effect/layout catalogs and accept a rendering-authority
  choice. The UI persists the user's application, effect, layout, and authority mappings.
- Publish the plugin contract, schemas, templates, and reference plugins as open-source
  components so third parties can add integrations without modifying the application.
- Keep platform support explicit in each plugin manifest. A plugin may be portable or may
  provide separate Windows, macOS, and Linux entry points behind the same contract.
- Core owns rule arbitration, debounce, transitions, settings, logging, and device ownership.
- Users can reorder or assign priorities to automatic rules. Manual mode overrides automatic
  detection; ties are deterministic and the UI explains why the active profile won.
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
- Game integrations expose versioned events from supported game APIs. Effects consume
  normalized capabilities where practical and namespaced game-specific data when necessary;
  unsupported telemetry degrades to a static profile rather than breaking selection.
- Expose a local MCP server whose tools use the same validated application commands as the UI.
  It can inspect and change profiles, rules, priorities, manual mode, and effect settings,
  subject to local authorization and confirmation for risky operations.

## Scope

The first implementation goal is a working Avalonia/.NET 10 Windows executable with a portable
background core and a separately packaged SignalRGB plugin: detection, deterministic/manual
selection, profiles, and visible SignalRGB effect switching. A fake integration remains useful
for tests, but a generic public plugin SDK must not delay this vertical slice.

The initial acceptance profiles are Default → Aurora, VS Code → Aurora, Media Player →
Logarithmic Visualizer, and Stremio → Screen Ambient. Their SignalRGB layouts remain
user-selected from the plugin catalog. Default is application-independent. On the first run it
is selected in manual mode. Later starts restore and apply the last mode and profile; restored
automatic mode may select another profile after the detector produces a stable match.

The second goal is game profiles. Each profile combines an effect and LED layout, can be
selected manually or by game/app rules, and may later consume supported game API events.

The third goal is the app's own effect renderer and layout system, followed by native output
plugins, so profiles can migrate away from SignalRGB without changing selection rules. The
external plugin SDK, local MCP control, reliability, and distribution work should grow around
these goals rather than becoming prerequisites for the first visible result.

Plugin package signing/distribution and installation UX remain undecided. Startup automation
follows reliable cleanup/recovery. The Arduino tester is independent. Existing
`modules/signalrgb/plugins/` contains SignalRGB device plugins, not this controller's
extension system.

Current authorization is documentation, not implementation or device reconfiguration.
Open product/implementation choices are tracked in `../.ai/STATUS.md`.
