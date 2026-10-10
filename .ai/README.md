# AI handoff

Updated 2026-10-09. Plain Markdown context independent of AI provider or chat history.

## Read in order

1. `AGENTS.md` — working instructions.
2. `../docs/app-brief.md` — desired app and user decisions.
3. `../docs/plugin-architecture.md` — proposed extension contracts.
4. `../docs/integration-evidence.md` — dated results and limitations.
5. `../docs/focus-profiles-plan.md` — milestones and acceptance checks.
6. `STATUS.md` — current progress and next step.

For implementation, use `../docs/implementation-slices.md` for bounded tasks/dependencies and
copy-paste start/resume prompts. `SLICE-HANDOFF.md` defines the session checkpoint format.

`WORKFLOWS.md` provides reusable investigation and handoff procedures. They can later be
packaged as skills for a chosen tool; they are not installed/executable skills today.

## Prompt for another model

> Work in FocusRGB. Read .ai/AGENTS.md and .ai/README.md, then their reading list.
> Continue from .ai/STATUS.md within my requested scope. Distinguish proposed design,
> historical observations, and tests you perform now. Update the shared handoff when done.

## Provenance

Derived from Claude's **RGB** conversation, session
`ca5cee85-477f-4450-81d7-3fbecb25b67f`, existing workspace files, and the user's clarification
on 2026-10-08: eventually replace SignalRGB but retain integrations with other apps.
Only relevant RGB context is included; no credentials or full chat export are required.
