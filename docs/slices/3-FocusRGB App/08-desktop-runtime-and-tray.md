# 08 — Desktop runtime and tray

[← All implementation slices](../../implementation-slices.md)

Depends on: 06, 07. Scope: host persisted state in the Avalonia app; add tray show/quit and
connect the profile picker, manual mode, desired/applied/reason display, save errors, and clean
shutdown to shared local commands. Automatic application selection remains unavailable until
slice 20. Use minimize-to-tray as the temporary default; slice 09 makes it configurable.

Acceptance: the last manual profile is restored and shown after restart; closing the window
leaves the local engine running; Quit saves valid state and exits cleanly; the UI stays responsive
during storage failure and reports it. No process observation, external plugin, or hardware output.
