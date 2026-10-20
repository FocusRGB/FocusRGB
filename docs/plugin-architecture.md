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

The dependency direction is strict: the app host depends on shared integration contracts;
.NET plugins depend on the SDK, which depends on those contracts; the core depends on neither
the SDK nor any plugin. SignalRGB is a separate plugin package and repository and is not
compiled into the core or Avalonia application. Removing that plugin must leave profile
selection, persistence, and fake-integration tests working.

Controller plugins are external processes, not SignalRGB-style JavaScript files loaded into
the application. Each package contains a versioned manifest, one or more platform entry
points, configuration schema, resources, and a license. The app launches explicitly enabled
plugins and communicates through a versioned JSON-RPC protocol over standard input/output.
The main FocusRGB repository owns the multi-language `sdk/` area, contracts, schemas, host,
conformance tests, templates, and a fake/reference plugin. SDK libraries implement the plugin
side of the protocol, while the published JSON schemas remain language-neutral.
The host launches and supervises plugins; it never loads third-party assemblies into the app
process.

Node and Python packages declare `runtime` in the manifest and keep `entryPoint` as the module
inside the package. The host resolves the runner named `focusrgb-*-runner`; it never executes the
module directly. On Windows, Node packages use `node.exe` from `PATH` with either the bundled
`runners/node/focusrgb-node-runner.js` or npm's
`node_modules/@focusrgb/plugin-sdk/dist/focusrgb-node-runner.js` beside its PATH shim. Python and
non-Windows runner executables are resolved first from the bundled `runners` directory and then
from `PATH`.
The supported minimums are Node.js 22 and Python 3.11. A missing runtime value for a runner
package leaves it in the direct-executable path; the no-SDK Python conformance example uses that
path deliberately. A declared runtime without its matching runner is reported before launch.

Process separation contains crashes and permits plugin restart, but is not a security
sandbox. Manifests declare operating systems, architectures, roles, capabilities, and needed
resources such as network, screen capture, or USB access. The UI must show those declarations
before enabling an untrusted plugin. Keep frame messages batched; add a measured binary
transport only if JSON becomes a demonstrated streaming bottleneck.

An initialize result may report `sdk { name, language, version }` for compatibility diagnostics.
That self-reported value is shown and logged but never establishes trust or causes acceptance or
rejection. Trust comes from the install source and verified package checksums; SDK runners do not
sandbox plugin modules, which continue to run with the user's permissions.

Start with the smallest SDK and host surface needed by the fake test integration. Pack it as a
versioned `0.x` prerelease before the first external plugin repository consumes it. Once the
fake and SignalRGB plugins prove the boundary, stabilize the public SDK rather than
guessing its complete version-1 API up front.

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
devices while SignalRGB owns them. A separate WLED integration/output plugin repository is
required for testing and operation without SignalRGB, after ownership is confirmed.
Provider-specific identifiers remain outside the rule engine.

## Repository and package ownership

Production plugins live in independent repositories named `FocusRGB-Plugin-<Name>`, such as
`FocusRGB/FocusRGB-Plugin-SignalRGB` and `FocusRGB/FocusRGB-Plugin-WLED`. A plugin repository owns
its executable, manifest, provider-specific code, tests, documentation, packaging, license,
and releases. Official and community plugins follow the same package contract.

Repository separation must not become a runtime source dependency. A plugin consumes a
published version of `FocusRgb.PluginSdk`; it does not use a project reference, Git submodule,
or source checkout of the app. Its CI must build against its declared package source without a
local FocusRGB clone. The app installs or discovers the plugin's packaged release and validates
the manifest's plugin and contract versions before launch.

Create each repository when its implementation slice starts, not as an empty placeholder.
Slice 15 moved maintained SignalRGB-specific material into
[`FocusRGB-Plugin-SignalRGB`](https://github.com/FocusRGB/FocusRGB-Plugin-SignalRGB); the main
repository retains only a migration link. Slice 22 creates `FocusRGB-Plugin-WLED`. A selected game integration follows the
same convention in slice 20 and consumes the public SDK established in slice 17. Shared rules,
layouts, native effects, and hardware-independent
tools remain in the main repository.

The main repository retains a raw-protocol fake plugin so the host can be tested without an SDK
source or package dependency. A lightweight catalog may later point to approved plugin releases;
plugins are never included through Git submodules. GitHub repositories are peers rather than
literal child repositories, so the naming convention and catalog express their relationship.
Official SDKs, templates, examples, and package publishing live in
[`FocusRGB-Plugin-SDK`](https://github.com/FocusRGB/FocusRGB-Plugin-SDK). Each SDK pins the
immutable protocol tag and passes this repository's language-neutral conformance kit.

## Contract sketch

[`plugin-protocol.md`](../sdk/spec/plugin-protocol.md) is the normative transport, lifecycle, message, and
versioning contract. The JSON schemas are normative machine-readable constraints. This section
explains architectural intent and does not redefine their wire shapes.

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
The initial host discovers a package at
`%LOCALAPPDATA%\FocusRGB\plugins\<plugin-id>\focusrgb-plugin.json` only when the ID appears in
`settings.json` under `plugins.enabled`. Each manifest resolves one entry point within its own
package directory; invalid paths, platforms, architectures, or contract majors are rejected
before launch.

## Selection rules

Users create named Application Groups containing executable identities, then map a group and a
profile into an ordered priority list. Each rule specifies either `while-running` or
`while-focused` eligibility. The first eligible row wins, so a top `while-running` rule wins
even when an application lower in the list has focus. A `while-focused` rule becomes ineligible
when focus is lost, allowing the next eligible rule in the list to win. This is configurable
per rule, including the top rule; process exit makes either rule ineligible. Manual mode
overrides the entire list until automatic mode is resumed.

There is no separate numeric priority: saved list order is authoritative. Match executable identities
case-insensitively and scope game IDs by launcher. Debounce ordinary focus changes about two
seconds. Ignored transient windows do not trigger focus-loss transitions. When no rule is
eligible, use the protected Default profile. Reapplying an unchanged
profile is a no-op except for output recovery.

Persist the desired selection mode and profile after every validated user change. First start
uses manual mode with the Default profile. On restart, restore and attempt to apply both
before normal detection. An unavailable output follows the fallback policy. Restored automatic
mode may select another profile after a stable observation through the ordinary priority rules.

## Plugin fallback and recovery

Each profile defines an ordered fallback policy for unavailable plugins, missing required
resources, unsupported actions, and disconnected devices. Candidates can be another profile
or a device-supported preset/static output through an available plugin. Validate references
and reject fallback cycles; bound traversal and report each skipped candidate. The initial
fallback is the app's saved native setup: its default setup until the user applies changes,
then the last applied native effect/layout/output configuration. Provider changes and temporary
previews must not overwrite this snapshot. If its output plugins or required resources are also
unavailable, show a pending/degraded state with no new output commands.

Keep desired profile/mode separate from confirmed applied output and temporary fallback.
Persist user intent even when output is unavailable. When a plugin becomes available again,
notify the user and offer to resume using it. Availability alone does not restore rendering
authority or output. Accept revalidates capabilities, ownership, and the current desired
selection before resuming; dismissing or declining keeps the native fallback active. Do not
restore a stale selection that a newer user command superseded. Coalesce repeated availability
events into one pending notification per plugin. Falling back from SignalRGB
to WLED still requires verified physical-device release. A live provider that reports unavailable
keeps its ownership claim. A confirmed local process exit or absent plugin clears that dead host
claim so automatic fallback may try another provider; physical-device arbitration remains the
incoming provider's responsibility. Partial output failure follows the transition rules.

## Layout editing and preview

Native layouts use a shared 2D canvas. Device geometry retains physical LED identities while
placement transforms provide position, rotation, and independent horizontal/vertical scale.
Users can stretch or compress a strip without changing its hardware LED count.

A `group` stacks selected devices on one shared placement footprint. Each member samples the
same canvas area using its own LED count and normalized LED positions, so a 30-LED and a
100-LED strip reproduce the same spatial effect with different resolution. Preserve per-member
orientation/reversal and geometry; grouping changes sampling placement, not device identity.
Group move/scale/rotation affects all members. Named key/LED mappings still target individual
members. Group membership and transforms belong to a layout, not the global device registry.

Only assigned devices participate in a layout. Registered but unassigned devices are excluded:
they receive no new frames from that layout and are not implicitly made black. On layout
change, stop prior app streams/overrides on newly excluded devices and release ownership using
the backend's supported policy. For screen ambience, assign the three monitor strips and leave
the two wall strips unassigned. Direct mappings must also respect layout participation.
Devices can be explicitly included for direct/zone mappings without a canvas placement; this
lets a keyboard use named keys alone. An entirely unassigned device remains excluded.

The editor has a Live preview checkbox. When enabled, commit a preview after pointer release
for placement/resize operations and after a completed edit for other controls; dragging does
not continuously send hardware updates. When disabled, edits remain drafts until Apply.
Previews are temporary and do not overwrite saved profile/layout state. Apply validates,
persists, and activates the draft; Cancel restores the previously applied state where supported.
Preview uses the same authority, capabilities, and ownership checks as normal output.

## Native game rendering

Use hybrid layers: a canvas effect supplies spatial animation, semantic zones provide reusable
targets such as health/cooldown/ambient, and direct named-key/LED mappings override selected
LEDs. Default composition is canvas base, then zone layers, then direct mappings, with stable
user-editable ordering and explicit blending where requested. A layer only claims its targets.

Plugins expose semantic keys (for example W, A, S, D), device zones, and strip ranges when
available. Users bind game events to these targets without exact keyboard placement. Canvas
sampling remains optional for keyboards and requires placement calibration if used. Strips and
fans can use canvas animation or named zones/ranges, avoiding per-LED configuration. Missing
keys/zones produce an actionable mapping warning. Native hybrid layers apply only under
application authority; provider-owned SignalRGB rendering remains authoritative.

## Local MCP boundary

The background app may expose a local MCP server. MCP is an adapter over the same query and
command services used by the UI; it must not edit profile files directly or bypass validation,
mapping-order arbitration, plugin capability checks, or ownership transitions. Initial tools can
list profiles/plugins/devices, explain the active selection, enter or leave manual mode,
reorder mappings, update effect settings, and preview a profile. Loading executable
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
    "unmatched": "default-profile",
    "firstRunMode": "manual",
    "firstRunProfile": "default",
    "restoreLastState": true
  },
  "profiles": {
    "default": { "actions": [
      { "plugin": "signalrgb", "action": "select-provider-scene", "renderingAuthority": "provider", "effect": "Aurora" }
    ] },
    "programming": { "actions": [
      { "plugin": "signalrgb", "action": "select-provider-scene", "renderingAuthority": "provider", "effect": "Aurora" }
    ] },
    "movie": { "actions": [
      { "plugin": "signalrgb", "action": "select-provider-scene", "renderingAuthority": "provider", "effect": "Screen Ambience" }
    ] },
    "music": { "actions": [
      { "plugin": "signalrgb", "action": "select-provider-scene", "renderingAuthority": "provider", "effect": "Logarithmic Visualizer" }
    ] },
    "game": { "actions": [
      { "plugin": "signalrgb", "action": "led-overrides", "device": "keyboard", "colours": { "W": "#ff0000", "A": "#ff0000", "S": "#ff0000", "D": "#ff0000" } },
      { "plugin": "wled", "action": "native-effect", "device": "desk", "effect": "game-zones" }
    ] }
  },
  "rules": [
    { "foreground": true, "exe": ["Code.exe", "devenv.exe"], "profile": "programming" },
    { "foreground": true, "exe": ["PotPlayerMini64.exe", "PotPlayerMini.exe", "stremio-shell-ng.exe"], "profile": "movie" },
    { "foreground": true, "exe": ["Microsoft.Media.Player.exe", "wmplayer.exe", "Spotify.exe"], "profile": "music" },
    { "foreground": true, "game": { "launcher": "steam", "id": "570" }, "profile": "game" }
  ]
}
```

The game example depends on established override transport and WLED ownership. Do not
launch SignalRGB implicitly if absent. Do not hard-code current USB topology IDs, IPs,
installed games, or account capabilities into the core.

Keep monitor direction and desk exclusion in logical layouts. Use zero-based half-open
LED ranges `[start,end)`. Human labels from the chat (`215-234`, `235-279`, `280-300`) need
indexing reconciliation with the actual board; do not copy them as software ranges.
