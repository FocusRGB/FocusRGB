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

## Runtime direction

Use .NET 10 for the application and Avalonia for the thin tray/settings UI. Detection,
selection, profiles, transitions, and command handling live in UI-independent core projects
so the same background engine can run on Windows, macOS, and Linux. Ship only a Windows
executable initially; portable projects must not reference Windows-only APIs.

The dependency direction is strict: the app and plugins depend on shared integration
contracts; the core depends on neither. SignalRGB is a separate plugin project/package and is
not compiled into the core or Avalonia application. Removing that plugin must leave profile
selection, persistence, and fake-integration tests working.

Controller plugins are external processes, not SignalRGB-style JavaScript files loaded into
the application. Each package contains a versioned manifest, one or more platform entry
points, configuration schema, resources, and a license. The app launches explicitly enabled
plugins and communicates through a versioned JSON-RPC protocol over standard input/output.
An official .NET SDK and templates implement that protocol, while its published JSON schemas
leave room for plugins written in other languages.

Process separation contains crashes and permits plugin restart, but is not a security
sandbox. Manifests declare operating systems, architectures, roles, capabilities, and needed
resources such as network, screen capture, or USB access. The UI must show those declarations
before enabling an untrusted plugin. Keep frame messages batched; add a measured binary
transport only if JSON becomes a demonstrated streaming bottleneck.

Do not build the complete external plugin host before the first working integration. Start
with the smallest internal contracts needed by the SignalRGB plugin and fake test integration.
Once that vertical slice works, extract and version the external protocol and SDK from the
proven boundary rather than guessing the entire public API up front.

## Profile composition

A profile has an effect reference and an optional explicit layout reference, plus settings and
output bindings. An effect produces colours or selects an external provider effect. A layout
is the configuration of LED placements: device positions, individual LED coordinates,
orientation, and logical LED/zone mappings. It does not contain application matching rules.
Rules only select profiles, and game integrations only provide events to the selected effect.

Each binding has `renderingAuthority: provider | application`. Provider authority delegates
effect and layout interpretation to the integration; its result wins, and the coordinator
must suppress application rendering for overlapping devices. Application authority uses the
app's native effect/layout and requires ordinary device acquisition. SignalRGB bindings use
provider authority by default. There is no implicit fallback between authorities: unavailable
provider resources are reported until the user configures an explicit fallback.

SignalRGB profiles may omit the layout reference to retain SignalRGB's current LED placements.
The initial SignalRGB tests require only effect selection. Explicit provider layout selection
remains optional and requires verified support. For native testing, an available SignalRGB
layout may be imported as an app-owned placement snapshot when export/read support is proven;
importing placements does not enable SignalRGB rendering or transfer device ownership.

Active SignalRGB provider authority overrides the app's effect, placement layout, output
routing, and native calibration for devices SignalRGB manages. Preserve the app settings for
later native use. App selection rules, priorities, manual mode, and persistence still apply.
SignalRGB already handles WLED output: do not send direct WLED commands or frames to those
devices while SignalRGB owns them. A separate WLED integration/output plugin under
`modules/wled/` is required for testing and operation without SignalRGB, after ownership is
confirmed. Provider-specific identifiers remain outside the rule engine.

## Module packaging

Use `modules/<integration>/` as the ownership boundary for an external system. A module keeps
its backend code, integration adapters, tools, examples, templates, and specific research
together, while exposing one or more of the contracts below. Shared rules, layouts, effects,
and hardware-independent tools stay outside integration modules.

`modules/signalrgb/` is the first such boundary. It contains the current SignalRGB content
and utilities before a loader exists. A later WLED module should follow the same rule when
its controller implementation enters this repository.

## Contract sketch

- Manifest: stable ID, plugin version, contract version, roles, config schema, prerequisites.
- Packaging: supported OS/architecture, entry point, license, declared resource access, and
  optional source/homepage URLs. Reject duplicate IDs and incompatible contract ranges.
- Provider catalog: enumerate available effects and layouts with stable provider IDs, display
  names, availability, and refresh time. If enumeration is unsupported, report that capability
  as unavailable rather than returning a guessed catalog.
- Lifecycle: initialize, report health, cancel, shut down; calls have bounded timeouts.
  Retry only actions safe to repeat; reject incompatible contract versions clearly.
- Detectors: executable identity/PID when available, foreground status, optional game ID,
  source, timestamp. Expire stale observations. Steam RunningAppID indicates running state,
  not necessarily foreground focus.
- Discovery: stable launcher-scoped catalog IDs and match identities; allow manual entries.
- Integrations: versioned event payloads, source, freshness. Static game profiles work
  without telemetry. Prefer common capabilities such as health, match phase, cooldown, and
  status; preserve uncommon fields under a game/plugin namespace instead of forcing a lossy
  universal schema.
- Outputs: logical device IDs, resolved current backend IDs, capabilities, and results
  distinguishing applied/rejected/unavailable/unverified. Save/restore only settings changed.
- Provider rendering: validate selected effect/layout IDs, apply both in provider-defined
  order when explicitly supplied; an omitted layout retains the provider's existing layout.
  Return the actual selection where observable. Expose optional layout export/import and the
  configuration fields/device scope controlled by provider authority as capabilities.
  Provider authority blocks native
  rendering on the same owned devices until release.
- Effects: accept time, layout, settings, and optional events; produce colours without
  knowing transport. Apply native per-device calibration once at an explicit stage.

Capabilities distinguish installed-effect selection, named-LED static overrides, preset
selection, streaming, and device release/acquisition. Explain unavailable actions: account
tier, missing API, disconnected hardware, or unverified support. API OK is not proof of
visible output. Claude's MCP tools do not automatically form a standalone app API.

Begin with explicitly installed and enabled local plugins. Do not build a marketplace or
automatic plugin updater in the first version. Keep secrets outside committed profiles/logs.

## Selection rules

Defaults: user-editable integer priority, then match specificity, then stable rule ID for
ties; case-insensitive executable matches; launcher-scoped game IDs. Foreground beats
running-state signals unless a higher-priority rule explicitly asks for background behavior.
Debounce ordinary focus changes about two seconds. Ignored windows do not replace a profile;
unmatched apps keep it. Unchanged profiles are no-ops except backend recovery. Manual mode
selects a profile and suppresses automatic switching until the user resumes automatic mode.

Persist the selection mode and active profile after every successful change. First start uses
manual mode with the Default profile. On restart, restore and apply both before enabling normal
detection. If automatic mode was restored, a later stable observation can select a different
profile through the ordinary rules; startup does not receive a special permanent priority.

## Local MCP boundary

The background app may expose a local MCP server. MCP is an adapter over the same query and
command services used by the UI; it must not edit profile files directly or bypass validation,
priority arbitration, plugin capability checks, or ownership transitions. Initial tools can
list profiles/plugins/devices, explain the active selection, enter or leave manual mode,
change rule priority, update effect settings, and preview a profile. Loading executable
plugins, deleting data, or changing persistent hardware ownership requires explicit user
confirmation. Bind locally and use per-install authentication where the transport permits it.

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
  "selection": {
    "debounceMs": 2000,
    "unmatched": "keep-current",
    "firstRunMode": "manual",
    "firstRunProfile": "default",
    "restoreLastState": true
  },
  "profiles": {
    "default": { "actions": [
      { "plugin": "signalrgb", "action": "select-provider-scene", "renderingAuthority": "provider", "effect": "Aurora" }
    ] },
    "media-player": { "actions": [
      { "plugin": "signalrgb", "action": "select-provider-scene", "renderingAuthority": "provider", "effect": "Logarithmic Visualizer" }
    ] },
    "code": { "actions": [
      { "plugin": "signalrgb", "action": "select-provider-scene", "renderingAuthority": "provider", "effect": "Aurora" }
    ] },
    "stremio": { "actions": [
      { "plugin": "signalrgb", "action": "select-provider-scene", "renderingAuthority": "provider", "effect": "Screen Ambient" }
    ] },
    "game": { "actions": [
      { "plugin": "signalrgb", "action": "led-overrides", "device": "keyboard", "colours": { "W": "#ff0000", "A": "#ff0000", "S": "#ff0000", "D": "#ff0000" } },
      { "plugin": "wled", "action": "native-effect", "device": "desk", "effect": "game-zones" }
    ] }
  },
  "rules": [
    { "priority": 100, "foreground": true, "exe": ["wmplayer.exe", "Microsoft.Media.Player.exe"], "profile": "media-player" },
    { "priority": 100, "foreground": true, "exe": ["stremio-shell-ng.exe"], "profile": "stremio" },
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
