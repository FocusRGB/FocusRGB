# 12 — Windows application observations

[← All implementation slices](../../implementation-slices.md)

**Status:** Implemented on `slice/12-windows-application-observations`; review and merge are pending.

The preserved branch is the implementation starting point, not optional historical material;
inventory its tests and reconcile it with the current platform boundary before writing replacements.
Cherry-pick only `f0cd094` (observer and process source) and `5d6f12f` (platform test project) onto
the new branch; drop `e3f704b`, which records the checkpoint under the old slice 07 numbering.

Depends on: 05. Scope: foreground and running-process observations behind the detector
boundary. Handle disappearing processes/access denial; identify actual Stremio window owner.
Acceptance: observation fixtures cover app close/helpers/ignored windows; a read-only local
run reports the configured Programming, Movie, and Music app identities where installed. No lights change.

Acceptance evidence (2026-10-16): the observer reported the running `Code.exe` Programming
identity and the installed PotPlayer window owner as `PotPlayerMini64.exe`. No runnable configured
Music app or Stremio installation was present; their identities remain covered by deterministic
fixtures. The check changed no settings or lighting state.

