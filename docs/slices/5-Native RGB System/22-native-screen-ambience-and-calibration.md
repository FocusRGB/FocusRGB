# 22 — Native screen ambience and calibration

[← All implementation slices](../../implementation-slices.md)

Depends on: 18, 19. Scope: Windows capture input, selected monitor, canvas mapping, native
per-device color calibration. Split capture and calibration into children if needed.
Acceptance: monitor direction and desk exclusion are correct; cancellation releases capture;
fake images check sampling; user validates actual colors. Capture is independent of app focus.

