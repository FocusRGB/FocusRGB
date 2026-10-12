# 09 — Command coordination and fallback state

[← All implementation slices](../../implementation-slices.md)

Depends on: 05, 06, 08. Scope: connect commands/selection to fake plugin results; track desired,
applied, pending, and native fallback states. Validate ownership and authority before output.
Implement reconnect notification state with Accept/Decline and coalescing.
Acceptance: failure retains desired state; no fallback cycle or competing writer; reconnect
does not resume automatically; Accept uses current intent; native fallback lacking an output
reports pending. No native renderer needed yet.

