# 23 — SignalRGB acceptance gate

[← All implementation slices](../../implementation-slices.md)

Depends on: 15, 20, 22. Scope: connect automatic observations and persisted mappings to shared
commands, provider catalogs, status, errors, and reconnect approval. Add capability-aware
effect/layout controls to profile bindings with explicit unavailable states, then run clean
build/tests and the explicitly requested live SignalRGB session using the four mappings. Retain
current SignalRGB layout and WLED routing.
Acceptance: first-start/manual, restart, automatic selection, priority eligibility, absent
plugin, and recovery notification match requirements. User verifies visible effect switching;
catalog or application limitations are recorded as remaining gates. The UI stays responsive
during provider timeout, and reconnect never resumes output without approval. Exact catalog
matches save provider resource IDs; unsupported catalogs never fabricate choices. No native WLED writes.
