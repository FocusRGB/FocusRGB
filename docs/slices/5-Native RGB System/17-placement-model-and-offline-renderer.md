# 17 — Placement model and offline renderer

[← All implementation slices](../../implementation-slices.md)

Depends on: 02; schedule after Goal 2 by default. Scope: coordinates/transforms, stable device
and LED identities, optional direct-only participation, exclusion, and one static/gradient
canvas effect producing frames to fake output. A minimal default native setup is created here.
Acceptance: transform/reversal sampling preserves indices; unassigned devices get no frames;
direct-only devices need no canvas coordinates; unequal LED counts sample normalized positions.

