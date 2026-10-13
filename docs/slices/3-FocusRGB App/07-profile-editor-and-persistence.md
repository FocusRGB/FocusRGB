# 07 — Profile editor and persistence

[← All implementation slices](../../implementation-slices.md)

Depends on: 03, 06. Scope: make the Profiles page a real editor backed by persisted settings.
Create, rename, duplicate, and delete profiles; edit the profile's single effect, single layout,
and fallback choices with
the shared selected-item actions (`Save As` creates, `Save` edits, `Remove` deletes), plus optional
Live preview. Default always exists and cannot be renamed or removed.
Use local built-in/fake capabilities only; external application catalogs arrive in later slices.

Acceptance: valid profile changes survive restart; Cancel restores the prior draft and preview;
unchecked preview sends nothing before Apply; duplicate IDs, fallback cycles, and deletion of a
referenced profile are rejected with a useful path. Deleting a non-Default profile requires its
mappings and fallback references to be reassigned first. A profile always has exactly one output
binding; a provider-managed `null` layout means keep that provider's current layout. No process
detection, SignalRGB call, or hardware output.
