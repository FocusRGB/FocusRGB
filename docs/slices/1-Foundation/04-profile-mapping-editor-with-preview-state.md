# 04 — Profile mapping editor with preview state

[← All implementation slices](../../implementation-slices.md)

**Status:** Complete — `dc32b07`, PR #10 dependent on PR #9. Build and 62 tests passed,
covering draft/apply/cancel, live preview, priority, profile-owned effect display, and
unavailable layouts.

Depends on: 03. Scope: build the applications/profile mapping editor against draft in-memory
state: application identity, profile, priority order, while-running/while-focused eligibility,
Apply/Cancel, and optional Live preview after completed edits. Show the selected profile's
effect as read-only and keep unavailable layout controls visibly disabled.
Acceptance: edits remain drafts until Apply; Cancel restores the prior draft and preview output;
unchecked preview sends no UI-state apply before Apply; priority reorder and eligibility are
keyboard accessible. No file writes, process detection, plugin process, or hardware output.

The original slice included a per-mapping provider-effect picker backed by a fake capability
catalog. That scope was intentionally removed after the persistence model was reviewed: effects
and layouts belong to profiles, while mappings only select a profile. Real resource selection
therefore belongs in the profile editor and provider-catalog slices.

