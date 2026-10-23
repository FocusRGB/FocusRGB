# FocusRGB repository instructions

These instructions apply to every assistant working in this repository. Keep work within the
owner's request, preserve unrelated edits, and do not change live devices without approval.

## Sources of truth

- Slice specifications and status are GitHub Issues
  [#26–#55](https://github.com/FocusRGB/FocusRGB/issues?q=is%3Aissue+number%3A26..55).
- The archived-document index is [#83](https://github.com/FocusRGB/FocusRGB/issues/83).
- Delivery groups are [Milestones #1–#5](https://github.com/FocusRGB/FocusRGB/milestones).
- Current planning is on the [FocusRGB project board](https://github.com/orgs/FocusRGB/projects/1).
- The normative plugin contract and schema rules are in `sdk/spec/plugin-protocol.md` and
  `sdk/spec/schemas/`.
- The reviewer follows the workspace `../REVIEWER.md` (relative to the repository root).

The main repository owns the app, host, protocol, schemas, conformance kit, and raw fake plugin.
Official SDKs live in `FocusRGB-Plugin-SDK`; production integrations live in separate
`FocusRGB-Plugin-<Name>` repositories and communicate over versioned stdio JSON-RPC.

## Source boundaries

- `FocusRgb.Core` targets `net10.0`; it is portable and has no Windows, SDK, or provider dependency.
- `FocusRgb.Platform.Windows` targets `net10.0-windows` and owns Windows observation APIs.
- `FocusRgb.App` targets `net10.0-windows` and owns the Avalonia tray/UI and plugin host.
- `FocusRgb.LocalMcp` and `FocusRgb.Mcp` are Windows-only local MCP pipe/stdio components.
- One provider or integration belongs in one module; shared code talks to it through interfaces.

## Persistence and state

- `settings.json` uses schema v5, recovers through `settings.last-known-good.json`, and protects the
  `default` profile.
- `state.json` stores desired mode/profile intent only.
- `native-setup.json` changes only while applying native setup.
- `mcp.json` stores the default-off local MCP switch and credential; MCP cannot enable itself.

## Selection and output

- Application rules use top-to-bottom priority with `while-running` and `while-focused` modes.
- Focus selection is debounced by about 2000 ms; manual selection overrides automatic rules.
- Exactly one writer may control a physical device.
- Rendering authority is explicit: `provider` or `application`, never both.
- Recovered plugins need explicit approval before resuming output.
- Every MCP mutation needs explicit, single-use approval in the app.

## Git workflow

- Branch from `origin/develop`; use `slice/`, `feature/`, `fix/`, `chore/`, `docs/`, or
  `dependabot/` branch names and open pull requests into `develop`.
- Make multiple focused commits where useful and stage exact paths with `git add <paths>`.
- Never push directly to `develop` or `main`; never rewrite history or include unrelated changes.
- Merge only after the reviewer agent's current-head “ready to merge”, green CI, and resolution of
  every review thread.
- End assistant-written commits and pull-request bodies with `Assisted-by: OpenAI Codex`.
- Tags, publishing, secrets, environments, hardware checks, and `main` are owner-only.

## Working conventions

Preserve Windows CRLF working-tree endings from `.gitattributes` and `core.autocrlf=true`. Put
generated output under `../build/`; never commit it. Match `.editorconfig`: block-scoped namespaces,
braces always, `_camelCase` private fields, sealed classes by default, and cancellation-aware async
methods. Use xUnit, descriptive `Method_Result_WhenCondition` names, and hand-written fakes.

The code graph is workspace-wide at the workspace folder's `build/graphify-out/` (the parent of
this clone; see the workspace `AGENTS.md`). Query from the workspace folder, or from the repository
root pass `--graph ../build/graphify-out/graph.json`. Confirm graph findings in source because the
graph can lag. Never run `graphify update` or `graphify hook install` inside a clone. Never run
`graphify extract` or semantic/deep modes unless the owner asks because they call an LLM. Never
commit graph output.

Repository instructions take precedence over the ponytail skill when they conflict; otherwise its
smallest-complete-change rule and `shortcut:` comment format apply.
