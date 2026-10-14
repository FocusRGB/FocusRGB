# 16 — WLED plugin and native output gate

[← All implementation slices](../../implementation-slices.md)

Depends on: 10, 14, 15. Scope: create `MAJ224/FocusRGB-Plugin-WLED`; implement WLED status/device
capabilities, configured network targets, static output then frame streaming, bounded timeouts,
cleanup/realtime expiry, and ownership against a published SDK version. Use fake network checks
first. This slice performs the narrow read-only ownership check needed to prove a target is
released from SignalRGB before any requested live test; slice 21 investigates broader SignalRGB capabilities.
Acceptance: the repository builds/tests without a FocusRGB checkout or Git submodule; its release
package installs through the ordinary plugin path; visible native WLED output has one writer;
offline devices are isolated; stop/reconnect and saved native fallback work where dependencies
and ownership are available. No implicit changes to SignalRGB device settings or WLED presets.

