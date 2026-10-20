# Git workflow

Adopted 2026-10-10. Applies to people and AI assistants.

## Branches

| Branch | Purpose | Updated by |
|---|---|---|
| `develop` | Default and integration branch; every change lands here first | Pull requests only |
| `main` | Stable releases | Pull requests from `develop` only, merged by the repository owner |
| `slice/NN-short-name` | One detail file linked from `implementation-slices.md` | Its author |
| `feature/…`, `fix/…`, `chore/…`, `docs/…` | Other scoped work | Its author |

## Rules

1. Start every branch from the latest `origin/develop`. If work depends on an unmerged
   branch, base it on that branch and say so in the pull request.
2. Never commit or push directly to `develop` or `main`. Push the working branch and open a
   pull request. A standalone pull request targets `develop`; a stacked pull request targets
   its immediate dependency branch so GitHub shows only that layer's changes. After the parent
   merges, retarget the child to `develop` before merging it.
3. One slice or one scoped change per pull request. Its description lists what changed, the
   verification actually run, and the remaining gates, and links the slice ID when there is one.
4. Assistants may create branches, commit, push working branches, and open pull requests into
   `develop` or an immediate dependency branch. They do not merge pull requests, push to
   `develop` or `main`, force-push, rewrite published history, or delete branches unless the
   owner explicitly asks.
5. Only the owner opens and merges the `develop` → `main` release pull request. `main`
   accepts pull requests only from `develop`; `develop` accepts them only from `feature/`,
   `slice/`, `fix/`, `chore/`, and `docs/` branches.
6. Merge slice pull requests into `develop` with squash or a merge commit. Merge `develop`
   into `main` with a merge commit, not a squash, so the two branches do not diverge.
7. Build a pull request from several focused commits, never one commit for the whole change.
   Stage each logical step separately (`git add <paths>` or `git add -p`), for example build
   configuration, each project, tests, CI, and documentation, so every commit can be reviewed
   on its own and still builds where practical. Do not stage an entire pull request with
   `git add -A`. A merge commit keeps these commits in `develop`; a squash merge collapses them.
8. Keep at most two dependent slice pull requests open. Merge or close the reviewed base before
   opening a third, then retarget its child to `develop`. If an old stack must be consolidated,
   merge its newest fully verified head with history preserved and close the contained pull
   requests as superseded; retain their branches until the consolidation is confirmed.

## Identity and assistance

- Commits use the repository owner's configured author and committer identity. Pull requests,
  reviews, comments, assignments, merges, and other GitHub actions use the owner's account.
- An assistant is not listed as an author, committer, assignee, or reviewer. Text or commits it
  prepares end with an attribution trailer such as `Assisted-by: OpenAI Codex` or
  `Assisted-by: Claude`, using the assistant that actually contributed.
- Do not rewrite existing history solely to add or change assistance attribution.

## Labels and version tags

- Add `slice` and `enhancement` to implementation-slice pull requests.
- Use `bug` for fixes, `documentation` for documentation-only changes, and `release` only for
  `develop` to `main` pull requests. Add more labels only when they change triage decisions.
- Tag only tested commits merged into `main`, using SemVer such as `v0.1.0-alpha.1`. Do not tag
  individual slices or integration-branch commits.
- Merge `develop` to `main` at a coherent, runnable milestone rather than after every slice. The
  first milestone, Slices 00-09, is tagged `v0.1.0-alpha.1`; later milestones advance the
  prerelease number until the product is ready for a stable version.

## Issues and milestones

- Track every implementation slice with one GitHub issue whose body links the authoritative slice
  document, its dependency issues, scope, and acceptance criteria. Keep the issue-to-slice map in
  [`github-roadmap.md`](github-roadmap.md).
- Assign slice issues only to the repository owner and label them `slice` and `enhancement`.
- Issue numbers are immutable and need not match slice numbers. Keep slice issues in numeric slice
  order in `github-roadmap.md` and the `FocusRGB Roadmap` project's manual order. Put a follow-up
  bug beside its owning slice, assign it to that slice's milestone, and link it from the slice
  document.
- Group slices into coherent release milestones without invented due dates. Close a slice issue as
  completed only when the roadmap says it is complete; close its milestone after the related pull
  requests have also landed.

## Pull request planning and review

- Add active slice pull requests to the `FocusRGB Roadmap` project and to the milestone that
  names their intended release checkpoint. Slices 10-11 share
  `v0.2.0-alpha.1 - Plugin runtime and coordination`.
- The [`FocusRGB Roadmap`](https://github.com/orgs/FocusRGB/projects/1) project is public and
  owned by the `FocusRGB` organization; keep its cards in canonical slice order.
- Assign only the repository owner. If an assistant helps implement or review a change, record it
  with the matching `Assisted-by:` trailer instead of assigning or requesting review from it.
- Reviews are submitted under the repository owner's account. A review drafted with Claude or
  another assistant carries that assistant's `Assisted-by:` trailer.

## External plugin repositories

Production plugins use peer repositories named `FocusRGB-Plugin-<Name>`. Create a repository
only when its implementation slice starts; do not create empty placeholders. Each plugin has
its own history, issues, CI, releases, license, and branch protection.

Plugin repositories consume a published `FocusRgb.PluginSdk` version and must build without a
FocusRGB source checkout. Do not connect repositories with Git submodules or cross-repository
project references. When a protocol change spans repositories, use separate linked pull
requests: publish the compatible SDK prerelease first, then update the plugin and the app's
compatibility fixtures to the exact declared versions.

Publish `FocusRgb.Contracts` and `FocusRgb.PluginSdk` to nuget.org through the .NET-specific
`sdk-dotnet-v*` tag before an external plugin consumes that version. Publishing uses the
nuget.org Trusted Publishing policy for `FocusRGB/FocusRGB`, the `publish-plugin-sdk.yml`
workflow, and the protected `nuget` environment. `NuGet/login@v1` exchanges GitHub's OIDC token
for a short-lived API key under the `maj224` nuget.org profile; do not store a long-lived NuGet
API key. External CI restores the public packages from nuget.org without credentials. The stable
spec and all language SDKs remain under this repository's `sdk/` area. Link the main slice issue
to the plugin repository's own issue and pull request, using the same release milestone.

The `nuget` GitHub environment must be protected with MAJ224 as its required reviewer. The
publication job validates the `sdk-dotnet-vMAJOR.MINOR.PATCH` tag against both package versions and
requires the tagged commit to belong to `develop` or `main` before requesting the short-lived key.
Each released schema `$id` and packaged protocol link resolves through its immutable
`sdk-dotnet-vX.Y.Z` tag so the published contract cannot move after release.
Move only maintained, publication-safe research: do not republish local security-testing notes,
credentials, private-network addresses, or machine-specific paths in a public plugin repository.

## Branch protection

Both branches are protected on GitHub by rulesets; the exported definitions are in
`../.github/rulesets/`. The shared rules:

- blocks direct pushes (a pull request is required), force-pushes, and deletion;
- requires review threads to be resolved (0 approvals, for a single maintainer);
- allows squash or merge commits into `develop`, and merge commits only into `main`;
- has no bypass actors, so the rules also apply to the repository owner and to automation.

Rulesets cannot limit which branch a pull request comes from, so the `branch-policy`
workflow does that: it fails a pull request into `main` unless it comes from `develop` in
this repository, and one into `develop` unless its branch uses an allowed prefix. It runs
on `pull_request_target`, so the version on the base branch decides and a pull request
cannot weaken its own check. It does not check out or run pull request code.

Both pull-request workflows run for every base branch so stacked changes receive policy and
build checks. CI also handles the `edited` event, ensuring a base-branch retarget starts a
fresh build without requiring another source commit.

Current live rules require `branch-policy` on `develop`. The workflow also passes on `main`, and
the `build` workflow passes on both branches, but those checks are not yet required by both
rulesets. When enabling them, update the live rulesets and the exported definitions together.

To recreate a ruleset, use Settings → Rules → Rulesets → New ruleset → Import a ruleset, or
`gh api repos/FocusRGB/FocusRGB/rulesets --method POST --input .github/rulesets/develop.json`.
In an emergency the owner can temporarily set a ruleset's enforcement to Disabled in the
GitHub UI.
