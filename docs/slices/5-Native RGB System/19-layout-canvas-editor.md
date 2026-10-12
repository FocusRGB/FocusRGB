# 19 — Layout canvas editor

[← All implementation slices](../../implementation-slices.md)

Depends on: 13, 17. Scope: assign/unassign devices, move/rotate/stretch/compress, direct-only
participation, persistence, and existing preview controls.
Acceptance: monitor-only layout excludes wall strips; changing inclusion stops outgoing app
streams through coordinator; editing keeps hardware LED counts unchanged. Check pointer and
keyboard interaction. Native preview can use fake output before live WLED is available.

