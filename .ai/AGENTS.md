# Working on FocusRGB

Read `README.md` first and follow its reading order. These instructions apply to any
AI assistant; access to the original Claude chat is unnecessary.

The RGB controller is in early implementation; consult `STATUS.md` for completed slices.
Keep work within the user's request;
a documentation request does not authorize implementing the app or changing live devices.

Final goal: replace SignalRGB with our own controller and effects, while retaining
integrations with other applications through plugins. SignalRGB is an interim backend.

Keep SignalRGB-specific implementation, maintained content, tools, and research in the peer
`FocusRGB/FocusRGB-Plugin-SignalRGB` repository; do not restore provider code to the main repository. Controller plugins are separate processes
over versioned JSON-RPC and consume a published `FocusRgb.PluginSdk` package. Other production
integrations receive their own peer repositories when their implementation slice starts.
The normative protocol, schemas, conformance kit, and host stay here. All language SDK source,
templates, and publishing live in the peer `FocusRGB/FocusRGB-Plugin-SDK` repository.

Preserve unrelated edits and untracked files. Follow `../.gitattributes` and native Windows
CRLF working-tree endings (`core.autocrlf=true`). Put generated builds, reports, caches, and
tool output under `../build/`; do not commit them.

The code graph is workspace-wide at the workspace folder's `build/graphify-out/` (the parent of
this clone; see the workspace `AGENTS.md`). Query from the workspace folder, or from the repository
root pass `--graph ../build/graphify-out/graph.json`. Confirm graph findings in source because the
graph can lag. Never run `graphify update` inside a clone; run it only from the workspace folder.
Never run `graphify hook install` in this repo. Never run `graphify extract` or semantic/deep modes
unless the owner asks because they call an LLM. Never commit graph output.

Repository instructions (git-workflow, attribution, conventions, and slice docs) take precedence
over the ponytail skill when they conflict; otherwise its "smallest complete change" applies,
and its `shortcut:` comment format is accepted.

Follow `../docs/git-workflow.md`. `develop` is the default branch: start standalone work from
`origin/develop`; stacked work starts from and targets its immediate dependency branch. Push a
scoped branch and open a pull request into that target. Never commit
or push directly to `develop` or `main`; `main` changes only through the owner's release pull
request from `develop`. For requested repository work, assistants may create and switch
branches, commit scoped changes, push working branches, and open pull requests into `develop`
without separate confirmation. Do not include unrelated user changes, force-push, rewrite
history, delete branches, or merge pull requests unless the user explicitly requests it.

The current reviewer follows the [reviewer handbook](REVIEWER.md). The workspace `REVIEWER.md` is
canonical; this repository copy is a snapshot. A review counts for merging only when it is on the
current head and explicitly says "ready to merge".

For implementation, use the numbered index in `../docs/implementation-slices.md`, open the
linked detail file for the active slice, and resume its exact state from `STATUS.md`. Use
`SLICE-HANDOFF.md` to record exact resume state.
Check capabilities before sending output, enforce one writer per physical device, and
include cleanup and restart recovery before depending on persistent overrides.

The slice detail file is authoritative for slice status. Update `STATUS.md` only with the active
resume pointer, changes, actual verification, unresolved questions, and next step; the index is
a navigation summary, not another status record. Requirements belong in `../docs/app-brief.md`, architecture in
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
