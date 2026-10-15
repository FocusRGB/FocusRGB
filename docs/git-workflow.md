# Git workflow

Adopted 2026-10-10. Applies to people and AI assistants.

## Branches

| Branch | Purpose | Updated by |
|---|---|---|
| `develop` | Default and integration branch; every change lands here first | Pull requests only |
| `main` | Stable releases | Pull requests from `develop` only, merged by the repository owner |
| `slice/NN-short-name` | One implementation slice from `implementation-slices.md` | Its author |
| `feature/…`, `fix/…`, `chore/…`, `docs/…` | Other scoped work | Its author |

## Rules

1. Start every branch from the latest `origin/develop`. If work depends on an unmerged
   branch, base it on that branch and say so in the pull request.
2. Never commit or push directly to `develop` or `main`. Push the working branch and open a
   pull request into `develop`.
3. One slice or one scoped change per pull request. Its description lists what changed, the
   verification actually run, and the remaining gates, and links the slice ID when there is one.
4. Assistants may create branches, commit, push working branches, and open pull requests
   into `develop`. They do not merge pull requests, push to `develop` or `main`, force-push,
   rewrite published history, or delete branches unless the owner explicitly asks.
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

## Branch protection

Both branches are protected on GitHub by rulesets; the exported definitions are in
`../.github/rulesets/`. Each ruleset:

- blocks direct pushes (a pull request is required), force-pushes, and deletion;
- requires review threads to be resolved (0 approvals, for a single maintainer);
- requires the `branch-policy` check from `../.github/workflows/branch-policy.yml`;
- allows squash or merge commits into `develop`, and merge commits only into `main`;
- has no bypass actors, so the rules also apply to the repository owner and to automation.

Rulesets cannot limit which branch a pull request comes from, so the `branch-policy`
workflow does that: it fails a pull request into `main` unless it comes from `develop` in
this repository, and one into `develop` unless its branch uses an allowed prefix. It runs
on `pull_request_target`, so the version on the base branch decides and a pull request
cannot weaken its own check. It does not check out or run pull request code.

Bootstrap: a required check runs from the workflow on the target branch, so it is added
to each ruleset only after `branch-policy.yml` exists on that branch. Until then the ruleset
is active without the `required_status_checks` rule. Add it to `develop` once the workflow
merges there, and to `main` after the first `develop` → `main` release merges.

To recreate a ruleset, use Settings → Rules → Rulesets → New ruleset → Import a ruleset, or
`gh api repos/MAJ224/FocusRGB/rulesets --method POST --input .github/rulesets/develop.json`.
Add the CI build as a second required check once slice 01 introduces it. In an emergency the
owner can temporarily set a ruleset's enforcement to Disabled in the GitHub UI.
