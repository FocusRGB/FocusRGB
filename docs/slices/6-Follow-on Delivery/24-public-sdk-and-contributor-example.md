# 24 — Stabilize the public SDK and contributor example

[← All implementation slices](../../implementation-slices.md)

Depends on: 11, 18. Scope: promote the proven main-repository SDK from `0.x` to version 1;
document compatibility and support policy; finish the plugin template, conformance kit, and
standalone example; and define the minimal official-plugin catalog format.
Acceptance: generate a new external repository from the template, build/test it against the
published SDK without an app checkout, install its package without changing app code, and reject
incompatible contracts clearly. Signing, hosted marketplace, and automatic updates remain
separate future work.

