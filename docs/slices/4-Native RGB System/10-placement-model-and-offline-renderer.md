# 10 — Placement model and offline renderer

[← All implementation slices](../../implementation-slices.md)

Depends on: 07. Scope: coordinates/transforms, stable device and LED identities, optional
direct-only participation, exclusion, and a small built-in static/gradient effect catalog
producing frames to an in-memory test sink. The fake plugin output arrives in slice 14. A minimal default native setup is created here.
Acceptance: transform/reversal sampling preserves indices; unassigned devices get no frames;
direct-only devices need no canvas coordinates; unequal LED counts sample normalized positions;
profiles can select a built-in effect without any external application or hardware output.
