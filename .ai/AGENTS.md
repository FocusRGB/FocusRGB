# Working on FocusRGB

Read `README.md` first and follow its reading order. These instructions apply to any
AI assistant; access to the original Claude chat is unnecessary.

The RGB controller is **planned, not implemented**. Keep work within the user's request;
a documentation request does not authorize implementing the app or changing live devices.

Final goal: replace SignalRGB with our own controller and effects, while retaining
integrations with other applications through plugins. SignalRGB is an interim backend.

Keep all SignalRGB-specific code, content, tools, and research in `../modules/signalrgb/`.
Its `plugins/` directory is for SignalRGB USB device plugins. The proposed controller's
plugin contracts are separate; controller plugins are separate processes over versioned
JSON-RPC, with a minimal host implemented before the complete public SDK. Other external
systems should receive their own `modules/<integration>/` boundary when implemented.

Preserve unrelated edits and untracked files. Follow `../.gitattributes` and native Windows
CRLF working-tree endings (`core.autocrlf=true`). Put generated builds, reports, caches, and
tool output under `../build/`; do not commit them. Graphify uses `../build/graphify-out/`, so
run extraction with `graphify extract . --out build` and pass that graph path to queries.

Follow `../docs/git-workflow.md`. `develop` is the default branch: start work from
`origin/develop`, push a scoped branch, and open a pull request into `develop`. Never commit
or push directly to `develop` or `main`; `main` changes only through the owner's release pull
request from `develop`. For requested repository work, assistants may create and switch
branches, commit scoped changes, push working branches, and open pull requests into `develop`
without separate confirmation. Do not include unrelated user changes, force-push, rewrite
history, delete branches, or merge pull requests unless the user explicitly requests it.

For implementation, follow the numbered slices in `../docs/implementation-slices.md` and
resume the active slice in `STATUS.md`. Use `SLICE-HANDOFF.md` to record exact resume state.
Check capabilities before sending output, enforce one writer per physical device, and
include cleanup and restart recovery before depending on persistent overrides.

Update `STATUS.md` after meaningful work: changes, actual verification, unresolved
questions, and next step. Requirements belong in `../docs/app-brief.md`, architecture in
`../docs/plugin-architecture.md`, historical observations in `../docs/integration-evidence.md`.

## Code conventions

Match the owner's existing .NET projects (HouseWatcher, MovieSearch); `../.editorconfig`
encodes the mechanical parts.

- Block-scoped namespaces (`namespace X { ... }`), braces on their own lines and always used.
- Private fields `_camelCase`, assigned from constructor parameters; `sealed` classes by default.
- Results as small `sealed` types with `init` properties and static factories (`Ok`, `Fail`)
  rather than exceptions for expected outcomes; async methods end in `Async` and take a
  `CancellationToken`.
- `<summary>` comments on public types and interface members explaining purpose and why.
- One provider/integration per folder or module; shared code talks to it only through an
  interface, and adding one never edits shared logic.
- Tests: xUnit with a global `Using Include="Xunit"`, names `Method_Result_WhenCondition`,
  hand-written fakes instead of a mocking library.
- READMEs include an annotated project tree and an "Adding a ..." walkthrough where relevant.
