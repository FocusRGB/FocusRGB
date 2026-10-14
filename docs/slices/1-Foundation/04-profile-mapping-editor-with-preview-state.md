# 04 — Profile mapping editor with preview state

[← All implementation slices](../../implementation-slices.md)

**Status:** Complete — `dc32b07`, PR #10 dependent on PR #9. Build and 62 tests passed,
covering draft/apply/cancel, live preview, ordered priority, and profile-owned output display.

Depends on: 03. Scope: build the applications/profile mapping editor against draft in-memory
state: named application groups, profile, list order, while-running/while-focused eligibility,
Save/Cancel, and optional Live preview after completed edits. Each profile owns one editable
effect and one editable layout. Mapping rows select only an application group and a profile.
Acceptance: edits remain drafts until Apply; Cancel restores the prior draft and preview output;
unchecked preview sends no UI-state save before Save; hover arrows and whole-row drag/drop reorder
the priority list. Dropping in a row's upper half inserts before it and dropping in its lower half
inserts after it, making the area around each row boundary a forgiving insertion target. Each row
also exposes Edit and Remove.
Executables are shown as a list and can be added with the Windows `.exe` picker or removed.
No file writes, process detection, plugin process, or hardware output.

The original slice included a per-mapping provider-effect picker backed by a fake capability
catalog. That scope was intentionally removed after the persistence model was reviewed: effects
and layouts belong to profiles, while mappings only select a profile. Real resource selection
therefore belongs in the profile editor and provider-catalog slices.

The later application-group revision intentionally replaced the original free-text name,
comma-separated executable field, numeric priority input, and global Move buttons. In this
preview slice groups are in-memory projections of existing rules; slice 11 gives named groups
their persistent schema and connects them to observed applications.

Profiles and Application Groups use the same selected-item pattern: a placeholder-bearing picker
at the top, options below for the selected item, and `Save`, `Save As`, and `Remove` actions.
The picker and editor body share the same width. Application Add/Remove actions sit to the right
of the executable list.
Across FocusRGB editors, `Save As` creates a new item, `Save` edits the current item, and `Remove`
deletes it subject to protected/default and reference checks. Mapping creation uses the two
pickers beneath the left list and the same `Save As` wording.

The separate Overview and Mappings tabs were later combined into the main `Home` tab. Its `Mode`
control is always visible: Manual displays profile/current-state controls, while Automatic
displays this mapping editor as the tab body.

