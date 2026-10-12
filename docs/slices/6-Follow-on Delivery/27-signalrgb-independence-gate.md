# 27 — SignalRGB independence gate

[← All implementation slices](../../implementation-slices.md)

Depends on: required native slices 18–23 and 26. Scope: inventory remaining provider-dependent
profiles, migrate required ones with user review, and run with SignalRGB stopped.
Acceptance: required effects/layouts/devices and fallback work independently; other integration
plugins remain usable. Removing/uninstalling SignalRGB or modifying its startup is a separate
explicit user action.

