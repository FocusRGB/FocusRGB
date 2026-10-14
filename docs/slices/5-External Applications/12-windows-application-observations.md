# 12 — Windows application observations

[← All implementation slices](../../implementation-slices.md)

**Status:** Prepared for `slice/12-windows-application-observations`. Fixture work is preserved on
`slice/07-windows-observations`; inventory and merge it when implementation starts.

The preserved branch is the implementation starting point, not optional historical material;
inventory its tests and reconcile it with the current platform boundary before writing replacements.

Depends on: 05. Scope: foreground and running-process observations behind the detector
boundary. Handle disappearing processes/access denial; identify actual Stremio window owner.
Acceptance: observation fixtures cover app close/helpers/ignored windows; a read-only local
run reports the configured Programming, Movie, and Music app identities where installed. No lights change.

