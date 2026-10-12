# 18 — WLED plugin and native output gate

[← All implementation slices](../../implementation-slices.md)

Depends on: 08, 09, 17. Scope: WLED status/device capabilities, configured network targets,
static output then frame streaming, bounded timeouts, cleanup/realtime expiry, and ownership.
Use fake network checks first. Before requested live tests, prove target release from SignalRGB.
Acceptance: visible native WLED output with one writer, offline device isolation, stop/reconnect
behavior, and working saved native fallback where dependencies/ownership are available.
No implicit changes to SignalRGB device settings or existing WLED presets.

