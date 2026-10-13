# 20 — Application mappings and persistence

[← All implementation slices](../../implementation-slices.md)

Depends on: 04, 07, 08, 19. Scope: connect the existing mapping editor to detected and manually
entered applications, persisted settings, and the profile editor. Persist named Application
Groups containing executable lists; add/remove executables using observed apps, manual names, or
the Windows `.exe` picker. Add/edit/remove mappings by selecting an Application Group and a
profile, reorder the list, and configure while-running/while-focused eligibility with
Apply/Cancel and optional Live preview. Mappings select profiles only; effects, layouts,
providers, and fallbacks remain profile-owned.

Acceptance: application groups and mappings survive restart; preview does not save; Cancel restores the prior draft;
unchecked preview sends nothing before Apply. Users can select an observed executable or enter
one manually. The first eligible mapping is the winner with no numeric priority field. Multiple
mappings for one profile cannot diverge on effect/layout, removing an
application makes its rule ineligible, and protected Default handles no-match automatic state.
No SignalRGB dependency or hardware output.
