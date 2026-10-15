# Focus-based RGB profile switcher: plan (v2)

> Historical archive. Superseded on 2026-10-08 by [the current plan](focus-profiles-plan.md),
> [app brief](app-brief.md), and [plugin architecture](plugin-architecture.md).
> This file preserves earlier assumptions; use [integration evidence](integration-evidence.md)
> for corrections, including unresolved handover and mouse visual confirmation.

Status: **planning only, nothing implemented.** Updated 2026-10-07 from tests run on this machine.

## 1. Concept

The app is a **profile switcher**. It watches which application has focus and activates the
matching profile. A profile is one of two types:

| Type | Who drives the lights | Purpose |
|---|---|---|
| **`signalrgb`** | SignalRGB (all devices) | Choose a SignalRGB effect for the profile. Temporary: it exists only until we have our own effects. |
| **`native`** | This app, directly | The app sends colours to the devices itself. Game profiles use this. It is the long-term replacement for SignalRGB. |

End goal: every profile is `native`, and SignalRGB is uninstalled. Until then, `signalrgb`
profiles reuse its existing effects (Screen Ambience, Aurora).

Example mapping:

| Focus | Profile | Type |
|---|---|---|
| Stremio, PotPlayer, Windows Media Player | media | `signalrgb`: Screen Ambience |
| VS Code | code | `signalrgb`: Aurora |
| a game | per-game or generic game | `native` |
| anything else | unchanged | |

## 2. Verified facts

| Question | Result |
|---|---|
| Can the focused app be seen? | Yes. `GetForegroundWindow` gave `chrome.exe` in 0.05 ms. |
| Switch a SignalRGB effect from outside, free? | **Yes, by URL**: `SignalRgbLauncher.exe --url=signalrgb://effect/apply/<name>?-silentlaunch-`. Tested Rainbow, Aurora, Screen Ambience. |
| Via MCP `effect_apply` or REST `/api/v1/lighting`? | No. Both are Pro (403). |
| URL route applies a paid effect or our own custom effect? | No. Nothing changed. |
| Custom screen-reading effect on WLED? | No. "Video lightscripts on third-party devices" is Pro. |
| WLED strips controllable without SignalRGB? | Yes (JSON API, DDP on 4048). |
| SignalRGB device settings readable from outside? | Yes, by device uid: `enabled`, `LightingMode` (Canvas or Forced), `forcedColor`, `turnOffOnShutdown`. |
| Can the tool write those settings? | `device_writesetting` takes any setting by uid. **`enabled` not tested yet.** |
| Per-key override tool (`device_ledoverrides`)? | **Works, free, confirmed by eye** (W, A, S, D turned red). Takes the device uid, a key name and a hex colour; `none` hands the key back to the effect. Display names are rejected: use the uid. |
| Does Windows expose the keyboard for native control? | **Yes.** The BlackWidow V4 appears as a Windows *LampArray* device. The Basilisk V3 does not. |

Key map for the keyboard is in SignalRGB's plugin (`Razer_Modern_Keyboard.js`): 134 named LEDs
on a 25 x 9 grid (116 keys + 18 underglow), including M1-M6 and the media keys. The mouse has
11 LEDs (logo, scroll wheel, 9 underglow).

## 3. Architecture

```
focus detector --> rule engine --> profile runner
(Win32 + Steam      (rules,          |-- signalrgb profile --> SignalRGB switcher (URL)   [temporary]
 registry)           debounce)       '-- native profile    --> device drivers
game scanner --> games list ----^                               |-- WLED driver    (JSON API / DDP)
                                                                |-- keyboard driver (Windows LampArray)
                                                                '-- mouse driver    (see section 8)
```

One background program, started at logon by a scheduled task (same pattern as the existing
"Synapse after SignalRGB" task).

## 4. The handover problem (the crux)

Only one program may send to a device at a time, or the lights flicker. Switching between a
`signalrgb` profile and a `native` profile therefore needs a clean handover.

| Option | Idea | Status |
|---|---|---|
| A. Disable devices in SignalRGB | When a native profile starts, set `enabled=false` on the devices the app will drive, via `device_writesetting`. Re-enable on exit. | **Test first.** Unknown whether `enabled` is writable and free. |
| B. Forced lighting mode | `LightingMode=Forced` with a colour. | Not a handover: the plugin still sends a frame every cycle. |
| C. Park on a black effect | Select Solid Color black. | Same problem as B. |
| D. Quit and restart SignalRGB | Hard stop. | Heavy: slow restart, and SignalRGB stops Razer's engine on each start. |
| E. Split by device (interim) | The app owns only the **strips**; `wled-desk` stays disabled in SignalRGB; keyboard and mouse stay with SignalRGB. | Works today, but then the strips do not follow SignalRGB's effects. |

Recommendation: test A. If it works, the transitions are:

- **SignalRGB profile to native:** disable the devices in SignalRGB, then start the native writers.
- **Native to SignalRGB:** stop the native writers (WLED falls back to its own state after its realtime timeout), re-enable the devices, then apply the effect by URL.

If A fails, fall back to E and accept that the strips are native-only.

**Keyboard and mouse need no handover for static game maps.** `device_ledoverrides` paints named
keys and LEDs on top of whatever SignalRGB effect is running, and the rest of the device keeps following
the effect. So a game profile can be: SignalRGB keeps a calm built-in effect, and the app overrides the
keys it wants (for example W, A, S, D, or the macro keys) while the strips are driven natively. When the
game loses focus the app sends `none` for every key it touched. Limits to design around:
- Overrides are static colours. Any animation means the app re-sends colours itself.
- Each call from outside takes about half a second today; a long-lived connection would be faster.
- Overrides stay on until cleared, so the app must always clear them on exit or a crash.

## 5. Detecting the focused app, safely

Priority, safest first:

1. **Steam games: `HKCU\Software\Valve\Steam\RunningAppID`.** A registry read; 0 means no game. It never touches the game.
2. **Everything else: foreground window to pid to exe name**, with the name from a process-list snapshot (`CreateToolhelp32Snapshot`), so no handle is opened on the game.
3. **Manual list** of exe names for anything the scan misses.

Anti-cheat rules: no injection, hooks, overlays or memory reads; no `OpenProcess` on the game; no
administrator rights; do not capture the game window directly. These are passive name lookups of the kind
the anti-cheats do themselves. The sources found describe them hunting known cheat signatures, not
flagging plain window queries, but nothing says either way for RGB tools, so this is
**low risk, not guaranteed**. Check each competitive game's FAQ.

## 6. Apps and games

| App | Match on | Notes |
|---|---|---|
| Stremio | `stremio-shell-ng.exe` | `stremio-runtime.exe` is a helper; do not match it. Confirm which process owns the window. |
| PotPlayer | `PotPlayerMini64.exe`, `PotPlayer64.exe` | `D:\Softwares\PotPlayer` |
| Windows Media Player (classic) | `wmplayer.exe` | |
| Media Player (new app) | `Microsoft.Media.Player.exe` | |
| VS Code | `Code.exe` | |

Steam games found (`D:\Softwares\Steam`, `E:\Steam Library`): The Witcher 2 (20920), The Witcher 3
Remastered (292030), Don't Starve Together (322330), Total War: SHOGUN 2 (34330), Stardew Valley
(413150), Dota 2 (570). Epic: none installed. Battle.net and Ubisoft Connect are installed with nothing
scanned yet.

## 7. Switching behaviour

- Act only after the new app has held focus for about 2 s.
- Ignore list: `explorer.exe`, Windows search and notification hosts, overlays, this terminal.
- No match: keep the current profile. Skip re-applying the effect already active.
- If SignalRGB is not running, do not call the launcher (it would start the app).
- Only name effects that are installed and free. A paid or unknown name silently does nothing.

## 8. Roadmap to dropping SignalRGB

| Stage | What changes | Needs |
|---|---|---|
| 1 (now) | `signalrgb` profiles via URL; strips driven by the app | Handover option A or E |
| 2 | **Native keyboard** through Windows LampArray (per-key, the key map above) | Dynamic Lighting switched back on; the app registered as the background light controller; no SignalRGB writing at the same time. **Unverified**, and SignalRGB's docs list Windows Dynamic Lighting as a conflict. |
| 3 | **Native mouse** | LampArray does not list the Basilisk V3. Options: port the Razer HID packets from SignalRGB's readable plugin, or OpenRGB (unverified for this model). |
| 4 | Own effects library (aurora, calibrated screen mirror, game zones) | Screen capture with per-channel gain |
| 5 | Retire SignalRGB and the Synapse delay task | Everything above working |

## 9. Profile definition sketch (not final)

```json
{
  "profiles": {
    "media": { "type": "signalrgb", "effect": "Screen Ambience" },
    "code":  { "type": "signalrgb", "effect": "Aurora" },
    "game":  { "type": "native", "devices": { "strips": "zones", "keyboard": "wasd-highlight" } }
  },
  "rules": [
    { "profile": "media", "exe": ["stremio-shell-ng.exe", "PotPlayerMini64.exe", "wmplayer.exe", "Microsoft.Media.Player.exe"] },
    { "profile": "code",  "exe": ["Code.exe"] },
    { "profile": "game",  "steam_appid": [20920, 292030, 322330, 34330, 413150, 570] }
  ]
}
```

## 10. Build phases

| Phase | Deliverable | Done when |
|---|---|---|
| 0 | Test handover option A (`enabled`) and per-key overrides | Known, with a visible check |
| 1 | Detector with dry-run log | Switching windows prints the right profile, no lights change |
| 2 | `signalrgb` profile driver | VS Code then a player switches Aurora and Screen Ambience |
| 3 | WLED native driver | Strips follow each profile without flicker |
| 4 | Game scanner | The six Steam games are found and matched by app id |
| 5 | Handover between profile types | Entering and leaving a game leaves no flicker and no stuck state |
| 6 | Logon task | Works after reboot with SignalRGB starting first |
| 7+ | Roadmap stages 2 to 5 | One at a time |

## 11. Open decisions

1. If handover option A fails, accept E (strips native-only for now)?
2. Which built-in effect should SignalRGB show during games, if it stays on?
3. Game look: one generic game profile, or one per game?
4. Which monitor does the screen mirror capture?
5. Keyboard first or mouse first for going native?

## 12. Free-plan limits

- No automatic switching to your own SignalRGB effects.
- No paid game effects.
- `effect_apply` and the lighting REST API are Pro. The URL route is the free way in.
