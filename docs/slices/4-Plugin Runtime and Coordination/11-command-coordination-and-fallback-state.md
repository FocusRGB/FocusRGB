# 11 — Command coordination and fallback state

[← All implementation slices](../../implementation-slices.md)

Depends on: 05, 06, 10. Scope: connect commands/selection to fake plugin results; track desired,
applied, pending, and native fallback states. Validate ownership and authority before output.
Implement reconnect notification state with Accept/Decline and coalescing.
Acceptance: failure retains desired state; no fallback cycle or competing writer; reconnect
does not resume automatically; Accept uses current intent; native fallback lacking an output
reports pending. No native renderer is needed; native output arrives in slices 21–22.

Status: **Complete (2026-10-15).** Selection commands persist intent before output and feed a
provider-neutral coordinator. Runtime status separates desired, effective, applied, pending,
and recovery notifications; resource/capability and writer-release checks run before apply.
The app adapter exercises the real fake process, while missing native output remains pending.
Release build: 0 warnings/errors; tests: 185/185.

