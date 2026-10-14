# 24 — Hybrid game layers

[← All implementation slices](../../implementation-slices.md)

Depends on: 17, 22, 23. Scope: canvas base, named semantic zones/ranges, direct key/LED layer,
deterministic composition, and a small binding editor. Use fake keyboard/device capabilities.
Acceptance: named keys work without precise placement; a direct key overrides only its target;
strip/fan zones work without per-LED edits; exclusions and provider authority remain enforced.

