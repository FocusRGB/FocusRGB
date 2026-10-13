# RGB controller: product brief

Planning only. Updated 2026-10-12 from the RGB chat, user clarification, and application
settings decision.

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
| Default profile | Protected and always present; use when no automatic application mapping matches; test with SignalRGB Aurora |
| Programming | VS Code or Visual Studio; test with SignalRGB Aurora |
| Movie | PotPlayer or Stremio; test with SignalRGB Screen Ambient |
| Music | Media Player or Spotify; test with SignalRGB Logarithmic Visualizer |
| Recognized game | Generic or per-game profile with custom keyboard/mouse maps and strip zones |
| Brief focus change or ignored system window | Avoid disruptive switches |
| Unmatched app | Select the protected Default profile |
| Manual mode | Keep the user-selected profile active until automatic selection is resumed |
| Restart | Restore and apply the last mode and selected profile before normal detection continues |
| Close main window | Minimize to the system tray by default, or exit when the user selects that behavior |
| Tray Quit or operating-system shutdown | Exit cleanly and release plugin processes and owned output |
| Missing or unsupported backend | Explain the limitation; only use an explicitly configured fallback |
| Game loses focus, profile exits, or app shuts down | Clear app-owned overrides and release output safely |

Profiles can combine backends: direct WLED strip output and SignalRGB keyboard overrides,
for example. They are not restricted to one exclusive `signalrgb` or `native` type.
Game detection is separate from telemetry: recognizing Dota 2 does not provide health or
cooldowns. Reactive effects require a separate supported integration.

Conceptually, every profile combines an RGB effect with an LED layout. The effect describes
which colours to produce; the layout configures LED placements, including device positions,
LED coordinates, orientation, and zone mappings. SignalRGB profiles may omit an explicit
layout and retain SignalRGB's current placements. Native profiles need a placement layout;
an available SignalRGB layout can seed native testing through a verified import/export path.
The profile concept survives migration without rebuilding rules and game assignments.

Application matches and provider resources are user-configurable. The UI lists detected or
manually entered applications and asks the active integration for its available effects and
layouts. Profiles store provider resource IDs when available and display names for the user;
missing resources remain visible as unavailable instead of silently selecting another one.

Each integration binding declares rendering authority. With `provider` authority, the
provider's effect and layout take priority and the app must not render its own effect/layout
to the same owned devices. With `application` authority, the app's native effect/layout is
used and normal device-ownership rules apply. SignalRGB profiles default to `provider`.

SignalRGB authority also takes precedence over app output routing and native calibration for
the devices it manages. App rules, mode, priorities, and persistence remain effective. Preserve
superseded app rendering settings for later native use. SignalRGB already drives its WLED
devices; a separate WLED integration plugin is required only for operation/testing without
SignalRGB ownership. Both plugins must never write to the same physical WLED device together.

## Design requirements

- Plugins cover focus/app detection, installed-app/game discovery, external integrations,
  RGB output, and extensible native effects. Production plugins use independent
  `FocusRGB-Plugin-<Name>` repositories; a plugin may provide several roles.
- The core references only integration contracts. SignalRGB-specific code is a separate
  repository/package and must never be referenced by the core or Avalonia application projects.
- Integration contracts expose provider effect/layout catalogs and accept a rendering-authority
  choice. The UI persists application-to-profile mappings separately from each profile's
  effect, layout, and authority bindings.
- Publish the plugin contract, schemas, templates, and reference plugins as open-source
  components so third parties can add integrations without modifying the application.
- Keep contracts, the .NET plugin SDK, host, schemas, conformance tests, template, and fake
  reference plugin in the main repository. External plugin repositories consume a published
  SDK version and must build without a local FocusRGB checkout or Git submodule.
- Keep platform support explicit in each plugin manifest. A plugin may be portable or may
  provide separate Windows, macOS, and Linux entry points behind the same contract.
- Core owns rule arbitration, debounce, transitions, settings, logging, and device ownership.
- Application-wide preferences are limited to desktop behavior, appearance, and diagnostics.
  Close-button behavior is user-selectable between minimize to tray and exit, with minimize to
  tray as the default. Profile, mapping, integration, device, layout, and effect controls stay
  in their own editors; see [`application-settings.md`](application-settings.md).
- Users can reorder or assign priorities to automatic rules. Manual mode overrides automatic
  detection; ties are deterministic and the UI explains why the active profile won.
- Each rule can stay eligible while its application is running or only while focused. A higher
  priority running rule can beat a lower priority focused app; focus loss advances to the next
  eligible rule only when configured. Closing an app removes its eligibility.
- Profiles have explicit ordered fallback policies when required plugins/resources are
  unavailable. Preserve desired selection separately from fallback output; reconnect behavior
  is configurable and device ownership must remain exclusive.
- Initial fallback uses the app's default native setup or its last applied native setup after
  user modification. Temporary previews and provider settings do not replace that saved setup.
  A recovered plugin raises a notification; it resumes output only after the user accepts.
  Dismissing/declining keeps the fallback active. Acceptance revalidates current selection and
  device ownership rather than blindly restoring an old profile.
- A Live preview checkbox applies completed edits after pointer release. Without it, output
  waits for Apply. Draft/preview changes do not overwrite saved settings; Cancel restores the
  previously applied state where supported.
- The 2D layout canvas supports position, rotation, stretching/compression, and stacked groups.
  Group members sample the same area at their own LED resolution. Registered devices may be
  left unassigned and excluded; exclusion releases prior app output rather than forcing black.
- Native game profiles combine canvas animation, semantic zones, and named-key/LED mappings.
  Direct key mappings do not require precise keyboard placement; canvas sampling does.
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

The first implementation goal is a self-contained Avalonia/.NET 10 Windows executable with a
portable background core: persistent profile management, manual selection, tray behavior,
application preferences, and restart restoration without depending on another desktop app.

The second goal is the app's native effect renderer and layout system, followed by WLED and one
peripheral output plugin. Profiles gain reusable effects/layouts before external automation.

The later SignalRGB acceptance profiles are Default → Aurora; Programming → Aurora for VS Code and
Visual Studio; Movie → Screen Ambient for PotPlayer and Stremio; and Music → Logarithmic
Visualizer for Media Player and Spotify. These tests leave the layout unset and use SignalRGB's
existing LED placements. Default is application-independent, protected from removal, and chosen
when automatic mode has no eligible mapped application. On first run it is selected in manual
mode. Later starts restore and apply the last mode and profile; restored automatic mode may
select another profile after the detector produces a stable match.

The third goal is external-application support: Windows app detection/mappings, the separately
packaged `FocusRGB-Plugin-SignalRGB`, game discovery/APIs, and local MCP control. Each profile
still combines an effect and LED layout and can later consume supported game events. External
failures must not make manual profiles or native output unusable. The main repository keeps the
minimal SDK and fake integration; the complete public SDK is stabilized only after real plugins.

Plugin package signing/distribution and installation UX remain undecided. Startup automation
is off by default, remains user-controlled, and follows reliable cleanup/recovery. The Arduino
tester is independent. Existing
`modules/signalrgb/plugins/` contains SignalRGB device plugins, not this controller's
extension system.

Current authorization is documentation, not implementation or device reconfiguration.
Open product/implementation choices are tracked in `../.ai/STATUS.md`.
