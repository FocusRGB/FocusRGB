# SignalRGB capability investigation — 2026-10-18

This is the read-only evidence record for Slice 14. The installed client was not started, no
application URL was submitted, and no lighting, layout, device, account, or provider setting was
changed. Results are specific to SignalRGB 2.5.80 and the account/cache state on this machine.

## Decision table

| Capability | Result | Evidence and consequence for Slice 15 |
|---|---|---|
| Process discovery | Verified | `SignalRgb.exe` is the process name and the latest executable is `%LOCALAPPDATA%\VortxEngine\app-2.5.80\Signal-x64\SignalRgb.exe` (file version `2.5.80.0`). No process was running during this check. Discover the newest semantic `app-*` directory; do not pin a version. |
| Launcher discovery | Verified | `%LOCALAPPDATA%\VortxEngine\SignalRgbLauncher.exe` exists (file version `1.0.0.1`). The reviewer's live check found the `signalrgb:` protocol registered in both HKCU and HKCR and pointing to this launcher. SignalRGB also documents direct launcher use with `--url`; use the launcher path because shell registration cannot be assumed on every machine. |
| Effect catalog | Verified with limits | Five bundled effects and two downloaded cached effects were readable without starting SignalRGB. The cache is evidence for this installation, not a stable API. Prefer the documented Local API when authorized; use file discovery only as an explicitly degraded catalog with honest availability. |
| Current effect | Unavailable in this session | The documented read-only endpoint is `GET http://127.0.0.1:16038/api/v1/lighting`. SignalRGB was stopped and port 16038 was not listening. A prior 2.5.74 check found the lighting API Pro-gated on this account. Do not infer the current effect from cache files. |
| Effect selection | Verified historically; not repeated | The documented `signalrgb://effect/apply/<encoded-name>` route previously selected installed/free effects through `SignalRgbLauncher.exe`. It provides no trustworthy confirmation of visible output. The Local API apply route was Pro-gated on this account. Slice 15 may expose launcher selection, but must report submission separately from confirmed application. |
| Layout catalog/current layout | Unavailable in this session | SignalRGB documents `GET /api/v1/scenes/layouts` and `GET /api/v1/scenes/current_layout`. The process was stopped, and no readable local layout state was present. The plugin must report this capability unavailable until a live authorized check succeeds. |
| Layout export/geometry | Unavailable | The documented Local API lists layout names and the selected layout only. It exposes no read/export endpoint for device positions, rotation, scale, inversion, or layout brightness. Do not advertise layout export from this interface. |
| Account restrictions | Verified | Official API documentation says most Local API endpoints require Pro and return HTTP 403 when the running user is unsigned or not Pro. The earlier 2.5.74 live check received this result for lighting reads and writes. URL submission is not an entitlement bypass. |
| External application endpoints | Verified | Officially documented surfaces are application URLs for effect/page navigation and Canvas events at `http://localhost:16034/canvas/event`. Canvas events target a selected custom lightscript; they do not select that effect. No request was sent during this read-only slice. |
| MCP transport | Unavailable in 2.5.80 | `app-2.5.74\Signal-x64\SignalRgbMcp.exe` exists (file version `1.0.0.1`), but the executable was removed from `app-2.5.80`. The repository launcher selects the newest `app-*` directory and therefore fails with 2.5.80. MCP tool names recorded in old research are not a supported wire contract for the plugin. |

## Resolved fixture names

The exact installed names are:

| Profile | Effect name | Local evidence |
|---|---|---|
| Default | `Aurora` | Cache catalog ID `-M6kb-ZFTdoKW5tRxV8Y`, price 0, downloaded effect file present |
| Programming | `Aurora` | Same catalog item |
| Movie | `Screen Ambience` | Bundled `Effects\Dynamic\Screen Ambience.html`; its HTML title is also `Screen Ambience` |
| Music | `Logarithmic Visualizer` | Cache catalog ID `-NZhF99k6cK__Zyh4yNx`, price 0, downloaded effect file present |

`Screen Ambient` was not present in the bundled effects, downloaded effect cache, or cached
catalog. The seeded Movie fixture has therefore been corrected to `Screen Ambience`. Resource
IDs remain unset in defaults because the two marketplace IDs are provider-owned cache data and
the built-in effect uses a filename rather than the same catalog identifier shape. Slice 15 must
resolve fresh, exact, case-sensitive names through the provider catalog.

## Reproduce the local read-only checks

Run these commands without starting SignalRGB:

```powershell
$base = Join-Path $env:LOCALAPPDATA 'VortxEngine'
$app = Get-ChildItem -LiteralPath $base -Directory -Filter 'app-*' |
  Sort-Object { [version]($_.Name.Substring(4)) } -Descending |
  Select-Object -First 1
$signalDir = Join-Path $app.FullName 'Signal-x64'

Get-Item -LiteralPath (Join-Path $base 'SignalRgbLauncher.exe'),
  (Join-Path $signalDir 'SignalRgb.exe') |
  Select-Object FullName, @{n='FileVersion';e={$_.VersionInfo.FileVersion}}

Get-CimInstance Win32_Process -Filter "Name = 'SignalRgb.exe'"
Get-NetTCPConnection -State Listen |
  Where-Object LocalPort -in 16034,16038

Get-ChildItem -LiteralPath (Join-Path $signalDir 'Effects') -File -Recurse -Filter '*.html' |
  Select-Object BaseName, FullName

reg.exe query 'HKCU\Software\Classes\signalrgb' /s
reg.exe query 'HKCR\signalrgb' /s

Get-ChildItem -LiteralPath $base -File -Recurse -Filter 'SignalRgbMcp.exe'
```

The cached marketplace catalog used only to reconcile fixture spelling is:

```text
%LOCALAPPDATA%\WhirlwindFX\SignalRgb\cache\catalog_en.json
```

Its timestamp was `2026-10-15 14:26:35Z`. Treat it as stale whenever SignalRGB has not refreshed
it, and never treat a marketplace entry as installed merely because it appears in this file.

## Documented interfaces

- [Local API introduction](https://docs.signalrgb.com/developer/signalrgb-api/introduction/)
  documents the `http://127.0.0.1:16038/api/v1` base URL, unstable contract warning, and Pro
  restriction.
- [Lighting API](https://docs.signalrgb.com/developer/signalrgb-api/lighting/) documents current
  effect and installed-effect reads plus effect application.
- [Layouts API](https://docs.signalrgb.com/developer/signalrgb-api/layouts/) documents current
  layout and layout-name reads; it does not document geometry export.
- [Application URLs](https://docs.signalrgb.com/developer/application-urls/) and
  [game/app integrations](https://docs.signalrgb.com/lightscripts/creating-dev-integrations/)
  document effect URLs, direct launcher use, URL encoding, silent launch, and Canvas events.
- [Pro features](https://docs.signalrgb.com/gettingstarted/free-vs--pro/) documents account and
  device-class restrictions that can affect otherwise installed effects.

## Slice 15 constraints

- Implement process and launcher discovery without a pinned application version.
- Treat capabilities independently: launcher effect submission may be supported while current
  effect, effect catalog, or layouts are unavailable.
- Never claim an effect was applied from launcher exit alone; expose a pending/unconfirmed result
  unless a read-back surface verifies it.
- Do not implement layout export or MCP transport against this client version.
- Schema 4 settings migrate the unresolved seeded Movie effect from `Screen Ambient` to
  `Screen Ambience`; resolved or custom resource references are preserved.
- Preserve provider authority: the plugin selects provider resources but does not control devices
  directly.

Assisted-by: OpenAI Codex
