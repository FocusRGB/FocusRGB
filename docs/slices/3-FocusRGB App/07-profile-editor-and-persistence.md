# 07 — Profile editor and persistence

[← All implementation slices](../../implementation-slices.md)

Depends on: 03, 06. Scope: make the Profiles page a real editor backed by persisted settings.
Create, rename, duplicate, and delete profiles; edit the profile's single effect, single layout,
and fallback choices with
the shared selected-item actions (`Save As` creates, `Save` edits, `Remove` deletes), plus optional
Live preview. Default always exists and cannot be renamed or removed.
Use local built-in/fake capabilities only; external application catalogs arrive in later slices.

Acceptance: valid profile changes survive restart; Cancel restores the prior draft and preview;
unchecked preview sends nothing before Save; duplicate IDs, fallback cycles, and deletion of a
referenced profile are rejected with a useful path. Deleting a non-Default profile requires its
mappings and fallback references to be reassigned first. A profile always has exactly one output
binding; a provider-managed `null` layout means keep that provider's current layout. No process
detection, SignalRGB call, or hardware output.

Status: **Complete.** The Avalonia app now opens the validated local store at
`%LOCALAPPDATA%\FocusRGB`, restores the saved selection, and persists profile edits atomically.
The Profiles page supports Save, Save As, Cancel, Remove, optional draft preview, one effect, one
layout, and a primary native-setup or profile fallback. Default cannot be renamed or removed;
mapped and fallback-referenced profiles must be reassigned before removal. If the removed profile
was saved as current/last selection, those state references are safely changed to Default first.

Verification: `dotnet build FocusRGB.slnx --no-restore` and
`dotnet test FocusRGB.slnx --no-restore` pass; 117 tests cover restart persistence, unique IDs,
Cancel/preview behavior, fallback cycles, protected references, and selection repair on deletion.
