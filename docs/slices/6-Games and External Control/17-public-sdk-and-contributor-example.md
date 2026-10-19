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
keeps the contracts, schemas, conformance kit, templates, and .NET/Node/Python SDKs together in
this repository's `sdk/` area. C# contract types stay hand-written; the normative JSON schemas
and drift tests enforce agreement.
Acceptance: generate a new external repository from the template, build/test it against the
published SDK without an app checkout, install its package without changing app code, and reject
incompatible contracts clearly. A failed plugin restarts without interrupting healthy peers.
Signing, hosted marketplace, and automatic updates remain
separate future work.

