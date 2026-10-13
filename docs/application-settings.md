# Application settings

FocusRGB needs a small Settings page for application-wide preferences. Profile selection,
application mappings, effects, layouts, devices, and integration bindings belong in their
own editors; they are not duplicated here.

These settings are planned, not implemented. The current version-1 configuration contract
remains unchanged until the settings slice adds a migration and tests.

## Initial settings

| Section | Setting | Values | Default | Notes |
|---|---|---|---|---|
| General | Close button behavior | Minimize to system tray; Exit application | Minimize to system tray | Exit performs the same clean shutdown as the tray **Quit** command. |
| General | Start minimized | On; Off | Off | Hides the main window after a normal launch. It does not register FocusRGB to start with the OS. |
| Appearance | Theme | System; Light; Dark | System | Applies to the Avalonia UI only and does not alter lighting output. |
| Diagnostics | Verbose logging | On; Off | Off | Intended for short troubleshooting sessions; never includes secrets or unrelated user data. |

The Settings page also provides actions to open the settings folder, open the logs folder,
and export a diagnostic bundle. Actions are not persisted preferences.

## Lifecycle behavior

- Closing the window with **Minimize to system tray** selected hides the window while the
  engine and integrations continue running. The tray icon must remain available.
- Closing the window with **Exit application** selected requests a real shutdown: save valid
  state, stop observations, release owned output, and shut down plugin processes.
- **Quit** in the tray menu always exits, regardless of the close-button preference.
- Operating-system session end and application shutdown requests always exit; they must never
  be converted into a minimize action.
- The first minimize-to-tray action should explain where FocusRGB went. This is an onboarding
  notice, not another permanent setting.
- Unsaved editor drafts continue to follow their editor's Apply/Cancel rules. Hiding the window
  must not silently apply a draft.

Minimize to tray is the default because FocusRGB's application detection and profile switching
only work while its background engine is running. Users who prefer conventional window behavior
can select Exit application.

## Settings owned by later features

Keep these controls with the feature that can implement and validate them:

| Setting | Owner |
|---|---|
| Start FocusRGB when the user signs in | Slice 26 packaging/startup; default off and explicitly user-controlled |
| Start minimized after sign-in | Slice 26; use the general Start minimized preference once startup is enabled |
| Automatic update checks and release channel | A future updater/distribution slice |
| Capture monitor, frame rate, and compatibility mode | The screen-ambience feature/editor |
| Plugin enablement, permissions, reconnect policy, and provider diagnostics | Integrations page |
| Focus debounce, ignored applications, rule priority, and eligibility | Mappings/automation editor |
| Device calibration, placement, grouping, and exclusion | Devices/layout editor |
| Effect parameters | Profile/effect editor |

Reconnect approval notifications are safety-relevant and remain enabled initially. Do not add
a global switch that can silently grant a recovered plugin output authority.

## Persistence and validation

- Application preferences belong in the versioned `settings.json` document, not runtime
  `state.json` or the native fallback snapshot.
- Add preferences through an explicit schema migration. Older valid settings receive the
  defaults above; unknown enum values fail validation and use the existing recovery path.
- Hand-edited JSON accepts comments and trailing commas. Semantic validation remains atomic;
  partially accepting linked profiles, rules, and bindings could create a different setup than
  the user intended. Comments and trailing-comma formatting are not preserved: the next save
  rewrites the document as canonical JSON without comments.
- Save changes atomically through the existing local store. A failed save leaves the last
  valid preferences active and reports the error.
- Maintain a last-known-good settings recovery copy once the Settings UI is implemented. If
  `settings.json` is invalid, archive it and restore that validated copy; use defaults only when
  no valid recovery copy exists, and report which source was loaded.
- UI availability follows platform capability. A platform without a supported tray must not
  offer a close action that could leave an invisible background process.
- Theme and window visibility never affect desired/applied profile state or output ownership.

The comparable-app research behind this scope is recorded in
[`integration-evidence.md`](integration-evidence.md#comparable-application-settings).
