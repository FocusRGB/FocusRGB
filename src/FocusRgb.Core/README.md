# FocusRgb.Core

Portable .NET 10 application logic. Configuration and pure profile selection are implemented;
persistence, coordination, and output remain later slices. Only shared Contracts may be
referenced. There are no provider SDKs, OS calls, file writes, or lighting calls here.

```text
Configuration/
  SettingsDocument.cs       typed settings and separate native snapshot; unknown-field storage
  SettingsValidator.cs      structural checks, ID uniqueness, references, fallback cycles
  SettingsJson.cs           in-memory load/serialize, seed loading, empty native snapshot
  ConfigurationError.cs     path-specific errors and load result
  default-settings.json     editable acceptance mappings, embedded as seed data
Selection/
  SelectionModels.cs        observation, mode, decision, and reason types
  SelectionEngine.cs        manual/automatic arbitration and focus debounce
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

Missing fields stay distinguishable from valid zero/false values. Only schema version 1 is
accepted. Migration/atomic file persistence arrives in slice 06. Application authority and
reserved game/device scope shapes can validate, but this does not claim runtime support.

`CreateDefaultNativeSetup()` returns an independent empty app-default snapshot. Slice 06
handles its storage; slices 17–18 add native rendering/output. It is not derived from provider
bindings. Runtime intent (`state.json`) remains outside this slice.

## Selecting a profile

Construct `SelectionEngine` with validated settings, then pass `SelectionMode`, the retained
manual profile ID, an `ApplicationObservation`, and its timestamp to `Select`. Automatic mode
uses case-insensitive executable matching, higher numeric priority, saved rule order, and stable
rule ID. Focus-only rules wait for `debounceMs`; ignored foreground executables preserve the
previous stable focus. A process must still appear in `RunningExecutables`, so exit removes
eligibility immediately. No eligible rule returns protected Default. The result reports
`manual`, `rule` (with its rule ID), or `unmatched-default`.

The caller owns process observation and time. The selector does not read the OS, persist state,
or call an integration or device. Runtime observation wiring arrives in slice 07.

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
