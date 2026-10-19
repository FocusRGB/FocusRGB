# Integration evidence

Reviewed 2026-10-18. Most observations are historical; the explicitly dated current-state
checks below were repeated on this machine. Verify versions and official docs before coding.

## SignalRGB

The Slice 14 read-only capability matrix and reproducible probes are recorded in
[the 2026-10-18 investigation](https://github.com/FocusRGB/FocusRGB-Plugin-SignalRGB/blob/develop/research/docs/capability-investigation-2026-10-18.md).
It supersedes older assumptions where the results differ.

| Surface | Recorded outcome | Limit |
|---|---|---|
| URL selection | Free installed Rainbow, Aurora, Screen Ambience switched | Verify actual selection after launch; not a paid-effect bypass |
| Paid/custom effect via URL | Dota 2 and API Event Demo did not apply | No established automatic custom-effect selection |
| MCP effect_apply / lighting REST | Pro rejection on tested account | Unavailable capability, not a core dependency |
| Custom Screen Ambience Desk Off on WLED | Runtime Pro message | File existence does not imply a working free backend |
| Keyboard LED overrides | WASD red visually confirmed by user; cleared with `none` | Static named-key control via Claude's tool transport |
| Mouse overrides | Logo/Scrollwheel blue request and restore accepted | No visual confirmation; retest |
| Device settings/handover | Original `enabled` test pending; later mouse write returned `No matching properties found` | Automatic device release is unresolved |

Literal historical launcher commands:

```text
SignalRgbLauncher.exe --url=signalrgb://effect/apply/Rainbow?-silentlaunch-
SignalRgbLauncher.exe --url=signalrgb://effect/apply/Screen%20Ambience?-silentlaunch-
```

Launcher was under `%LOCALAPPDATA%\VortxEngine\`; discover the installed path/handler and
encode effect names. Check SignalRGB is running first. Silent launch is not proof of success.

Recorded interfaces: Canvas events on 16034 and app/lighting REST on 16038. The chat initially
mentioned untested `POST /api/v1/effect/{effectId}/apply`. Later sibling notes report tested
`POST /api/v1/lighting/effects/<effectId>/apply`, also Pro-rejected. Do not copy the initial
guess as the API contract.

**Transport gap:** `device_ledoverrides`/`device_writesetting` are tool names, not established
wire APIs for the controller. Discover supported transport, connection setup, prerequisites,
and error behavior before implementing them. The historical launcher in the external plugin's
[`research/tools`](https://github.com/FocusRGB/FocusRGB-Plugin-SignalRGB/tree/develop/research/tools) is a
lead, not a guaranteed app dependency. Calls took roughly half a second historically;
animation performance and persistent-connection benefits were not measured.

Current check on 2026-10-08: SignalRGB 2.5.74 answered `/api/v1/app` on port 16038 with
`authorized: false`. The user `Effects`, `Plugins`, and `Components` folders existed under
OneDrive Documents and contained no files. The old MCP launcher is retained only as research
because newer SignalRGB versions removed the executable it targets.

Current check on 2026-10-18: SignalRGB 2.5.80 was installed but stopped. Neither control port
was listening. The reviewer's live check found the shell URL handler registered in HKCU and
HKCR. `SignalRgbMcp.exe` exists in 2.5.74 but was removed from 2.5.80, so the newest-version
launcher cannot start it. The bundled/downloaded files confirm `Aurora`, `Screen Ambience`, and
`Logarithmic Visualizer`; the seeded Movie fixture was corrected from `Screen Ambient`.

Slice 16 read-only package check on 2026-10-19: the merged peer plugin passed 10/10 tests and
produced a dry-run ZIP containing its manifest at the root with the `FocusRGB` organization
source URL. The packaged executable completed JSON-RPC initialize, status, effect/layout catalog,
and shutdown calls. SignalRGB was stopped; Aurora, Screen Ambience, and Logarithmic Visualizer
were available in the effect catalog, while layout catalog/selection remained explicitly
unsupported. No `binding/apply` request or application URL was sent, so this is package/API
evidence only, not visible-output acceptance.

## WLED/layout/calibration

Desk and Wall are known at `10.0.0.36` and `10.0.0.37`. On 2026-10-08, Desk answered with
WLED 16.0.1, 300 LEDs, IO16:215 plus IO2:85, boot preset 1, and active realtime streaming.
Wall timed out. Do not assume both boards share the Desk configuration.

Preferred Desk snapshot: `../../WLED/profiles/wled-desk-blends-2026-10-07/`, containing info,
config, state, presets, and SignalRGB settings. Inspect it before tests, but do not overwrite
later user changes by reapplying it automatically.

Recorded Desk: 215 LEDs IO16 plus 85 IO2; 300 WS2815 total, RGB order, 12 mA/LED estimate,
2700 mA limiter. Boot preset 1 was user's **Blends**, effect 115, `[172,0,215]`, brightness 60.
Gamma 2.5, realtime gamma off; CCT also recorded. These are snapshot values only. The older
tidal-purple preset is not the desired current default. A possible segment gap at LED 214
was noted, not fixed; inspect half-open endpoints before correcting it.

Media layout excluded desk lights and put monitor top right-to-left. Preserve this intent
without changing board wiring/presets to fix an effect-specific layout issue.

Calibration was judged by the user, not measured. Cyan remained blue-leaning. Gamma tests
did not establish global per-channel correction. SignalRGB plugin inspection reported UDP
realtime packets carrying a two-second timeout, not DDP in that path. Native output may
use DDP; do not equate the packet timeout with WLED's 2500 ms DDP timeout.

## Peripherals and detection

- BlackWidow V4: VID `0x1532`, PID `0x0287`; prior map counted 134 LEDs (116 keys + 18
  underglow), grid 25 x 9. Verify against installed plugin.
- Basilisk V3: VID `0x1532`, PID `0x0099`; prior map reported 11 LEDs; native output unverified.
- Keyboard exposed LampArray; mouse did not. Enumeration is not proof our controller can
  drive it. Dynamic Lighting was disabled; ownership/conflicts need investigation first.
- Synapse without Chroma was retained for macros/special keys. RGB chat used delayed startup;
  later notes mention an allowlist. Inspect current setup before changing startup tasks.
- Foreground lookup was demonstrated. Steam RunningAppID is a running-state signal, not
  proof of foreground focus. Anti-cheat compatibility is not guaranteed by these observations.
- Earlier Steam inventory: 20920, 292030, 322330, 34330, 413150, 570. Rescan configured
  libraries rather than hard-coding that dated list.
- App executable names in the brief are candidates to confirm on the actual foreground
  process. Stremio's runtime helper should not be the primary match.
- Slice 12 check on 2026-10-16: the Windows observer reported running `Code.exe`,
  `PotPlayerMini64.exe`, and the modern Media Player's `Microsoft.Media.Player.exe`. It also
  reported `PotPlayerMini64.exe` as the actual PotPlayer foreground-window owner. Stremio was not
  installed, so a deterministic fixture covers its shell/helper distinction. No lighting or saved
  settings changed.

## Comparable application settings

Reviewed 2026-10-12 from project-owned documentation and repositories:

- [OpenRGB settings documentation](https://gitlab.com/OpenRGBDevelopers/OpenRGB-Wiki/-/blob/stable/User-Documentation/OpenRGB-Settings.md)
  exposes minimize-on-close versus exit, a tray restore path, System/Light/Dark themes,
  detector enablement, and HID safe mode.
- [SignalRGB startup guidance](https://docs.signalrgb.com/troubleshooting/startup-issues/)
  documents user-controlled startup and silent/minimized launch. Its
  [dashboard guide](https://docs.signalrgb.com/quick-start/understanding-the-dashboard/)
  groups application preferences, updates, account details, and device permissions under
  Settings. Its
  [device troubleshooting guide](https://docs.signalrgb.com/troubleshooting/common-issues/device-rgb-freezing/)
  exposes basic diagnostic logging under Application > Advanced, and its tray has an explicit Quit.
- [Fan Control's official repository](https://github.com/Rem0o/FanControl.Releases) lists
  theme/color customization and user-controlled Windows startup. An
  [official maintainer response](https://github.com/Rem0o/FanControl.Releases/discussions/804)
  confirms that closing the window keeps the background app in the tray, an explicit tray Exit
  closes it, and Start with Windows and Start minimized are separate options.

The common useful pattern for FocusRGB is a clear background lifecycle, an explicit real-exit
path, separate startup/minimized controls, theme selection, and opt-in diagnostic logging.
Account, cloud, updater, capture, and device-specific settings depend on product capabilities
and should not be copied into the application settings page without their owning feature.

## Sources

- RGB Claude session `ca5cee85-477f-4450-81d7-3fbecb25b67f`.
- Former v2 delivery plan, consolidated into `focus-profiles-plan.md` on 2026-10-16.
- Historical local SignalRGB security-testing notes remain in repository history and are not a
  development roadmap or part of the public plugin package.
- `../../WLED/profiles/README.md` and preferred snapshot above.

Future records should include date/version/account/device context and distinguish proposed,
API-accepted, observed in software, and visually confirmed outcomes.
