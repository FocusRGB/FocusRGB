# 18 — Local MCP commands

[← All implementation slices](../../implementation-slices.md)

Depends on: 07, 11, 13. Scope: local server/client boundary using shared query/command services,
profiles/application groups/ordered mappings/manual mode and existing recovery acceptance.
Acceptance: tool calls follow the same validation/persistence/authority paths as UI; local
access restrictions work; no direct configuration-file edits. Model choice is not required.

Implementation is split into three reviewable boundaries:

1. One application state service owns settings, selection state, revisions and runtime status for
   both UI and MCP callers.
2. A default-off Windows named pipe exposes authenticated read tools through `FocusRgb.Mcp.exe`.
3. Selection and settings commands require one expiring, payload-bound in-app approval. Approval
   waiting never holds the command gate; execution checks the settings revision again under it.

Hidden-window requests are surfaced through the tray without activating FocusRGB, whose own process
is ignored by automatic foreground selection. Decline, expiry and disconnect are side-effect free,
and a short decline cool-down prevents repeated prompts. Real-output profile preview is deferred to
[#112](https://github.com/FocusRGB/FocusRGB/issues/112).
