# Reusable work procedures

## Planning

Read the brief, architecture, evidence, and status. Define the desired user behavior and
acceptance check. Separate requirements from proposed choices. Keep app-specific detection
and output in plugins; keep rules and device ownership in the core. Update the relevant
document and STATUS.md; do not invent answers to unresolved preferences.

## Integration investigation

1. Read dated evidence; inspect installed versions and current available interfaces.
2. Check current official docs before implementing a version-sensitive API.
3. Start with read-only discovery. Record prerequisites, capabilities, transport, device
   IDs, timeouts, and actual error behavior.
4. Before a live test, save relevant state, establish one writer per device, explain the
   visible sequence, and define restoration.
5. Calibration: change one thing, wait for user feedback, then change the next. Preserve
   the user's current preset/layout. Ask before WLED reboot or destructive actions, following
   the original board-configuration instructions.
6. Verify outcome rather than only a tool's OK. Record API acceptance separately from
   visual confirmation. Restore changed state and note anything restoration cannot guarantee.

## Implementation and handoff

Check repository status and preserve unrelated work. Implement the requested milestone.
Test meaningful failure paths: rule arbitration, debounce, unsupported actions, disconnects,
partial transitions, cleanup, and restart recovery as applicable. Record how to run the work,
what was tested, and remaining limits. Update STATUS.md and the roadmap. A rejected backend
capability needs a supported alternative or a clearly unavailable profile.
