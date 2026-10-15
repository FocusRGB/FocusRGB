# 10 — Minimal plugin runtime, SDK, and fake output

[← All implementation slices](../../implementation-slices.md)

Depends on: 02, 08. Scope: add `FocusRgb.PluginSdk` to the main repository with only the plugin-side
manifest, handshake, JSON-RPC stdin/stdout, cancellation, logging, and shutdown helpers required
by one fake executable. Add the app-side host for explicitly enabled local packages, including
timeouts and health. Produce a versioned `0.x` SDK package and JSON schemas; keep protocol logs
off stdout. The fake plugin remains a main-repository conformance fixture. Record which local
packages are enabled through a versioned `settings.json` migration (proposed default: a per-user
`plugins` folder plus an explicit enabled list, all disabled until the user enables them).

Acceptance: fake catalog/apply works; unsupported version, malformed responses, timeout, and
plugin exit do not crash the app or hang a request; the SDK packs reproducibly; an external
clean-room fixture restores/builds against the declared package feed or versioned release
artifact without a FocusRGB source checkout; the app has no project reference to the fake
plugin; removing the fake executable leaves core and app usable. No marketplace, automatic
plugin updater, or complete version-1 SDK design.

Status: **Complete (2026-10-15).** The host loads only explicitly enabled per-user packages,
validates manifests and handshakes, bounds requests, and isolates plugin failures. The portable
SDK package, protocol/manifest schemas, fake executable, and clean-room package consumer are in
place. Release build: 0 warnings/errors; tests: 178/178; package-content reproducibility and the
external consumer build pass via `tools/verify-plugin-sdk-package.ps1`.

