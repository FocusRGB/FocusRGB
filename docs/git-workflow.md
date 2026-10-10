# Git workflow

Adopted 2026-10-10. Applies to people and AI assistants.

## Branches

| Branch | Purpose | Updated by |
|---|---|---|
| `develop` | Default and integration branch; every change lands here first | Pull requests only |
| `main` | Stable releases | Pull requests from `develop` only, merged by the repository owner |
| `slice/NN-short-name` | One implementation slice from `implementation-slices.md` | Its author |
| `chore/…`, `docs/…`, `fix/…` | Other scoped work | Its author |

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
5. Only the owner opens and merges the `develop` → `main` release pull request.
6. Merge slice pull requests into `develop` with squash or a merge commit. Merge `develop`
   into `main` with a merge commit, not a squash, so the two branches do not diverge.

## GitHub rulesets

Ready-to-import rulesets are in `../.github/rulesets/`. Import them on GitHub under
Settings → Rules → Rulesets → New ruleset → Import a ruleset.

- `develop.json`: requires a pull request with resolved review threads and blocks deletion
  and force-push. Approvals are set to 0 for a single maintainer; allows squash or merge.
- `main.json`: the same protections, merge commits only.

Rulesets cannot restrict which branch a pull request comes from, so rule 5 is enforced by
convention. After slice 01 adds a CI workflow, add its job as a required status check to both
rulesets. To let the owner bypass a rule in an emergency, add the repository admin role as a
bypass actor in the GitHub UI.
