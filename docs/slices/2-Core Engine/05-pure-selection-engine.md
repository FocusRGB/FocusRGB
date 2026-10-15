# 05 — Pure selection engine

[← All implementation slices](../../implementation-slices.md)

**Status:** Complete — `eb68a82`, PR #12 dependent on PR #11. Build and 74 tests passed,
covering manual override, arbitration, debounce, ignored windows, exit, and Default fallback.

Depends on: 02. Scope: supplied observations select a profile with deterministic ordered
priority and per-rule eligibility. Implement manual override, ignored windows, debounce,
process exit, and unmatched policy. Inject time/observations for tests; no OS or lighting calls.
Acceptance: an earlier running A beats a later focused B; focus-only A yields; A exiting removes
eligibility; manual mode wins; saved ordering and brief focus changes are stable. Return selection reason.

