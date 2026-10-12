# 03 — UI shell with preview state

[← All implementation slices](../../implementation-slices.md)

Depends on: 02. Scope: replace the placeholder window with the minimal settings shell,
navigation, manual/automatic control, profile picker, desired/applied/reason status, and clear
preview-only integration status. Populate it from validated default settings through a small
UI-facing state model; do not implement detection, persistence, tray behavior, or outputs.
Acceptance: the Windows app builds and opens with Default in manual mode; all four fixture
profiles are visible; changing the preview mode/profile updates displayed state; state behavior
has focused tests; the UI never suggests that mock output reached hardware.

