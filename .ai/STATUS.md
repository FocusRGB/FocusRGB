# Current status

Updated: 2026-10-08.

## Completed

- Provider-independent brief, plugin design, roadmap, evidence record, and AI instructions.
- Final goal confirmed: replace SignalRGB, retain integrations with other apps.
- Historical v2 plan archived; new plan supports profiles combining plugin actions.
- Keyboard visual confirmation distinguished from mouse API acceptance and unresolved handover.
- SignalRGB-only starters, examples, and the Pro-blocked experiment separated from the empty
  active `effects/` and `plugins/` install folders; tools now have explicit names.
- SignalRGB-specific content, tools, MCP launcher, reference notes, and security research are
  grouped under `modules/signalrgb/`; module ownership is documented for future integrations.

## What exists

The SignalRGB module contains templates, a Canvas API example, and an archived Pro-blocked
effect. The shared screen colour test and Arduino sketch remain available. No custom
SignalRGB effects or user device plugins are active in the repo or Documents folders.
There is no controller app, plugin loader,
implemented profile schema, or new startup task. Architecture JSON is illustrative.

Read-only checks on 2026-10-08 confirmed SignalRGB 2.5.74, WLED Desk 16.0.1 with its 215/85
split, Dynamic Lighting disabled, and the Synapse delayed task still present. WLED Wall did
not respond. No live settings, presets, layouts, registry values, services, or tasks changed.

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
