# 26 — Startup, packaging, and recovery gate

[← All implementation slices](../../implementation-slices.md)

Depends on: 14; repeat relevant gate checks after native output ships. Scope: packaging,
optional user-controlled startup, crash journal recovery, and clean shutdown/reconnect checks.
Acceptance: install/run/uninstall checks preserve user settings; recovered stale output is
reported honestly; startup applies saved intent without granting recovered plugins authority
against the user's reconnect preference. No unrelated task/service changes.

