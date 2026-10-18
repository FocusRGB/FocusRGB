# 28 — Startup, packaging, and recovery gate

[← All implementation slices](../../implementation-slices.md)

Depends on: 09, 11, 16, 18, 20, 22, 26, 27. Scope: packaging,
optional user-controlled startup wired to the application settings, crash journal recovery,
and clean shutdown/reconnect checks for everything delivered through slice 27, including the
local MCP server, game plugins, and screen-capture release. This gate
repeats the packaging regression after external-observation and SignalRGB work has landed. Its
Windows lifecycle coverage distinguishes a session-end query from a committed end so a cancelled
shutdown leaves the app running ([#76](https://github.com/MAJ224/FocusRGB/issues/76)).
Acceptance: install/run/uninstall checks preserve user settings; recovered stale output is
reported honestly; startup applies saved intent without granting recovered plugins authority
against the user's reconnect preference; cancelled and committed Windows shutdown paths behave
correctly. No unrelated task/service changes.

