# Current status

Updated: 2026-10-08.

## Completed

- Provider-independent brief, plugin design, roadmap, evidence record, and AI instructions.
- Final goal confirmed: replace SignalRGB, retain integrations with other apps.
- Historical v2 plan archived; new plan supports profiles combining plugin actions.
- Keyboard visual confirmation distinguished from mouse API acceptance and unresolved handover.

## What exists

Existing SignalRGB effects, device-plugin template, test tools, calibration page, and
Arduino sketch remain intact. There is no controller app, plugin loader, implemented profile
schema, or new startup task. Architecture JSON is illustrative, not runnable configuration.

No live WLED, SignalRGB, registry, services, presets, layouts, or startup settings were
changed for this documentation task. Integrations were reviewed from records, not retested.

## Next step

When implementation is requested, choose the runtime and initial UI, then build milestone 1:
dry-run focus detection, deterministic profile selection, and fake output plugins.
Before live output, establish WLED ownership and the standalone transport for SignalRGB LED
overrides. Claude tool availability does not itself establish an app API.

## Open choices

- Runtime/language and first interface: tray/settings UI, CLI, or another approach.
- Generic game profile first or individual game profiles.
- Interim SignalRGB background effect during games.
- Capture display and monitor/desk layout editor.
- Accept native-only WLED ownership while automatic handover is unresolved?
- Native keyboard first or mouse first?

These remain pending; they do not block documentation work.
