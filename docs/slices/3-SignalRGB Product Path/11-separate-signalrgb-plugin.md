# 11 — Separate SignalRGB plugin

[← All implementation slices](../../implementation-slices.md)

Depends on: 08, 10. Scope: plugin under its module, status/catalog requests, effect application,
optional supported layout selection, and provider authority/device scope. Omitted layout keeps
current provider placements. Never launch SignalRGB implicitly. Mock external calls in tests.
Acceptance: encoded identifiers and failure paths checked; app/core have no provider project
reference; unsupported APIs return honest errors; removing package keeps core operational.
Live effect application waits for the end-to-end gate in 14.

