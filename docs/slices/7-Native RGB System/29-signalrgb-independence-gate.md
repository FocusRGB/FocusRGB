# 29 — SignalRGB independence gate

[← All implementation slices](../../implementation-slices.md)

Depends on: 16, 22, 24–28. Required runtime foundations from 13, 15, 21, and 23 are inherited
through those dependencies. Scope: inventory remaining provider-dependent
profiles, migrate required ones with user review, and run with SignalRGB stopped.
Acceptance: required effects/layouts/devices and fallback work independently; other integration
plugins remain usable. Removing/uninstalling SignalRGB or modifying its startup is a separate
explicit user action.

