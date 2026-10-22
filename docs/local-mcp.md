# Local MCP

FocusRGB exposes an optional Windows-only, read-only Model Context Protocol adapter. It uses the
official `ModelContextProtocol` C# SDK 2.2.0 and MCP protocol version 2026-07-28. Run
`FocusRgb.Mcp.exe` as a stdio MCP server while the FocusRGB desktop app is running.

The Settings-page switch is off by default. Its versioned state and random per-install credential
live in `%LOCALAPPDATA%\FocusRGB\mcp.json`, outside `settings.json`; MCP has no tool that can change
the switch. Missing, invalid, or disabled configuration means no named pipe is created. The switch
is hidden on unsupported platforms. A running stdio adapter reloads this file for every tool call,
so changing the UI switch does not require restarting the MCP host.

## Tools

- `listProfiles`, `listApplicationGroups`, and `listMappings` return saved configuration; mappings
  include their effective list order.
- `listPlugins` returns installed packages and their official, enabled, and running state.
- `listDevices` returns configured provider-managed or explicit device scopes. It does not claim
  physical-device discovery.
- `getStatus` returns the current revision, saved selection state, and runtime output status.
- `explainActiveSelection` returns desired/effective/applied selection and its reason.

These tools query the app-owned state service. They do not read or edit configuration files,
launch processes, install plugins, change output, or preview profiles.

## Local security boundary

The adapter talks to the running app through a per-user Windows named pipe. The app creates the
pipe with `CurrentUserOnly` and `FirstPipeInstance`, accepts a bounded private protocol, and checks
the random credential before dispatch. The adapter verifies both the pipe server process ID and
its full executable path before sending the credential. Disabling MCP closes the listener and its
active connection.

This protects against other OS users and accidental clients. It is not a privilege boundary from
malware already running as the same user: such a process can read the app-data credential. Future
write tools therefore still require the separate in-app approval design from Slice 18 PR 3.
