# Local MCP

FocusRGB exposes an optional Windows-only Model Context Protocol adapter. It uses the
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
- `selectProfile`, `setMode`, `applySettings`, and `respondToNotification` expose the frozen
  selection and settings commands.
- `upsertProfile`, `upsertApplicationGroup`, and `reorderMappings` build a complete candidate
  document and use the same revision-checked `applySettings` path as the UI.

The tools call the app-owned state service. They never edit configuration files directly, launch
processes, install plugins, or preview profiles. Real-output preview remains deferred to #112.

## Write approval

Every command that persists data or can change output waits for an explicit decision in FocusRGB.
The prompt shows the connected MCP client's reported identity and a readable, command-specific
summary; raw settings JSON is not used as the consent text. One request may wait at a time. Later requests get
`busy`, approvals expire after one minute, and a decline starts a three-second cool-down.

The approval is bound to the command, payload hash, client identity, and settings revision. The
application checks the revision again under its shared command gate after approval. A concurrent UI
save therefore returns `conflict` without applying the approved request. Waiting for the owner does
not hold that gate. Once approved, FocusRGB completes the command even if the MCP client disconnects.

When the app is hidden, the tray icon and its **Review MCP request** item advertise the request
without activating FocusRGB or replacing the observed foreground application. The approval panel
opens only after the owner uses that tray item. Decline, expiry, client disconnect, disabling MCP,
or shutdown clears the request without side effects. MCP exposes no approval or switch-control tool.

## Local security boundary

The adapter talks to the running app through a per-user Windows named pipe. The app creates a
protected ACL containing only the current user SID, uses `FirstPipeInstance`, accepts a bounded
private protocol, and checks the random credential before dispatch. The adapter verifies both the
pipe server process ID and its full executable path before sending the credential. Disabling MCP
closes the listener and its active connection.

This protects against other OS users and accidental clients. It is not a privilege boundary from
malware already running as the same user: such a process can read the app-data credential. In-app
approval, revision checks, and the one-pending limit are the meaningful write boundary.
