# Minimal data and command contract

Slice 00. Frozen 2026-10-10 as the starting contract for slices 01–12. It covers only what the
core, the fake integration, and the first SignalRGB plugin need. It is not the public SDK
schema; that is extracted later (slice 22) from working code. Requirements remain in
`app-brief.md` and behavior rules in `plugin-architecture.md`; this document fixes shapes.

Status words below: **frozen** means slices 01–12 implement it as written unless a slice
records a deliberate amendment here; **reserved** means the name is held but not implemented.

## Conventions

- JSON, UTF-8, camelCase property names. Enumerated values are lowercase kebab-case strings.
- App-owned IDs (profiles, rules, bindings) match `^[a-z0-9][a-z0-9-]{0,63}$` and are stable
  across renames; `displayName` is free text. Plugin IDs follow the same pattern.
- Provider resource IDs are opaque strings owned by the plugin. The core stores and compares
  them byte-for-byte and never parses, encodes, or derives them from display names.
- Times are UTC ISO-8601 (`2026-10-10T09:00:00Z`); durations are integer milliseconds.
- Readers ignore unknown properties. Writers that load and save a document preserve unknown
  properties they did not change. Absent optional properties and `null` mean the same thing.
- No shared contract type, enum value, or capability ID names a provider. Provider names
  appear only as data: a plugin ID such as `signalrgb` or a resource display name.

## Versioning

| Item | Field | Rule |
|---|---|---|
| Each persisted file | `schemaVersion` (integer, starts at 1) | Older versions migrate forward on load and are saved only after a successful migration, keeping the original as a backup. A newer version than the app understands is loaded read-only or rejected with a reason; it is never overwritten. |
| Plugin protocol | `contractVersion` (`"major.minor"`, starts at `"0.1"`) | The host and plugin must share the major version. A plugin may report a lower minor; the host then avoids methods added later. A mismatch fails the handshake with `contract-incompatible`. |

While the major version is `0`, breaking changes are allowed between slices but must update
this document, the fake plugin, and tests in the same commit.

## Persisted documents

Three separate files live in the per-user app data directory. Splitting them keeps user
configuration, runtime intent, and the native fallback snapshot from overwriting each other.

| File | Owner | Written when |
|---|---|---|
| `settings.json` | User configuration | A validated Apply in the UI or the MCP adapter |
| `state.json` | Selection intent | After every validated mode or profile change |
| `native-setup.json` | Native fallback snapshot | Only when the user applies a native setup (never by previews or provider changes) |

### `settings.json` — frozen

Seeded on first run with the four acceptance profiles. Every value is user-editable.

```json
{
  "schemaVersion": 1,
  "selection": {
    "defaultProfile": "default",
    "unmatched": "keep-current",
    "debounceMs": 2000,
    "ignoredExecutables": ["ShellExperienceHost.exe", "SearchHost.exe", "LockApp.exe"]
  },
  "profiles": [
    {
      "id": "default",
      "displayName": "Default",
      "bindings": [
        {
          "id": "main",
          "plugin": "signalrgb",
          "renderingAuthority": "provider",
          "deviceScope": { "kind": "provider-managed" },
          "effect": { "resourceId": null, "displayName": "Aurora" },
          "layout": null
        }
      ],
      "fallback": [{ "kind": "native-setup" }]
    },
    {
      "id": "code",
      "displayName": "VS Code",
      "bindings": [
        {
          "id": "main",
          "plugin": "signalrgb",
          "renderingAuthority": "provider",
          "deviceScope": { "kind": "provider-managed" },
          "effect": { "resourceId": null, "displayName": "Aurora" },
          "layout": null
        }
      ],
      "fallback": [{ "kind": "native-setup" }]
    },
    {
      "id": "media-player",
      "displayName": "Media Player",
      "bindings": [
        {
          "id": "main",
          "plugin": "signalrgb",
          "renderingAuthority": "provider",
          "deviceScope": { "kind": "provider-managed" },
          "effect": { "resourceId": null, "displayName": "Logarithmic Visualizer" },
          "layout": null
        }
      ],
      "fallback": [{ "kind": "native-setup" }]
    },
    {
      "id": "stremio",
      "displayName": "Stremio",
      "bindings": [
        {
          "id": "main",
          "plugin": "signalrgb",
          "renderingAuthority": "provider",
          "deviceScope": { "kind": "provider-managed" },
          "effect": { "resourceId": null, "displayName": "Screen Ambient" },
          "layout": null
        }
      ],
      "fallback": [{ "kind": "native-setup" }]
    }
  ],
  "rules": [
    {
      "id": "media-player",
      "profile": "media-player",
      "match": { "executables": ["wmplayer.exe", "Microsoft.Media.Player.exe"] },
      "eligibility": "while-focused",
      "priority": 100,
      "enabled": true
    },
    {
      "id": "stremio",
      "profile": "stremio",
      "match": { "executables": ["stremio-shell-ng.exe"] },
      "eligibility": "while-focused",
      "priority": 100,
      "enabled": true
    },
    {
      "id": "code",
      "profile": "code",
      "match": { "executables": ["Code.exe"] },
      "eligibility": "while-focused",
      "priority": 100,
      "enabled": true
    }
  ]
}
```

Profiles:

- `bindings` is an ordered list; a profile may combine plugins. Binding IDs are unique within
  a profile. An empty list is valid and means the profile produces no output.
- `renderingAuthority` is `provider` or `application`. Only `provider` is implemented before
  Goal 3; an `application` binding validates but reports `unsupported` until native rendering
  exists. There is no implicit fallback between authorities.
- `effect` is required for a `provider` binding. `layout` is optional; `null` means the plugin
  keeps the provider's current placements and sends no layout command.
- A resource reference stores `resourceId` when known and `displayName` for the user. When
  `resourceId` is `null` (seeded fixtures before a catalog was read), the core resolves it by
  exact, case-sensitive `displayName` match against a fresh catalog and saves the ID only after
  the user applies. No match, or a catalog that is unsupported, makes the binding unavailable
  with reason `resource-not-found` or `capability-unsupported`; the core never picks a
  different resource. Fixture names are data and are reconciled with the real catalog in slice
  08 (for example Screen Ambient versus the historical Screen Ambience).
- `deviceScope.kind` is `provider-managed` (every device the provider currently manages; the
  only kind implemented in Goal 1) or **reserved** `devices` with an explicit `deviceIds` list.
  The coordinator treats a provider-managed scope as owning every device the provider reports;
  when the provider cannot report its devices, application rendering is suppressed for all
  devices that are not explicitly assigned to another writer.
- `fallback` is an ordered list of `{ "kind": "native-setup" }` or
  `{ "kind": "profile", "profile": "<id>" }`. Validation rejects unknown profiles and cycles;
  traversal is bounded by the number of profiles and records each skipped candidate.

Rules:

- The array order is the saved list order. Winner order: higher `priority`, then lower list
  index, then ordinal comparison of `id`.
- `eligibility` is `while-running` or `while-focused`. Process exit removes eligibility for
  both. `enabled: false` keeps the rule but never makes it eligible.
- `match.executables` compares file names case-insensitively. **Reserved:** `match.game` with
  `{ "launcher": "steam", "id": "570" }` for slice 13. A rule must have at least one matcher.
- Executable names are candidates until slice 05 confirms the real foreground owners.

Selection:

- `unmatched` is `keep-current` or `default-profile`.
- `ignoredExecutables` never produce focus changes; while one is foreground, the previous
  focused application keeps its focus eligibility.

Validation errors name the JSON path, for example `profiles[2].bindings[0].effect: required for
provider authority` or `rules[1].profile: unknown profile 'stremo'`. Duplicate IDs, unknown
profile references, empty matchers, and fallback cycles fail the whole document; the previous
valid file stays in use.

### `state.json` — frozen

Records selection intent only. Applied output is never persisted as fact.

```json
{
  "schemaVersion": 1,
  "mode": "manual",
  "manualProfile": "default",
  "lastSelectedProfile": "default",
  "updatedAt": "2026-10-10T09:00:00Z"
}
```

- `mode` is `manual` or `automatic`. `manualProfile` is required in manual mode and kept in
  automatic mode, so resuming manual mode returns to the last manual choice.
- `lastSelectedProfile` is the most recent desired profile in either mode. On restart in
  automatic mode it is applied first, then ordinary rules may replace it after a stable
  observation.
- A missing file means first run. A corrupt or unreadable file is renamed to
  `state.json.corrupt-<timestamp>` and treated as first run, and the UI reports this. A
  reference to a profile that no longer exists falls back to `selection.defaultProfile` in
  manual mode with a reported reason.

### `native-setup.json` — frozen shape, content grows in Goal 3

```json
{
  "schemaVersion": 1,
  "origin": "app-default",
  "appliedAt": null,
  "bindings": []
}
```

- `origin` is `app-default` until the user applies a native setup, then `user-applied` with
  `appliedAt` set. `bindings` uses the profile binding shape with `application` authority.
- Previews, drafts, provider selections, and fallback activations never write this file.
- Until a native renderer and output exist (slices 15–16), the default snapshot has no
  bindings, so activating native fallback reports `pending` with reason
  `native-output-unavailable` and sends no output commands.

## Runtime status — frozen

The core exposes this snapshot to the UI and later the MCP adapter. It is not persisted.

```json
{
  "mode": "automatic",
  "desired": {
    "profile": "code",
    "reason": { "kind": "rule", "ruleId": "code", "detail": "Code.exe focused for 2000 ms" }
  },
  "effective": { "profile": "code", "source": "desired" },
  "applied": {
    "profile": "code",
    "bindings": [
      { "bindingId": "main", "plugin": "signalrgb", "outcome": "unverified",
        "reason": { "code": "selection-not-observable", "message": "Provider accepted the request but cannot report its current effect." },
        "at": "2026-10-10T09:00:02Z" }
    ]
  },
  "pending": [],
  "notifications": []
}
```

- `desired.reason.kind` is `first-run`, `restored`, `manual`, `rule`, `unmatched-default`, or
  `invalid-state-reset`. `rule` names the winning rule.
- `effective.source` is `desired` or `fallback`; with `fallback`, `effective` also carries
  `candidateIndex` and `skipped` (each with a reason code). Desired state never changes because
  a fallback is active.
- `applied` is what the plugins reported, per binding; `null` before the first apply finishes.
- `pending` lists bindings waiting on output that cannot currently be sent, such as native
  fallback without an output plugin.
- `notifications` holds at most one `plugin-recovered` entry per plugin:
  `{ "id": "n-1", "kind": "plugin-recovered", "plugin": "signalrgb", "actions": ["accept", "decline"] }`.
  Availability alone never restores output. Accept revalidates the current desired selection,
  capabilities, and ownership; decline or dismiss keeps the fallback active.

## Commands — frozen

UI and MCP use the same commands. Each returns `{ "ok": true, "status": { ... } }` or
`{ "ok": false, "error": { "code": "...", "message": "..." } }` without partial persistence.

| Command | Parameters | Effect |
|---|---|---|
| `selectProfile` | `profile` | Enters manual mode with that profile and persists it |
| `setMode` | `mode` | `automatic` resumes rules; `manual` keeps the current desired profile |
| `applySettings` | full `settings` document | Validates, persists, and reevaluates selection |
| `respondToNotification` | `id`, `response` (`accept`, `decline`) | Recovery choice; dismiss equals decline |
| `getStatus` | none | Returns the runtime status |

Preview, draft, and editor commands are added in slice 11 and must reuse these validation paths.

## Plugin protocol — frozen for the first plugin

JSON-RPC 2.0, one UTF-8 JSON message per line on standard input/output. Plugin logs go to
standard error. The host sends requests; the plugin sends responses and the `status/changed`
notification. Every request has a host-side timeout (default 5000 ms; `initialize` 10000 ms).
On timeout the host sends `$/cancel` with the request ID, records `timeout`, and ignores any
late response. Examples below are wrapped for readability; on the wire each is one line.

### Methods

| Method | Direction | Purpose |
|---|---|---|
| `initialize` | host → plugin | Version handshake and capability report |
| `status/get` | host → plugin | Provider availability and current observable selection |
| `catalog/list` | host → plugin | Effects or layouts the provider offers |
| `binding/apply` | host → plugin | Apply one provider binding |
| `binding/release` | host → plugin | Clear app-owned changes for a binding; may be a no-op |
| `shutdown` | host → plugin | Release resources; the plugin then exits |
| `status/changed` | plugin → host notification | Availability changed; the host coalesces repeats |
| `$/cancel` | host → plugin notification | Abandon a timed-out request |

### Handshake

```json
{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"contractVersion":"0.1","host":{"name":"rgb-playground","version":"0.1.0"}}}
{"jsonrpc":"2.0","id":1,"result":{"pluginId":"signalrgb","pluginVersion":"0.1.0","contractVersion":"0.1","capabilities":[
  {"id":"provider.effect.catalog","support":"supported"},
  {"id":"provider.effect.select","support":"supported"},
  {"id":"provider.selection.observe","support":"unverified","reason":{"code":"selection-not-observable","message":"Current effect could not be read back."}},
  {"id":"provider.layout.catalog","support":"unsupported","reason":{"code":"capability-unsupported","message":"No supported layout enumeration interface found."}},
  {"id":"provider.layout.select","support":"unsupported"},
  {"id":"provider.layout.export","support":"unsupported"},
  {"id":"provider.devices.report","support":"unsupported"}
]}}
```

The values above are an illustration of shape; slice 08 determines the real SignalRGB support.
`support` is `supported`, `unsupported`, or `unverified`. Unknown capability IDs are ignored.
A capability absent from the list is `unsupported`.

### Status

```json
{"jsonrpc":"2.0","id":2,"method":"status/get","params":{}}
{"jsonrpc":"2.0","id":2,"result":{"availability":"unavailable","reason":{"code":"provider-not-running","message":"SignalRGB is not running. It will not be started automatically."},"current":null}}
```

`availability` is `available`, `degraded`, or `unavailable`. `current` is
`{ "effect": <resource ref>, "layout": <resource ref or null> }` when observable, else `null`.

### Catalog

```json
{"jsonrpc":"2.0","id":3,"method":"catalog/list","params":{"kind":"effect"}}
{"jsonrpc":"2.0","id":3,"result":{"support":"supported","refreshedAt":"2026-10-10T09:00:00Z","items":[
  {"resourceId":"opaque-id-1","displayName":"Aurora","available":true},
  {"resourceId":"opaque-id-2","displayName":"Example Paid Effect","available":false,"reason":{"code":"account-restricted","message":"Requires a paid account tier."}}
]}}
```

`kind` is `effect` or `layout`. An unsupported catalog returns `"support":"unsupported"`, an
empty `items` list, and a reason. It never returns a guessed list.

### Apply and release

```json
{"jsonrpc":"2.0","id":4,"method":"binding/apply","params":{"profile":"code","bindingId":"main","renderingAuthority":"provider","deviceScope":{"kind":"provider-managed"},"effect":{"resourceId":"opaque-id-1","displayName":"Aurora"},"layout":null}}
{"jsonrpc":"2.0","id":4,"result":{"outcome":"unverified","reason":{"code":"selection-not-observable","message":"Request accepted; current effect cannot be read back."},"actual":null}}
```

- The host sends only bindings whose `resourceId` is resolved and whose required capabilities
  are `supported` or `unverified`. `layout: null` means send no layout command.
- `outcome` is one of:

  | Outcome | Meaning |
  |---|---|
  | `applied` | Provider confirmed the selection by reading it back (`actual` is set) |
  | `unverified` | Provider accepted the request but the result could not be observed |
  | `rejected` | Provider refused (for example `account-restricted`) |
  | `unsupported` | The plugin cannot perform this action |
  | `unavailable` | Provider or resource is absent (for example `provider-not-running`) |

  The host adds `timeout`, `plugin-absent`, `plugin-exited`, `contract-incompatible`, and
  `malformed-response` without plugin involvement. Only `applied` and `unverified` count as
  applied output; neither proves visible lighting, which is recorded separately.
- `binding/release` takes `{ "profile", "bindingId" }` and returns `{ "outcome": "released" |
  "nothing-to-release" | "unsupported" }`. A provider-authority effect selection has nothing
  app-owned to clear, so it returns `nothing-to-release`.
- Protocol errors (malformed params, unknown method) use JSON-RPC error codes; business
  outcomes always use `result.outcome`.

### Reason codes

`provider-not-running`, `account-restricted`, `resource-not-found`, `capability-unsupported`,
`selection-not-observable`, `device-owned-elsewhere`, `native-output-unavailable`,
`plugin-absent`, `plugin-exited`, `contract-incompatible`, `timeout`, `malformed-response`.
New codes may be added in a minor version; unknown codes display their `message`.

## Scenarios

### First run

No `state.json` exists. The core seeds `settings.json`, writes `state.json` with
`mode: manual`, `manualProfile: default`, and reports `desired.reason.kind: first-run`. The
foreground application is ignored. The coordinator resolves `Aurora` from the catalog and sends
`binding/apply`; the status shows `unverified` or `applied` as reported.

### Restart

`state.json` contains `{ "mode": "automatic", "lastSelectedProfile": "stremio" }`. Before
detection starts, the core applies `stremio` with `desired.reason.kind: restored`. After VS Code
holds focus beyond `debounceMs`, rule `code` wins and the reason changes to `rule`. With
`mode: manual`, the restored `manualProfile` stays active regardless of focus.

### Absent plugin

The `signalrgb` package is not installed or not enabled. Status shows desired profile `default`,
`applied.bindings[0].outcome: unavailable` with `plugin-absent`, and the profile fallback
`native-setup` activates. Because the default native setup has no bindings and no native output
exists, `effective.source` is `fallback` and `pending` reports `native-output-unavailable`. No
command is sent to any device. Selection, rules, and persistence keep working. If the plugin
later appears, one `plugin-recovered` notification is shown; output resumes only after Accept.

### Explicit user selection

In automatic mode with `code` active, the user picks Media Player. `selectProfile` persists
`{ "mode": "manual", "manualProfile": "media-player", "lastSelectedProfile": "media-player" }`
before applying, and the reason becomes `manual`. Focus changes no longer alter the selection
until `setMode automatic`. If apply then fails, desired state remains `media-player` and the
fallback chain runs as for an absent plugin.

### Priority with eligibility

Rule `media-player` is changed to `while-running` and moved above `code`. With Media Player
running in the background and VS Code focused, `media-player` wins. Setting it back to
`while-focused` lets `code` win on the next evaluation. Closing Media Player removes its
eligibility in either case.

## Out of scope for this contract

Native effects, layouts, frames and calibration; device registry and ownership journal; WLED
output; game discovery and telemetry events; plugin manifests and packaging; MCP transport and
authentication; preview and draft commands. Each is defined by its own slice and extends this
document or the extracted SDK instead of being guessed here.
