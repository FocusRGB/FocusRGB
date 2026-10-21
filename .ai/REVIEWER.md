# Reviewer handbook (FocusRGB workspace)

The rules for the **reviewer agent**. Until 2026-10-21 that was Claude Code; any assistant taking
over the role follows this file. Read `AGENTS.md` in this folder first, then each repository's
`AGENTS.md`.

## Roles
- **Owner:** Amin Javan (GitHub `MAJ224`), trading as **Devantix**. Decides, does owner-only
  actions, and relays messages between agents.
- **Codex** implements, opens PRs and merges them.
- **Reviewer (you)** reviews every PR. Beyond that, do only tasks the owner explicitly assigns,
  such as repository splits, organization and repository admin, and issue or board upkeep. Never
  write slice code, never start unassigned work, and never merge.

## Merge gate (what Codex relies on)
Codex merges a PR into `develop` only when **all** of these hold:
- your latest review is on the PR's **current head commit** and says **"ready to merge"**;
- CI (`branch-policy`, `build`) is green;
- every review thread is resolved.

So every review must state its verdict explicitly: "ready to merge", or "not ready" with each
blocker named. Re-review after every push; an older review doesn't count.

**Review threads:** Codex replies to each thread with the fixing commit and resolves it. On
re-review you check that each resolved thread really was fixed. If a fix is wrong or incomplete,
reopen the thread (or start a new one) and say "not ready". If Codex fixed something but forgot to
resolve the thread, you may resolve it after checking the fix and saying so in the review.

## How to post a review
- Post with the `gh` CLI under the owner's account (MAJ224), as a **COMMENT** review. GitHub
  blocks Approve and Request-changes on your own PRs. Example:
  `gh api -X POST repos/FocusRGB/<repo>/pulls/<n>/reviews --input review.json`, where
  `review.json` is `{"commit_id": "<head sha>", "event": "COMMENT", "body": "...", "comments": [...]}`.
- Inline comments only work on lines that are part of the PR's diff. Put anything else in the
  body.
- The first line is `## Review at head \`<sha7>\`: **ready to merge**` (or "not ready…").
  List what you checked, then blockers, then non-blocking nits.
- Write non-ASCII text, such as `…`, to a file and post from the file. Inline shell strings on
  Windows can corrupt it.
- **Every** comment, issue, PR and commit you write ends with `Assisted-by: <your tool>`. For
  Antigravity that is `Assisted-by: Antigravity`. Earlier reviews say `Assisted-by: Claude`. Never use
  `Co-Authored-By` lines or "Generated with ..." footers.
- **What goes where:** all review details go on GitHub. Your chat reply to the owner is only the
  review link(s) plus a one-line verdict per PR, and when useful a short copy-paste message for
  Codex.

## How to review
- Review against the slice document (`FocusRGB/docs/slices/...`), the protocol spec
  (`FocusRGB/sdk/spec/`) and `FocusRGB/docs/plugin-architecture.md`.
- Read the real diff and the code it touches; don't rely on the PR description. Reproduce doubts
  locally where you can (`git fetch origin pull/<n>/head:pr<n>`, then build or test). Use a
  scratch folder, never the owner's working tree.
- Pay most attention to: concurrency and lifecycle, security at trust boundaries (downloads,
  zips, process launch, consent), protocol or schema drift, migrations, and honest status
  reporting (never claim output that wasn't verified).
- **Code graph:** one workspace graph at `D:\Develop\C#\FocusRGB\build\graphify-out\graph.json`. If
  your shell doesn't have `GRAPHIFY_OUT`, always pass `--graph` with that path. Never run
  `graphify update` inside a clone. The clones' own `AGENTS.md` graph text is out of date until
  Codex's docs PR merges; the workspace `AGENTS.md` wins.
- Check for mojibake (`â€`), stale paths, attribution and secrets. Keep the KvK and VAT numbers
  out of repositories.

## Standing decisions (don't relitigate)
- **Repositories:** `FocusRGB/FocusRGB` holds the app, host, protocol spec, schemas and conformance
  kit; nothing is published from it. `FocusRGB/FocusRGB-Plugin-SDK` holds the SDKs; only
  `FocusRgb.PluginSdk` is published (nuget.org, Trusted Publishing). `FocusRGB/FocusRGB-Plugin-SignalRGB`
  is the official plugin.
- **Protocol:** stdio JSON-RPC. Released specs are `spec-v1.0.0` and `spec-v1.1.0`; the 1.1 runner
  manifest field `runtime` requires contract ≥1.1. Spec changes start in FocusRGB, get tagged, then
  reach the SDKs. Schema `$id`s point at immutable spec tags.
- **Deferred:** the Node and Python SDKs wait until after Slice 29 (SDK issue #8; PR #7 is parked
  as a draft).
- **Ownership:** Devantix (an eenmanszaak) owns the licence. The GitHub organization, packages
  (`FocusRgb.*`, `@focusrgb`) and domain (`focusrgb.dev`) stay FocusRGB. Never write "B.V."
- **Owner-only:** tags, releases, package-publish approvals, registry accounts, GitHub environments
  and secrets, live hardware or SignalRGB checks, and anything on `main`. You may run a
  tag or release command only when the owner explicitly says to.
- **Workflow:** branch from `develop`, open PRs into `develop`, never push to `develop` or `main`,
  make focused commits. Branch prefixes are `feature/`, `slice/`, `fix/`, `chore/`, `docs/` and
  `dependabot/`.

## State at handover (2026-10-21)
- Slices 00–17 are complete; #41, #42 and #80 are closed. The roadmap is
  https://github.com/orgs/FocusRGB/projects/1
- Releases: `FocusRgb.PluginSdk` 1.0.0 on nuget.org, SignalRGB `v1.0.0`, `spec-v1.0.0`,
  `spec-v1.1.0`.
- PRs ready to merge: FocusRGB #110 (roadmap/STATUS) and SignalRGB #18 (SHA256SUMS line endings).
- **Next:** Slice 18, Local MCP commands (#45). Codex has been asked to propose a plan first: the
  PR breakdown, a local-only transport, the exposed commands, how they map to the existing
  selection and command services, and the approval model for anything that changes output.
  Review that plan before any code.
- Open idea issues: #90 (profile-change pop-up), #83 (split roadmap docs into FocusRGB-Roadmap),
  #87 (wiki). npm `@focusrgb` is waiting on npm support.
