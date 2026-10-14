# 17 — Stabilize the public SDK and contributor example

[← All implementation slices](../../implementation-slices.md)

Depends on: 10, 15. Scope: promote the SDK proven by the fake output and SignalRGB plugins from `0.x` to version 1;
document compatibility and support policy; finish the plugin template, conformance kit, and
standalone example; and define the minimal official-plugin catalog format. Version 1 covers the proven provider,
catalog, apply, status, and lifecycle surfaces; it must leave room for game events (19–20) and
native frame output (21–22) to arrive later as backward-compatible 1.x minor versions.
Acceptance: generate a new external repository from the template, build/test it against the
published SDK without an app checkout, install its package without changing app code, and reject
incompatible contracts clearly. Signing, hosted marketplace, and automatic updates remain
separate future work.

