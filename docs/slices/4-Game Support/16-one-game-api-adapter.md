# 16 — One game API adapter

[← All implementation slices](../../implementation-slices.md)

Depends on: 15. Scope: choose one supported API, document its event contract, implement one
separate plugin, and record replay fixtures with source/freshness. Share only proven common
fields; preserve game-specific data namespaced.
Acceptance: replay changes effect inputs; stale/disconnected telemetry degrades predictably;
static selection remains usable. No injection/memory reads or universal game schema.

