# Proposed plugin architecture

Design sketch; no runtime, API, or profile schema has been implemented.

## Boundaries

```text
detectors --> app observations --> core rule engine --> selected profile
discovery --> app/game catalog -------^
integrations --> app/game events --> effect inputs
profile --> transition/ownership coordinator --> effects + output plugins
```

| Role | Responsibility | Candidates |
|---|---|---|
| Detector | Report focus/running state with identity, timestamp, and source | Win32 foreground window, Steam running-app state |
| Discovery | Enumerate installed apps/games, with manual corrections | Steam manifests, manual catalog, later other launchers |
| Integration | Receive supported external events/telemetry | Game or app APIs selected later |
| Output | Discover devices, expose capabilities, apply output and restore changed state | SignalRGB, WLED, later native peripheral backends |
| Effect | Produce native frames/zone colours independent of transport | Aurora-style animation, screen mirror, static game maps |

A plugin can provide multiple roles with distinct contracts. Screen capture is an input
to media effects, not proof of game focus. The core must not depend on SignalRGB, WLED,
Steam, or Razer specifics. Other-app integrations remain independently usable after the
SignalRGB plugin is retired.

## Contract sketch

- Manifest: stable ID, plugin version, contract version, roles, config schema, prerequisites.
- Lifecycle: initialize, report health, cancel, shut down; calls have bounded timeouts.
  Retry only actions safe to repeat; reject incompatible contract versions clearly.
- Detectors: executable identity/PID when available, foreground status, optional game ID,
  source, timestamp. Expire stale observations. Steam RunningAppID indicates running state,
  not necessarily foreground focus.
- Discovery: stable launcher-scoped catalog IDs and match identities; allow manual entries.
- Integrations: versioned event payloads, source, freshness. Static game profiles work
  without telemetry.
- Outputs: logical device IDs, resolved current backend IDs, capabilities, and results
  distinguishing applied/rejected/unavailable/unverified. Save/restore only settings changed.
- Effects: accept time, layout, settings, and optional events; produce colours without
  knowing transport. Apply native per-device calibration once at an explicit stage.

Capabilities distinguish installed-effect selection, named-LED static overrides, preset
selection, streaming, and device release/acquisition. Explain unavailable actions: account
tier, missing API, disconnected hardware, or unverified support. API OK is not proof of
visible output. Claude's MCP tools do not automatically form a standalone app API.

Begin with explicitly enabled local plugins. Packaging/process isolation depend on runtime;
no marketplace or arbitrary-code loader exists. Keep secrets outside committed profiles/logs.

## Selection rules

Proposed defaults: explicit priority, stable rule order for ties, case-insensitive executable
matches, launcher-scoped game IDs. Foreground beats running-state signals unless a rule
explicitly asks for background behavior. Debounce ordinary focus changes about two seconds.
Ignored windows do not replace a profile; unmatched apps keep it. Unchanged profiles are
no-ops except backend recovery. Manual selection/pause should override automatic rules;
its UI and persistence behavior remain to be specified.

## Ownership and transitions

Track the real writer per physical device; different backend IDs can refer to the same
hardware. Only one native streamer owns a device. SignalRGB LED overrides go through its
existing writer and do not constitute a second hardware writer.

1. Resolve devices and required capabilities before changing lights.
2. Plan the transition; save state for backend settings that will change.
3. Clear outgoing app-owned overrides and stop outgoing streams.
4. Confirm release before granting a device to a new writer. Black or Forced mode does
   not release hardware.
5. Apply incoming actions; record individual results and actual ownership.
6. On partial failure, stop new output and restore a known safe state where supported.
   Do not promise atomic rollback from backends that cannot restore state.

Automatic WLED handover is unavailable until SignalRGB release is proven. Fixed native-only
WLED ownership, with that device deliberately disabled in SignalRGB, is an alternative
requiring the user's choice because media behavior changes.

Track only overrides created by this app. Clear them on exit, shutdown, failure, and
reconnect. A crash cannot run cleanup: persist an ownership/override journal for next-start
recovery. Immediate crash cleanup may require a watchdog; it is not established behavior.

## Illustrative configuration

Proposed IDs/action names, not executable config or a finalized schema:

```json
{
  "schemaVersion": 1,
  "selection": { "debounceMs": 2000, "unmatched": "keep-current" },
  "profiles": {
    "media": { "actions": [
      { "plugin": "signalrgb", "action": "select-installed-effect", "effect": "Screen Ambience" }
    ] },
    "code": { "actions": [
      { "plugin": "signalrgb", "action": "select-installed-effect", "effect": "Aurora" }
    ] },
    "game": { "actions": [
      { "plugin": "signalrgb", "action": "led-overrides", "device": "keyboard", "colours": { "W": "#ff0000", "A": "#ff0000", "S": "#ff0000", "D": "#ff0000" } },
      { "plugin": "wled", "action": "native-effect", "device": "desk", "effect": "game-zones" }
    ] }
  },
  "rules": [
    { "priority": 100, "foreground": true, "exe": ["stremio-shell-ng.exe", "PotPlayerMini64.exe", "PotPlayer64.exe", "wmplayer.exe", "Microsoft.Media.Player.exe"], "profile": "media" },
    { "priority": 100, "foreground": true, "exe": ["Code.exe"], "profile": "code" },
    { "priority": 100, "foreground": true, "game": { "launcher": "steam", "id": "570" }, "profile": "game" }
  ]
}
```

The game example depends on established override transport and WLED ownership. Do not
launch SignalRGB implicitly if absent. Do not hard-code current USB topology IDs, IPs,
installed games, or account capabilities into the core.

Keep monitor direction and desk exclusion in logical layouts. Use zero-based half-open
LED ranges `[start,end)`. Human labels from the chat (`215-234`, `235-279`, `280-300`) need
indexing reconciliation with the actual board; do not copy them as software ranges.
