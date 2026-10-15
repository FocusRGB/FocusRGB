# 17 — One native peripheral backend

[← All implementation slices](../../implementation-slices.md)

Depends on: 10, 14, 15. Scope: choose keyboard or mouse and one actual supported model, then implement
its separate output plugin and semantic/geometry map. Repeat as new slices for other models.
Acceptance: static/canvas ownership and release are verified, actual LEDs checked,
macros/special keys preserved;
no support claims for untested devices.
