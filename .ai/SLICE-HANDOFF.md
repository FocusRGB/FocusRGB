# Slice handoff template

Copy this block into STATUS.md's active implementation section at the end of a working session.
Use actual paths, commands, and results. Do not report planned behavior as implemented.

```markdown
## Active implementation slice

- Slice: NN — title from docs/implementation-slices.md
- State: pending / in-progress / blocked / complete
- Branch, latest relevant commit, and pull request into `develop`:
- Scope and dependencies verified:
- Implemented files and entry points:
- Verification: exact command, pass/fail, relevant result (or not run + reason)
- External/live evidence: requested / not requested; API result versus visible result
- Remaining acceptance criteria:
- Current blocker or required product input:
- Uncommitted work to preserve:
- Next action: one concrete step with file/function/command
- Next slice: ID, only if current acceptance criteria passed
```

Before stopping mid-slice, save coherent local edits, inspect git status, and record failing
checks and unfinished paths. Do not commit broken work as a completed slice. If a partial
checkpoint commit is useful, label it explicitly as partial. Never paste credentials or whole
logs here; generated reports belong under build/.
