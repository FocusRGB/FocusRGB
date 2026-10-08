# Working on rgb-playground

Read `README.md` first and follow its reading order. These instructions apply to any
AI assistant; access to the original Claude chat is unnecessary.

The RGB controller is **planned, not implemented**. Keep work within the user's request;
a documentation request does not authorize implementing the app or changing live devices.

Final goal: replace SignalRGB with our own controller and effects, while retaining
integrations with other applications through plugins. SignalRGB is an interim backend.

Keep all SignalRGB-specific code, content, tools, and research in `../modules/signalrgb/`.
Its `plugins/` directory is for SignalRGB USB device plugins. The proposed controller's
plugin contracts are separate; their runtime and packaging are undecided. Other external
systems should receive their own `modules/<integration>/` boundary when implemented.

Preserve unrelated edits and untracked files. Follow `../.gitattributes` and native Windows
CRLF working-tree endings (`core.autocrlf=true`). Do not commit Arduino build output.

For implementation, start with the dry-run milestone in `../docs/focus-profiles-plan.md`.
Check capabilities before sending output, enforce one writer per physical device, and
include cleanup and restart recovery before depending on persistent overrides.

Update `STATUS.md` after meaningful work: changes, actual verification, unresolved
questions, and next step. Requirements belong in `../docs/app-brief.md`, architecture in
`../docs/plugin-architecture.md`, historical observations in `../docs/integration-evidence.md`.
