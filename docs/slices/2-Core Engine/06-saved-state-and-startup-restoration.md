# 06 — Saved state and startup restoration

[← All implementation slices](../../implementation-slices.md)

Depends on: 05. Scope: versioned local settings, atomic save, preserved prior valid data on
failure, first-run manual Default, restart restoration, and saved native fallback snapshot.
Acceptance: restart restores desired mode/profile; preview/provider changes never overwrite
native fallback; corrupt/missing files have explicit recovery behavior without silently
destroying the previous file. Desired state can persist while applied output is unavailable.

