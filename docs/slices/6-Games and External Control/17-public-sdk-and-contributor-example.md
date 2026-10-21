# 17 — Stabilize the public SDK and contributor example

[← All implementation slices](../../implementation-slices.md)

Depends on: 10, 15. Scope: promote the SDK proven by the fake output and SignalRGB plugins from `0.x` to version 1;
document compatibility and support policy; finish the plugin template, conformance kit, and
standalone example; and define the minimal official-plugin catalog format. Version 1 covers the proven provider,
catalog, apply, status, and lifecycle surfaces; it must leave room for game events (19–20) and
native frame output (21–22) to arrive later as backward-compatible 1.x minor versions. Complete
per-plugin runtime ownership so recovery restarts only the failed plugin ([#75](https://github.com/FocusRGB/FocusRGB/issues/75)).
The normative protocol and credential-free nuget.org publication are tracked by
[#79](https://github.com/FocusRGB/FocusRGB/issues/79). Official GitHub discovery and
user-confirmed official/custom installation are tracked by
[#80](https://github.com/FocusRGB/FocusRGB/issues/80). [#84](https://github.com/FocusRGB/FocusRGB/issues/84)
keeps the normative schemas, conformance kit, and host here while all .NET, Node, and Python SDK
source, templates, and publishing live in
[`FocusRGB-Plugin-SDK`](https://github.com/FocusRGB/FocusRGB-Plugin-SDK). The host's hand-written
types stay non-packaged under `FocusRgb.Core.Protocol`; each SDK pins an immutable spec tag and
its drift tests enforce agreement.
Acceptance: generate a new external repository from the template, build/test it against the
published SDK without an app checkout, install its package without changing app code, and reject
incompatible contracts clearly. A failed plugin restarts without interrupting healthy peers.
Signing, hosted marketplace, and automatic updates remain
separate future work.

Status (2026-10-21): the protocol, tagged specs, .NET SDK 1.0, template, conformance checks,
runner host support, official discovery, package validation, install/runtime wiring and release
assets are implemented. Follow-ups #79 and #84 are complete. Node and Python SDKs are deferred
until after Slice 29 in `FocusRGB-Plugin-SDK#8` and do not block this slice. Issue #80 and this
slice remain open until a SignalRGB `v1.0.0` release exists and the official install, enable,
start and uninstall path is exercised through the real UI without restarting FocusRGB.

