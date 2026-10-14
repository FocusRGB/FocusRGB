# 08 — Desktop runtime and tray

[← All implementation slices](../../implementation-slices.md)

Depends on: 06, 07. Scope: host persisted state in the Avalonia app; add tray show/quit and
connect the profile picker, manual mode, desired/applied/reason display, save errors, and clean
shutdown to shared local commands. Automatic application selection remains unavailable until
slice 13. Use minimize-to-tray as the temporary default; slice 09 makes it configurable.

Acceptance: the last manual profile is restored and shown after restart; closing the window
leaves the local engine running; Quit saves valid state and exits cleanly; the UI stays responsive
during storage failure and reports it. No process observation, external plugin, or hardware output.

Status: **Complete.** The app uses explicit-shutdown lifetime with a FocusRGB tray icon, Show and
Quit actions, and close-to-tray behavior. Profile and mode changes run asynchronously through the
portable `SelectionCommandService`, which persists state before updating the UI. Quit flushes the
current valid state; a failed save keeps the app open, restores the window, and reports the error.
The Home status distinguishes desired selection from unavailable applied output.

Verification: `dotnet build FocusRGB.slnx --no-restore` passes with no warnings, and
`dotnet test FocusRGB.slnx --no-restore` passes 122 tests. Tests cover persisted manual restart,
mode retention, honest unapplied status, atomic command failure, and responsive UI error state.
The Avalonia tray XAML/code compiles; interactive tray-menu clicking was not automated.
