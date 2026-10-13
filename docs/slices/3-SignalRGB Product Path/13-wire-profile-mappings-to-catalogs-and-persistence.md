# 13 — Wire profile mappings to catalogs and persistence

[← All implementation slices](../../implementation-slices.md)

Depends on: 04, 11, 12. Scope: connect the existing editor to detected/manual apps, persisted
settings, and real provider catalogs. Keep Apply/Cancel and Live preview behavior; replace fake
capabilities with capability-aware profile controls and explicit unavailable states. Application
mappings select a profile only; effect/layout/provider choices belong to that profile's bindings.
Acceptance: mappings persist, preview does not save, Cancel restores supported prior output,
unchecked preview sends nothing before Apply. Fake catalogs test unavailable resources;
unsupported SignalRGB catalogs remain a visible limitation, not a fabricated list. Multiple
mappings for one profile cannot diverge on effect/layout, and a zero-binding profile remains a
valid explicit no-output choice.

