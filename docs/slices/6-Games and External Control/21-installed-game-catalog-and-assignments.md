# 21 — Installed game catalog and assignments

[← All implementation slices](../../implementation-slices.md)

Depends on: 11, 19. Scope: separate Steam discovery plugin built with the public SDK, manual game entries, scoped IDs,
and generic/per-game profile assignments. Choose the first game with the user before real
fixtures are required. Reuse the existing editor/rules.
Acceptance: duplicate titles across launchers do not collide; running state never invents focus;
static game profiles work without telemetry.

