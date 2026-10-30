# FocusRgb.Core

Portable .NET 10 application logic. Configuration, pure profile selection, atomic local
selection commands, and provider-neutral output coordination are implemented. Only shared Contracts
may be referenced. There are no provider SDKs, platform APIs, or lighting calls here.

```text
Configuration/
  SettingsDocument.cs       typed settings and separate native snapshot; unknown-field storage
  SettingsValidator.cs      structural checks, ID uniqueness, references, fallback cycles
  SettingsJson.cs           in-memory load/serialize, seed loading, empty native snapshot
  ConfigurationError.cs     path-specific errors and load result
  default-settings.json     editable acceptance mappings, embedded as seed data
Commands/
  SelectionCommandService.cs persisted select-profile, set-mode, save, and local status
Coordination/
  OutputCoordinator.cs       desired/applied/fallback state, ownership, and recovery consent
Selection/
  SelectionModels.cs        observation, mode, decision, and reason types
  SelectionEngine.cs        manual/automatic arbitration and focus debounce
Persistence/
  PersistenceModels.cs      startup snapshot, recovery notice, and result types
  ISelectionStateStore.cs   narrow write boundary used by shared selection commands
  LocalStateStore.cs        validated load/save, atomic replacement, startup recovery
```

## Loading settings

Call `SettingsJson.Load(json)` and check `IsSuccess`. Invalid input returns `Errors` with
JSON paths and no settings object. `LoadDefaults()` loads the seed resource through that same
validation path. `SettingsValidator.Validate(document)` also validates programmatically edited
settings. Serialization preserves unknown fields on each modeled object and opaque provider
IDs; it does not validate or write a file. Call validation before accepting/saving edits.
The `default` profile ID is protected: validation requires it and requires
`selection.defaultProfile` to keep referencing it. Seeded unmatched selection is always
`default-profile`.

Missing fields stay distinguishable from valid zero/false values. Settings schema versions 1
and 2 migrate to version 3 with application preferences and disabled-by-default plugin
enablement. Unsupported newer versions are preserved and reported rather than overwritten.
Application authority and reserved game/device scope shapes can validate, but this does not
claim runtime support.

`NativeSetupJson.CreateDefault()` returns an independent empty app-default snapshot, and
`LocalStateStore` handles its storage. Slices 20–21 add native rendering and output. The snapshot
is not derived from provider bindings. Runtime intent is stored separately in `state.json`.

## Selecting a profile

Construct `SelectionEngine` with validated settings, then pass `SelectionMode`, the retained
manual profile ID, an `ApplicationObservation`, and its timestamp to `Select`. Automatic mode
uses case-insensitive executable matching and saved rule order; the first eligible rule wins.
Focus-only rules wait for `application.switchingSpeed` (Fast 300 ms, Normal 750 ms, Smooth 2000 ms);
schema v6 migrates existing settings to Normal. Schema v7 adds default-off `application.debuggingMode`;
schema v8 adds `application.notification` (size, opaque background preset and duration) while preserving
the independent debugging/logging choices in v7.
older settings with diagnostic logging enabled migrate with Debugging mode enabled. Recovery through
`settings.last-known-good.json` applies the same migration. The legacy `selection.debounceMs` field is retained
for document compatibility; the application preset controls selection. Ignored foreground executables preserve the
previous stable focus. A process must still appear in `RunningExecutables`, so exit removes
eligibility immediately. No eligible rule returns protected Default. The result reports
`manual`, `rule` (with its rule ID), `observation-pending`, or `unmatched-default`. On startup
and whenever manual mode returns to automatic, the engine clears old focus history and retains
the supplied current profile until the first foreground observation is stable. While-running
rules can still win immediately.

The caller owns process observation and time. The selector does not read the OS or call an
integration or device. `LocalStateStore` persists its desired result independently of output.
Windows observations arrive in slice 12; slice 13 wires them to automatic selection and persistence.

## Persisting startup state

Construct `LocalStateStore` with the per-user data directory selected by the application, then
call `InitializeAsync`. It owns `settings.json`, its internal
`settings.last-known-good.json` recovery copy, `state.json`, and `native-setup.json` within that
directory. Missing files receive validated defaults; the first missing state starts in manual
Default. A valid restart restores the saved mode and desired profile before detection.

Writes serialize and flush a unique temporary file before atomically replacing the target. A
validation, serialization, or replacement failure leaves the prior target untouched. Malformed
documents are renamed with a timestamp before a replacement is seeded, and the returned startup
snapshot includes a `RecoveryNotice`. Unknown state profile references are archived and reset
to manual Default with `invalid-state-reset`. Only a numeric schema version above the supported
version is left in place and returned as an error; missing or malformed versions follow corrupt
document recovery. An invalid current settings file restores the validated last-known-good copy
before defaults are considered. JSON readers accept comments and trailing commas for hand-edited
files.

Use `SaveSettingsAsync`, `SaveStateAsync`, and `SaveNativeSetupAsync` only for their named
documents. Saving desired state requires no applied output. Provider edits, previews, and
fallback activation must never call `SaveNativeSetupAsync`; only an explicit user-applied
native setup owns that snapshot.

The store does not choose an OS data path, read processes, or call an integration or device.
The Windows app stores these files under `%LOCALAPPDATA%\FocusRGB`.

## Running local selection commands

Construct `SelectionCommandService` with an `ISelectionStateStore` and optional
`OutputCoordinator`. `SelectProfileAsync` and `SetModeAsync` save replacement intent before output,
so failed persistence never changes lighting. The coordinator resolves provider resources and
capabilities, prevents an unconfirmed second writer, walks validated fallbacks, and reports
native fallback as pending until native output exists. Recovered plugins remain blocked behind a
coalesced Accept/Decline notification; Accept reapplies current intent. The Avalonia UI uses this
service, and the future local MCP adapter can call the same boundary without desktop controls.

## Adding a profile

1. Add a profile with a stable lowercase ID, display name, bindings, and fallback list to
   user settings (or `default-settings.json` for a deliberate first-run seed change).
2. Choose a plugin ID and rendering authority. Provider bindings need an effect reference;
   an unresolved resource ID is null with a display name. Omit/null layout to retain provider
   placements. Empty bindings are valid for a profile that produces no output.
3. Add an optional rule targeting that profile with executable names, eligibility, priority,
   and enabled state. Array order is the saved priority tie order.
4. Validate the whole document. The protected `default` profile cannot be removed or replaced.
   Repeated profile/rule IDs, duplicate bindings within a profile, missing references, and
   fallback cycles are rejected with field paths.
5. Add an acceptance case in `tests/FocusRgb.Core.Tests/SettingsTests.cs` if new behavior is
   introduced. Do not add plugin-specific C# constants or resolve provider catalogs here.

Run from the repository root:

```powershell
dotnet build FocusRGB.slnx -c Release
dotnet test --solution FocusRGB.slnx -c Release
```
