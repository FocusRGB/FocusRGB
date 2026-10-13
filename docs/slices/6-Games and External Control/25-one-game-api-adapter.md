# 25 — One game API adapter

[← All implementation slices](../../implementation-slices.md)

Depends on: 14, 24. Scope: choose one supported API, create its `FocusRGB-Plugin-<Game>` repository,
document its event contract, implement the separate plugin against a published SDK version, and
record replay fixtures with source/freshness. Share only proven common fields; preserve
game-specific data namespaced.
Acceptance: replay changes effect inputs; stale/disconnected telemetry degrades predictably;
the repository builds without the app checkout; static selection remains usable. No
injection/memory reads or universal game schema.

