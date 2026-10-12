# 05 — Pure selection engine

[← All implementation slices](../../implementation-slices.md)

Depends on: 02. Scope: supplied observations select a profile with deterministic ordered
priority and per-rule eligibility. Implement manual override, ignored windows, debounce,
process exit, and unmatched policy. Inject time/observations for tests; no OS or lighting calls.
Acceptance: higher-priority running A beats focused B; focus-only A yields; A exiting removes
eligibility; manual mode wins; ties and brief focus changes are stable. Return selection reason.

