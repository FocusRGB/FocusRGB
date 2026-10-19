# 15 — External SignalRGB plugin repository

[← All implementation slices](../../implementation-slices.md)

Depends on: 10, 14. Scope: create the public `FocusRGB/FocusRGB-Plugin-SignalRGB` repository when
implementation starts. Move maintained SignalRGB-specific research/tools from the staging module,
then implement status/catalog requests, effect application, optional supported layout selection,
and provider authority/device scope against a published `FocusRgb.PluginSdk` version. Omitted
layout keeps current provider placements. Never launch SignalRGB implicitly. Mock external calls
in tests.

Acceptance: the plugin repository builds/tests without a FocusRGB checkout or Git submodule;
its manifest records contract range, permissions, source, and license; encoded identifiers and
failure paths are checked; app/core have no provider project reference; unsupported APIs return
honest errors; installing/removing its release package leaves core operational. The main repo
retains only a migration link for moved material where needed. Live effect application waits for
the end-to-end gate in 16.

