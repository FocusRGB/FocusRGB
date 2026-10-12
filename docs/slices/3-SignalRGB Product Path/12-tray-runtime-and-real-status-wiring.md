# 12 — Tray runtime and real status wiring

[← All implementation slices](../../implementation-slices.md)

Depends on: 07, 09. Scope: wire the background engine into the existing Avalonia shell; add tray
show/quit and connect the previewed profile picker, manual/automatic control,
desired/applied/reason display, errors, and reconnect notification to shared commands.
Acceptance: closing window leaves engine running; Quit shuts down plugin processes cleanly;
UI stays responsive during timeout; accepting/declining recovery reaches shared commands.
No startup registration or full editor.

