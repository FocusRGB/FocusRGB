# 09 — Application settings and lifecycle preferences

[← All implementation slices](../../implementation-slices.md)

Status: **Complete on `slice/09-application-settings`.**

Depends on: 06, 08. Scope: add the small Settings page defined in
[`application-settings.md`](../../application-settings.md); migrate the versioned settings
document; persist close-button behavior, start-minimized, theme, and diagnostic-logging
preferences; maintain last-known-good settings recovery; and provide settings/log folder and
diagnostic-export actions. Keep profile, mapping, integration, device, layout, and effect
controls in their existing feature pages.

Implemented with a version-1-to-version-2 settings migration, validated last-known-good
recovery, saved-versus-draft preference behavior, capability-aware tray choices, shared clean
shutdown, UI-only theme selection, and a privacy-limited diagnostic ZIP.

Post-review remediation makes startup asynchronous, saves on OS shutdown, starts hidden without
a window flash, serializes concurrent writers, preserves schema-1 backups, keeps recovery data
current without advancing it before the main save, and adds a second-Quit escape after save failure.

Acceptance: existing settings migrate to documented defaults; preferences survive restart;
System/Light/Dark apply without changing lighting state; Start minimized does not register OS
startup; window close either hides to an available tray or performs the same clean shutdown as
tray Quit; OS shutdown cannot be intercepted as minimize; hiding never applies editor drafts;
unsupported tray behavior is not offered; diagnostics exclude secrets; focused persistence,
lifecycle, and UI-state tests pass. An invalid current settings file is archived and restores a
validated last-known-good copy before falling back to defaults. No OS startup registration,
updater, cloud/account setting, or provider-specific control.

Verification: `dotnet build FocusRGB.slnx --no-restore` and
`dotnet test --solution FocusRGB.slnx -c Release --no-restore`; 157/157 tests pass after review remediation.
