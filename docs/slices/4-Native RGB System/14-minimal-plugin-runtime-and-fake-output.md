# 14 — Minimal plugin runtime, SDK, and fake output

[← All implementation slices](../../implementation-slices.md)

Depends on: 02. Scope: add `FocusRgb.PluginSdk` to the main repository with only the plugin-side
manifest, handshake, JSON-RPC stdin/stdout, cancellation, logging, and shutdown helpers required
by one fake executable. Add the app-side host for explicitly enabled local packages, including
timeouts and health. Produce a versioned `0.x` SDK package and JSON schemas; keep protocol logs
off stdout. The fake plugin remains a main-repository conformance fixture.

Acceptance: fake catalog/apply works; unsupported version, malformed responses, timeout, and
plugin exit do not crash the app or hang a request; the SDK packs reproducibly; an external
clean-room fixture restores/builds against the declared package feed or versioned release
artifact without a FocusRGB source checkout; the app has no project reference to the fake
plugin; removing the fake executable leaves core and app usable. No marketplace, automatic
plugin updater, or complete version-1 SDK design.

