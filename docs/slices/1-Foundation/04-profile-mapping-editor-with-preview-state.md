# 04 — Profile mapping editor with preview state

[← All implementation slices](../../implementation-slices.md)

**Status:** Complete — `dc32b07`, PR #10 dependent on PR #9. Build and 62 tests passed,
covering draft/apply/cancel, live preview, ordered priority, and profile-owned output display.

Depends on: 03. Scope: build the applications/profile mapping editor against draft in-memory
state: named application groups, profile, list order, while-running/while-focused eligibility,
Apply/Cancel, and optional Live preview after completed edits. Each profile owns one editable
effect and one editable layout. Mapping rows select only an application group and a profile.
Acceptance: edits remain drafts until Apply; Cancel restores the prior draft and preview output;
unchecked preview sends no UI-state apply before Apply; hover arrows and drag/drop reorder the
priority list. Executables are shown as a list and can be added with the Windows `.exe` picker
or removed. No file writes, process detection, plugin process, or hardware output.

The original slice included a per-mapping provider-effect picker backed by a fake capability
catalog. That scope was intentionally removed after the persistence model was reviewed: effects
and layouts belong to profiles, while mappings only select a profile. Real resource selection
therefore belongs in the profile editor and provider-catalog slices.

The later application-group revision intentionally replaced the original free-text name,
comma-separated executable field, numeric priority input, and global Move buttons. In this
preview slice groups are in-memory projections of existing rules; slice 20 gives named groups
their persistent schema and connects them to observed applications.

