# 08 — Minimal external plugin host and fake plugin

[← All implementation slices](../../implementation-slices.md)

Depends on: 02. Scope: explicitly enabled local package manifest, version handshake,
JSON-RPC stdin/stdout requests, cancellation/timeouts, health, and process shutdown. Implement
one fake executable using the shared contract. Keep protocol logs off stdout.
Acceptance: fake catalog/apply works; unsupported version, malformed responses, timeout, and
plugin exit do not crash the app or hang a request. Removing fake plugin leaves core usable.

