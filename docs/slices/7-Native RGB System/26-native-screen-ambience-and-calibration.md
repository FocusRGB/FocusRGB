# 26 — Native screen ambience and calibration

[← All implementation slices](../../implementation-slices.md)

Depends on: 21, 23. Scope: Windows capture input behind `FocusRgb.Platform.Windows`, selected monitor, canvas mapping, native
per-device color calibration. Split capture and calibration into children if needed.
Acceptance: monitor direction and desk exclusion are correct; cancellation releases capture;
fake images and fake output check sampling. Actual-device color validation uses slice 22 WLED output only in an explicitly requested live check.
Capture is independent of app focus and external application observation.
