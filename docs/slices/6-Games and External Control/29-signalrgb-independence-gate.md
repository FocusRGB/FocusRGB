# 29 — SignalRGB independence gate

[← All implementation slices](../../implementation-slices.md)

Depends on: required native slices 10–18 plus 23. Scope: inventory remaining provider-dependent
profiles, migrate required ones with user review, and run with SignalRGB stopped.
Acceptance: required effects/layouts/devices and fallback work independently; other integration
plugins remain usable. Removing/uninstalling SignalRGB or modifying its startup is a separate
explicit user action.

