# 13 — Native screen ambience and calibration

[← All implementation slices](../../implementation-slices.md)

Depends on: 10, 11. Scope: Windows capture input, selected monitor, canvas mapping, native
per-device color calibration. Split capture and calibration into children if needed.
Acceptance: monitor direction and desk exclusion are correct; cancellation releases capture;
fake images and fake output check sampling. Actual-device color validation waits for slice 16.
Capture is independent of app focus and external application observation.

