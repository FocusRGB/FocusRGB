# 04 — Profile mapping editor with preview state

[← All implementation slices](../../implementation-slices.md)

Depends on: 03. Scope: build the applications/profile mapping editor against draft in-memory
state: application identity, profile, priority order, while-running/while-focused eligibility,
Apply/Cancel, and optional Live preview after completed edits. Show provider effect and optional
layout choices from a fake capability catalog, with unavailable controls visibly disabled.
Acceptance: edits remain drafts until Apply; Cancel restores the prior draft and preview output;
unchecked preview sends no UI-state apply before Apply; priority reorder and eligibility are
keyboard accessible. No file writes, process detection, plugin process, or hardware output.

