# 20 — Application mappings and persistence

[← All implementation slices](../../implementation-slices.md)

Depends on: 04, 07, 08, 19. Scope: connect the existing mapping editor to detected and manually
entered applications, persisted settings, and the profile editor. Add/edit/remove mappings,
assign profiles, reorder priority, and configure while-running/while-focused eligibility with
Apply/Cancel and optional Live preview. Mappings select profiles only; effects, layouts,
providers, and fallbacks remain profile-owned.

Acceptance: mappings survive restart; preview does not save; Cancel restores the prior draft;
unchecked preview sends nothing before Apply. Users can select an observed executable or enter
one manually. Multiple mappings for one profile cannot diverge on effect/layout, removing an
application makes its rule ineligible, and protected Default handles no-match automatic state.
No SignalRGB dependency or hardware output.
