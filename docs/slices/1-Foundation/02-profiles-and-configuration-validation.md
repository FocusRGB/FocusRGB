# 02 — Profiles and configuration validation

[← All implementation slices](../../implementation-slices.md)

**Status:** Complete — PR from `slice/02-profile-validation`, dependent on PR #6. Build and 52
tests passed, covering fixtures, validation, unknown fields, and the separate native snapshot.

Depends on: 01. Scope: implement the minimal model from 00; validate IDs, rule targets,
authority, and required fields. Seed fixtures: protected Default/Aurora; Programming/Aurora
for VS Code and Visual Studio; Movie/Screen Ambient for PotPlayer and Stremio; Music/
Logarithmic Visualizer for Media Player and Spotify. Provider layout stays unset.
Acceptance: valid fixtures load; removing/reassigning Default and duplicates/missing references fail with usable errors;
SignalRGB display names are fixtures, not core constants. Native setup is a separate snapshot.

